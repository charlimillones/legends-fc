using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Transfers
{
    /// <summary>
    /// Prices and demands (confirmed Oct 9):
    /// fair price = value × importance × form; asking price = fair × 1.00–1.20;
    /// wage demand = market wage × 1.00–1.20 (Businessman +20%), with a preferred contract length.
    /// Random parts are stable for a player for a month, so reopening talks doesn't reroll them.
    /// </summary>
    public static class Pricing
    {
        public static double Rating(Player p, GameData d) => PositionRating.Base(p.Attributes, p.MainPosition, d.PositionRatings);

        public static int Age(GameWorld w, Player p) => w.SeasonStartYear - p.BirthYear;

        /// <summary>Recent form (average of the last ~10 match ratings); neutral until matches are played.</summary>
        public static double Form(Player p, GameData d)
            => p.RecentMatchRatings.Count == 0 ? d.Transfers.Form.Neutral : p.RecentMatchRatings.Average();

        public static double Value(GameWorld w, Player p, GameData d)
        {
            int years = Math.Max(0, p.ContractEndYear - w.SeasonStartYear);
            return MarketValue.Eur(Rating(p, d), Form(p, d), Age(w, p), p.Potential, years, d.MarketValue);
        }

        /// <summary>How important he is to his club (rank by rating in the squad; listed players are wanted out).</summary>
        public static double Importance(GameWorld w, Player p, GameData d)
        {
            var c = d.Transfers.Importance;
            if (w.Market.IsListed(p.Id)) return c.Listed;
            if (p.ClubId == null) return c.SquadPlayer;
            int rank = SquadRank(w, p, d);
            return rank <= c.KeyPlayers ? c.KeyPlayer : rank <= c.Starters ? c.Starter : rank <= c.SquadPlayers ? c.SquadPlayer : c.Fringe;
        }

        /// <summary>Rating cached in the world (ratings only change with training; the cache is dropped after training).</summary>
        public static double CachedRating(GameWorld w, Player p, GameData d)
        {
            var cache = w.Market.RatingCache ??= new Dictionary<string, double>();
            if (!cache.TryGetValue(p.Id, out double r)) { r = Rating(p, d); cache[p.Id] = r; }
            return r;
        }

        /// <summary>His rank by rating in his squad (1 = best). Cached per club; dropped when that squad changes.</summary>
        public static int SquadRank(GameWorld w, Player p, GameData d)
        {
            if (p.ClubId == null) return 99;
            var m = w.Market;
            m.RankByClub ??= new Dictionary<string, Dictionary<string, int>>();
            if (!m.RankByClub.TryGetValue(p.ClubId, out var ranks))
            {
                ranks = new Dictionary<string, int>(); int i = 0;
                foreach (var x in Market.Squad(w, p.ClubId).OrderByDescending(x => CachedRating(w, x, d)).ThenBy(x => x.Id, StringComparer.Ordinal)) ranks[x.Id] = ++i;
                m.RankByClub[p.ClubId] = ranks;
            }
            return ranks.TryGetValue(p.Id, out int r) ? r : 99;
        }

        /// <summary>Good form makes the club want to keep him (Carlos, Oct 9).</summary>
        public static double FormFactor(Player p, GameData d)
        {
            var f = d.Transfers.Form;
            return Clamp(1 + f.Slope * (Form(p, d) - f.Neutral), f.Min, f.Max);
        }

        /// <summary>
        /// Fair price = value × importance × form (Carlos, Oct 9). The value is taken as if he had a normal contract
        /// (at least transfers.json minContractYearsForPrice), so a short contract doesn't make him a bargain to flip
        /// straight away (balancing check "no risk-free exploit", PROPOSAL Oct 9).
        /// </summary>
        public static double FairPrice(GameWorld w, Player p, GameData d)
        {
            int years = Math.Max(Math.Max(0, p.ContractEndYear - w.SeasonStartYear), d.Transfers.MinContractYearsForPrice);
            double value = MarketValue.Eur(Rating(p, d), Form(p, d), Age(w, p), p.Potential, years, d.MarketValue);
            return value * Importance(w, p, d) * FormFactor(p, d);
        }

        /// <summary>The selling club's asking price: fair price × 1.00–1.20 (stable for the month).</summary>
        public static double AskingPrice(GameWorld w, Player p, GameData d)
        {
            var t = d.Transfers;
            return FairPrice(w, p, d) * Stable(w, "ask|" + p.Id + "|" + p.ClubId, t.AskingSpreadMin, t.AskingSpreadMax);
        }

        /// <summary>
        /// The wage he wants at this club (shown to the user: offering it at his preferred length closes the deal).
        /// Moving to a richer league is a raise; a poorer league can't get him cheap: he expects the higher of the two
        /// leagues' wage levels. Free agents expect the new league's level. Bonuses lower (or raise) what he asks:
        /// his academy club or a former club −5%, Loyal renewing −10%, Diva +10%; Businessman wants 20% more.
        /// </summary>
        public static double WageDemand(GameWorld w, Club club, Player p, bool renewal, GameData d)
        {
            var t = d.Transfers; var f = d.Finance;
            double value = MarketValue.Eur(Rating(p, d), Form(p, d), Age(w, p), p.Potential, 3, d.MarketValue);
            double market = Money.Finance.ExpectedWage(value, w.MoneyKey(club), f);
            if (p.ClubId != null && p.ClubId != club.Id)
            {
                var current = w.Clubs.First(c => c.Id == p.ClubId);
                market = Math.Max(market, Money.Finance.ExpectedWage(value, w.MoneyKey(current), f));
            }
            double demand = market * Stable(w, "wage|" + p.Id + "|" + club.Id, t.WageDemandMin, t.WageDemandMax);
            if (p.PersonalityId == "PER-BUSINESSMAN") demand *= Money.Contracts.BusinessmanWageFactor;
            double bonus = 0;
            if (!renewal && (p.AcademyClubId == club.Id || p.FormerClubIds.Contains(club.Id))) bonus += t.Bonuses.AcademyOrFormerClub;
            if (renewal && p.PersonalityId == "PER-LOYAL") bonus += t.Bonuses.LoyalRenewal;
            if (p.PersonalityId == "PER-DIVA") bonus += t.Bonuses.Diva;
            return demand * (1 - bonus);
        }

        /// <summary>His preferred contract length: 4–5 years up to 23, 3–4 up to 29, then 2–3 (stable for the season).</summary>
        public static int PreferredYears(GameWorld w, Player p, GameData d)
        {
            var y = d.Transfers.Years; int age = Age(w, p);
            var band = age <= y.YoungMaxAge ? y.Young : age <= y.PrimeMaxAge ? y.Prime : y.Veteran;
            double u = Stable(w, "years|" + p.Id + "|" + w.SeasonStartYear, 0, 1);
            return band[0] + (int)Math.Min(band[1] - band[0], Math.Floor(u * (band[1] - band[0] + 1)));
        }

        /// <summary>Player side: offered wage ÷ his demand, minus 5% for each year away from his preferred length.</summary>
        public static double PlayerRatio(double wage, int years, double demand, int preferredYears, GameData d)
            => wage / Math.Max(1, demand) - d.Transfers.Years.PenaltyPerYear * Math.Abs(years - preferredYears);

        /// <summary>
        /// Acceptance (Carlos, Oct 9): meeting the demand closes the deal (100%); below it the chance falls
        /// in a straight line to 0% at 70% of the demand.
        /// </summary>
        public static double AcceptChance(double ratio, GameData d)
        {
            var a = d.Transfers.Acceptance;
            if (ratio >= 1 - 1e-9) return 1;
            if (ratio <= a.ZeroAt) return 0;
            return (ratio - a.ZeroAt) / (1 - a.ZeroAt);
        }

        /// <summary>What the screen shows: 0–100%, whole numbers (Carlos, Oct 9).</summary>
        public static int DisplayPercent(double p) => (int)Math.Round(Clamp(p, 0, 1) * 100, MidpointRounding.AwayFromZero);

        /// <summary>Deterministic "random" value for a key, stable for the current month of the world (no rerolls by reopening talks).</summary>
        public static double Stable(GameWorld w, string key, double min, double max)
        {
            ulong h = 14695981039346656037UL ^ w.Seed;
            foreach (char ch in key + "|" + (w.Market.Day / 30)) { h ^= ch; h *= 1099511628211UL; }
            h += 0x9E3779B97F4A7C15UL; h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL; h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL; h ^= h >> 31;
            return min + (max - min) * ((h >> 11) * (1.0 / (1UL << 53)));
        }

        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
