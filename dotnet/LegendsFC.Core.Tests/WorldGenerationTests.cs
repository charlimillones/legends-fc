using System;
using System.Diagnostics;
using System.Linq;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using LegendsFC.Core.World;
using Newtonsoft.Json;
using Xunit;

public class WorldGenerationTests
{
    private static readonly Lazy<GameWorld> Shared = new Lazy<GameWorld>(() => new WorldGenerator(TestData.Data).Generate(12345));
    private static GameWorld W => Shared.Value;

    [Fact]
    public void LeagueSizesAreTheRealOnes()
    {
        int Count(string league) => W.ClubLeague.Count(kv => kv.Value == league);
        Assert.Equal(20, Count("ENG-1")); Assert.Equal(24, Count("ENG-2"));
        Assert.Equal(20, Count("ESP-1")); Assert.Equal(22, Count("ESP-2"));
        Assert.Equal(20, Count("BRA-1")); Assert.Equal(20, Count("BRA-2"));
        Assert.Equal(30, Count("ARG-1")); Assert.Equal(36, Count("ARG-2"));
    }

    [Fact]
    public void CupOnlyCountriesHaveFiveClubsEach()
    {
        foreach (var c in new[] { "MEX", "USA", "POR", "NED", "URU", "COL" })
            Assert.Equal(5, W.Clubs.Count(x => x.CountryId == c && W.ClubLeague[x.Id] == null));
        Assert.Equal(222, W.Clubs.Count);
    }

    [Fact]
    public void IdsAndClubNamesAreUnique()
    {
        Assert.Equal(W.Clubs.Count, W.Clubs.Select(c => c.Id).Distinct().Count());
        Assert.Equal(W.Clubs.Count, W.Clubs.Select(c => c.Name).Distinct().Count());
        Assert.Equal(W.Players.Count, W.Players.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void EverySquadHas25PlusAcademy_AndAtLeastTwoKeepers()
    {
        foreach (var club in W.Clubs)
        {
            var squad = W.Players.Where(p => p.ClubId == club.Id).ToList();
            Assert.Equal(30, squad.Count);
            Assert.True(squad.Count(p => p.MainPosition == Position.GK) >= 3);
        }
    }

    [Fact]
    public void SameSeedSameWorld_DifferentSeedDifferentWorld()
    {
        string a = JsonConvert.SerializeObject(new WorldGenerator(TestData.Data).Generate(777));
        string b = JsonConvert.SerializeObject(new WorldGenerator(TestData.Data).Generate(777));
        string c = JsonConvert.SerializeObject(new WorldGenerator(TestData.Data).Generate(778));
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void PersonalityShareIsInsideConfirmedRange()
    {
        double share = W.Players.Count(p => p.PersonalityId != null) / (double)W.Players.Count;
        Assert.InRange(share, 0.40, 0.50);
    }

    [Fact]
    public void AKeeperRules()
    {
        var keepers = W.Players.Where(p => p.ArchetypeId == "ARC-RARE-A-KEEPER").ToList();
        Assert.All(keepers, k =>
        {
            Assert.NotEqual(Position.GK, k.MainPosition);
            Assert.Equal("PER-A-KEEPER", k.PersonalityId);
            Assert.InRange(k.Attributes[Attr.Reflexes], 45, 60);
        });
        Assert.DoesNotContain(W.Players, p => p.PersonalityId == "PER-A-KEEPER" && p.ArchetypeId != "ARC-RARE-A-KEEPER");
        // 1 in 1,000 outfield players across 10 seeds: expect roughly 6 per world
        var gen = new WorldGenerator(TestData.Data);
        double avg = Enumerable.Range(1, 10).Average(s => gen.Generate((ulong)s).Players.Count(p => p.ArchetypeId == "ARC-RARE-A-KEEPER"));
        double outfield = W.Players.Count(p => p.MainPosition != Position.GK);
        Assert.InRange(avg, outfield / 1000 * 0.5, outfield / 1000 * 1.6);
    }

    [Fact]
    public void ArchetypesMatchPositionGroups()
    {
        var byId = TestData.Data.Archetypes.ToDictionary(a => a.Id);
        Assert.All(W.Players, p =>
        {
            var a = byId[p.ArchetypeId];
            Assert.True(a.IsAnyOutfield || a.Group == Positions.GroupOf(p.MainPosition).ToString(), p.Id);
        });
    }

    [Fact]
    public void AgeDecayRangesFollowTheConfirmedRules()
    {
        Assert.All(W.Players, p =>
        {
            int age = W.SeasonStartYear - p.BirthYear;
            int extra = 0; // goalkeepers use the same ranges (Oct 8)
            Assert.True(p.RetireAge > age, p.Id);
            Assert.True(p.RetireAge - p.DeclineStartAge >= 2, p.Id);
            Assert.InRange(p.DeclineAmount, 15, 25);
            if (p.RetireAge <= 40 + extra) Assert.InRange(p.DeclineStartAge, 26, 32 + extra);
        });
    }

    [Fact]
    public void RatingsArePlausible()
    {
        var d = TestData.Data;
        double Rating(Player p) => PositionRating.Base(p.Attributes, p.MainPosition, d.PositionRatings);
        var seniors = W.Players.Where(p => W.SeasonStartYear - p.BirthYear >= 18).ToList();
        Assert.All(W.Players, p => Assert.True(p.Potential >= (int)Math.Round(Rating(p)) - 1, p.Id));
        double eng1 = W.Clubs.Where(c => W.ClubLeague[c.Id] == "ENG-1").Average(c => W.Players.Where(p => p.ClubId == c.Id).Average(Rating));
        double arg2 = W.Clubs.Where(c => W.ClubLeague[c.Id] == "ARG-2").Average(c => W.Players.Where(p => p.ClubId == c.Id).Average(Rating));
        Assert.True(eng1 > arg2 + 8, $"ENG-1 {eng1:F1} vs ARG-2 {arg2:F1}");
    }

    [Fact]
    public void SidedPlayersMostlyUseTheMatchingFoot()
    {
        var sided = W.Players.Where(p => Positions.SideOf(p.MainPosition) != Side.Central).ToList();
        var exempt = TestData.Data.Archetypes.Where(a => a.WrongFootExempt).Select(a => a.Id).ToHashSet();
        var normal = sided.Where(p => !exempt.Contains(p.ArchetypeId)).ToList();
        Assert.All(normal, p => Assert.False(OutOfPosition.IsWrongFoot(p.Foot, p.MainPosition), p.Id));
    }

    [Fact]
    public void GeneratesAWholeWorldQuickly()
    {
        var sw = Stopwatch.StartNew();
        new WorldGenerator(TestData.Data).Generate(99);
        Assert.True(sw.ElapsedMilliseconds < 3000, sw.ElapsedMilliseconds + " ms");
    }
}
