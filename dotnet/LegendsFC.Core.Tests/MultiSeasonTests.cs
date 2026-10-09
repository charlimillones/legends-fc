using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using LegendsFC.Core.Season;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Xunit;
using Xunit.Abstractions;

public class DevelopmentTests
{
    [Fact]
    public void CeilingFollowsTheConfirmedAgeDecayCurve()
    {
        var p = new Player { Potential = 90, DeclineStartAge = 30, RetireAge = 36, DeclineAmount = 20 };
        Assert.Equal(90, Development.Ceiling(p, 29), 6);
        Assert.Equal(90, Development.Ceiling(p, 30), 6);
        Assert.Equal(90 - 20 * Math.Pow(3 / 6.0, 1.5), Development.Ceiling(p, 33), 6); // slow at first...
        Assert.Equal(70, Development.Ceiling(p, 36), 6);                              // ...full drop by retirement
    }

    [Theory]
    [InlineData(6.5, 1.0)]
    [InlineData(7.5, 1.5)]
    [InlineData(5.5, 0.5)]
    [InlineData(9.0, 1.5)]
    [InlineData(4.0, 0.0)]
    public void FormFactor(double form, double expected) => Assert.Equal(expected, Development.FormFactor(form, TestData.Data.Development), 6);

    [Fact]
    public void YoungsterGrowsAndNeverPassesPotential()
    {
        var d = TestData.Data;
        var w = new WorldGenerator(d).Generate(5);
        var kid = w.Players.Where(p => 2026 - p.BirthYear <= 18 && p.Potential - Rating(p) > 15).First();
        var arch = d.Archetype(kid.ArchetypeId);
        double before = Rating(kid);
        for (int season = 0; season < 6; season++)
            for (int week = 0; week < 40; week++)
                Development.TrainWeek(kid, 18 + season, arch, 1.3, "heavy", 3, 6, 6.5, d);
        double after = Rating(kid);
        Assert.True(after > before + 4, $"{before:F1} -> {after:F1}");
        Assert.True(after <= kid.Potential + 1, $"{after:F1} vs potential {kid.Potential}");
    }

    [Fact]
    public void BadFormLowersTheRating()
    {
        var d = TestData.Data;
        var w = new WorldGenerator(d).Generate(6);
        var p = w.Players.First(x => 2026 - x.BirthYear == 26);
        double before = Rating(p);
        for (int week = 0; week < 40; week++)
            Development.TrainWeek(p, 26, d.Archetype(p.ArchetypeId), 1.0, "moderate", 8, 5, 4.5, d);
        Assert.True(Rating(p) < before, $"{before:F1} -> {Rating(p):F1}");
    }

    private static double Rating(Player p) => PositionRating.Base(p.Attributes, p.MainPosition, TestData.Data.PositionRatings);
}

public class MultiSeasonTests
{
    private readonly ITestOutputHelper _out;
    public MultiSeasonTests(ITestOutputHelper output) { _out = output; }

    private static double Rating(Player p) => PositionRating.Base(p.Attributes, p.MainPosition, TestData.Data.PositionRatings);

    [Fact]
    public void TenSeasons_StructureStaysValid_AndTheWorldDoesNotInflate()
    {
        var d = TestData.Data;
        var w = new WorldGenerator(d).Generate(2026);
        var cycle = new SeasonCycle(d);
        var rng = new GameRandom(77);
        double LeagueAvg(string id) => w.Clubs.Where(c => w.ClubLeague[c.Id] == id)
            .Average(c => w.Players.Where(p => p.ClubId == c.Id).Select(Rating).OrderByDescending(x => x).Take(11).Average());
        double startEng = LeagueAvg("ENG-1");
        var sizes = d.Competitions.Where(c => c.Type == CompetitionType.League).ToDictionary(c => c.Id, c => c.Teams);
        var champions = new HashSet<string>();
        var sw = Stopwatch.StartNew();

        for (int s = 0; s < 10; s++)
        {
            var report = cycle.Advance(w, rng);
            champions.Add(report.Outcomes.Single(o => o.CompetitionId == "ENG-1").Titles["Champion"]);
            foreach (var kv in sizes) Assert.Equal(kv.Value, w.ClubLeague.Count(x => x.Value == kv.Key));
            foreach (var club in w.Clubs)
            {
                int n = w.Players.Count(p => p.ClubId == club.Id);
                Assert.InRange(n, d.Development.MinSquadSize, d.Development.MaxSquadSize);
                Assert.True(w.Players.Count(p => p.ClubId == club.Id && p.MainPosition == Position.GK) >= d.Development.AiMinGoalkeepers);
            }
            Assert.InRange(report.AcademyGraduates, 2 * w.Clubs.Count, 4 * w.Clubs.Count);
            _out.WriteLine($"{report.SeasonStartYear}/{report.SeasonStartYear + 1 - 2000}: ENG-1 avg XI {LeagueAvg("ENG-1"):F1}, retired {report.Retired}, academy {report.AcademyGraduates}, released {report.Released}, free-agent signings {report.FreeAgentSignings}");
        }
        Assert.Equal(2036, w.SeasonStartYear);
        Assert.InRange(LeagueAvg("ENG-1") - startEng, -4.0, 4.0);
        Assert.All(w.Players.Where(p => p.Retired), p => Assert.True(2036 - p.BirthYear >= p.RetireAge || p.RetireAge <= 2036 - p.BirthYear));
        Assert.DoesNotContain(w.Players, p => !p.Retired && p.ClubId != null && 2036 - p.BirthYear >= p.RetireAge);
        Assert.True(champions.Count >= 2, "same champion every season");
        Assert.True(sw.Elapsed.TotalSeconds < 120, sw.Elapsed.TotalSeconds + " s");
        _out.WriteLine($"10 seasons in {sw.Elapsed.TotalSeconds:F1} s; {champions.Count} different ENG-1 champions");
    }

    [Fact]
    public void SameSeedSameFuture()
    {
        var d = TestData.Data;
        string Run()
        {
            var w = new WorldGenerator(d).Generate(11);
            var cycle = new SeasonCycle(d); var rng = new GameRandom(12);
            cycle.Advance(w, rng); cycle.Advance(w, rng);
            return string.Join(",", w.Players.Where(p => p.ClubId != null).Take(200).Select(p => p.Id + ":" + Rating(p).ToString("F2")));
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void UserClubIsNeverTrimmedOrToppedUp()
    {
        var d = TestData.Data;
        var w = new WorldGenerator(d).Generate(8);
        var club = w.Clubs.First();
        w.UserClubId = club.Id;
        var before = w.Players.Where(p => p.ClubId == club.Id).Select(p => p.Id).ToHashSet();
        new SeasonCycle(d).Advance(w, new GameRandom(3));
        var nowIds = w.Players.Where(p => p.ClubId == club.Id).Select(p => p.Id).ToHashSet();
        int retired = w.Players.Count(p => before.Contains(p.Id) && p.Retired);
        var left = w.Players.Where(p => before.Contains(p.Id) && !p.Retired && p.ClubId != club.Id).ToList();
        int academy = nowIds.Count(id => !before.Contains(id));
        Assert.InRange(academy, 2, 4);                                   // only the random intake joins
        Assert.Equal(before.Count - retired - left.Count + academy, nowIds.Count);   // nobody released, nobody signed
        // The only other way out: his contract ran out and the renewal talks failed (Oct 9).
        // (He may have signed for another club in the summer window since.)
        Assert.All(left, p => { Assert.Contains(club.Id, p.FormerClubIds); Assert.True(p.ClubId == null || p.ContractEndYear > w.SeasonStartYear); });
    }
}
