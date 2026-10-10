using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Transfers
{
    /// <summary>
    /// Negotiations, the same for the user and the AI (confirmed Oct 9):
    /// - the probability is always shown (0–100%, whole numbers) and changes with every fee, wage or years change;
    /// - meeting the demand closes the deal; below it the chance falls to 0% at 70%;
    /// - when not accepted, how far off the offer was decides the answer: close → counter or keep talking;
    ///   far apart → counter, keep talking or walk away; under 70% → offended, walks away;
    /// - 3 failed offers and they walk away; a walk-away blocks that club from that player for 1 week.
    /// </summary>
    public static class Market
    {
        // ------------------------------------------------------------------ helpers

        public static Club ClubById(GameWorld w, string id) => w.Clubs.First(c => c.Id == id);
        public static Player PlayerById(GameWorld w, string id) => w.Players.First(p => p.Id == id);

        /// <summary>What a club pays in wages per season, loans included (borrower pays its share, owner the rest).</summary>
        public static double WageBill(GameWorld w, string clubId)
        {
            var m = w.Market;
            if (m.WageBillCache == null)
            {
                m.WageBillCache = new Dictionary<string, double>();
                foreach (var p in w.Players)
                {
                    if (p.Retired || p.ClubId == null) continue;
                    m.WageBillCache.TryGetValue(p.ClubId, out double a);
                    m.WageBillCache[p.ClubId] = a + (p.LoanFromClubId == null ? p.Wage : p.Wage * p.LoanWageShare);
                    if (p.LoanFromClubId != null)
                    {
                        m.WageBillCache.TryGetValue(p.LoanFromClubId, out double b);
                        m.WageBillCache[p.LoanFromClubId] = b + p.Wage * (1 - p.LoanWageShare);
                    }
                }
            }
            return m.WageBillCache.TryGetValue(clubId, out double v) ? v : 0;
        }

        /// <summary>The players registered with a club (loan signings included). Cached until squads change.</summary>
        public static List<Player> Squad(GameWorld w, string clubId)
        {
            var m = w.Market;
            if (m.SquadCache == null)
            {
                m.SquadCache = new Dictionary<string, List<Player>>();
                foreach (var p in w.Players)
                {
                    if (p.ClubId == null) continue;
                    if (!m.SquadCache.TryGetValue(p.ClubId, out var l)) m.SquadCache[p.ClubId] = l = new List<Player>();
                    l.Add(p);
                }
            }
            return m.SquadCache.TryGetValue(clubId, out var s) ? s : new List<Player>();
        }

        /// <summary>Room left under the club's wage bar (AI clubs: the standard objective; the user: the board's objective).</summary>
        public static double WageRoom(GameWorld w, Club club, GameData d)
        {
            // The board's objective sets the user's wage bar (ambitious raises it); asking mid-season can add more (Oct 9 night).
            bool user = club.Id == w.UserClubId;
            string objective = user && d.Finance.ObjectiveMultipliers.ContainsKey(w.Career.ObjectiveKind ?? "") ? w.Career.ObjectiveKind : "standard";
            double bar = Money.Finance.WageBar(club, w.MoneyKey(club), objective, d.Finance) * (user ? 1 + w.Career.WageBarBonus : 1);
            return bar - WageBill(w, club.Id);
        }

        private static Talk NewTalk(GameWorld w, TalkKind kind, Player p, string buyer, string seller)
        {
            var t = new Talk { Id = ++w.Market.Seq, Kind = kind, PlayerId = p.Id, BuyerClubId = buyer, SellerClubId = seller, OpenedDay = w.Market.Day };
            w.Market.Talks.Add(t);
            return t;
        }

        public static IEnumerable<Talk> OpenTalks(GameWorld w, string clubId)
            => w.Market.Talks.Where(t => t.Status == TalkStatus.Open && (t.BuyerClubId == clubId || t.SellerClubId == clubId));

        // ------------------------------------------------------------------ opening talks

        /// <summary>Can this club start talks for this player right now?</summary>
        public static Blocked CanApproach(GameWorld w, Club buyer, Player p, GameData d)
        {
            if (p.Retired || p.ClubId == buyer.Id) return Blocked.NotAvailable;
            if (p.LoanFromClubId != null) return Blocked.NotAvailable;            // on loan: back at season end
            if (w.Market.OnCooldown(buyer.Id, p.Id)) return Blocked.Cooldown;
            if (p.ClubId != null && !w.Market.WindowOpen) return Blocked.WindowClosed; // free agents: any time
            if (Squads.Count(w, buyer.Id) >= d.Development.MaxSquadSize) return Blocked.SquadFull;
            if (p.ClubId != null && !Squads.CanSell(w, p.ClubId, d)) return Blocked.SellerTooFewPlayers;
            // AI squad balance: an AI club doesn't let its goalkeepers go below the minimum (2).
            if (p.ClubId != null && p.ClubId != w.UserClubId && p.MainPosition == Position.GK
                && w.Players.Count(x => x.ClubId == p.ClubId && x.MainPosition == Position.GK) <= d.Development.AiMinGoalkeepers) return Blocked.NotAvailable;
            return Blocked.None;
        }

        /// <summary>Start talks to sign a player (a transfer, or a free agent with no fee).</summary>
        public static Talk OpenSigning(GameWorld w, Club buyer, Player p, GameData d, out Blocked blocked)
        {
            blocked = CanApproach(w, buyer, p, d);
            if (blocked != Blocked.None) return null;
            var existing = w.Market.Talks.FirstOrDefault(t => t.Status == TalkStatus.Open && t.PlayerId == p.Id && t.BuyerClubId == buyer.Id
                                                             && (t.Kind == TalkKind.Transfer || t.Kind == TalkKind.FreeAgent));
            if (existing != null) return existing;
            bool free = p.ClubId == null;
            var t = NewTalk(w, free ? TalkKind.FreeAgent : TalkKind.Transfer, p, buyer.Id, p.ClubId);
            t.ClubAgreed = free;
            t.ClubDemand = free ? 0 : Math.Round(Pricing.AskingPrice(w, p, d));
            t.WageDemand = Math.Round(Pricing.WageDemand(w, buyer, p, false, d));
            t.PreferredYears = Pricing.PreferredYears(w, p, d);
            return t;
        }

        /// <summary>Start renewal talks with one of your own players (player side only).</summary>
        public static Talk OpenRenewal(GameWorld w, Club club, Player p, GameData d)
        {
            if (p.ClubId != club.Id || p.LoanFromClubId != null) return null;
            var t = NewTalk(w, TalkKind.Renewal, p, club.Id, club.Id);
            t.ClubAgreed = true;
            t.WageDemand = Math.Round(Pricing.WageDemand(w, club, p, true, d));
            t.PreferredYears = Pricing.PreferredYears(w, p, d);
            return t;
        }

        /// <summary>
        /// Start talks to borrow a player until the end of the season (club side only; he keeps his contract wage).
        /// The owner wants his wage covered plus a fee: 0% of value for fringe players, 5% for squad players.
        /// AI owners don't lend their best 11.
        /// </summary>
        public static Talk OpenLoan(GameWorld w, Club borrower, Player p, GameData d, out Blocked blocked)
        {
            blocked = CanApproach(w, borrower, p, d);
            if (blocked == Blocked.None && p.ClubId == null) blocked = Blocked.NotAvailable;
            if (blocked != Blocked.None) return null;
            var lc = d.Transfers.Loans;
            int rank = Pricing.SquadRank(w, p, d);
            if (p.ClubId != w.UserClubId && rank <= d.Transfers.Importance.Starters) { blocked = Blocked.NotAvailable; return null; }
            var t = NewTalk(w, TalkKind.Loan, p, borrower.Id, p.ClubId);
            double feeShare = rank <= d.Transfers.Importance.SquadPlayers ? lc.SquadPlayerFeeShare : 0;
            t.ClubDemand = Math.Round(p.Wage + feeShare * Pricing.Value(w, p, d));
            t.PlayerAgreed = true;
            return t;
        }

        // ------------------------------------------------------------------ the live probability

        /// <summary>Chance the club side says yes to this fee (1 once agreed and the fee isn't lowered).</summary>
        public static double ClubChance(Talk t, double fee, GameData d)
        {
            if (t.Kind == TalkKind.FreeAgent || t.Kind == TalkKind.Renewal) return 1;
            if (t.ClubAgreed) return fee >= t.AgreedFee ? 1 : Pricing.AcceptChance(fee / Math.Max(1, t.AgreedFee), d);
            return Pricing.AcceptChance(fee / Math.Max(1, t.ClubDemand), d);
        }

        public static double PlayerChance(Talk t, double wage, int years, GameData d)
        {
            if (t.Kind == TalkKind.Loan) return 1;
            if (t.PlayerAgreed && wage >= t.AgreedWage && years == t.AgreedYears) return 1;
            return Pricing.AcceptChance(Pricing.PlayerRatio(wage, years, t.WageDemand, t.PreferredYears, d), d);
        }

        /// <summary>What the screen shows while you change the fee, wage or years: the chance the whole deal is accepted.</summary>
        public static double Chance(Talk t, double fee, double wage, int years, GameData d)
            => ClubChance(t, fee, d) * PlayerChance(t, wage, years, d);

        /// <summary>Loans: the money the borrower puts in (fee + its share of the wage) against the owner's demand.</summary>
        public static double LoanChance(GameWorld w, Talk t, double fee, double wageShare, GameData d)
        {
            var p = PlayerById(w, t.PlayerId);
            return Pricing.AcceptChance((fee + wageShare * p.Wage) / Math.Max(1, t.ClubDemand), d);
        }

        // ------------------------------------------------------------------ responses

        /// <summary>
        /// How the other side answers (Carlos, Oct 9). Meeting the demand is always accepted. Otherwise the shown chance
        /// is rolled; if it fails, how far off the offer was decides: under 70% → offended, walks away; 85–99% → counter
        /// 70% / keep talking 30%; 70–84% → counter 40% / keep talking 40% / walk away 20%. 3 failed offers → walk away.
        /// </summary>
        public static Reply Respond(double ratio, ref int failures, GameRandom rng, GameData d)
        {
            var a = d.Transfers.Acceptance; var r = d.Transfers.Responses;
            double p = Pricing.AcceptChance(ratio, d);
            if (p >= 1 || (p > 0 && rng.Chance(p))) return Reply.Accepted;
            if (ratio < a.ZeroAt) return Reply.WalkedAway;
            failures++;
            if (failures >= r.Patience) return Reply.WalkedAway;
            double u = rng.NextDouble();
            if (ratio >= a.CloseFrom) return u < r.CloseCounter ? Reply.Countered : Reply.KeepTalking;
            return u < r.FarCounter ? Reply.Countered : u < r.FarCounter + r.FarKeepTalking ? Reply.KeepTalking : Reply.WalkedAway;
        }

        private static void WalkAway(GameWorld w, Talk t, GameData d)
        {
            t.Status = TalkStatus.WalkedAway;
            w.Market.Cooldowns.Add(new Cooldown { ClubId = t.BuyerClubId, PlayerId = t.PlayerId, UntilDay = w.Market.Day + d.Transfers.Responses.CooldownDays });
        }

        /// <summary>The user ends talks himself (also a walk-away: the 1-week cooldown applies to the buying club).</summary>
        public static void EndTalks(GameWorld w, Talk t, GameData d)
        {
            if (t.Status == TalkStatus.Open || t.Status == TalkStatus.Pending) WalkAway(w, t, d);
        }

        // ------------------------------------------------------------------ making offers (buying side)

        /// <summary>Checks that the buyer can pay: the fee from the balance (never below zero) and the wage under the wage bar.</summary>
        public static Blocked CheckMoney(GameWorld w, Talk t, double fee, double wage, GameData d)
        {
            var buyer = ClubById(w, t.BuyerClubId);
            if (fee < 0 || wage < 0) return Blocked.InvalidTerms;
            if (!Money.Finance.CanAfford(buyer, fee)) return Blocked.NotEnoughMoney;
            var p = PlayerById(w, t.PlayerId);
            double added = t.Kind == TalkKind.Renewal ? wage - p.Wage : wage;
            if (added > 0 && added > WageRoom(w, buyer, d)) return Blocked.OverWageBar;
            return Blocked.None;
        }

        /// <summary>
        /// Make an offer: fee (0 for free agents and renewals), wage per season, years. The club side answers first, then the player.
        /// On "Countered", the counter terms are in t.CounterFee / t.CounterWage / t.CounterYears, and accepting them is 100%.
        /// </summary>
        public static Reply Offer(GameWorld w, Talk t, long fee, long wage, int years, GameRandom rng, GameData d, out Blocked blocked)
        {
            blocked = Blocked.None;
            if (t.Status != TalkStatus.Open || t.Kind == TalkKind.Loan || t.Kind == TalkKind.Bid) { blocked = Blocked.TalksOver; return Reply.Invalid; }
            var rc = d.Finance.Renewal;
            if (years < rc.YearsMin || years > rc.YearsMax || wage <= 0) { blocked = Blocked.InvalidTerms; return Reply.Invalid; }
            var p = PlayerById(w, t.PlayerId); var buyer = ClubById(w, t.BuyerClubId);
            if (t.Kind != TalkKind.Renewal)
            {
                blocked = CanApproach(w, buyer, p, d);
                if (blocked == Blocked.Cooldown) blocked = Blocked.None; // talks already open
                if (blocked != Blocked.None) return Reply.Invalid;
            }
            if (t.Kind == TalkKind.FreeAgent || t.Kind == TalkKind.Renewal) fee = 0;
            blocked = CheckMoney(w, t, fee, wage, d);
            if (blocked != Blocked.None) return Reply.Invalid;

            // Club side.
            if (!t.ClubAgreed || fee < t.AgreedFee)
            {
                double demand = t.ClubAgreed ? t.AgreedFee : t.ClubDemand;
                var reply = Respond(fee / Math.Max(1, demand), ref t.ClubFailures, rng, d);
                if (reply == Reply.Accepted) { t.ClubAgreed = true; t.AgreedFee = Math.Max(fee, t.AgreedFee); }
                else
                {
                    if (reply == Reply.Countered)
                    {
                        t.ClubDemand = Math.Round((fee + demand) / 2);
                        t.ClubAgreed = false;
                        t.CounterFee = (long)t.ClubDemand;
                    }
                    if (reply == Reply.WalkedAway) WalkAway(w, t, d);
                    return reply;
                }
            }

            // Player side.
            var pr = Respond(Pricing.PlayerRatio(wage, years, t.WageDemand, t.PreferredYears, d), ref t.PlayerFailures, rng, d);
            if (pr == Reply.Accepted || (t.PlayerAgreed && wage >= t.AgreedWage && years == t.AgreedYears))
            {
                Execute(w, t, fee, wage, years, d);
                return Reply.Accepted;
            }
            if (pr == Reply.Countered)
            {
                t.WageDemand = wage < t.WageDemand ? Math.Round((wage + t.WageDemand) / 2) : t.WageDemand;
                t.CounterWage = (long)t.WageDemand; t.CounterYears = t.PreferredYears; t.CounterFee = (long)t.AgreedFee;
            }
            if (pr == Reply.WalkedAway) WalkAway(w, t, d);
            return pr;
        }

        /// <summary>Offer a loan: fee plus the share of his wage you pay (0, 0.5 or 1). Club side only.</summary>
        public static Reply OfferLoan(GameWorld w, Talk t, long fee, double wageShare, GameRandom rng, GameData d, out Blocked blocked)
        {
            blocked = Blocked.None;
            if (t.Status != TalkStatus.Open || t.Kind != TalkKind.Loan) { blocked = Blocked.TalksOver; return Reply.Invalid; }
            if (!d.Transfers.Loans.WageShares.Contains(wageShare) || fee < 0) { blocked = Blocked.InvalidTerms; return Reply.Invalid; }
            var p = PlayerById(w, t.PlayerId);
            blocked = CheckMoney(w, t, fee, p.Wage * wageShare, d);
            if (blocked != Blocked.None) return Reply.Invalid;
            double money = fee + wageShare * p.Wage;
            var reply = Respond(money / Math.Max(1, t.ClubDemand), ref t.ClubFailures, rng, d);
            if (reply == Reply.Accepted) { t.LoanWageShare = wageShare; ExecuteLoan(w, t, fee, wageShare, d); }
            else if (reply == Reply.Countered)
            {
                t.ClubDemand = Math.Round((money + t.ClubDemand) / 2);
                t.CounterFee = (long)Math.Max(0, t.ClubDemand - wageShare * p.Wage);
            }
            else if (reply == Reply.WalkedAway) WalkAway(w, t, d);
            return reply;
        }

        // ------------------------------------------------------------------ selling: listings and bids

        /// <summary>List a player for sale (optionally with a price). Bids come during the window: 0–5, depending on how useful he is.</summary>
        public static void List(GameWorld w, Player p, long? price, GameRandom rng, GameData d)
        {
            if (w.Market.IsListed(p.Id)) return;
            var l = new Listing { PlayerId = p.Id, Price = price };
            w.Market.Listings.Add(l);
            if (w.Market.WindowOpen) AiMarket.CreateListingBids(w, l, rng, d);
        }

        public static void Unlist(GameWorld w, Player p) => w.Market.Listings.RemoveAll(l => l.PlayerId == p.Id);

        /// <summary>The bids waiting for this club's answer.</summary>
        public static IEnumerable<Talk> BidsFor(GameWorld w, string sellerClubId)
            => w.Market.Talks.Where(t => t.Kind == TalkKind.Bid && t.Status == TalkStatus.Open && t.SellerClubId == sellerClubId);

        /// <summary>Chance the bidder accepts your price (shown live while you change it).</summary>
        public static double SellChance(Talk bid, double price, GameData d)
            => price <= bid.Bid ? 1 : Pricing.AcceptChance(bid.BidderMax / price, d);

        public enum SellerAction { Accept, Counter, Reject, EndTalks }

        /// <summary>
        /// Answer a bid. Accept: sold at the bid. Counter: name your price (the bidder answers like any other negotiation).
        /// Reject: a plain "no" — the bid stays on the table and the bidder answers on the next day (improves, holds or withdraws).
        /// EndTalks: you walk away; that club can't bid for him for a week.
        /// </summary>
        public static Reply RespondToBid(GameWorld w, Talk bid, SellerAction action, long price, GameRandom rng, GameData d)
        {
            if (bid.Kind != TalkKind.Bid || bid.Status != TalkStatus.Open) return Reply.Invalid;
            switch (action)
            {
                case SellerAction.Accept:
                    return SellToBidder(w, bid, bid.Bid, d) ? Reply.Accepted : Reply.Invalid;
                case SellerAction.EndTalks:
                    WalkAway(w, bid, d); return Reply.WalkedAway;
                case SellerAction.Reject:
                    bid.AwaitingBidder = true; return Reply.KeepTalking;
            }
            if (price <= bid.Bid) return SellToBidder(w, bid, bid.Bid, d) ? Reply.Accepted : Reply.Invalid;
            var reply = Respond(bid.BidderMax / price, ref bid.ClubFailures, rng, d);
            if (reply == Reply.Accepted) return SellToBidder(w, bid, price, d) ? Reply.Accepted : Reply.Invalid;
            if (reply == Reply.Countered)
            {
                long raised = (long)Math.Round(Math.Min(bid.BidderMax, (bid.Bid + price) / 2.0));
                bid.Bid = Math.Max(bid.Bid, raised); bid.CounterFee = bid.Bid;
            }
            if (reply == Reply.WalkedAway) WalkAway(w, bid, d);
            return reply;
        }

        /// <summary>The AI bidder pays the fee and gives the player what he asks (it only bids when it can afford both).</summary>
        private static bool SellToBidder(GameWorld w, Talk bid, long fee, GameData d)
        {
            var p = PlayerById(w, bid.PlayerId); var buyer = ClubById(w, bid.BuyerClubId);
            if (p.ClubId != bid.SellerClubId || !Squads.CanSell(w, p.ClubId, d) || Squads.Count(w, buyer.Id) >= d.Development.MaxSquadSize
                || !Money.Finance.CanAfford(buyer, fee)) { bid.Status = TalkStatus.Cancelled; return false; }
            int years = Pricing.PreferredYears(w, p, d);
            double wage = Math.Min(Pricing.WageDemand(w, buyer, p, false, d), Math.Max(1, WageRoom(w, buyer, d)));
            Execute(w, bid, fee, (long)Math.Round(wage), years, d);
            return true;
        }

        // ------------------------------------------------------------------ executing deals

        private static void Execute(GameWorld w, Talk t, long fee, long wage, int years, GameData d)
        {
            var p = PlayerById(w, t.PlayerId); var buyer = ClubById(w, t.BuyerClubId);
            if (t.Kind == TalkKind.Renewal)
            {
                var cache = w.Market.WageBillCache;
                if (cache != null && cache.ContainsKey(p.ClubId) && p.LoanFromClubId == null) cache[p.ClubId] += wage - p.Wage; else w.Market.WageBillCache = null;
                p.Wage = wage; p.ContractEndYear = w.SeasonStartYear + years;
                t.Status = TalkStatus.Done; t.AgreedWage = wage; t.AgreedYears = years;
                return;
            }
            string from = p.ClubId;
            if (from != null)
            {
                var seller = ClubById(w, from);
                Money.Finance.TrySpend(buyer, fee);
                seller.Balance += fee;
                if (!p.FormerClubIds.Contains(from)) p.FormerClubIds.Add(from);
            }
            double valueBefore = Pricing.Value(w, p, d);
            p.ClubId = buyer.Id; p.Wage = wage; p.ContractEndYear = w.SeasonStartYear + years;
            p.LoanFromClubId = null; p.LoanWageShare = 0;
            t.Status = TalkStatus.Done; t.AgreedFee = fee; t.AgreedWage = wage; t.AgreedYears = years;
            Closed(w, p, from, buyer.Id, fee, false);
            w.Market.History[w.Market.History.Count - 1].ValueAtDeal = valueBefore;
        }

        private static void ExecuteLoan(GameWorld w, Talk t, long fee, double wageShare, GameData d)
        {
            var p = PlayerById(w, t.PlayerId); var borrower = ClubById(w, t.BuyerClubId); var owner = ClubById(w, p.ClubId);
            Money.Finance.TrySpend(borrower, fee);
            owner.Balance += fee;
            p.LoanFromClubId = owner.Id; p.LoanWageShare = wageShare; p.ClubId = borrower.Id;
            t.Status = TalkStatus.Done; t.AgreedFee = fee;
            Closed(w, p, owner.Id, borrower.Id, fee, true);
        }

        private static void Closed(GameWorld w, Player p, string from, string to, long fee, bool loan)
        {
            w.Market.Listings.RemoveAll(l => l.PlayerId == p.Id);
            foreach (var other in w.Market.Talks.Where(x => x.PlayerId == p.Id && (x.Status == TalkStatus.Open || x.Status == TalkStatus.Pending)))
                other.Status = TalkStatus.Cancelled;
            w.Market.History.Add(new TransferRecord { Day = w.Market.Day, Season = w.SeasonStartYear, PlayerId = p.Id, FromClubId = from, ToClubId = to, Fee = fee, Loan = loan, FreeAgent = from == null });
            w.Market.Touch(from, to);
        }

        /// <summary>End of season: every loan ends and the player goes back to his club.</summary>
        public static int ReturnLoans(GameWorld w)
        {
            int n = 0;
            foreach (var p in w.Players.Where(p => p.LoanFromClubId != null))
            {
                if (!p.Retired) p.ClubId = p.LoanFromClubId;
                p.LoanFromClubId = null; p.LoanWageShare = 0; n++;
            }
            w.Market.SquadsChanged();
            return n;
        }
    }
}
