using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Squad
{
    /// <summary>data/config/coaches.json (rules decided Oct 7 and Oct 9; numbers PROPOSAL).</summary>
    public sealed class CoachConfig
    {
        public double NaturalFactor = 0.6, QualityBase = 0.5;
        public int CoachesPerLevel = 3;
        public double PriceBaseEur = 25000, PriceGrowth = 1.1, PriceBaseRating = 40, ExPlayerDiscount = 0.3;
        public int ContractSeasonsMin = 1, ContractSeasonsMax = 4;
        public double RatingFromReputationBase = 30, RatingFromReputationSlope = 0.5, RatingSd = 6;
        public int PoolSize = 150;
        public double RetiredPlayerBecomesCoach = 0.15;
        public int AgeMin = 32, AgeMax = 65, RetireAgeMin = 60, RetireAgeMax = 72;
        public double SurpriseChancePerSeason = 0.02;
        public List<CoachTarget> AiTargetCoaches = new List<CoachTarget>();
        public double AiMaxShareOfBalance = 0.03;
        public int GroupTooBig = 7;
    }

    public sealed class CoachTarget { public int MinReputation, Coaches; }

    public enum HireResult { Done, NotEnoughMoney, LimitReached, NotAvailable }

    /// <summary>
    /// Coaches (decided Oct 7; Carlos Oct 9: coaches speed training up, every player still develops naturally). A coach trains
    /// players of his group only; fewer players per coach means faster growth; the Training Grounds level sets how many coaches
    /// a club can have. Same rules for AI clubs and the user.
    /// </summary>
    public static class Coaching
    {
        public static readonly string[] Groups = { "GK", "DEF", "MID", "FWD" };

        public static string GroupOf(Position p) => Season.SeasonCycle.CoachGroup(p);

        public static string GroupName(string group)
        {
            switch (group)
            {
                case "GK": return "goalkeeper";
                case "DEF": return "defence";
                case "MID": return "midfield";
                default: return "forward";
            }
        }

        public static List<Coach> OfClub(GameWorld w, string clubId) => w.Coaches.Where(c => c.ClubId == clubId).ToList();

        /// <summary>How many coaches a club can have: 3 × the Training Grounds working level (at least 1).</summary>
        public static int Limit(Club club, GameData d)
            => Math.Max(1, (int)Math.Round(d.Coaches.CoachesPerLevel * Facilities.FacilityRules.WorkingLevel(club.Facilities[Facility.TrainingGround], d.FacilityRules)));

        /// <summary>Contract price for the given seasons (no wages). Ex-players are cheaper at a club they played for.</summary>
        public static long Price(Coach coach, Club club, int seasons, GameData d)
        {
            var c = d.Coaches;
            double perSeason = c.PriceBaseEur * Math.Pow(c.PriceGrowth, coach.Rating - c.PriceBaseRating);
            if (club != null && coach.FormerClubIds.Contains(club.Id)) perSeason *= 1 - c.ExPlayerDiscount;
            return (long)Math.Round(perSeason * Math.Max(1, seasons));
        }

        /// <summary>Weekly growth multiplier for a player: his coach's quality × crowd, or natural development without one.</summary>
        public static (double quality, int playersOnCoach) TrainingFor(GameWorld w, Player p, Dictionary<string, int> load, Dictionary<string, Coach> coaches, GameData d)
        {
            if (p.CoachId != null && coaches.TryGetValue(p.CoachId, out var coach) && coach.ClubId == p.ClubId && coach.Group == GroupOf(p.MainPosition))
                return (d.Coaches.QualityBase + coach.Rating / 100.0, Math.Max(1, load.TryGetValue(coach.Id, out var n) ? n : 1));
            return (d.Coaches.NaturalFactor, 1);
        }

        public static HireResult Hire(GameWorld w, Club club, Coach coach, int seasons, GameData d, bool free = false)
        {
            if (coach.ClubId != null) return HireResult.NotAvailable;
            if (OfClub(w, club.Id).Count >= Limit(club, d)) return HireResult.LimitReached;
            seasons = Math.Max(d.Coaches.ContractSeasonsMin, Math.Min(d.Coaches.ContractSeasonsMax, seasons));
            long price = free ? 0 : Price(coach, club, seasons, d);
            if (!Money.Finance.TrySpend(club, price)) return HireResult.NotEnoughMoney;
            club.SpentOnCoaches += price;
            coach.ClubId = club.Id;
            coach.ContractEndYear = w.SeasonStartYear + seasons;
            return HireResult.Done;
        }

        /// <summary>Extends a contract by the given seasons at the coach's price.</summary>
        public static bool Renew(GameWorld w, Club club, Coach coach, int seasons, GameData d)
        {
            if (coach.ClubId != club.Id) return false;
            seasons = Math.Max(d.Coaches.ContractSeasonsMin, Math.Min(d.Coaches.ContractSeasonsMax, seasons));
            long price = Price(coach, club, seasons, d);
            if (!Money.Finance.TrySpend(club, price)) return false;
            club.SpentOnCoaches += price;
            coach.ContractEndYear = Math.Max(coach.ContractEndYear, w.SeasonStartYear + 1) + seasons;
            return true;
        }

        /// <summary>Lets a coach go (no refund); his players develop naturally until reassigned.</summary>
        public static void Release(GameWorld w, Coach coach)
        {
            foreach (var p in w.Players.Where(p => p.CoachId == coach.Id)) p.CoachId = null;
            coach.ClubId = null;
        }

        /// <summary>A coach trains only players of his group, at his club.</summary>
        public static bool Assign(GameWorld w, Player p, Coach coach)
        {
            if (coach == null) { p.CoachId = null; return true; }
            if (coach.ClubId == null || coach.ClubId != p.ClubId || coach.Group != GroupOf(p.MainPosition)) return false;
            p.CoachId = coach.Id;
            return true;
        }

        /// <summary>AI (and the user's auto-assign button): each group's players shared evenly between its coaches, youngest first.</summary>
        public static void AutoAssign(GameWorld w, Club club)
        {
            var coaches = OfClub(w, club.Id);
            var squad = Transfers.Market.Squad(w, club.Id);
            foreach (var g in Groups)
            {
                var gc = coaches.Where(c => c.Group == g).OrderByDescending(c => c.Rating).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
                var players = squad.Where(p => GroupOf(p.MainPosition) == g).OrderByDescending(p => p.BirthYear).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
                for (int i = 0; i < players.Count; i++) players[i].CoachId = gc.Count == 0 ? null : gc[i % gc.Count].Id;
            }
        }

        // ---- the world's coaches

        public static Coach NewCoach(GameWorld w, string group, int rating, string nationality, GameRandom rng, GameData d)
        {
            var c = d.Coaches;
            var lang = d.Names.Languages[d.Names.CountryLanguage[nationality]];
            int age = rng.NextInt(c.AgeMin, c.AgeMax);
            return new Coach
            {
                Id = "CCH-" + (++w.CoachSeq).ToString("00000"), Group = group, Rating = Math.Max(20, Math.Min(95, rating)), NationalityId = nationality,
                Name = lang.First[rng.NextInt(0, lang.First.Count - 1)] + " " + lang.Last[rng.NextInt(0, lang.Last.Count - 1)],
                BirthYear = w.SeasonStartYear - age, RetireAge = Math.Max(age + 1, rng.NextInt(c.RetireAgeMin, c.RetireAgeMax)),
            };
        }

        /// <summary>World creation: one coach per group at every club (rating from reputation) and a free pool.</summary>
        public static void GenerateWorld(GameWorld w, GameRandom rng, GameData d)
        {
            var c = d.Coaches;
            foreach (var club in w.Clubs)
                foreach (var g in Groups)
                {
                    string nat = rng.Chance(d.WorldGen.Nationality.HomeShare) ? club.CountryId : w.Countries[rng.NextInt(0, w.Countries.Count - 1)].Id;
                    var coach = NewCoach(w, g, (int)Math.Round(c.RatingFromReputationBase + c.RatingFromReputationSlope * club.Reputation + rng.Gaussian(0, c.RatingSd)), nat, rng, d);
                    coach.ClubId = club.Id;
                    coach.ContractEndYear = w.SeasonStartYear + rng.NextInt(c.ContractSeasonsMin, c.ContractSeasonsMax);
                    w.Coaches.Add(coach);
                }
            RefillPool(w, rng, d);
            foreach (var club in w.Clubs) AutoAssign(w, club);
        }

        private static void RefillPool(GameWorld w, GameRandom rng, GameData d)
        {
            int free = w.Coaches.Count(x => x.ClubId == null);
            for (int i = free; i < d.Coaches.PoolSize; i++)
                w.Coaches.Add(NewCoach(w, Groups[rng.NextInt(0, 3)], (int)Math.Round(rng.Gaussian(50, 12)), w.Countries[rng.NextInt(0, w.Countries.Count - 1)].Id, rng, d));
        }

        /// <summary>
        /// Season end (after the new year starts): coaches retire; contracts that end go back to the pool unless the AI renews;
        /// some retired players become coaches; rarely a former player joins a club for free; AI clubs hire; the pool is refilled.
        /// Returns the coaches who joined the user's club (for the inbox).
        /// </summary>
        public static List<Coach> SeasonEnd(GameWorld w, List<(Player player, string lastClub)> retiredPlayers, GameRandom rng, GameData d)
        {
            var c = d.Coaches;
            var joinedUser = new List<Coach>();
            // Retirement.
            foreach (var coach in w.Coaches.Where(x => w.SeasonStartYear - x.BirthYear >= x.RetireAge).ToList())
            {
                Release(w, coach);
                w.Coaches.Remove(coach);
            }
            // Contracts ending: the AI renews if it still fits the budget; the user's coaches leave unless renewed before.
            foreach (var coach in w.Coaches.Where(x => x.ClubId != null && x.ContractEndYear <= w.SeasonStartYear).ToList())
            {
                var club = w.Clubs.First(k => k.Id == coach.ClubId);
                int seasons = rng.NextInt(2, 3);
                long price = Price(coach, club, seasons, d);
                bool renew = club.Id != w.UserClubId && price <= c.AiMaxShareOfBalance * club.Balance && Money.Finance.TrySpend(club, price);
                if (renew) { coach.ContractEndYear = w.SeasonStartYear + seasons; club.SpentOnCoaches += price; }
                else Release(w, coach);
            }
            // Retired players who become coaches.
            foreach (var (p, lastClub) in retiredPlayers)
            {
                if (!rng.Chance(c.RetiredPlayerBecomesCoach)) continue;
                double rating = Transfers.Pricing.Rating(p, d);
                var coach = NewCoach(w, GroupOf(p.MainPosition), (int)Math.Round(45 + 0.3 * (rating - 60) + rng.Gaussian(0, 8)), p.NationalityId, rng, d);
                coach.Name = p.Name; coach.BirthYear = p.BirthYear; coach.FormerPlayerId = p.Id;
                coach.FormerClubIds = p.FormerClubIds.Concat(lastClub == null ? new string[0] : new[] { lastClub }).Distinct().ToList();
                coach.RetireAge = Math.Max(w.SeasonStartYear - p.BirthYear + 1, coach.RetireAge);
                w.Coaches.Add(coach);
            }
            // Surprise staff: rarely a former player of the club joins for free.
            foreach (var club in w.Clubs)
            {
                if (!rng.Chance(c.SurpriseChancePerSeason)) continue;
                var ex = w.Coaches.Where(x => x.ClubId == null && x.FormerClubIds.Contains(club.Id)).OrderByDescending(x => x.Rating).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
                if (ex != null && Hire(w, club, ex, rng.NextInt(1, 2), d, free: true) == HireResult.Done && club.Id == w.UserClubId) joinedUser.Add(ex);
            }
            // AI clubs hire up to their target.
            foreach (var club in w.Clubs.Where(k => k.Id != w.UserClubId).OrderByDescending(k => k.Reputation).ThenBy(k => k.Id, StringComparer.Ordinal))
                AiHire(w, club, rng, d);
            RefillPool(w, rng, d);
            foreach (var club in w.Clubs.Where(k => k.Id != w.UserClubId)) AutoAssign(w, club);
            return joinedUser;
        }

        /// <summary>AI: a coach for every group first, then more (bigger clubs) for the most crowded group, within the limit and budget.</summary>
        public static void AiHire(GameWorld w, Club club, GameRandom rng, GameData d)
        {
            var c = d.Coaches;
            int target = Math.Min(Limit(club, d), c.AiTargetCoaches.Where(t => club.Reputation >= t.MinReputation).Select(t => t.Coaches).DefaultIfEmpty(4).Max());
            var squad = Transfers.Market.Squad(w, club.Id);
            for (int guard = 0; guard < 12; guard++)
            {
                var mine = OfClub(w, club.Id);
                if (mine.Count >= target) break;
                string group = Groups.OrderBy(g => mine.Any(x => x.Group == g) ? 1 : 0)
                    .ThenByDescending(g => squad.Count(p => GroupOf(p.MainPosition) == g) / (double)(1 + mine.Count(x => x.Group == g))).First();
                int seasons = rng.NextInt(2, 3);
                double budget = c.AiMaxShareOfBalance * club.Balance;
                var pick = w.Coaches.Where(x => x.ClubId == null && x.Group == group && Price(x, club, seasons, d) <= budget)
                    .OrderByDescending(x => x.Rating).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
                if (pick == null || Hire(w, club, pick, seasons, d) != HireResult.Done) break;
            }
        }
    }
}
