using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Season;
using LegendsFC.Core.Transfers;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Xunit;
using Xunit.Abstractions;

/// <summary>Transfer market rules confirmed by Carlos on Oct 9 (09:14, 09:40, 09:48, 09:57).</summary>
public class NegotiationRuleTests
{
    private static GameData D => TestData.Data;

    [Fact]
    public void MeetingTheDemandCloses_BelowItFallsTo0At70()
    {
        Assert.Equal(1.0, Pricing.AcceptChance(1.0, D));
        Assert.Equal(1.0, Pricing.AcceptChance(1.3, D));            // more than asked: still 100%, never above
        Assert.Equal(2.0 / 3, Pricing.AcceptChance(0.9, D), 6);
        Assert.Equal(1.0 / 3, Pricing.AcceptChance(0.8, D), 6);
        Assert.Equal(0.0, Pricing.AcceptChance(0.7, D));
        Assert.Equal(0.0, Pricing.AcceptChance(0.5, D));
    }

    [Fact]
    public void ShownAsWholeNumbers_0To100()
    {
        Assert.Equal(67, Pricing.DisplayPercent(2.0 / 3));
        Assert.Equal(33, Pricing.DisplayPercent(1.0 / 3));
        Assert.Equal(100, Pricing.DisplayPercent(1.4));
        Assert.Equal(0, Pricing.DisplayPercent(-0.2));
        Assert.Equal(97, Pricing.DisplayPercent(Pricing.AcceptChance(0.99, D)));
    }

    [Fact]
    public void Lowball_WalksAway_Close_NeverWalksOnTheFirstOffer()
    {
        var rng = new GameRandom(5);
        for (int i = 0; i < 500; i++)
        {
            int f = 0;
            Assert.Equal(Reply.WalkedAway, Market.Respond(0.65, ref f, rng, D));       // offended
            f = 0;
            var close = Market.Respond(0.9, ref f, rng, D);
            Assert.NotEqual(Reply.WalkedAway, close);
            f = 0;
            Assert.Equal(Reply.Accepted, Market.Respond(1.0, ref f, rng, D));          // meeting the demand
        }
    }

    [Fact]
    public void FarApart_CanBeAnyAnswer_AndThreeFailuresEndTalks()
    {
        var rng = new GameRandom(9);
        var seen = new HashSet<Reply>();
        for (int i = 0; i < 2000; i++) { int f = 0; seen.Add(Market.Respond(0.75, ref f, rng, D)); }
        Assert.Contains(Reply.Accepted, seen); Assert.Contains(Reply.Countered, seen);
        Assert.Contains(Reply.KeepTalking, seen); Assert.Contains(Reply.WalkedAway, seen);
        int failures = 2;   // two failed offers already
        var r = Market.Respond(0.71, ref failures, new GameRandom(1), D);
        Assert.True(r == Reply.Accepted || r == Reply.WalkedAway);
    }

    [Fact]
    public void CloseBand_Shares_CounterAbout70_KeepTalkingAbout30()
    {
        var rng = new GameRandom(3);
        int counter = 0, keep = 0, n = 0;
        for (int i = 0; i < 20000; i++)
        {
            int f = 0; var r = Market.Respond(0.86, ref f, rng, D);
            if (r == Reply.Accepted) continue;
            n++; if (r == Reply.Countered) counter++; else if (r == Reply.KeepTalking) keep++;
        }
        Assert.InRange(counter / (double)n, 0.67, 0.73);
        Assert.InRange(keep / (double)n, 0.27, 0.33);
    }
}

public class TransferTests
{
    private static GameData D => TestData.Data;

