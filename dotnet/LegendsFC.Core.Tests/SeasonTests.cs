using System;
using System.Diagnostics;
using System.Linq;
using LegendsFC.Core.Season;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Xunit;

public class FixtureTests
{
    [Theory]
    [InlineData(20, 2)]
    [InlineData(24, 2)]
    [InlineData(30, 1)]
    [InlineData(5, 2)]   // odd count → byes
    public void EveryPairMeetsOncePerLeg_AndPlaysOncePerRound(int clubs, int legs)
    {
        var ids = Enumerable.Range(1, clubs).Select(i => "C" + i).ToList();
        var rounds = Fixtures.RoundRobin(ids, legs, new GameRandom(1));
        int n = clubs % 2 == 0 ? clubs : clubs + 1;
        Assert.Equal((n - 1) * legs, rounds.Count);
        foreach (var r in rounds)
        {
            var teams = r.SelectMany(f => new[] { f.Home, f.Away }).ToList();
            Assert.Equal(teams.Count, teams.Distinct().Count());
        }
        var pairs = rounds.SelectMany(r => r).GroupBy(f => string.Join("|", new[] { f.Home, f.Away }.OrderBy(x => x, StringComparer.Ordinal)));
        Assert.Equal(clubs * (clubs - 1) / 2, pairs.Count());
        Assert.All(pairs, g => Assert.Equal(legs, g.Count()));
        if (legs == 2) Assert.All(pairs, g => Assert.NotEqual(g.First().Home, g.Last().Home)); // home and away once each
    }

    [Fact]
    public void HomeGamesAreBalanced()
    {
        var ids = Enumerable.Range(1, 20).Select(i => "C" + i).ToList();
        var homes = Fixtures.RoundRobin(ids, 1, new GameRandom(3)).SelectMany(r => r).GroupBy(f => f.Home).ToDictionary(g => g.Key, g => g.Count());
        Assert.All(ids, id => Assert.InRange(homes.TryGetValue(id, out var h) ? h : 0, 8, 11));
    }
}

public class SeasonSimTests
{
    // A season changes the world (energy, injuries, bans, stats since Oct 9), so every test plays a fresh copy.
    private static GameWorld Fresh() => new WorldGenerator(TestData.Data).Generate(4242);

    [Fact]
    public void TableAddsUp()
    {
        var tables = new SeasonSimulator(TestData.Data).PlayLeagues(Fresh(), new GameRandom(5));
        var eng = tables["ENG-1"];
        Assert.Equal(20, eng.Count);
        Assert.All(eng, r => Assert.Equal(38, r.Played));
        Assert.Equal(eng.Sum(r => r.GoalsFor), eng.Sum(r => r.GoalsAgainst));
        Assert.Equal(eng.Sum(r => r.Won), eng.Sum(r => r.Lost));
        Assert.All(tables["ARG-1"], r => Assert.Equal(32, r.Played)); // annual table: Apertura + Clausura, 16 each
    }

    [Fact]
    public void SameSeedSameTables()
    {
        var sim = new SeasonSimulator(TestData.Data);
        var a = sim.PlayLeagues(Fresh(), new GameRandom(9))["ESP-1"].Select(r => r.ClubId + r.Points);
        var b = sim.PlayLeagues(Fresh(), new GameRandom(9))["ESP-1"].Select(r => r.ClubId + r.Points);
        Assert.Equal(a, b);
    }

    /// <summary>DaiVinci balancing target: title winners ≈80-95 points over 38 games; a realistic draw rate.</summary>
    [Fact]
    public void BalancingTarget_TitlePointsAndDraws()
    {
        var sim = new SeasonSimulator(TestData.Data);
        var champs = Enumerable.Range(0, 20).Select(s => sim.PlayLeagues(Fresh(), new GameRandom((ulong)s))["ENG-1"]).ToList();
        double avgTitle = champs.Average(t => t[0].Points);
        double drawRate = champs.Average(t => t.Sum(r => r.Drawn) / (double)t.Sum(r => r.Played));
        Assert.InRange(avgTitle, 78, 97);
        Assert.InRange(drawRate, 0.18, 0.32);
    }

    [Fact]
    public void AllLeaguesSimulateQuickly()
    {
        var sw = Stopwatch.StartNew();
        new SeasonSimulator(TestData.Data).PlayLeagues(Fresh(), new GameRandom(1));
        Assert.True(sw.ElapsedMilliseconds < 2000, sw.ElapsedMilliseconds + " ms");
    }
}
