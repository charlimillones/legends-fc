using System;
using System.Linq;
using LegendsFC.Core.Model;
using LegendsFC.Core.Money;
using LegendsFC.Core.Season;
using LegendsFC.Core.Transfers;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Xunit;
using Xunit.Abstractions;

public class CurrencyTests
{
    private static CurrencyConfig C => TestData.Data.Currencies;

    [Fact]
    public void AllMainCurrencies_EurIsDefault()
    {
        Assert.Equal(22, C.Currencies.Count);
        Assert.Equal("EUR", C.Default);
        Assert.Equal(C.Currencies.Count, C.Currencies.Select(c => c.Code).Distinct().Count());
        Assert.All(C.Currencies, c => Assert.True(c.PerEur > 0 && !string.IsNullOrEmpty(c.Symbol)));
        foreach (var code in new[] { "USD", "GBP", "BRL", "ARS", "MXN", "COP", "UYU", "JPY" }) Assert.NotNull(C.Get(code));
    }

    [Fact]
    public void FormatsShortAmounts()
    {
        Assert.Equal("€12.5M", C.Format(12_500_000, "EUR"));
        Assert.Equal("€840K", C.Format(840_000, "EUR"));
        Assert.Equal(1716.31 * 1e6, C.FromEur(1e6, "ARS"), 3);
    }

    [Fact]
    public void OneCurrencyPerWorld_ChosenAtCreation()
    {
        var gen = new WorldGenerator(TestData.Data);
        Assert.Equal("EUR", gen.Generate(1).CurrencyCode);
        Assert.Equal("ARS", gen.Generate(1, "ARS").CurrencyCode);
        Assert.Throws<ArgumentException>(() => gen.Generate(1, "XXX"));
        // The currency is display only: the same seed gives the same world in EUR terms.
        var a = gen.Generate(5, "EUR"); var b = gen.Generate(5, "GBP");
        Assert.Equal(a.Clubs.Select(c => c.Balance), b.Clubs.Select(c => c.Balance));
    }
}

public class ContractTests
{
    private static LegendsFC.Core.Rules.ProbabilityConfig P => TestData.Data.Probability;

    [Fact]
    public void FairOfferIsAbout66Percent()
    {
        Assert.InRange(Contracts.AcceptChance(1e6, 1e6, 0, 3, 0, P), 0.65, 0.67);
        Assert.InRange(Contracts.AcceptChance(1e6, 1e6, 0, 3, Contracts.AcademyOrExClubBonus, P), 0.77, 0.79);
        Assert.InRange(Contracts.AcceptChance(1.1e6, 1e6, 0, 3, 0, P), 0.70, 0.72);
        Assert.Equal(0.05, Contracts.AcceptChance(1, 1e6, -3, 1, 0, P), 6);      // clamped
        Assert.Equal(0.95, Contracts.AcceptChance(1e9, 1e6, 0, 4, 0, P), 6);
    }

    [Fact]
    public void PersonalitiesChangeRenewals()
    {
        double plain = Contracts.RenewalChance(null, 1e6, 1e6, 3, P);
        Assert.True(Contracts.RenewalChance("PER-LOYAL", 1e6, 1e6, 3, P) > plain);
        Assert.True(Contracts.RenewalChance("PER-DIVA", 1e6, 1e6, 3, P) < plain);
        Assert.True(Contracts.RenewalChance("PER-BUSINESSMAN", 1e6, 1e6, 3, P) < plain);
        Assert.Equal(plain, Contracts.RenewalChance("PER-BUSINESSMAN", 1.2e6, 1e6, 3, P), 9);  // he wants 20% more
    }
}

public class FinanceTests
{
    private static FinanceConfig F => TestData.Data.Finance;

    [Fact]
    public void EveryLeagueAndCupOnlyCountryHasMoney()
    {
        var d = TestData.Data;
        foreach (var l in d.Competitions.Where(c => c.Type == CompetitionType.League)) Assert.True(F.Leagues.ContainsKey(l.Id), l.Id);
        foreach (var c in d.Countries.Where(c => !c.HasLeague)) Assert.True(F.CupOnly.ContainsKey(c.Id), c.Id);
    }