    private static (GameWorld w, Club user, GameRandom rng) World(ulong seed = 41)
    {
        var w = new WorldGenerator(D).Generate(seed);
        // The English top-flight club with the most room under its wage bar, so the tests aren't blocked by money.
        var user = w.Clubs.Where(c => w.ClubLeague[c.Id] == "ENG-1").OrderByDescending(c => Market.WageRoom(w, c, D)).First();
        w.UserClubId = user.Id; user.Balance = 2_000_000_000;
        var rng = new GameRandom(seed + 1);
        AiMarket.OpenWindow(w, "summer", rng, D);
        while (Squads.Count(w, user.Id) > 24) Squads.Leave(w, Market.Squad(w, user.Id).OrderBy(p => p.Wage).First());
        return (w, user, rng);
    }

    private static Player Target(GameWorld w, Club user, Func<Player, bool> extra = null)
        => w.Players.First(p => p.ClubId != null && p.ClubId != user.Id && Market.CanApproach(w, user, p, D) == Blocked.None && (extra == null || extra(p)));

    [Fact]
    public void FairPrice_IsValueTimesImportanceTimesForm_AskingIsUpTo20PercentMore()
    {
        var (w, user, _) = World();
        var p = Target(w, user);
        p.ContractEndYear = w.SeasonStartYear + 4;   // a normal contract: the fair price uses the plain value
        double fair = Pricing.Value(w, p, D) * Pricing.Importance(w, p, D) * Pricing.FormFactor(p, D);
        Assert.Equal(fair, Pricing.FairPrice(w, p, D), 0);
        double ask = Pricing.AskingPrice(w, p, D);
        Assert.InRange(ask, fair * 0.999, fair * 1.2001);
        Assert.Equal(ask, Pricing.AskingPrice(w, p, D));   // stable: reopening talks doesn't reroll it
    }

    [Fact]
    public void ImportanceAndFormMakeHimDearer_ListingMakesHimCheaper()
    {
        var (w, user, rng) = World();
        var club = w.Clubs.First(c => c.Id != user.Id);
        var squad = Market.Squad(w, club.Id).OrderBy(p => Pricing.SquadRank(w, p, D)).ToList();
        Assert.Equal(1.5, Pricing.Importance(w, squad[0], D));
        Assert.Equal(1.25, Pricing.Importance(w, squad[5], D));
        Assert.Equal(1.0, Pricing.Importance(w, squad[15], D));
        Assert.Equal(0.9, Pricing.Importance(w, squad[squad.Count - 1], D));
        var p = squad[5];
        p.RecentMatchRatings = Enumerable.Repeat(8.5, 10).ToList();
        Assert.Equal(1.1, Pricing.FormFactor(p, D), 6);      // good form: the club wants to keep him
        p.RecentMatchRatings = Enumerable.Repeat(4.0, 10).ToList();
        Assert.Equal(0.9, Pricing.FormFactor(p, D), 6);
        p.RecentMatchRatings.Clear();
        Market.List(w, p, null, rng, D);
        Assert.Equal(0.85, Pricing.Importance(w, p, D));
    }

    [Fact]
    public void PayingWhatTheyAsk_AlwaysCloses()
    {
        for (ulong s = 0; s < 15; s++)
        {
            var (w, user, rng) = World(100 + s);
            var p = Target(w, user, x => Pricing.CachedRating(w, x, D) < 75);
            var t = Market.OpenSigning(w, user, p, D, out _);
            Assert.Equal(1.0, Market.Chance(t, t.ClubDemand, t.WageDemand, t.PreferredYears, D));
            string seller = p.ClubId; long sellerBefore = w.Clubs.First(c => c.Id == seller).Balance, mine = user.Balance;
            var r = Market.Offer(w, t, (long)Math.Ceiling(t.ClubDemand), (long)Math.Ceiling(t.WageDemand), t.PreferredYears, rng, D, out var blocked);
            Assert.Equal(Blocked.None, blocked);
            Assert.Equal(Reply.Accepted, r);
            Assert.Equal(user.Id, p.ClubId);
            long fee = (long)Math.Ceiling(t.ClubDemand);
            Assert.Equal(mine - fee, user.Balance);
            Assert.Equal(sellerBefore + fee, w.Clubs.First(c => c.Id == seller).Balance);
            Assert.Contains(seller, p.FormerClubIds);
            Assert.Equal(w.SeasonStartYear + t.PreferredYears, p.ContractEndYear);
        }
    }

