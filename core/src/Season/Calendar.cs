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
    }

    /// <summary>What happened in one week (for the inbox, reports and tests).</summary>
    public sealed class WeekReport
    {
        public int Week;
        public int MatchesPlayed;
        public bool Trained, WindowOpen;
        public List<MatchResult> UserMatches = new List<MatchResult>();
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
            int weeks = c.LastMatchWeek - c.FirstMatchWeek + 1;
            foreach (var run in cal.Runs)
                cal.RoundWeeks[run.CompetitionId] = Enumerable.Range(0, run.PlannedRounds)
                    .Select(i => c.FirstMatchWeek + (int)((long)i * weeks / System.Math.Max(1, run.PlannedRounds))).ToList();
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

            // This week's rounds.
            var sim = new SeasonSimulator(_d);
            Dictionary<string, double> strength = null;
            foreach (var run in cal.Runs)
            {
                var weeks = cal.RoundWeeks[run.CompetitionId];
                while (!run.Finished && run.PlayedRounds.Count < weeks.Count && weeks[run.PlayedRounds.Count] <= cal.Week)
                {
                    strength = strength ?? sim.Strengths(w);
                    var round = run.Next(_d);
                    if (round == null) break;
                    var results = RoundPlayer.Sim(round, id => strength[id], _d, rng);
                    run.Record(results, _d);
                    report.MatchesPlayed += results.Count;
                    if (w.UserClubId != null) report.UserMatches.AddRange(results.Where(r => r.Home == w.UserClubId || r.Away == w.UserClubId));
                }
            }

            // Facilities: weekly wear; AI clubs repair and upgrade (confirmed Oct 9).
            Facilities.FacilityRules.WeeklyWear(w, rng, _d.FacilityRules);
            Facilities.FacilityRules.AiWeekly(w, _d);

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
                        strength = strength ?? sim.Strengths(w);
                        run.Record(RoundPlayer.Sim(round, id => strength[id], _d, rng), _d);
                    }
                cal.SeasonOver = true;
            }
            return report;
        }

        public List<CompetitionOutcome> Outcomes(GameWorld w) => w.Calendar.Runs.Select(r => r.Outcome).ToList();
    }
}
