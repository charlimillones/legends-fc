using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Season;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Newtonsoft.Json;
using Xunit;

public class CalendarTests
{
    private static LegendsFC.Core.Data.GameData D => TestData.Data;

    [Fact]
    public void FiftyTwoWeeks_WindowsInTheRightWeeks_AllRoundsPlayedByWeek50()
    {
        var w = new WorldGenerator(D).Generate(77);
        var cal = new SeasonCalendar(D); var rng = new GameRandom(78);
        cal.Start(w, rng);
        var weeks = new List<WeekReport>();
        while (!w.Calendar.SeasonOver) weeks.Add(cal.PlayWeek(w, rng));
        Assert.Equal(52, weeks.Count);
        Assert.All(weeks.Where(x => x.Week <= 8), x => Assert.True(x.WindowOpen));
        Assert.All(weeks.Where(x => x.Week >= 9 && x.Week <= 26), x => Assert.False(x.WindowOpen));
        Assert.All(weeks.Where(x => x.Week >= 27 && x.Week <= 30), x => Assert.True(x.WindowOpen));
        Assert.All(weeks.Where(x => x.Week >= 31), x => Assert.False(x.WindowOpen));
        Assert.All(weeks.Where(x => x.Week <= 8 || x.Week > 50), x => Assert.Equal(0, x.MatchesPlayed));
        Assert.Equal(40, weeks.Count(x => x.Trained));
        Assert.All(w.Calendar.Runs, r => { Assert.True(r.Finished); Assert.Equal(r.PlannedRounds, r.PlayedRounds.Count); });
        // At most two rounds of a league in one week (midweek games when a league has more rounds than weeks).
        foreach (var kv in w.Calendar.RoundWeeks)
            Assert.True(kv.Value.GroupBy(x => x).Max(g => g.Count()) <= 2, kv.Key);
        Assert.Equal(46, w.Calendar.Runs.Single(r => r.CompetitionId == "ENG-2").PlannedRounds);
        Assert.Equal(38, w.Calendar.Runs.Single(r => r.CompetitionId == "ENG-1").PlannedRounds);
    }

    [Fact]
    public void AHalfPlayedSeasonSurvivesASave()
    {
        // Same seeds: one world plays straight through; the other is saved to JSON at week 20 and reloaded.
        string Finish(bool saveHalfway)
        {
            var w = new WorldGenerator(D).Generate(5);
            var cal = new SeasonCalendar(D); var rng = new GameRandom(6);
            cal.Start(w, rng);
            for (int i = 0; i < 20; i++) cal.PlayWeek(w, rng);
            if (saveHalfway)
            {
                var json = JsonConvert.SerializeObject(w.Calendar);
                w.Calendar = JsonConvert.DeserializeObject<CalendarState>(json);
            }
            while (!w.Calendar.SeasonOver) cal.PlayWeek(w, rng);
            return string.Join("|", cal.Outcomes(w).Select(o => o.CompetitionId + ":" + string.Join(",", o.Titles.Select(t => t.Key + "=" + t.Value))
                                                                 + ":" + string.Join(",", o.Relegated)));
        }
        Assert.Equal(Finish(false), Finish(true));
    }

    [Fact]
    public void UserMatchesAreReportedEachWeek()
    {
        var w = new WorldGenerator(D).Generate(9);
        w.UserClubId = w.Clubs.First(c => w.ClubLeague[c.Id] == "ENG-1").Id;
        var cal = new SeasonCalendar(D); var rng = new GameRandom(10);
        cal.Start(w, rng);
        int mine = 0;
        while (!w.Calendar.SeasonOver) mine += cal.PlayWeek(w, rng).UserMatches.Count;
        Assert.Equal(38, mine);
    }
}