    [Fact]
    public void ChanceMovesWithFeeWageAndYears()
    {
        var (w, user, _) = World();
        var t = Market.OpenSigning(w, user, Target(w, user), D, out _);
        double full = Market.Chance(t, t.ClubDemand, t.WageDemand, t.PreferredYears, D);
        double lessFee = Market.Chance(t, t.ClubDemand * 0.9, t.WageDemand, t.PreferredYears, D);
        double lessWage = Market.Chance(t, t.ClubDemand, t.WageDemand * 0.9, t.PreferredYears, D);
        int otherYears = t.PreferredYears == 5 ? 4 : t.PreferredYears + 1;
        double wrongYears = Market.Chance(t, t.ClubDemand, t.WageDemand, otherYears, D);
        Assert.Equal(1.0, full);
        Assert.Equal(2.0 / 3, lessFee, 6);
        Assert.Equal(2.0 / 3, lessWage, 6);
        Assert.Equal(5.0 / 6, wrongYears, 6);                           // one year off = 5% less money
        Assert.Equal(4.0 / 9, Market.Chance(t, t.ClubDemand * 0.9, t.WageDemand * 0.9, t.PreferredYears, D), 6);  // both sides
    }

    [Fact]
    public void ACounterIsAccepted_IfYouTakeIt()
    {
        for (ulong s = 0; s < 40; s++)
        {
            var (w, user, rng) = World(200 + s);
            var p = Target(w, user, x => Pricing.CachedRating(w, x, D) < 75);
            var t = Market.OpenSigning(w, user, p, D, out _);
            var r = Market.Offer(w, t, (long)(t.ClubDemand * 0.9), (long)Math.Ceiling(t.WageDemand), t.PreferredYears, rng, D, out _);
            if (r != Reply.Countered) continue;
            Assert.InRange(t.CounterFee, t.ClubDemand * 0.999, t.ClubDemand * 1.001);
            Assert.Equal(1.0, Market.ClubChance(t, t.CounterFee, D));
            Assert.Equal(Reply.Accepted, Market.Offer(w, t, t.CounterFee, (long)Math.Ceiling(t.WageDemand), t.PreferredYears, rng, D, out _));
            return;
        }
        Assert.Fail("no counter in 40 tries");
    }

    [Fact]
    public void WalkAway_BlocksThatClubForOneWeek()
    {
        var (w, user, rng) = World();
        var p = Target(w, user);
        var t = Market.OpenSigning(w, user, p, D, out _);
        var r = Market.Offer(w, t, (long)(t.ClubDemand * 0.5), (long)t.WageDemand, t.PreferredYears, rng, D, out _);
        Assert.Equal(Reply.WalkedAway, r);
        Assert.Equal(Blocked.Cooldown, Market.CanApproach(w, user, p, D));
        Assert.Null(Market.OpenSigning(w, user, p, D, out var why));
        Assert.Equal(Blocked.Cooldown, why);
        for (int i = 0; i < 6; i++) AiMarket.AdvanceDay(w, rng, D);
        if (p.ClubId != user.Id && w.Market.WindowOpen && p.ClubId != null)
            Assert.Equal(Blocked.Cooldown, Market.CanApproach(w, user, p, D));
        AiMarket.AdvanceDay(w, rng, D);
        Assert.False(w.Market.OnCooldown(user.Id, p.Id));        // a week later the club can try again
    }

