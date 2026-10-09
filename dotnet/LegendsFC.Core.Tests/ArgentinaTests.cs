using System;
using System.Linq;
using LegendsFC.Core.Season;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Xunit;

public class ArgentinaTests
{
    private static readonly Lazy<GameWorld> World = new Lazy<GameWorld>(() => new WorldGenerator(TestData.Data).Generate(31));
    private static CompetitionOutcome Play(string id, ulong seed) =>
        new SeasonSimulator(TestData.Data).PlaySeason(World.Value, new GameRandom(seed)).Single(o => o.CompetitionId == id);

    [Fact]
    public void FirstDivision_TwoZonesOf15_16MatchesPerTournament()
    {
        var o = Play("ARG-1", 1);
        foreach (var t in new[] { "Apertura A", "Apertura B", "Clausura A", "Clausura B" })
        {
            Assert.Equal(15, o.Tables[t].Count);
            Assert.All(o.Tables[t], r => Assert.Equal(16, r.Played));
        }
        Assert.Empty(o.Tables["Apertura A"].Select(r => r.ClubId).Intersect(o.Tables["Apertura B"].Select(r => r.ClubId)));
        Assert.Equal(30, o.Tables["Annual"].Count);
        Assert.All(o.Tables["Annual"], r => Assert.Equal(32, r.Played));
    }

    [Fact]
    public void FirstDivision_PlayoffsCrownAperturaAndClausuraChampions_AndTwoGoDown()
    {
        var o = Play("ARG-1", 2);
        Assert.Equal(2 * 15, o.KnockoutMatches.Count); // 2 tournaments × (8 + 4 + 2 + 1)
        foreach (var title in new[] { "Apertura", "Clausura" })
        {
            var tournament = o.Tables[title + " A"].Take(8).Concat(o.Tables[title + " B"].Take(8)).Select(r => r.ClubId);
            Assert.Contains(o.Titles[title], tournament); // only top-8 clubs can win
        }
        Assert.Equal(o.Tables["Annual"][0].ClubId, o.Titles["League"]);
        Assert.Equal(2, o.Relegated.Distinct().Count());
        Assert.Contains(o.Tables["Annual"].Last().ClubId, o.Relegated);
    }

    [Fact]
    public void SecondDivision_ZonesOf18_FinalAndReducidoPromoteTwo()
    {
        var o = Play("ARG-2", 3);
        Assert.Equal(18, o.Tables["Zone A"].Count);
        Assert.All(o.Tables["Zone A"].Concat(o.Tables["Zone B"]), r => Assert.Equal(34, r.Played));
        Assert.Equal(2, o.Promoted.Distinct().Count());
        var zoneWinners = new[] { o.Tables["Zone A"][0].ClubId, o.Tables["Zone B"][0].ClubId };
        Assert.Contains(o.Titles["Champion"], zoneWinners);
        var eligible = o.Tables["Zone A"].Take(8).Concat(o.Tables["Zone B"].Take(8)).Select(r => r.ClubId);
        Assert.Contains(o.Titles["Reducido"], eligible);
        Assert.NotEqual(o.Titles["Champion"], o.Titles["Reducido"]);
        Assert.Equal(1 + 7 + 4 + 2 + 1, o.KnockoutMatches.Count); // final + reducido rounds
        Assert.Empty(o.Relegated);
    }

    [Fact]
    public void BalancedPromotionAndRelegation()
    {
        var all = new SeasonSimulator(TestData.Data).PlaySeason(World.Value, new GameRandom(4));
        int Up(string id) => all.Single(o => o.CompetitionId == id).Promoted.Count;
        int Down(string id) => all.Single(o => o.CompetitionId == id).Relegated.Count;
        foreach (var c in new[] { "ENG", "ESP", "BRA", "ARG" }) Assert.Equal(Down(c + "-1"), Up(c + "-2"));
    }
}
