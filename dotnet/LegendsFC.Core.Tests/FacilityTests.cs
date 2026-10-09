using System;
using System.Linq;
using LegendsFC.Core.Facilities;
using LegendsFC.Core.Model;
using LegendsFC.Core.Season;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Xunit;
using Xunit.Abstractions;

/// <summary>Facility rules confirmed by Carlos on Oct 9 (facilities-draft.md v4).</summary>
public class FacilityTests
{
    private static LegendsFC.Core.Data.GameData D => TestData.Data;
    private static FacilityConfig C => D.FacilityRules;

    [Fact]
    public void FixedPrices_DoublingPerLevel_SameForEveryClub()
    {
        Assert.Equal(new long[] { 500_000, 1_000_000, 2_000_000, 4_000_000, 8_000_000, 15_000_000, 30_000_000, 60_000_000, 120_000_000 },
                     Enumerable.Range(2, 9).Select(l => FacilityRules.UpgradePrice(l, C)));
        Assert.Equal(240_500_000, Enumerable.Range(2, 9).Sum(l => FacilityRules.UpgradePrice(l, C)));
    }

    [Fact]
    public void UpgradesAreInstant_AndNeedTheMoney()
    {
        var club = new Club { Balance = 5_000_000 };
        club.Facilities[Facility.Stadium] = new FacilityState { Level = 4, Condition = 100 };
        Assert.Equal(UpgradeResult.Done, FacilityRules.Upgrade(club, Facility.Stadium, C));
        Assert.Equal(5, club.Facilities[Facility.Stadium].Level);
        Assert.Equal(1_000_000, club.Balance);
        Assert.Equal(UpgradeResult.NotEnoughMoney, FacilityRules.Upgrade(club, Facility.Stadium, C));
        Assert.Equal(5, club.Facilities[Facility.Stadium].Level);
        Assert.Equal(1_000_000, club.Balance);                 // nothing paid, never below zero
        club.Facilities[Facility.Stadium].Level = 10; club.Balance = 1_000_000_000;
        Assert.Equal(UpgradeResult.MaxLevel, FacilityRules.Upgrade(club, Facility.Stadium, C));
    }

    [Fact]
    public void RepairsArePricedFromTheLevel_NotTheClub()
    {
        var s = new FacilityState { Level = 10, Condition = 70 };
        Assert.Equal(30 * 0.002 * 120_000_000, FacilityRules.RepairCost(s, C));       // 30% at level 10 = €7.2M
        s.Level = 1; s.Condition = 0;
        Assert.Equal(100 * 0.002 * 250_000, FacilityRules.RepairCost(s, C));          // a full repair = 20% of the level price
        var rich = new Club { Balance = 1_000_000_000, Reputation = 99 };
        var poor = new Club { Balance = 1_000_000_000, Reputation = 5 };
        foreach (var c in new[] { rich, poor })
            foreach (Facility f in Enum.GetValues(typeof(Facility))) c.Facilities[f] = new FacilityState { Level = 6, Condition = 55 };
        Assert.Equal(FacilityRules.RepairAllCost(rich, C), FacilityRules.RepairAllCost(poor, C));
    }

    [Fact]
    public void RepairAll_PaysEverythingAtOnce_OrNothing()
    {
        var club = new Club { Balance = 100 };
        foreach (Facility f in Enum.GetValues(typeof(Facility))) club.Facilities[f] = new FacilityState { Level = 5, Condition = 50 };
        Assert.False(FacilityRules.RepairAll(club, C));
        Assert.Equal(100, club.Balance);
        Assert.Equal(50, FacilityRules.TotalCondition(club));
        long cost = FacilityRules.RepairAllCost(club, C);
        Assert.Equal(6 * 50 * 0.002 * 4_000_000, cost);
        club.Balance = cost;
        Assert.True(FacilityRules.RepairAll(club, C));
        Assert.Equal(0, club.Balance);
        Assert.Equal(100, FacilityRules.TotalCondition(club));
        Assert.Equal(cost, club.SpentOnFacilities);
    }

    [Fact]
    public void ConditionSetsTheWorkingLevel_AndTheEffects()
    {
        var s = new FacilityState { Level = 5, Condition = 100 };
        Assert.Equal(5, FacilityRules.WorkingLevel(s, C), 6);
        Assert.Equal(1.0, FacilityRules.Effect(s, C), 6);              // level 5 in full condition = today's stadium and store
        s.Condition = 50;
        Assert.Equal(4, FacilityRules.WorkingLevel(s, C), 6);          // works like 80% of its level
        s.Level = 10; s.Condition = 100;
        Assert.Equal(1.4, FacilityRules.Effect(s, C), 6);
        Assert.Equal(0.7, FacilityRules.RecoveryTime(s, C), 6);        // Medical Building: faster recovery
    }