    [Fact]
    public void MoneyLimits_FeeFromBalance_WageUnderTheBar()
    {
        var (w, user, rng) = World();
        var t = Market.OpenSigning(w, user, Target(w, user), D, out _);
        user.Balance = (long)(t.ClubDemand / 2);
        Market.Offer(w, t, (long)Math.Ceiling(t.ClubDemand), (long)t.WageDemand, t.PreferredYears, rng, D, out var blocked);
        Assert.Equal(Blocked.NotEnoughMoney, blocked);
        user.Balance = 2_000_000_000;
        double room = Market.WageRoom(w, user, D);
        Market.Offer(w, t, (long)Math.Ceiling(t.ClubDemand), (long)(room + 1_000_000), t.PreferredYears, rng, D, out blocked);
        Assert.Equal(Blocked.OverWageBar, blocked);
    }

    [Fact]
    public void ClosedWindow_NoTransfers_FreeAgentsAnyTime()
    {
        var (w, user, rng) = World();
        AiMarket.CloseWindow(w, D);
        Assert.Equal(Blocked.WindowClosed, Market.CanApproach(w, user, w.Players.First(x => x.ClubId != null && x.ClubId != user.Id), D));
        var fa = w.Players.First(x => x.ClubId != null && x.ClubId != user.Id && x.LoanFromClubId == null);
        Squads.Leave(w, fa);
        var t = Market.OpenSigning(w, user, fa, D, out var blocked);
        Assert.Equal(Blocked.None, blocked);
        Assert.Equal(TalkKind.FreeAgent, t.Kind);
        Assert.Equal(Reply.Accepted, Market.Offer(w, t, 0, (long)Math.Ceiling(t.WageDemand), t.PreferredYears, rng, D, out _));
        Assert.Equal(user.Id, fa.ClubId);
    }

    [Fact]
    public void Listing_Brings0To5Bids_AndYouCanAcceptCounterRejectOrEnd()
    {
        var (w, user, rng) = World(47);
        var mine = Market.Squad(w, user.Id).OrderByDescending(p => Pricing.CachedRating(w, p, D)).ToList();
        int total = 0;
        foreach (var p in mine.Take(6))
        {
            Market.List(w, p, null, rng, D);
            int n = w.Market.Talks.Count(t => t.Kind == TalkKind.Bid && t.PlayerId == p.Id);
            Assert.InRange(n, 0, 5);
            total += n;
        }
        Assert.True(total > 0, "nobody bid for any of our 6 best players");
        for (int i = 0; i < 5; i++) AiMarket.AdvanceDay(w, rng, D);   // bids arrive within 5 days
        var bids = Market.BidsFor(w, user.Id).ToList();
        Assert.NotEmpty(bids);

        var a = bids[0];
        Assert.Equal(1.0, Market.SellChance(a, a.Bid, D));
        Assert.True(Market.SellChance(a, a.Bid * 2, D) < 1);
        var player = w.Players.First(p => p.Id == a.PlayerId);
        long before = user.Balance; long bid = a.Bid;
        Assert.Equal(Reply.Accepted, Market.RespondToBid(w, a, Market.SellerAction.Accept, 0, rng, D));
        Assert.Equal(a.BuyerClubId, player.ClubId);
        Assert.Equal(before + bid, user.Balance);

        var b = Market.BidsFor(w, user.Id).FirstOrDefault();
        if (b == null) return;
        Assert.Equal(Reply.KeepTalking, Market.RespondToBid(w, b, Market.SellerAction.Reject, 0, rng, D));
        AiMarket.AdvanceDay(w, rng, D);                                     // the bidder improves, holds or withdraws
        Assert.True(b.Status == TalkStatus.Open || b.Status == TalkStatus.WalkedAway || b.Status == TalkStatus.Cancelled);
        if (b.Status == TalkStatus.Open)
        {
            Assert.Equal(Reply.WalkedAway, Market.RespondToBid(w, b, Market.SellerAction.EndTalks, 0, rng, D));
            Assert.True(w.Market.OnCooldown(b.BuyerClubId, b.PlayerId));
        }
    }

