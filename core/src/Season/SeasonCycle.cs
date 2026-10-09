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

        public SeasonReport Advance(GameWorld w, GameRandom rng)
        {
            var report = new SeasonReport { SeasonStartYear = w.SeasonStartYear };
            report.Outcomes = new SeasonSimulator(_d).PlaySeason(w, rng);
            Train(w);
            SettleFinances(w, report);
            ApplyPromotionAndRelegation(w, report.Outcomes);

            w.SeasonStartYear++;
            foreach (var p in w.Players.Where(p => !p.Retired && w.SeasonStartYear - p.BirthYear >= p.RetireAge))
            {
                p.Retired = true; p.ClubId = null; report.Retired++;
            }

            RenewContracts(w, rng, report);

            var gen = new WorldGenerator(_d);
            var c = _d.Development;
            foreach (var club in w.Clubs)
            {
                int intake = rng.NextInt(c.AcademyIntakeMin, c.AcademyIntakeMax);
                gen.AddAcademyIntake(w, club, rng, intake);
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
                    Transfers.Squads.Leave(p); excess--; report.Released++;
                }
            }
            AiFreeAgentWindow(w, rng, report);
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
                int age = w.SeasonStartYear - p.BirthYear;
                int years = age <= 23 ? rng.NextInt(3, 4) : age <= 29 ? rng.NextInt(2, 4) : rng.NextInt(1, 2);
                double expected = Transfers.FreeAgents.ExpectedWage(w, club, p, years, _d);
                if (p.PersonalityId == "PER-BUSINESSMAN") expected *= Money.Contracts.BusinessmanWageFactor;
                var result = Transfers.FreeAgents.Offer(w, club, p, (long)System.Math.Max(1, System.Math.Round(expected)), years, rng, _d);
                if (result != Transfers.OfferResult.Accepted) return false;
                pool.Remove(p); squads[club.Id].Add(p); bill[club.Id] += p.Wage; report.FreeAgentSignings++;
                return true;
            }

            // 1) Strengthen the squad.
            foreach (var club in ai)
            {
                var squad = squads[club.Id];
                if (squad.Count == 0 || squad.Count >= c.MaxSquadSize) continue;
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
                        Transfers.Squads.Leave(weakest); squad.Remove(weakest); bill[club.Id] -= weakest.Wage;
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
            foreach (var o in report.Outcomes)
            {
                var table = o.Tables.TryGetValue("League", out var t) ? t : o.Tables.TryGetValue("Annual", out var a) ? a
                          : o.Tables.Values.SelectMany(x => x).OrderByDescending(r => r.Points).ThenByDescending(r => r.GoalDifference).ToList();
                for (int i = 0; i < table.Count; i++) rows[table[i].ClubId] = (i + 1, table.Count, o.CompetitionId, table[i]);
                foreach (var id in o.Titles.Values.Distinct()) Mood(w, id, fm.Title);
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

                var income = Money.Finance.SeasonIncome(club, key, pos, teams, SeasonSimulator.HomeLeagueMatches(key, teams, _d), f);
                double wages = w.Players.Where(p => p.ClubId == club.Id).Sum(p => (double)p.Wage);
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
        /// AI clubs offer to players they still want (PROPOSAL rule in finance.json) at the expected wage;
        /// the player accepts with the approved signing/renewal chance. The user's club negotiates in the UI;
        /// headless runs use the same AI rule for it.
        /// </summary>
        private void RenewContracts(GameWorld w, GameRandom rng, SeasonReport report)
        {
            var f = _d.Finance; var rc = f.Renewal;
            foreach (var club in w.Clubs)
            {
                var squad = w.Players.Where(p => p.ClubId == club.Id).ToList();
                if (squad.Count == 0) continue;
                double median = squad.Select(Rating).OrderBy(x => x).ElementAt((int)System.Math.Min(squad.Count - 1, System.Math.Floor(rc.AiQualityPercentile * squad.Count)));
                foreach (var p in squad.Where(p => p.ContractEndYear <= w.SeasonStartYear))
                {
                    int age = w.SeasonStartYear - p.BirthYear;
                    double rating = Rating(p);
                    bool wanted = age <= rc.AiOfferYoungAge || (age <= rc.AiOfferMaxAge && rating >= median);
                    int years = rng.NextInt(rc.YearsMin, rc.YearsMax);
                    double value = MarketValue.Eur(rating, 6.5, age, p.Potential, years, _d.MarketValue);
                    double expected = Money.Finance.ExpectedWage(value, w.MoneyKey(club), f);
                    // The AI offers what he expects (a Businessman expects 20% more); the personality still changes his answer.
                    double offer = p.PersonalityId == "PER-BUSINESSMAN" ? expected * Money.Contracts.BusinessmanWageFactor : expected;
                    if (!wanted) { Transfers.Squads.Leave(p); report.LeftAtContractEnd++; report.NotOfferedRenewal++; }
                    else if (rng.Chance(Money.Contracts.RenewalChance(p.PersonalityId, offer, expected, years, _d.Probability)))
                    {
                        p.ContractEndYear = w.SeasonStartYear + years;
                        p.Wage = (long)System.Math.Round(offer);
                        report.Renewed++;
                    }
                    else { Transfers.Squads.Leave(p); report.LeftAtContractEnd++; report.RefusedRenewal++; }
                }
            }
        }

        /// <summary>AI default until coaches exist: one coach per group (GK / defence / midfield / attack), moderate regime.</summary>
        private void Train(GameWorld w)
        {
            var c = _d.Development;
            var arch = _d.Archetypes.ToDictionary(a => a.Id);
            foreach (var club in w.Clubs)
            {
                var squad = w.Players.Where(p => p.ClubId == club.Id).ToList();
                var fac = club.Facilities[Facility.TrainingGround];
                int effective = (int)System.Math.Round(fac.Level * (0.6 + 0.4 * fac.Condition / 100));
                var groups = squad.GroupBy(p => CoachGroup(p.MainPosition)).ToDictionary(g => g.Key, g => g.Count());
                for (int week = 0; week < c.TrainingWeeksPerSeason; week++)
                    foreach (var p in squad)
                        Development.TrainWeek(p, w.SeasonStartYear - p.BirthYear, arch[p.ArchetypeId], c.DefaultCoachQuality, c.DefaultRegime,
                            groups[CoachGroup(p.MainPosition)], effective, c.FormNeutral, _d);
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
            foreach (var o in outcomes)
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
