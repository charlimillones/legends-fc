using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Transfers
{
    public enum OfferResult { Accepted, Declined, SquadFull, NotAFreeAgent, InvalidTerms }

    /// <summary>AI settings for the free-agent market (finance.json "freeAgents"; PROPOSAL).</summary>
    public sealed class FreeAgentConfig
    {
        /// <summary>How many free agents an AI club approaches per window.</summary>
        public int AiApproachesPerClub = 3;
        /// <summary>AI only signs a free agent at least this good: its squad's rating at this percentile (0.5 = median).</summary>
        public double AiQualityPercentile = 0.5;
        /// <summary>AI keeps its wage bill under the wage bar for this objective.</summary>
        public string AiWageObjective = "standard";
    }

    /// <summary>
    /// Free transfers (added Oct 9): a player without a club can be signed with no fee. One rule for AI and user:
    /// the club offers a wage and years, and the player accepts with the approved signing chance
    /// (fee fairness 0, +0.6 if it's his academy or a former club).
    /// </summary>
    public static class FreeAgents
    {
        public static double Rating(Player p, GameData d) => PositionRating.Base(p.Attributes, p.MainPosition, d.PositionRatings);

        public static double MarketValueEur(Player p, int seasonStartYear, int years, GameData d)
            => MarketValue.Eur(Rating(p, d), 6.5, seasonStartYear - p.BirthYear, p.Potential, years, d.MarketValue);

        /// <summary>What he expects to earn per season (EUR) at this club: the wage curve from his market value, at the club's league wage level.</summary>
        public static double ExpectedWage(GameWorld w, Club club, Player p, int years, GameData d)
        {
            int age = w.SeasonStartYear - p.BirthYear;
            double value = MarketValue.Eur(Rating(p, d), 6.5, age, p.Potential, years, d.MarketValue);
            return Money.Finance.ExpectedWage(value, w.MoneyKey(club), d.Finance);
        }

        public static double Bonus(Player p, Club club)
            => p.AcademyClubId == club.Id || p.FormerClubIds.Contains(club.Id) ? Money.Contracts.AcademyOrExClubBonus : 0;

        /// <summary>The chance he says yes. Shown in the UI rounded to 5%.</summary>
        public static double AcceptChance(GameWorld w, Club club, Player p, long wageEur, int years, GameData d)
        {
            double expected = ExpectedWage(w, club, p, years, d);
            if (p.PersonalityId == "PER-BUSINESSMAN") expected *= Money.Contracts.BusinessmanWageFactor;
            return Money.Contracts.AcceptChance(wageEur, expected, 0, years, Bonus(p, club), d.Probability);
        }

        /// <summary>Make an offer. On acceptance he joins with that wage and a contract to SeasonStartYear + years.</summary>
        public static OfferResult Offer(GameWorld w, Club club, Player p, long wageEur, int years, GameRandom rng, GameData d)
        {
            var rc = d.Finance.Renewal;
            if (p.Retired || p.ClubId != null) return OfferResult.NotAFreeAgent;
            if (wageEur <= 0 || years < rc.YearsMin || years > rc.YearsMax) return OfferResult.InvalidTerms;
            // Every club, the user's included, has at most 32 players (Carlos, Oct 9).
            if (Squads.Count(w, club.Id) >= d.Development.MaxSquadSize) return OfferResult.SquadFull;
            if (!rng.Chance(AcceptChance(w, club, p, wageEur, years, d))) return OfferResult.Declined;
            p.ClubId = club.Id;
            p.Wage = wageEur;
            p.ContractEndYear = w.SeasonStartYear + years;
            return OfferResult.Accepted;
        }

        public static List<Player> List(GameWorld w) => w.Players.Where(p => !p.Retired && p.ClubId == null).ToList();
    }

    /// <summary>Squad-size rules (confirmed Oct 9): no club may go below 16 players by selling or releasing.</summary>
    public static class Squads
    {
        public static int Count(GameWorld w, string clubId) => w.Players.Count(p => p.ClubId == clubId);

        /// <summary>True if the club can lose one player (sale, release) and still have the minimum.</summary>
        public static bool CanSell(GameWorld w, string clubId, GameData d) => Count(w, clubId) - 1 >= d.Development.MinSquadSize;

        /// <summary>Releases a player: he becomes a free agent and remembers the club. Refused below the minimum.</summary>
        public static bool Release(GameWorld w, Player p, GameData d)
        {
            if (p.ClubId == null || !CanSell(w, p.ClubId, d)) return false;
            Leave(p);
            return true;
        }

        /// <summary>He leaves his club (released, or out of contract): becomes a free agent.</summary>
        internal static void Leave(Player p)
        {
            if (p.ClubId != null && !p.FormerClubIds.Contains(p.ClubId)) p.FormerClubIds.Add(p.ClubId);
            p.ClubId = null;
        }
    }
}
