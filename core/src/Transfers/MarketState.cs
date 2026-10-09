using System.Collections.Generic;
using System.Linq;

namespace LegendsFC.Core.Transfers
{
    public enum TalkKind { Transfer, Loan, FreeAgent, Renewal, Bid }
    public enum TalkStatus { Open, Done, WalkedAway, Cancelled, Pending }

    /// <summary>The answer to an offer (Carlos, Oct 9): accept, counter, reject but keep talking, or walk away.</summary>
    public enum Reply { Accepted, Countered, KeepTalking, WalkedAway, Invalid }

    /// <summary>Why an offer can't even be made.</summary>
    public enum Blocked
    {
        None, WindowClosed, Cooldown, NotEnoughMoney, OverWageBar, SquadFull, SellerTooFewPlayers,
        NotAvailable, InvalidTerms, TalksOver, NotOnTheMarket,
    }

    /// <summary>
    /// One negotiation. A transfer has two sides, club ↔ club (fee) and club ↔ player (wage, years);
    /// a free agent or a renewal only the player side; a loan only the club side; a bid is an AI club
    /// bidding for a player, waiting for the selling club's answer.
    /// </summary>
    public sealed class Talk
    {
        public int Id;
        public TalkKind Kind;
        public string PlayerId, BuyerClubId, SellerClubId;   // seller null for free agents; buyer = seller for renewals
        public TalkStatus Status = TalkStatus.Open;
        public int OpenedDay, ArrivesDay;

        // Club side. For buying: the seller's demand (asking price, visible). For loans: the owner's demand (money for the loan).
        public double ClubDemand;
        public bool ClubAgreed;
        public long AgreedFee;
        public int ClubFailures;

        // Player side.
        public double WageDemand;
        public int PreferredYears;
        public bool PlayerAgreed;
        public long AgreedWage;
        public int AgreedYears;
        public int PlayerFailures;

        // Bids (an AI club buying): its standing bid and its hidden maximum.
        public long Bid;
        public double BidderMax;
        public bool AwaitingBidder;   // the seller said "no" and the bidder answers on the next day

        // Loans: the agreed share of his wage the borrowing club pays.
        public double LoanWageShare;

        // Last counter from the other side (shown to the user).
        public long CounterFee, CounterWage;
        public int CounterYears;
    }

    public sealed class Cooldown { public string ClubId, PlayerId; public int UntilDay; }

    public sealed class Listing { public string PlayerId; public long? Price; public bool BidsCreated; }

    public sealed class TransferRecord
    {
        public int Day, Season;
        public string PlayerId, FromClubId, ToClubId;
        public long Fee;
        public bool Loan, FreeAgent;
    }

    /// <summary>Transfer market state inside the save.</summary>
    public sealed class MarketState
    {
        /// <summary>Absolute game day (the calendar counts up through seasons). Cooldowns and arrivals use it.</summary>
        public int Day;
        /// <summary>"summer", "winter" or null when the window is closed. Free agents can be signed any time.</summary>
        public string Window;
        public int WindowDay, WindowLength;
        public int Seq;
        public List<Talk> Talks = new List<Talk>();
        public List<Cooldown> Cooldowns = new List<Cooldown>();
        public List<Listing> Listings = new List<Listing>();
        public List<TransferRecord> History = new List<TransferRecord>();

        /// <summary>Squad ranks by rating (not saved). Null = rebuild on next use.</summary>
        [Newtonsoft.Json.JsonIgnore] public Dictionary<string, int> RankCache;
        [Newtonsoft.Json.JsonIgnore] public Dictionary<string, double> RatingCache;
        /// <summary>Counters for balancing reports (not saved).</summary>
        [Newtonsoft.Json.JsonIgnore] public Dictionary<string, int> Stats = new Dictionary<string, int>();
        public void Count(string key) { Stats.TryGetValue(key, out int n); Stats[key] = n + 1; }
        public void SquadsChanged() => RankCache = null;

        public bool WindowOpen => Window != null;
        public bool IsListed(string playerId) => Listings.Any(l => l.PlayerId == playerId);
        public bool OnCooldown(string clubId, string playerId) => Cooldowns.Any(c => c.ClubId == clubId && c.PlayerId == playerId && c.UntilDay > Day);
        public bool InDeadlineRush(int deadlineDays) => WindowOpen && WindowLength - WindowDay < deadlineDays;
    }
}
