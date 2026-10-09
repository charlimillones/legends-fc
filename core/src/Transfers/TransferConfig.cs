using System.Collections.Generic;

namespace LegendsFC.Core.Transfers
{
    /// <summary>data/config/transfers.json (confirmed by Carlos Oct 9; numbers marked PROPOSAL there are for balancing).</summary>
    public sealed class TransferConfig
    {
        public ImportanceConfig Importance = new ImportanceConfig();
        public FormConfig Form = new FormConfig();
        public double AskingSpreadMin = 1.0, AskingSpreadMax = 1.2, WageDemandMin = 1.0, WageDemandMax = 1.2;
        public AcceptanceConfig Acceptance = new AcceptanceConfig();
        public ResponseConfig Responses = new ResponseConfig();
        public YearsConfig Years = new YearsConfig();
        public BonusConfig Bonuses = new BonusConfig();
        public BidConfig Bids = new BidConfig();
        public WindowConfig Windows = new WindowConfig();
        public AiMarketConfig Ai = new AiMarketConfig();
        public LoanConfig Loans = new LoanConfig();
    }

    public sealed class ImportanceConfig
    {
        public int KeyPlayers = 3, Starters = 11, SquadPlayers = 18;
        public double KeyPlayer = 1.5, Starter = 1.25, SquadPlayer = 1.0, Fringe = 0.9, Listed = 0.85;
    }

    public sealed class FormConfig { public double Neutral = 6.5, Slope = 0.05, Min = 0.9, Max = 1.15; }

    public sealed class AcceptanceConfig { public double ZeroAt = 0.7, CloseFrom = 0.85; }

    public sealed class ResponseConfig
    {
        public double CloseCounter = 0.7, FarCounter = 0.4, FarKeepTalking = 0.4;
        public int Patience = 3, CooldownDays = 7;
        /// <summary>After the seller says a plain "no" to a bid, the bidder improves it, holds it, or withdraws (PROPOSAL).</summary>
        public double AfterRejectImprove = 0.5, AfterRejectHold = 0.3;
    }

    public sealed class YearsConfig
    {
        public int YoungMaxAge = 23, PrimeMaxAge = 29;
        public int[] Young = { 4, 5 }, Prime = { 3, 4 }, Veteran = { 2, 3 };
        public double PenaltyPerYear = 0.05;
    }

    public sealed class BonusConfig { public double AcademyOrFormerClub = 0.05, LoyalRenewal = 0.10, Diva = -0.10; }

    public sealed class BidConfig
    {
        public int MaxBids = 5, ArrivalDaysMax = 5;
        public double BidMin = 0.85, BidMax = 1.1, BidderMaxMin = 1.0, BidderMaxMax = 1.25, UnsolicitedChancePerDay = 0.01;
    }

    public sealed class WindowConfig { public int SummerDays = 56, WinterDays = 28, DeadlineDays = 3; public double DeadlineMultiplier = 3; }

    public sealed class AiMarketConfig
    {
        public double BudgetShareOfBalance = 0.5, UpgradeMargin = 1.0, WeakStarterGap = 3.0;
        public Dictionary<string, int> ShortGroupMinimum = new Dictionary<string, int> { { "GK", 2 }, { "DEF", 7 }, { "MID", 7 }, { "FWD", 4 } };
        public Dictionary<string, int> StartersPerGroup = new Dictionary<string, int> { { "GK", 1 }, { "DEF", 4 }, { "MID", 3 }, { "FWD", 3 } };
        public double SearchChancePerDay = 0.06, SearchBand = 8;
        public int CandidatesPerSearch = 8;
        public double OpeningBidMin = 0.85, OpeningBidMax = 1.0, MaxOverAsking = 1.1, MaxOverWageDemand = 1.1;
        public int NormalSquadSize = 28;
        public double WageShareBeforeSelling = 0.8;
    }

    public sealed class LoanConfig
    {
        public double MaxFeeShareOfValue = 0.1, SquadPlayerFeeShare = 0.05;
        public double[] WageShares = { 0.0, 0.5, 1.0 };
        public int AiLoanMaxAge = 21, AiLoanOutsideBest = 18;
        public double AiLoanChancePerDay = 0.01;
    }
}