    [Fact]
    public void WeeklyWear_About0Point5Percent_FasterWhenFansAreUnhappy()
    {
        var w = new WorldGenerator(D).Generate(3);
        foreach (var c in w.Clubs) { foreach (var s in c.Facilities.Values) s.Condition = 100; c.FanMood = 50; }
        var sad = w.Clubs[0]; sad.FanMood = 10;
        var rng = new GameRandom(4);
        for (int i = 0; i < 52; i++) FacilityRules.WeeklyWear(w, rng, C);
        double normal = w.Clubs.Skip(1).Average(c => 100 - FacilityRules.TotalCondition(c));
        Assert.InRange(normal, 24, 28);                                // ≈26% a season
        Assert.InRange(100 - FacilityRules.TotalCondition(sad), normal * 1.3, normal * 1.7);
    }

    [Fact]
    public void LevelDrops_OnlyBelow20Percent_AndResetTo50()
    {
        var w = new WorldGenerator(D).Generate(3);
        foreach (var c in w.Clubs) foreach (var s in c.Facilities.Values) { s.Level = 5; s.Condition = 60; }
        Assert.Empty(FacilityRules.LevelDrops(w, new GameRandom(1), C));
        foreach (var c in w.Clubs) foreach (var s in c.Facilities.Values) s.Condition = 10;
        var drops = FacilityRules.LevelDrops(w, new GameRandom(1), C);
        int total = w.Clubs.Count * 6;
        Assert.InRange(drops.Count, total * 0.45, total * 0.55);
        Assert.All(drops, x => { Assert.Equal(4, x.club.Facilities[x.f].Level); Assert.Equal(50, x.club.Facilities[x.f].Condition); });
    }

    [Fact]
    public void EveryFacilityHasAManager_WhoRetiresAndCostsAHandover()
    {
        var w = new WorldGenerator(D).Generate(3);
        Assert.All(w.Clubs.SelectMany(c => c.Facilities.Values), s =>
        {
            Assert.NotNull(s.Manager);
            Assert.False(string.IsNullOrWhiteSpace(s.Manager.Name));
            Assert.InRange(w.SeasonStartYear - s.Manager.BirthYear, 35, 64);
            Assert.InRange(s.Manager.RetireAge, 60, 70);
        });
        var club = w.Clubs[0]; var st = club.Facilities[Facility.ClubStore];
        st.Manager.BirthYear = w.SeasonStartYear - st.Manager.RetireAge; st.Condition = 90;
        var old = st.Manager;
        var changes = FacilityRules.ManagerHandovers(w, new GameRandom(2), D);
        Assert.Contains(changes, x => x.old == old);
        Assert.NotSame(old, st.Manager);
        Assert.InRange(st.Condition, 75, 85);                          // handover costs 5-15%
    }

    [Fact]
    public void StadiumAndStoreLevelsChangeIncome()
    {
        var w = new WorldGenerator(D).Generate(3);
        var club = w.Clubs.First(c => w.ClubLeague[c.Id] == "ENG-1");
        string key = w.MoneyKey(club);
        foreach (var s in club.Facilities.Values) { s.Level = 5; s.Condition = 100; }
        var mid = LegendsFC.Core.Money.Finance.SeasonIncome(club, key, 10, 20, 19, D.Finance, C);
        club.Facilities[Facility.Stadium].Level = 10; club.Facilities[Facility.ClubStore].Level = 10;
        var top = LegendsFC.Core.Money.Finance.SeasonIncome(club, key, 10, 20, 19, D.Finance, C);
        Assert.Equal(mid.Gate * 1.4, top.Gate, 0);
        Assert.Equal(mid.Store * 1.4, top.Store, 0);
        Assert.Equal(mid.Tv, top.Tv);
    }
}

public class FacilityBalancingTests
{
    private readonly ITestOutputHelper _out;
    public FacilityBalancingTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void TenSeasons_FacilitiesShowWealth_AndSoakUpRichClubsCash()
    {
        var d = TestData.Data;
        var w = new WorldGenerator(d).Generate(2026);
        var cycle = new SeasonCycle(d); var rng = new GameRandom(2033);
        for (int s = 0; s < 10; s++) cycle.Advance(w, rng);
        double Level(string league) => w.Clubs.Where(c => w.ClubLeague[c.Id] == league).Average(c => c.Facilities.Values.Average(f => f.Level));
        double Spent(string league) => w.Clubs.Where(c => w.ClubLeague[c.Id] == league).Average(c => (double)c.SpentOnFacilities);
        foreach (var l in new[] { "ENG-1", "ESP-1", "BRA-1", "ARG-1", "ENG-2", "ARG-2" })
            _out.WriteLine($"{l}: facility level {Level(l):F1}, spent {Spent(l) / 1e6:F0}M per club");
        Assert.True(Level("ENG-1") > Level("BRA-1") && Level("BRA-1") > Level("ARG-2"));   // levels show wealth
        Assert.True(Spent("ENG-1") > 100e6);                                                 // rich clubs spend their surplus
        Assert.All(w.Clubs, c => Assert.True(c.Balance >= 0));
        Assert.All(w.Clubs.SelectMany(c => c.Facilities.Values), f => { Assert.InRange(f.Level, 1, 10); Assert.InRange(f.Condition, 0, 100); });
    }
}
