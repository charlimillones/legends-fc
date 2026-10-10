using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Season
{
    /// <summary>data/config/calendar.json.</summary>
    public sealed class CalendarConfig
    {
        public int WeeksPerSeason = 52, SummerWindowStartWeek = 1, FirstMatchWeek = 9, LastMatchWeek = 50, WinterWindowStartWeek = 27, FirstTrainingWeek = 9;
    }

    /// <summary>The season in progress (saved). Week 0 = not started; 1..52 = the week just played.</summary>
    public sealed class CalendarState
    {
        public int Week;
        public List<CompetitionRun> Runs = new List<CompetitionRun>();
        /// <summary>Competition id → the week each of its rounds is played in.</summary>
        public Dictionary<string, List<int>> RoundWeeks = new Dictionary<string, List<int>>();
        public bool SeasonOver;
        /// <summary>Club seeds frozen at the start of the season (cup draws use them, so a reloaded season draws the same).</summary>
        public Dictionary<string, ClubSeed> Seeds = new Dictionary<string, ClubSeed>();
    }

    /// <summary>What happened in one week (for the inbox, reports and tests).</summary>
    public sealed class WeekReport
    {
        public int Week;
        public int MatchesPlayed;
        public bool Trained, WindowOpen;
        public List<MatchResult> UserMatches = new List<MatchResult>();
        /// <summary>The user's players back from injury this week, and those recovering ahead of schedule.</summary>
        public List<string> Recovered = new List<string>(), AheadOfSchedule = new List<string>();
        /// <summary>Average energy of club players after this week's matches, before the weekly rest (balancing).</summary>
        public double EnergyBeforeRest;
        /// <summary>The user's scout reports filed this week.</summary>
        public List<Model.ScoutReport> ScoutReports = new List<Model.ScoutReport>();
    }

    /// <summary>
    /// Plays the season week by week: transfer windows day by day, league rounds spread over the match weeks,
    /// weekly training. End-of-season work (money, promotion, contracts, academy) stays in SeasonCycle.
    /// </summary>
    public sealed class SeasonCalendar
    {
        private readonly GameData _d;
        public SeasonCalendar(GameData d) { _d = d; }

        /// <summary>Sets up the season's competitions and their schedule (week 0 → ready for week 1).</summary>
        public void Start(GameWorld w, GameRandom rng)
        {
            var cal = w.Calendar; var c = _d.Calendar;
            cal.Week = 0; cal.SeasonOver = false;
            cal.Runs = new SeasonSimulator(_d).NewRuns(w, rng);
            cal.RoundWeeks = new Dictionary<string, List<int>>();
            foreach (var run in cal.Runs)
                cal.RoundWeeks[run.CompetitionId] = Spread(run.PlannedRounds, c.FirstMatchWeek, c.LastMatchWeek);

            // Cups (Oct 9): entrants from last season's results, seeds frozen now, rounds in the weeks set in cups.json.
            cal.Seeds = Qualification.Seeds(w);
            var entrants = Qualification.Resolve(w, _d);
            foreach (var def in _d.Cups.Cups)
            {
                var e = entrants[def.Id];
                var run = new CompetitionRun
                {
                    CompetitionId = def.Id, Seed = rng.NextUInt64(), Clubs = e.All,
                    Pools = new Dictionary<string, List<string>> { ["Direct"] = e.Direct, ["Qualifying"] = e.Qualifying, ["Late"] = e.Late },
                    Info = cal.Seeds,
                };
                run.PlannedRounds = CompetitionRun.CountRounds(def.Id, run.Clubs, run.Seed, _d, cal.Seeds, run.Pools);
                cal.Runs.Add(run);
                cal.RoundWeeks[def.Id] = CupWeeks(def, run.PlannedRounds);
            }
        }

        /// <summary>Rounds spread evenly from the first to the last week.</summary>
        public static List<int> Spread(int rounds, int first, int last)
        {
            int weeks = last - first + 1;
            return Enumerable.Range(0, rounds).Select(i => first + (int)((long)i * weeks / System.Math.Max(1, rounds))).ToList();
        }

        /// <summary>
        /// A cup's weeks from cups.json. If the draw needs fewer rounds than weeks listed (fewer early rounds this season),
        /// the last weeks are used so the final keeps its date; if it needs more, the rounds are spread over the same span.
        /// </summary>
        public static List<int> CupWeeks(CupDef def, int rounds)
        {
            if (def.SpreadWeeks.Count == 2) return Spread(rounds, def.SpreadWeeks[0], def.SpreadWeeks[1]);
            if (rounds <= def.Weeks.Count) return def.Weeks.Skip(def.Weeks.Count - rounds).ToList();
            return Spread(rounds, def.Weeks.First(), def.Weeks.Last());
        }

        /// <summary>Plays the next week. Returns false once the season's last week has been played.</summary>
        public WeekReport PlayWeek(GameWorld w, GameRandom rng)
        {
            var cal = w.Calendar; var c = _d.Calendar; var m = w.Market;
            if (cal.SeasonOver) return null;
            if (cal.Runs.Count == 0) Start(w, rng);
            cal.Week++;
            var report = new WeekReport { Week = cal.Week };

            // Transfer windows, day by day (the market also counts the days when no window is open).
            if (cal.Week == c.SummerWindowStartWeek) Transfers.AiMarket.OpenWindow(w, "summer", rng, _d);
            if (cal.Week == c.WinterWindowStartWeek) Transfers.AiMarket.OpenWindow(w, "winter", rng, _d);
            report.WindowOpen = m.WindowOpen;
            for (int day = 0; day < 7; day++) Transfers.AiMarket.AdvanceDay(w, rng, _d);

            // This week's rounds: real teams (squad and tactics, Oct 9). AI clubs set training regimes first.
            var swM = System.Diagnostics.Stopwatch.StartNew();
            Squad.Fitness.AiRegimes(w, _d);
            var ctx = new Squad.MatchEngine.Context { World = w, Data = _d, Ratings = new Squad.RatingTable(_d), Rng = rng };
            foreach (var run in cal.Runs)
            {
                run.Info = cal.Seeds;
                var weeks = cal.RoundWeeks[run.CompetitionId];
                while (!run.Finished && run.PlayedRounds.Count < weeks.Count && weeks[run.PlayedRounds.Count] <= cal.Week)
                {
                    var round = run.Next(_d);
                    if (round == null) break;
                    Squad.Discipline.BeforeRound(w, run, round, _d);
                    var results = RoundPlayer.Sim(round, run.CompetitionId, ctx);
                    run.Record(results, _d);
                    SendExports(cal, run);
                    report.MatchesPlayed += results.Count;
                    if (w.UserClubId != null) report.UserMatches.AddRange(results.Where(r => r.Home == w.UserClubId || r.Away == w.UserClubId));
                }
            }

            // Facilities: weekly wear; AI clubs repair and upgrade (confirmed Oct 9).
            Facilities.FacilityRules.WeeklyWear(w, rng, _d.FacilityRules);
            Facilities.FacilityRules.AiWeekly(w, _d);

            m.Stats.TryGetValue("ms:matches", out int msm); m.Stats["ms:matches"] = msm + (int)swM.ElapsedMilliseconds;
            // Rest and recovery by regime; injuries heal (Oct 9).
            double sumE = 0; int nE = 0;
            foreach (var p in w.Players) if (p.ClubId != null && !p.Injured) { sumE += p.Energy; nE++; }
            report.EnergyBeforeRest = nE == 0 ? 100 : sumE / nE;
            Squad.Fitness.WeekEnd(w, _d, rng, report);
            report.ScoutReports = Scouting.Scouts.Week(w, _d);   // own random stream

            // Weekly training (40 weeks from the first training week).
            if (cal.Week >= c.FirstTrainingWeek && cal.Week < c.FirstTrainingWeek + _d.Development.TrainingWeeksPerSeason)
            {
                SeasonCycle.TrainOneWeek(w, _d);
                m.RatingCache = null; m.SquadsChanged();
                report.Trained = true;
            }

            if (cal.Week >= c.WeeksPerSeason)
            {
                // Safety: any rounds left (none with the standard schedule) are played now.
                foreach (var run in cal.Runs)
                    for (var round = run.Next(_d); round != null; round = run.Next(_d))
                    {
                        Squad.Discipline.BeforeRound(w, run, round, _d);
                        run.Record(RoundPlayer.Sim(round, run.CompetitionId, ctx), _d);
                        SendExports(cal, run);
                    }
                cal.SeasonOver = true;
            }
            return report;
        }

        /// <summary>Clubs a competition sends on (qualifying losers, 3rd-placed clubs) reach the competitions waiting for them.</summary>
        private void SendExports(CalendarState cal, CompetitionRun from)
        {
            foreach (var kv in from.Exports)
            {
                string key = from.CompetitionId + ":" + kv.Key;
                foreach (var to in cal.Runs)
                {
                    if (to == from || to.Inputs.ContainsKey(key)) continue;
                    var def = _d.Cups.Cups.FirstOrDefault(c => c.Id == to.CompetitionId);
                    if (def != null && def.ImportCount(key) > 0) to.Inputs[key] = kv.Value.ToList();
                }
            }
        }

        public List<CompetitionOutcome> Outcomes(GameWorld w) => w.Calendar.Runs.Select(r => r.Outcome).ToList();
    }
}