    [Fact]
    public void TvAndPrizeMoneyAddUpAcrossTheLeague()
    {
        var club = new Club { Reputation = 50, FanMood = 50, StadiumCapacity = 0 };
        int n = 20;
        double tv = Enumerable.Range(1, n).Sum(pos => Finance.SeasonIncome(club, "ENG-1", pos, n, 0, F).Tv);
        double prize = Enumerable.Range(1, n).Sum(pos => Finance.SeasonIncome(club, "ENG-1", pos, n, 0, F).Prize);
        Assert.Equal(n * F.For("ENG-1").TvPerClubEur, tv, 0);
        Assert.Equal(n * F.For("ENG-1").TvPerClubEur * F.PrizePotShareOfTv, prize, 0);
        Assert.True(Finance.SeasonIncome(club, "ENG-1", 1, n, 0, F).Total > Finance.SeasonIncome(club, "ENG-1", n, n, 0, F).Total);
    }

    [Fact]
    public void WageBarGiantVsSmallestIs25To45x()
    {
        var giant = new Club { Reputation = 100 }; var small = new Club { Reputation = 20 };
        double r = Finance.WageBar(giant, "ENG-1", "ambitious", F) / Finance.WageBar(small, "ENG-2", "safe", F);
        Assert.InRange(r, 25, 45);
    }

    [Fact]
    public void StartingBalanceIsHalfASeasonOfIncome_AndEveryoneHasAWage()
    {
        var d = TestData.Data;
        var w = new WorldGenerator(d).Generate(4);
        Assert.All(w.Players, p => Assert.True(p.Wage > 0));
        Assert.All(w.Clubs, c => Assert.True(c.Balance > 0));
        var eng = w.Clubs.Where(c => w.ClubLeague[c.Id] == "ENG-1").OrderByDescending(c => c.Reputation).ToList();
        var top = eng[0];
        int home = SeasonSimulator.HomeLeagueMatches("ENG-1", eng.Count, d);
        Assert.Equal(F.StartingBalanceSeasons * Finance.SeasonIncome(top, "ENG-1", 1, eng.Count, home, F).Total, top.Balance, 0);
    }

    [Fact]
    public void SeasonSettlesIncomeMinusWagesAndUpkeep()
    {
        var d = TestData.Data;
        var w = new WorldGenerator(d).Generate(6);
        var before = w.Clubs.ToDictionary(c => c.Id, c => c.Balance);
        var report = new SeasonCycle(d).Advance(w, new GameRandom(1));
        foreach (var c in w.Clubs)
        {
            double expected = report.Income[c.Id].Total * (1 - F.UpkeepShareOfIncome) - report.WageBill[c.Id];
            Assert.InRange(c.Balance - before[c.Id] - expected, -1, 1);
            Assert.InRange(c.FanMood, 0, 100);
        }
        // Every academy graduate now has a wage too.
        Assert.All(w.Players.Where(p => p.ClubId != null), p => Assert.True(p.Wage > 0));
    }
}

public class FreeAgentTests
{
    private static (LegendsFC.Core.Data.GameData d, GameWorld w) Fresh(ulong seed = 9)
    {
        var d = TestData.Data; var w = new WorldGenerator(d).Generate(seed);
        return (d, w);
    }

    [Fact]
    public void OnlyFreeAgentsCanBeSignedForFree()
    {
        var (d, w) = Fresh();
        var club = w.Clubs[0]; var other = w.Players.First(p => p.ClubId == w.Clubs[1].Id);
        Assert.Equal(OfferResult.NotAFreeAgent, FreeAgents.Offer(w, club, other, 1_000_000, 2, new GameRandom(1), d));
    }

    [Fact]
    public void AcceptedOfferSetsWageAndContract()
    {
        var (d, w) = Fresh();
        var p = w.Players.First(x => x.ClubId == w.Clubs[1].Id);
        p.ClubId = null;
        var club = w.Clubs[0];
        // A huge wage makes it 95%: find a seed where he accepts.
        var rng = new GameRandom(2);
        OfferResult r;
        do r = FreeAgents.Offer(w, club, p, 1_000_000_000, 3, rng, d); while (r == OfferResult.Declined);
        Assert.Equal(OfferResult.Accepted, r);
        Assert.Equal(club.Id, p.ClubId);
        Assert.Equal(1_000_000_000, p.Wage);
        Assert.Equal(w.SeasonStartYear + 3, p.ContractEndYear);
        var q = w.Players.First(x => x.ClubId == w.Clubs[2].Id); q.ClubId = null;
        Assert.Equal(OfferResult.InvalidTerms, FreeAgents.Offer(w, club, q, 1000, 9, rng, d));   // 9 years: too long
        Assert.Equal(OfferResult.InvalidTerms, FreeAgents.Offer(w, club, q, 0, 2, rng, d));      // no wage
    }

