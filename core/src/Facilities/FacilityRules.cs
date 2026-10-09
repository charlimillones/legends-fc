using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Facilities
{
    /// <summary>data/config/facilities.json (confirmed Oct 9).</summary>
    public sealed class FacilityConfig
    {
        /// <summary>Price of each level, index 0 = level 1 (used for repairs only), index 9 = level 10.</summary>
        public long[] LevelPriceEur = { 250_000, 500_000, 1_000_000, 2_000_000, 4_000_000, 8_000_000, 15_000_000, 30_000_000, 60_000_000, 120_000_000 };
        public double RepairShareOfPricePerPercent = 0.002;
        public double WearPerWeek = 0.5, WearRandomMin = 0.5, WearRandomMax = 1.5, LowMoodBelow = 30, LowMoodWearMultiplier = 1.5;
        public double LevelDropBelow = 20, LevelDropChance = 0.5, ConditionAfterDrop = 50;
        public double WorkingLevelBase = 0.6, WorkingLevelCondition = 0.4;
        public double EffectBase = 0.6, EffectPerLevel = 0.08, MedicalBase = 1.3, MedicalPerLevel = -0.06;
        public int ManagerStartAgeMin = 35, ManagerStartAgeMax = 64, ManagerRetireAgeMin = 60, ManagerRetireAgeMax = 70;
        public double HandoverLossMin = 5, HandoverLossMax = 15;
        public double AiRepairBelow = 70, AiUpgradeReserveSeasonsOfWages = 1.0;
    }

    public enum UpgradeResult { Done, MaxLevel, NotEnoughMoney }

    /// <summary>
    /// Facility rules (confirmed Oct 9): instant upgrades at fixed prices, repairs priced from the level,
    /// weekly wear, rare level drops, working level from condition, and named managers who retire.
    /// Same rules for every club.
    /// </summary>
    public static class FacilityRules
    {
        public const int MaxLevel = 10;

        /// <summary>Price of a level (2–10 to upgrade; level 1's price is only used for repairs). Fixed, the same for every club.</summary>
        public static long UpgradePrice(int level, FacilityConfig c) => c.LevelPriceEur[Math.Max(1, Math.Min(MaxLevel, level)) - 1];

        /// <summary>Instant upgrade (Carlos, Oct 9). Money never goes below zero.</summary>
        public static UpgradeResult Upgrade(Club club, Facility f, FacilityConfig c)
        {
            var s = club.Facilities[f];
            if (s.Level >= MaxLevel) return UpgradeResult.MaxLevel;
            long price = UpgradePrice(s.Level + 1, c);
            if (!Money.Finance.TrySpend(club, price)) return UpgradeResult.NotEnoughMoney;
            club.SpentOnFacilities += price;
            s.Level++;
            return UpgradeResult.Done;
        }

        /// <summary>Repair cost (Carlos, Oct 9): each 1% of condition costs 0.2% of the level's price.</summary>
        public static long RepairCost(FacilityState s, FacilityConfig c)
            => (long)Math.Round((100 - s.Condition) * c.RepairShareOfPricePerPercent * UpgradePrice(s.Level, c));

        public static long RepairAllCost(Club club, FacilityConfig c) => club.Facilities.Values.Sum(s => RepairCost(s, c));

        /// <summary>Repair one facility to 100%. Refused (nothing paid) if the club can't afford it.</summary>
        public static bool Repair(Club club, Facility f, FacilityConfig c)
        {
            var s = club.Facilities[f];
            if (s.Condition >= 100) return true;
            long cost = RepairCost(s, c);
            if (!Money.Finance.TrySpend(club, cost)) return false;
            club.SpentOnFacilities += cost;
            s.Condition = 100;
            return true;
        }

        /// <summary>Repair all: pays every repair at once, or nothing if the club can't afford the total.</summary>
        public static bool RepairAll(Club club, FacilityConfig c)
        {
            long cost = RepairAllCost(club, c);
            if (!Money.Finance.TrySpend(club, cost)) return false;
            club.SpentOnFacilities += cost;
            foreach (var s in club.Facilities.Values) s.Condition = 100;
            return true;
        }

        /// <summary>The facilities screen total: average condition of the 6.</summary>
        public static double TotalCondition(Club club) => club.Facilities.Values.Average(s => s.Condition);

        /// <summary>How well it works: level × (0.6 + 0.4 × condition). At 50% condition it works like 80% of its level.</summary>
        public static double WorkingLevel(FacilityState s, FacilityConfig c) => s.Level * (c.WorkingLevelBase + c.WorkingLevelCondition * s.Condition / 100);

        /// <summary>Stadium capacity and Club Store income: × (0.6 + 0.08 × working level), so level 5 in full condition is ×1.0.</summary>
        public static double Effect(FacilityState s, FacilityConfig c) => c.EffectBase + c.EffectPerLevel * WorkingLevel(s, c);

        /// <summary>Injury recovery time multiplier from the Medical Building (used once injuries exist).</summary>
        public static double RecoveryTime(FacilityState s, FacilityConfig c) => c.MedicalBase + c.MedicalPerLevel * WorkingLevel(s, c);

        /// <summary>Seats today: the club's base capacity × the Stadium effect.</summary>
        public static int StadiumCapacity(Club club, FacilityConfig c) => (int)Math.Round(club.StadiumCapacity * Effect(club.Facilities[Facility.Stadium], c));

        /// <summary>Weekly wear: 0.5% × 0.5–1.5, ×1.5 when fan mood is below 30.</summary>
        public static void WeeklyWear(GameWorld w, GameRandom rng, FacilityConfig c)
        {
            foreach (var club in w.Clubs)
            {
                double mult = club.FanMood < c.LowMoodBelow ? c.LowMoodWearMultiplier : 1;
                foreach (Facility f in Enum.GetValues(typeof(Facility)))
                {
                    var s = club.Facilities[f];
                    s.Condition = Math.Max(0, Math.Round(s.Condition - c.WearPerWeek * rng.Uniform(c.WearRandomMin, c.WearRandomMax) * mult, 2));
                }
            }
        }

        /// <summary>Season end: a facility below 20% has a 50% chance to drop a level (condition reset to 50%).</summary>
        public static List<(Club club, Facility f)> LevelDrops(GameWorld w, GameRandom rng, FacilityConfig c)
        {
            var dropped = new List<(Club, Facility)>();
            foreach (var club in w.Clubs)
                foreach (Facility f in Enum.GetValues(typeof(Facility)))
                {
                    var s = club.Facilities[f];
                    if (s.Condition >= c.LevelDropBelow || s.Level <= 1 || !rng.Chance(c.LevelDropChance)) continue;
                    s.Level--; s.Condition = c.ConditionAfterDrop;
                    dropped.Add((club, f));
                }
            return dropped;
        }

        /// <summary>Season end: managers who reach their retirement age leave; the new one costs 5–15% condition (handover).</summary>
        public static List<(Club club, Facility f, FacilityManager old)> ManagerHandovers(GameWorld w, GameRandom rng, GameData d)
        {
            var c = d.FacilityRules; var list = new List<(Club, Facility, FacilityManager)>();
            foreach (var club in w.Clubs)
                foreach (Facility f in Enum.GetValues(typeof(Facility)))
                {
                    var s = club.Facilities[f];
                    if (s.Manager == null || w.SeasonStartYear - s.Manager.BirthYear < s.Manager.RetireAge) continue;
                    var old = s.Manager;
                    s.Manager = NewManager(w, club, rng, d, replacement: true);
                    s.Condition = Math.Max(0, s.Condition - rng.Uniform(c.HandoverLossMin, c.HandoverLossMax));
                    list.Add((club, f, old));
                }
            return list;
        }

        /// <summary>A random manager: name and nationality like players (mostly local), aged 35–64 (a replacement: 35–50), retiring at 60–70.</summary>
        public static FacilityManager NewManager(GameWorld w, Club club, GameRandom rng, GameData d, bool replacement = false)
        {
            var c = d.FacilityRules;
            string nat = rng.Chance(d.WorldGen.Nationality.HomeShare) ? club.CountryId : w.Countries[rng.NextInt(0, w.Countries.Count - 1)].Id;
            var lang = d.Names.Languages[d.Names.CountryLanguage[nat]];
            int age = rng.NextInt(c.ManagerStartAgeMin, replacement ? Math.Min(50, c.ManagerStartAgeMax) : c.ManagerStartAgeMax);
            int retire = Math.Max(age + 1, rng.NextInt(c.ManagerRetireAgeMin, c.ManagerRetireAgeMax));
            return new FacilityManager
            {
                Name = lang.First[rng.NextInt(0, lang.First.Count - 1)] + " " + lang.Last[rng.NextInt(0, lang.Last.Count - 1)],
                NationalityId = nat, BirthYear = w.SeasonStartYear - age, RetireAge = retire,
            };
        }

        /// <summary>
        /// AI clubs, every week (PROPOSAL): repair anything below 70% if affordable; with more than a season of wages in the bank
        /// after paying, upgrade the lowest-level facility. This is the money sink for rich clubs.
        /// </summary>
        public static void AiWeekly(GameWorld w, GameData d)
        {
            var c = d.FacilityRules;
            foreach (var club in w.Clubs)
            {
                if (club.Id == w.UserClubId) continue;
                foreach (Facility f in Enum.GetValues(typeof(Facility)))
                    if (club.Facilities[f].Condition < c.AiRepairBelow) Repair(club, f, c);
                var open = club.Facilities.Where(kv => kv.Value.Level < MaxLevel).OrderBy(kv => kv.Value.Level).ThenBy(kv => kv.Key).ToList();
                if (open.Count == 0) continue;
                var target = open[0].Key;
                long price = UpgradePrice(club.Facilities[target].Level + 1, c);
                double reserve = c.AiUpgradeReserveSeasonsOfWages * Transfers.Market.WageBill(w, club.Id);
                if (club.Balance - price >= reserve) Upgrade(club, target, c);
            }
        }
    }
}