    [Fact]
    public void Loans_WageSplit_AndBackAtSeasonEnd()
    {
        var (w, user, rng) = World();
        var p = w.Players.First(x => x.ClubId != null && x.ClubId != user.Id && Pricing.SquadRank(w, x, D) > 20 && Market.CanApproach(w, user, x, D) == Blocked.None);
        string owner = p.ClubId;
        double ownerBill = Market.WageBill(w, owner), myBill = Market.WageBill(w, user.Id);
        var t = Market.OpenLoan(w, user, p, D, out var blocked);
        Assert.Equal(Blocked.None, blocked);
        Assert.Equal(1.0, Market.LoanChance(w, t, t.ClubDemand - p.Wage, 1.0, D));   // his wage plus the fee they ask
        Assert.Equal(Reply.Accepted, Market.OfferLoan(w, t, (long)Math.Ceiling(t.ClubDemand - p.Wage * 0.5), 0.5, rng, D, out _));
        Assert.Equal(user.Id, p.ClubId);
        Assert.Equal(owner, p.LoanFromClubId);
        Assert.Equal(myBill + p.Wage * 0.5, Market.WageBill(w, user.Id), 0);
        Assert.Equal(ownerBill - p.Wage * 0.5, Market.WageBill(w, owner), 0);
        Assert.Equal(1, Market.ReturnLoans(w));
        Assert.Equal(owner, p.ClubId);
        Assert.Null(p.LoanFromClubId);
    }

    [Fact]
    public void AiClubsDontLendTheirBest11()
    {
        var (w, user, _) = World();
        var club = w.Clubs.First(c => c.Id != user.Id);
        var star = Market.Squad(w, club.Id).OrderBy(p => Pricing.SquadRank(w, p, D)).First();
        Assert.Null(Market.OpenLoan(w, user, star, D, out var blocked));
        Assert.Equal(Blocked.NotAvailable, blocked);
    }

    [Fact]
    public void RenewalTalks_PlayerSideOnly()
    {
        var (w, user, rng) = World();
        var p = Market.Squad(w, user.Id).First();
        var t = Market.OpenRenewal(w, user, p, D);
        Assert.Equal(1.0, Market.Chance(t, 0, t.WageDemand, t.PreferredYears, D));
        Assert.Equal(Reply.Accepted, Market.Offer(w, t, 0, (long)Math.Ceiling(t.WageDemand), t.PreferredYears, rng, D, out _));
        Assert.Equal(w.SeasonStartYear + t.PreferredYears, p.ContractEndYear);
        Assert.Equal(user.Id, p.ClubId);
    }
}

/// <summary>Market balancing checks (approved targets, Oct 8: the trade loop must not be a risk-free exploit).</summary>
public class MarketBalancingTests
{
    private readonly ITestOutputHelper _out;
    public MarketBalancingTests(ITestOutputHelper o) { _out = o; }
    private static GameData D => TestData.Data;

