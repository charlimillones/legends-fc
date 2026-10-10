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
    public void StarsAskASmallerShareOfTheirValue()
    {
        double star = Finance.ExpectedWage(182e6, "ENG-1", F), mid = Finance.ExpectedWage(14e6, "ENG-1", F), cheap = Finance.ExpectedWage(1e6, "ENG-1", F);
        Assert.True(star / 182e6 < mid / 14e6 && mid / 14e6 < cheap / 1e6);
        Assert.InRange(star, 25e6, 40e6);   // a 90-rated star at 25 earns about €32M a season in England
    }

    [Fact]
    public void SamePlayerEarnsLessInAPoorerLeague()
    {
        double eng = Finance.ExpectedWage(20e6, "ENG-1", F);
        Assert.True(Finance.ExpectedWage(20e6, "ESP-1", F) < eng);
        Assert.True(Finance.ExpectedWage(20e6, "ARG-1", F) < 0.25 * eng);
        Assert.True(Finance.ExpectedWage(20e6, "ARG-2", F) < Finance.ExpectedWage(20e6, "ARG-1", F));
    }

    [Fact]
    public void MoneyNeverGoesBelowZero()
    {
        var club = new Club { Balance = 1_000_000 };
        Assert.False(Finance.TrySpend(club, 1_000_001));
        Assert.Equal(1_000_000, club.Balance);
        Assert.True(Finance.TrySpend(club, 1_000_000));
        Assert.Equal(0, club.Balance);
        Assert.False(Finance.TrySpend(club, 1));
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
        Assert.Equal(F.StartingBalanceSeasons * Finance.SeasonIncome(top, "ENG-1", 1, eng.Count, home, F, d.FacilityRules).Total, top.Balance, 0);
    }

    [Fact]
    public void SeasonSettlesIncomeMinusWagesAndUpkeep()
    {
        var d = TestData.Data;
        var w = new WorldGenerator(d).Generate(6);
        var before = w.Clubs.ToDictionary(c => c.Id, c => c.Balance);
        var spentBefore = w.Clubs.ToDictionary(c => c.Id, c => c.SpentOnFacilities);
        var coachesBefore = w.Clubs.ToDictionary(c => c.Id, c => c.SpentOnCoaches);
        var report = new SeasonCycle(d).Advance(w, new GameRandom(1));
        // Transfer fees from the windows move money between clubs on top of the season's settlement.
        double Fees(string id) => w.Market.History.Where(h => h.FromClubId == id).Sum(h => (double)h.Fee) - w.Market.History.Where(h => h.ToClubId == id && h.FromClubId != null).Sum(h => (double)h.Fee);
        foreach (var c in w.Clubs)
        {
            double expected = Math.Max(0, before[c.Id] + Fees(c.Id) - (c.SpentOnFacilities - spentBefore[c.Id]) + report.Income[c.Id].Total * (1 - F.UpkeepShareOfIncome) - report.WageBill[c.Id])
                              - (c.SpentOnCoaches - coachesBefore[c.Id]);   // windows come before the season's settlement; coach contracts after it
            Assert.InRange(c.Balance - expected, -1, 1);
            Assert.True(c.Balance >= 0);
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
    public void AskingWhatHeWantsCloses_LessIsAChance()
    {
        var (d, w) = Fresh();
        var p = w.Players.First(x => x.ClubId == w.Clubs[1].Id && x.PersonalityId == null);
        p.ClubId = null;
        var club = w.Clubs[0];
        int years = Pricing.PreferredYears(w, p, d);
        double demand = Pricing.WageDemand(w, club, p, false, d);
        Assert.Equal(1.0, FreeAgents.AcceptChance(w, club, p, (long)Math.Ceiling(demand), years, d));
        Assert.Equal(0.0, FreeAgents.AcceptChance(w, club, p, (long)(demand * 0.69), years, d));
        long offer = (long)(demand * 0.9);
        double chance = FreeAgents.AcceptChance(w, club, p, offer, years, d);
        Assert.InRange(chance, 0.65, 0.68);
        int yes = 0, n = 4000; var rng = new GameRandom(7);
        for (int i = 0; i < n; i++)
        {
            p.ClubId = null;
            if (FreeAgents.Offer(w, club, p, offer, years, rng, d) == OfferResult.Accepted) yes++;
        }
        Assert.InRange(yes / (double)n, chance - 0.03, chance + 0.03);
    }

    [Fact]
    public void FormerClubAndAcademyAskLess()
    {
        var (d, w) = Fresh();
        var club = w.Clubs[0];
        var p = w.Players.First(x => x.ClubId == club.Id && x.AcademyClubId == null && x.PersonalityId == null);
        Assert.True(Squads.Release(w, p, d));
        Assert.Contains(club.Id, p.FormerClubIds);
        // Same league, so the only difference is the former-club discount (and each club's own 0-20% spread).
        double home = Pricing.WageDemand(w, club, p, false, d) / Pricing.Stable(w, "wage|" + p.Id + "|" + club.Id, 1.0, 1.2);
        var other = w.Clubs[5];
        double away = Pricing.WageDemand(w, other, p, false, d) / Pricing.Stable(w, "wage|" + p.Id + "|" + other.Id, 1.0, 1.2);
        Assert.Equal(0.95, home / away, 6);
        Assert.Contains(w.Players, x => x.AcademyClubId == club.Id);
    }

    [Fact]
    public void EveryClubHasAtMost32_UserIncluded()
    {
        var (d, w) = Fresh();
        var ai = w.Clubs[0]; var user = w.Clubs[1]; w.UserClubId = user.Id;
        var spare = w.Players.Where(p => p.ClubId == w.Clubs[2].Id).ToList();
        foreach (var c in new[] { ai, user })
            while (Squads.Count(w, c.Id) < d.Development.MaxSquadSize) { var s = spare[0]; spare.RemoveAt(0); s.ClubId = c.Id; w.Market.SquadsChanged(); }
        var fa = spare[0]; fa.ClubId = null;
        w.Market.SquadsChanged();   // the test moved players by hand
        Assert.Equal(OfferResult.SquadFull, FreeAgents.Offer(w, ai, fa, 1_000_000_000, 2, new GameRandom(1), d));
        Assert.Equal(OfferResult.SquadFull, FreeAgents.Offer(w, user, fa, 1_000_000_000, 2, new GameRandom(1), d));
    }

    [Fact]
    public void NobodyCanSellOrReleaseBelow16()
    {
        var (d, w) = Fresh();
        var club = w.Clubs[0];
        var squad = w.Players.Where(p => p.ClubId == club.Id).ToList();
        foreach (var p in squad.Skip(d.Development.MinSquadSize)) p.ClubId = null;
        w.Market.SquadsChanged();   // the test moved players by hand
        Assert.Equal(16, Squads.Count(w, club.Id));
        Assert.False(Squads.CanSell(w, club.Id, d));
        Assert.False(Squads.Release(w, squad[0], d));
        Assert.Equal(club.Id, squad[0].ClubId);
        squad[20].ClubId = club.Id;
        w.Market.SquadsChanged();
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
        var leagueAtStart = w.Clubs.ToDictionary(c => c.Id, c => w.MoneyKey(c));
        for (int s = 0; s < 10; s++)
        {
            var r = cycle.Advance(w, rng);
            Assert.True(r.Renewed > 0 && r.LeftAtContractEnd > 0 && r.FreeAgentSignings > 0);
            Assert.All(w.Players.Where(p => p.ClubId != null), p => Assert.True(p.ContractEndYear > w.SeasonStartYear - 1 && p.Wage > 0));
            foreach (var c in w.Clubs)
                Assert.InRange(Squads.Count(w, c.Id), d.Development.MinSquadSize, d.Development.MaxSquadSize);
            Assert.All(w.Clubs, c => Assert.True(c.Balance >= 0));
            if (s == 0)
                foreach (var g in w.Clubs.GroupBy(c => r.Income.ContainsKey(c.Id) ? leagueAtStart[c.Id] : null))
                {   // balancing target (Oct 9): the typical club spends about 65% of income on wages (real football 60-70%)
                    double median = g.Select(c => r.WageBill[c.Id] / r.Income[c.Id].Total).OrderBy(x => x).ElementAt(g.Count() / 2);
                    _out.WriteLine($"  {g.Key}: median wages/income {median:P0}");
                    // With the market spending up to the wage bar the typical club settles near 70%; second divisions (small stadiums
                    // and stores, almost no cup money) run hotter, as in real football (76-85% in the Oct 9 sims).
                    // Cup-only countries have no league: continental prize money dominates their income (Oct 9), so no target.
                    if (g.Key == null || !g.Key.Contains("-")) continue;
                    bool second = g.Key.EndsWith("-2");
                    Assert.InRange(median, second ? 0.60 : 0.50, second ? 0.90 : 0.80);
                }
            _out.WriteLine($"{r.SeasonStartYear}: renewed {r.Renewed}, left at contract end {r.LeftAtContractEnd}, free-agent signings {r.FreeAgentSignings}, " +
                           $"free agents now {w.Players.Count(p => !p.Retired && p.ClubId == null)}, clubs at zero {r.ClubsAtZero}");
        }
    }
}

public class UnsignedFreeAgentTests
{
    [Fact]
    public void AFreeAgentUnsignedForTwoFullSeasons_Retires()
    {
        var d = TestData.Data;
        var w = new LegendsFC.Core.World.WorldGenerator(d).Generate(4);
        var released = w.Players.First(p => p.ClubId != null);
        var signedAgain = w.Players.Where(p => p.ClubId != null).Skip(1).First();
        string club = signedAgain.ClubId;
        Squads.Leave(w, released); Squads.Leave(w, signedAgain);
        LegendsFC.Core.Season.SeasonCycle.RetireLongUnsigned(w, d);      // season end when he was released
        signedAgain.ClubId = club;                                         // signed during the next season
        LegendsFC.Core.Season.SeasonCycle.RetireLongUnsigned(w, d);      // 1 full season unsigned
        Assert.False(released.Retired);
        Squads.Leave(w, signedAgain);                                      // released again: the count starts over
        Assert.Equal(1, LegendsFC.Core.Season.SeasonCycle.RetireLongUnsigned(w, d));   // 2 full seasons unsigned
        Assert.True(released.Retired);
        Assert.False(signedAgain.Retired);
        Assert.Equal(1, signedAgain.UnsignedSeasons);
    }
}
