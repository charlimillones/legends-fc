using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Season
{
    public sealed class SeasonReport
    {
        public int SeasonStartYear;
        public List<CompetitionOutcome> Outcomes = new List<CompetitionOutcome>();
        public int Retired, AcademyGraduates, Released, FreeAgentSignings, Renewed, LeftAtContractEnd, NotOfferedRenewal, RefusedRenewal, ClubsAtZero;
        public int Transfers, Loans, LoansReturned, FacilityLevelDrops, ManagerHandovers;
        public double TransferFees;
        public Dictionary<string, Money.IncomeBreakdown> Income = new Dictionary<string, Money.IncomeBreakdown>();
        public Dictionary<string, double> WageBill = new Dictionary<string, double>();
    }

    /// <summary>
    /// One full season and the move to the next: matches (sim mode), weekly training, promotion and
    /// relegation, ageing, retirements and the academy intake. Endless career = call Advance again.
    /// Interim until those systems exist: no transfers, contracts or per-player form yet (form = neutral).
    /// </summary>
    public sealed class SeasonCycle
    {
        private readonly GameData _d;
        public SeasonCycle(GameData d) { _d = d; }

        /// <summary>One whole season, week by week (52 weeks), then the end-of-season work. Endless career = call again.</summary>
        public SeasonReport Advance(GameWorld w, GameRandom rng)
        {
            var calendar = new SeasonCalendar(_d);
            var m = w.Market;
            int historyBefore = m.History.Count;
            if (w.Calendar.Week == 0 || w.Calendar.Runs.Count == 0) calendar.Start(w, rng);
            while (!w.Calendar.SeasonOver) calendar.PlayWeek(w, rng);
            var report = EndSeason(w, rng, calendar.Outcomes(w));
            var deals = m.History.Skip(historyBefore).ToList();
            report.Transfers = deals.Count(x => !x.Loan && !x.FreeAgent);
            report.Loans = deals.Count(x => x.Loan);
            report.TransferFees = deals.Sum(x => (double)x.Fee);
            return report;
        }

        /// <summary>
        /// End of season: money and fan mood, promotion and relegation, the new year (loans back, retirements),
        /// contract renewals, academy intake and squad limits, the free-agent market. Then the next season's calendar is reset.
        /// </summary>
        public SeasonReport EndSeason(GameWorld w, GameRandom rng, List<CompetitionOutcome> outcomes)
        {
            var report = new SeasonReport { SeasonStartYear = w.SeasonStartYear, Outcomes = outcomes };
            SettleFinances(w, report);
            ApplyPromotionAndRelegation(w, report.Outcomes);

            // Cups (Oct 9): coefficients, honours, and the results next season's places and super cups come from.
            CupRewards.UpdateCoefficients(w, CupRewards.SeasonPoints(w.Calendar.Runs, _d), _d);
            foreach (var o in outcomes.Where(o => o != null))
                foreach (var t in o.Titles.Where(t => t.Key != CupFormats.RunnerUp))
                    w.Honours.Add(new TitleRecord { CompetitionId = o.CompetitionId, Title = t.Key, ClubId = t.Value, SeasonStartYear = w.SeasonStartYear });
            w.LastOutcomes = outcomes.Where(o => o != null).ToList();

            report.FacilityLevelDrops = Facilities.FacilityRules.LevelDrops(w, rng, _d.FacilityRules).Count;
            w.SeasonStartYear++;
            report.ManagerHandovers = Facilities.FacilityRules.ManagerHandovers(w, rng, _d).Count;
            report.LoansReturned = Transfers.Market.ReturnLoans(w);   // loans last until the end of the season
            foreach (var p in w.Players.Where(p => !p.Retired && w.SeasonStartYear - p.BirthYear >= p.RetireAge))
            {
                p.Retired = true; p.ClubId = null; report.Retired++; w.Market.SquadsChanged();
            }

            var swR = System.Diagnostics.Stopwatch.StartNew();
            RenewContracts(w, rng, report);
            w.Market.Stats.TryGetValue("ms:renew", out int msr); w.Market.Stats["ms:renew"] = msr + (int)swR.ElapsedMilliseconds;

            var gen = new WorldGenerator(_d);
            var c = _d.Development;
            foreach (var club in w.Clubs)
            {
                int intake = rng.NextInt(c.AcademyIntakeMin, c.AcademyIntakeMax);
                gen.AddAcademyIntake(w, club, rng, intake);
                w.Market.SquadsChanged();
                report.AcademyGraduates += intake;
                if (club.Id == w.UserClubId) continue;   // the user's squad is never trimmed or topped up for him
                var squad = w.Players.Where(p => p.ClubId == club.Id).ToList();
                int excess = squad.Count - c.MaxSquadSize;
                if (excess <= 0) continue;
                // AI: release the least valuable (rating + half the remaining potential for players 21 and under),
                // keeping at least the AI goalkeeper minimum. PROPOSAL until contracts and transfers exist.
                int year = w.SeasonStartYear;
                foreach (var p in squad.OrderBy(p => Rating(p) + (year - p.BirthYear <= 21 ? 0.5 * System.Math.Max(0, p.Potential - Rating(p)) : 0)))
                {
                    if (excess == 0) break;
                    if (p.MainPosition == Position.GK && squad.Count(x => x.ClubId == club.Id && x.MainPosition == Position.GK) <= c.AiMinGoalkeepers) continue;
                    Transfers.Squads.Leave(w, p); excess--; report.Released++;
                }
            }
            AiFreeAgentWindow(w, rng, report);

            // Ready for the next season: a fresh calendar; old finished talks are cleared.
            w.Market.Talks.RemoveAll(t => t.Status != Transfers.TalkStatus.Open && t.Status != Transfers.TalkStatus.Pending);
            w.Calendar = new CalendarState();
            return report;
        }

        /// <summary>
        /// AI clubs in the free-agent market (PROPOSAL rules in finance.json). Same offer and acceptance rule as the user.
        /// 1) Best reputation first, each approaches up to N free agents at least as good as its squad median, within its wage bar.
        /// 2) AI balance: at least 2 goalkeepers. 3) Minimum squad of 16 (confirmed Oct 9): sign the best it can afford.
        /// The user's club is never topped up: it simply can't sell below 16.
        /// </summary>
        private void AiFreeAgentWindow(GameWorld w, GameRandom rng, SeasonReport report)
        {
            w.Market.SquadsChanged();
            var f = _d.Finance; var fa = f.FreeAgents; var c = _d.Development;
            var rating = new Dictionary<Player, double>();
            var value = new Dictionary<Player, double>();  // market value on a 2-year deal, for affordability checks
            void Track(Player p)
            {
                rating[p] = Rating(p);
                value[p] = Transfers.FreeAgents.MarketValueEur(p, w.SeasonStartYear, 2, _d);
            }
            var pool = Transfers.FreeAgents.List(w);
            foreach (var p in pool) Track(p);
            pool = pool.OrderByDescending(p => rating[p]).ThenBy(p => p.Id, System.StringComparer.Ordinal).ToList();

            var squads = w.Players.Where(p => p.ClubId != null).GroupBy(p => p.ClubId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var club in w.Clubs) if (!squads.ContainsKey(club.Id)) squads[club.Id] = new List<Player>();
            var bill = squads.ToDictionary(kv => kv.Key, kv => kv.Value.Sum(p => (double)p.Wage));
            var ai = w.Clubs.Where(x => x.Id != w.UserClubId).OrderByDescending(x => x.Reputation).ThenBy(x => x.Id, System.StringComparer.Ordinal).ToList();
            double Headroom(Club club) => Money.Finance.WageBar(club, w.MoneyKey(club), fa.AiWageObjective, f) - bill[club.Id];
            double Wish(Club club, Player p) => Money.Finance.ExpectedWage(value[p], w.MoneyKey(club), f);

            bool TrySign(Club club, Player p)
            {
                // The AI offers his market wage at his preferred length; the chance depends on what he asks (Oct 9 rules).
                int years = Transfers.Pricing.PreferredYears(w, p, _d);
                double expected = Transfers.FreeAgents.ExpectedWage(w, club, p, years, _d);
                var result = Transfers.FreeAgents.Offer(w, club, p, (long)System.Math.Max(1, System.Math.Round(expected)), years, rng, _d);
                if (result != Transfers.OfferResult.Accepted) return false;
                pool.Remove(p); squads[club.Id].Add(p); bill[club.Id] += p.Wage; report.FreeAgentSignings++;
                return true;
            }

            // 1) Strengthen the squad.
            foreach (var club in ai)
            {
                var squad = squads[club.Id];
                if (squad.Count == 0 || squad.Count >= _d.Transfers.Ai.NormalSquadSize) continue;   // strengthen only below the normal squad size
                var ratings = squad.Select(Rating).OrderBy(x => x).ToList();
                double bar = ratings[(int)System.Math.Min(ratings.Count - 1, System.Math.Floor(fa.AiQualityPercentile * ratings.Count))];
                int approaches = 0;
                foreach (var p in pool.ToList())
                {
                    if (approaches >= fa.AiApproachesPerClub || squad.Count >= c.MaxSquadSize) break;
                    if (rating[p] < bar) break;   // pool is sorted best first
                    if (Wish(club, p) > Headroom(club)) continue;
                    approaches++;
                    TrySign(club, p);
                }
            }
            // 2) and 3) Goalkeeper minimum, then the 16 minimum: best affordable first, else the cheapest.
            foreach (var club in ai)
            {
                var squad = squads[club.Id];
                var saidNo = new HashSet<Player>();
                for (int guard = 0; guard < 200; guard++)
                {
                    bool needGk = squad.Count(p => p.MainPosition == Position.GK) < c.AiMinGoalkeepers;
                    if (!needGk && squad.Count >= c.MinSquadSize) break;
                    var candidates = pool.Where(p => !saidNo.Contains(p) && (!needGk || p.MainPosition == Position.GK)).ToList();
                    if (candidates.Count == 0) break;
                    if (needGk && squad.Count >= c.MaxSquadSize)
                    {   // make room: release the weakest outfield player
                        var weakest = squad.Where(p => p.MainPosition != Position.GK).OrderBy(Rating).First();
                        Transfers.Squads.Leave(w, weakest); squad.Remove(weakest); bill[club.Id] -= weakest.Wage;
                        Track(weakest); pool.Add(weakest); report.Released++;
                    }
                    double room = Headroom(club);
                    var pick = candidates.FirstOrDefault(p => Wish(club, p) <= room) ?? candidates.OrderBy(p => value[p]).First();
                    if (!TrySign(club, pick)) saidNo.Add(pick);   // he said no to this club: try the next one
                }
            }
        }

        private double Rating(Player p) => PositionRating.Base(p.Attributes, p.MainPosition, _d.PositionRatings);

        /// <summary>Fan mood from results and season outcome, then season income minus wages and upkeep (agreed Oct 9).</summary>
        private void SettleFinances(GameWorld w, SeasonReport report)
        {
            var f = _d.Finance; var fm = f.FanMood;
            var rows = new Dictionary<string, (int pos, int teams, string league, TableRow row)>();
            var cupMoney = CupRewards.PrizeMoney(w.Calendar.Runs, _d);
            var leagues = new HashSet<string>(w.Competitions.Where(c => c.Type == CompetitionType.League).Select(c => c.Id));
            foreach (var o in report.Outcomes.Where(o => o != null))
            {
                foreach (var id in o.Titles.Where(t => t.Key != CupFormats.RunnerUp).Select(t => t.Value).Distinct()) Mood(w, id, fm.Title);
                if (!leagues.Contains(o.CompetitionId)) continue;
                var table = o.Tables.TryGetValue("League", out var t) ? t : o.Tables.TryGetValue("Annual", out var a) ? a
                          : o.Tables.Values.SelectMany(x => x).OrderByDescending(r => r.Points).ThenByDescending(r => r.GoalDifference).ToList();
                for (int i = 0; i < table.Count; i++) rows[table[i].ClubId] = (i + 1, table.Count, o.CompetitionId, table[i]);
                foreach (var id in o.Promoted) Mood(w, id, fm.Promotion);
                foreach (var id in o.Relegated) Mood(w, id, fm.Relegation);
            }
            foreach (var club in w.Clubs)
            {
                string key = w.MoneyKey(club);
                int pos = 1, teams = 1;
                if (rows.TryGetValue(club.Id, out var r))
                {
                    pos = r.pos; teams = r.teams;
                    club.FanMood = Clamp(club.FanMood + fm.Win * r.row.Won + fm.Draw * r.row.Drawn + fm.Loss * r.row.Lost, 0, 100);
                }
                double normal = Money.Finance.NormalFanMood(club, f);
                club.FanMood = normal + (club.FanMood - normal) * System.Math.Pow(1 - fm.MonthlyDriftShare, 10); // drift over the season

                var income = Money.Finance.SeasonIncome(club, key, pos, teams, SeasonSimulator.HomeLeagueMatches(key, teams, _d), f, _d.FacilityRules);
                income.Cups = cupMoney.TryGetValue(club.Id, out var prize) ? prize : 0;
                double wages = Transfers.Market.WageBill(w, club.Id);   // loans: each club pays its share
                double upkeep = income.Total * f.UpkeepShareOfIncome;
                club.Balance = System.Math.Max(0, club.Balance + (long)System.Math.Round(income.Total - wages - upkeep)); // never below zero (Oct 9)
                if (club.Balance == 0) report.ClubsAtZero++;
                report.Income[club.Id] = income; report.WageBill[club.Id] = wages;
            }
        }

        private static void Mood(GameWorld w, string clubId, double delta)
        {
            var club = w.Clubs.First(c => c.Id == clubId);
            club.FanMood = Clamp(club.FanMood + delta, 0, 100);
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>
        /// Expiring contracts go to a renewal negotiation; the outcome decides if he stays (confirmed Oct 9).
        /// AI clubs talk to players they still want (PROPOSAL rule in finance.json): they open at his market wage
        /// for his preferred length and move toward what he asks, up to 10% over market (transfers.json ai.maxOverWageDemand).
        /// The user's club negotiates in the UI; headless runs use the same AI rule for it.
        /// </summary>
        private void RenewContracts(GameWorld w, GameRandom rng, SeasonReport report)
        {
            w.Market.SquadsChanged();
            var f = _d.Finance; var rc = f.Renewal;
            foreach (var club in w.Clubs)
            {
                var squad = w.Players.Where(p => p.ClubId == club.Id).ToList();
                if (squad.Count == 0) continue;
                // Rank by rating: the AI keeps its best (normal squad size − 2) players up to 32, and its prospects (PROPOSAL).
                var rank = squad.OrderByDescending(Rating).ThenBy(p => p.Id, System.StringComparer.Ordinal).Select((p, i) => (p, i + 1)).ToDictionary(x => x.p, x => x.Item2);
                int keep = _d.Transfers.Ai.NormalSquadSize - 2;
                foreach (var p in squad.Where(p => p.ContractEndYear <= w.SeasonStartYear))
                {
                    int age = w.SeasonStartYear - p.BirthYear;
                    bool wanted = (age <= rc.AiOfferYoungAge && rank[p] <= _d.Development.MaxSquadSize - 2) || (age <= rc.AiOfferMaxAge && rank[p] <= keep);
                    if (!wanted) { Transfers.Squads.Leave(w, p); report.LeftAtContractEnd++; report.NotOfferedRenewal++; continue; }
                    var talk = Transfers.Market.OpenRenewal(w, club, p, _d);
                    int years = talk.PreferredYears;
                    double market = Transfers.FreeAgents.ExpectedWage(w, club, p, years, _d);
                    double max = market * _d.Transfers.Ai.MaxOverWageDemand, wage = market;
                    bool renewed = false;
                    for (int round = 0; round < 6 && talk.Status == Transfers.TalkStatus.Open; round++)
                    {
                        var reply = Transfers.Market.Offer(w, talk, 0, (long)System.Math.Max(1, System.Math.Round(wage)), years, rng, _d, out _);
                        if (reply == Transfers.Reply.Accepted) { renewed = true; break; }
                        if (reply == Transfers.Reply.Invalid || reply == Transfers.Reply.WalkedAway) break;
                        double target = reply == Transfers.Reply.Countered ? talk.CounterWage : talk.WageDemand;
                        double next = reply == Transfers.Reply.Countered && target <= max ? target : System.Math.Min(max, (wage + target) / 2);
                        if (next <= wage + 1) break;   // the club won't go higher
                        wage = next;
                    }
                    if (talk.Status == Transfers.TalkStatus.Open) talk.Status = Transfers.TalkStatus.Cancelled;
                    if (renewed) report.Renewed++;
                    else { Transfers.Squads.Leave(w, p); report.LeftAtContractEnd++; report.RefusedRenewal++; }
                }
            }
        }

        /// <summary>AI default until coaches exist: one coach per group (GK / defence / midfield / attack), moderate regime.</summary>
        public static void TrainOneWeek(GameWorld w, GameData d)
        {
            var c = d.Development;
            var arch = d.Archetypes.ToDictionary(a => a.Id);
            foreach (var club in w.Clubs)
            {
                var squad = Transfers.Market.Squad(w, club.Id);
                var fac = club.Facilities[Facility.TrainingGround];
                int effective = (int)System.Math.Round(fac.Level * (0.6 + 0.4 * fac.Condition / 100));
                var groups = squad.GroupBy(p => CoachGroup(p.MainPosition)).ToDictionary(g => g.Key, g => g.Count());
                foreach (var p in squad)
                    Development.TrainWeek(p, w.SeasonStartYear - p.BirthYear, arch[p.ArchetypeId], c.DefaultCoachQuality, c.DefaultRegime,
                        groups[CoachGroup(p.MainPosition)], effective, c.FormNeutral, d);
            }
        }

        public static string CoachGroup(Position p)
        {
            switch (p)
            {
                case Position.GK: return "GK";
                case Position.CB: case Position.LB: case Position.RB: return "DEF";
                case Position.DM: case Position.CM: case Position.LM: case Position.RM: return "MID";
                default: return "FWD";
            }
        }

        private static void ApplyPromotionAndRelegation(GameWorld w, List<CompetitionOutcome> outcomes)
        {
            var byId = w.Clubs.ToDictionary(c => c.Id);
            foreach (var o in outcomes.Where(o => o != null && (o.Relegated.Count > 0 || o.Promoted.Count > 0)))
            {
                var comp = w.Competitions.First(c => c.Id == o.CompetitionId);
                foreach (var id in o.Relegated) Move(w, byId[id], comp.CountryId, comp.Level + 1);
                foreach (var id in o.Promoted) Move(w, byId[id], comp.CountryId, comp.Level - 1);
            }
        }

        private static void Move(GameWorld w, Club club, string countryId, int level)
        {
            var target = w.Competitions.First(c => c.CountryId == countryId && c.Level == level && c.Type == CompetitionType.League);
            w.ClubLeague[club.Id] = target.Id;
            club.Division = level;
        }
    }
}