    [Fact]
    public void BuyThenFlipInTheSameWindow_IsNoRiskFreeProfit()
    {
        var results = new List<double>();
        foreach (ulong seed in new ulong[] { 2026, 7 })
        {
            var w = new WorldGenerator(D).Generate(seed);
            var user = w.Clubs.First(c => w.ClubLeague[c.Id] == "ENG-1");
            w.UserClubId = user.Id; user.Balance = 5_000_000_000;
            var rng = new GameRandom(seed + 3);
            AiMarket.OpenWindow(w, "summer", rng, D);
            var pool = w.Players.Where(p => p.ClubId != null && p.ClubId != user.Id).ToList();
            for (int tries = 0; tries < 600 && results.Count < (seed == 7 ? 240 : 120); tries++)
            {
                var p = pool[rng.NextInt(0, pool.Count - 1)];
                if (p.ClubId == null || p.ClubId == user.Id) continue;
                while (Squads.Count(w, user.Id) > 20) Squads.Leave(w, w.Players.First(q => q.ClubId == user.Id && q != p));
                var t = Market.OpenSigning(w, user, p, D, out _);
                if (t == null) continue;
                long fee = (long)(t.ClubDemand * 0.92), wage = (long)Math.Ceiling(t.WageDemand);
                var r = Market.Offer(w, t, fee, wage, t.PreferredYears, rng, D, out _);
                if (r == Reply.Countered || r == Reply.KeepTalking) { fee = (long)Math.Ceiling(t.ClubDemand); r = Market.Offer(w, t, fee, wage, t.PreferredYears, rng, D, out _); }
                if (r != Reply.Accepted) continue;
                Market.List(w, p, null, rng, D);
                var bids = w.Market.Talks.Where(b => b.Kind == TalkKind.Bid && b.PlayerId == p.Id && b.Status == TalkStatus.Pending).ToList();
                if (bids.Count == 0) { Market.Unlist(w, p); continue; }
                foreach (var b in bids) b.Status = TalkStatus.Open;
                var top = bids.OrderByDescending(b => b.Bid).First();
                long ask = (long)(top.Bid * 1.10);
                long sale = Market.RespondToBid(w, top, Market.SellerAction.Counter, ask, rng, D) == Reply.Accepted ? ask : 0;
                if (sale == 0)
                {
                    var still = bids.Where(b => b.Status == TalkStatus.Open).OrderByDescending(b => b.Bid).FirstOrDefault();
                    if (still == null) { Market.Unlist(w, p); continue; }
                    sale = still.Bid; Market.RespondToBid(w, still, Market.SellerAction.Accept, 0, rng, D);
                }
                results.Add(sale / (double)Math.Max(1, fee) - 1);
            }
        }
        double share = results.Count(x => x > 0) / (double)results.Count, avg = results.Average();
        _out.WriteLine($"{results.Count} flips: profit in {share:P0}, average {avg:P1}");
        Assert.True(results.Count >= 100);
        Assert.InRange(share, 0.30, 0.70);     // approved Oct 8: profit possible, never risk-free
        Assert.InRange(avg, -0.10, 0.03);      // no money machine on average
    }

    [Fact]
    public void TenSeasons_TheMarketIsAliveAndLeaguesStayStable()
    {
        var w = new WorldGenerator(D).Generate(3);
        var cycle = new SeasonCycle(D); var rng = new GameRandom(4);
        double Avg(string league) => w.Clubs.Where(c => w.ClubLeague[c.Id] == league)
            .Average(c => Market.Squad(w, c.Id).Select(p => Pricing.Rating(p, D)).OrderByDescending(x => x).Take(11).Average());
        double start = Avg("ENG-1");
        for (int s = 0; s < 10; s++)
        {
            var r = cycle.Advance(w, rng);
            _out.WriteLine($"{r.SeasonStartYear}: transfers {r.Transfers}, loans {r.Loans}, fees {r.TransferFees / 1e6:F0}M, free agents {r.FreeAgentSignings}, ENG-1 XI {Avg("ENG-1"):F1}");
            Assert.InRange(r.Transfers, 100, 1500);
            Assert.True(r.Loans > 0);
            Assert.All(w.Clubs, c => Assert.True(c.Balance >= 0));
            Assert.Empty(w.Players.Where(p => p.LoanFromClubId != null && p.ClubId == p.LoanFromClubId));
        }
        Assert.InRange(Avg("ENG-1") - start, -3.0, 3.0);
    }

    [Fact]
    public void SameSeedSameMarket()
    {
        string Run()
        {
            var w = new WorldGenerator(D).Generate(12);
            new SeasonCycle(D).Advance(w, new GameRandom(13));
            return string.Join(";", w.Market.History.Select(h => h.PlayerId + ">" + h.ToClubId + ":" + h.Fee));
        }
        Assert.Equal(Run(), Run());
    }
}