    [Fact]
    public void AcceptanceRateMatchesTheApprovedChance()
    {
        var (d, w) = Fresh();
        var p = w.Players.First(x => x.ClubId == w.Clubs[1].Id && x.PersonalityId == null);
        p.ClubId = null;
        var club = w.Clubs[0];
        long fair = (long)FreeAgents.ExpectedWage(p, w.SeasonStartYear, 3, d);
        double chance = FreeAgents.AcceptChance(w, club, p, fair, 3, d);
        Assert.InRange(chance, 0.65, 0.67);
        int yes = 0, n = 4000; var rng = new GameRandom(7);
        for (int i = 0; i < n; i++)
        {
            p.ClubId = null;
            if (FreeAgents.Offer(w, club, p, fair, 3, rng, d) == OfferResult.Accepted) yes++;
        }
        Assert.InRange(yes / (double)n, chance - 0.03, chance + 0.03);
    }

    [Fact]
    public void FormerClubAndAcademyGetTheBonus()
    {
        var (d, w) = Fresh();
        var club = w.Clubs[0];
        var p = w.Players.First(x => x.ClubId == club.Id && x.AcademyClubId == null);
        Assert.True(Squads.Release(w, p, d));
        Assert.Contains(club.Id, p.FormerClubIds);
        long fair = (long)FreeAgents.ExpectedWage(p, w.SeasonStartYear, 3, d);
        Assert.True(FreeAgents.AcceptChance(w, club, p, fair, 3, d) > FreeAgents.AcceptChance(w, w.Clubs[5], p, fair, 3, d));
        Assert.Contains(w.Players, x => x.AcademyClubId == club.Id);
    }

    [Fact]
    public void MaxSquadOnlyBindsAiClubs()
    {
        var (d, w) = Fresh();
        var ai = w.Clubs[0]; var user = w.Clubs[1]; w.UserClubId = user.Id;
        var spare = w.Players.Where(p => p.ClubId == w.Clubs[2].Id).ToList();
        foreach (var c in new[] { ai, user })
            while (Squads.Count(w, c.Id) < d.Development.MaxSquadSize) { var s = spare[0]; spare.RemoveAt(0); s.ClubId = c.Id; }
        var fa = spare[0]; fa.ClubId = null;
        Assert.Equal(OfferResult.SquadFull, FreeAgents.Offer(w, ai, fa, 1_000_000_000, 2, new GameRandom(1), d));
        Assert.NotEqual(OfferResult.SquadFull, FreeAgents.Offer(w, user, fa, 1_000_000_000, 2, new GameRandom(1), d));
    }

    [Fact]
    public void NobodyCanSellOrReleaseBelow16()
    {
        var (d, w) = Fresh();
        var club = w.Clubs[0];
        var squad = w.Players.Where(p => p.ClubId == club.Id).ToList();
        foreach (var p in squad.Skip(d.Development.MinSquadSize)) p.ClubId = null;
        Assert.Equal(16, Squads.Count(w, club.Id));
        Assert.False(Squads.CanSell(w, club.Id, d));
        Assert.False(Squads.Release(w, squad[0], d));
        Assert.Equal(club.Id, squad[0].ClubId);
        squad[20].ClubId = club.Id;
        Assert.True(Squads.CanSell(w, club.Id, d));
    }
}

/// <summary>
/// 10 seasons of money. Structural checks only until Carlos approves the balancing targets;
/// the numbers are printed for the balancing report.
/// </summary>
public class FinanceSeasonsTests
{
    private readonly ITestOutputHelper _out;
    public FinanceSeasonsTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void TenSeasons_ContractsAndFreeAgentsKeepSquadsValid()
    {
        var d = TestData.Data;
        var w = new WorldGenerator(d).Generate(31);
        var cycle = new SeasonCycle(d); var rng = new GameRandom(32);
        for (int s = 0; s < 10; s++)
        {
            var r = cycle.Advance(w, rng);
            Assert.True(r.Renewed > 0 && r.LeftAtContractEnd > 0 && r.FreeAgentSignings > 0);
            Assert.All(w.Players.Where(p => p.ClubId != null), p => Assert.True(p.ContractEndYear > w.SeasonStartYear - 1 && p.Wage > 0));
            foreach (var c in w.Clubs)
                Assert.InRange(Squads.Count(w, c.Id), d.Development.MinSquadSize, d.Development.MaxSquadSize);
            _out.WriteLine($"{r.SeasonStartYear}: renewed {r.Renewed}, left at contract end {r.LeftAtContractEnd}, free-agent signings {r.FreeAgentSignings}, " +
                           $"free agents now {w.Players.Count(p => !p.Retired && p.ClubId == null)}, clubs in debt {w.Clubs.Count(c => c.Balance < 0)}");
        }
    }
}
