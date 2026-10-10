using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Scouting
{
    /// <summary>data/config/scouting.json (rules decided Oct 7; numbers PROPOSAL).</summary>
    public sealed class ScoutingConfig
    {
        public List<CoverageStep> Coverage = new List<CoverageStep>();
        public List<int> RangeByLevel = new List<int>(), PotentialRangeByLevel = new List<int>();
        public int PotentialFromLevel = 7, PersonalityFromLevel = 9, OwnPersonalityFromLevel = 5, OpponentReportFromLevel = 3;
        public int MaxScouts = 5, ReportsPerWeekMax = 2, RevealPersonalityFromRating = 70, WonderkidMaxAge = 19, WonderkidPotential = 80, KnownSeasons = 2, PoolSize = 80;
        public double ReportChance = 0.6, TalentEye = 0.25, AccuracyDivisor = 8;
    }

    public sealed class CoverageStep { public int Level; public string Scope; }

    /// <summary>
    /// What the user can see of a player (architecture rule 7): never the archetype, the hidden age values or exact potential;
    /// attributes and rating exact or as ranges; personality only once revealed.
    /// </summary>
    public sealed class PlayerView
    {
        public string Id, Name, NationalityId, ClubId;
        public int Age;
        public Position MainPosition;
        /// <summary>Other positions he plays without a big drop (comfortable or cover), from his archetype. The archetype itself stays hidden.</summary>
        public List<Position> OtherPositions = new List<Position>();
        public Foot Foot;
        public bool Exact;
        /// <summary>Attribute ranges (low = high when exact), in Attr order.</summary>
        public int[] AttributeLow, AttributeHigh;
        public int RatingLow, RatingHigh;
        /// <summary>Potential range, or null when not seen (never exact).</summary>
        public int? PotentialLow, PotentialHigh;
        /// <summary>Null when unknown or he has none: the UI shows "?" until revealed.</summary>
        public string PersonalityId;
        public bool PersonalityKnown;
        public double Form;
        public long Wage;
        public int ContractEndYear;
        public bool Injured, Suspended;
        public double Energy;
    }

    /// <summary>
    /// Scouting (decided Oct 7). The Scouting Centre level sets which players the user sees in the market and how exactly;
    /// scouts with tasks add reports (accuracy from their rating only). AI clubs don't scout: the AI market reads the world directly.
    /// Report randomness uses its own stream, so scouting never changes what happens in the game.
    /// </summary>
    public static class Scouts
    {
        // ---- what the user sees

        public static int Level(Club club, GameData d)
            => Math.Max(1, Math.Min(10, (int)Math.Round(Facilities.FacilityRules.WorkingLevel(club.Facilities[Facility.ScoutingCentre], d.FacilityRules))));

        /// <summary>Is this player in the user's market (coverage by level, or a recent scout report)?</summary>
        public static bool Visible(GameWorld w, Club viewer, Player p, GameData d)
        {
            if (p.ClubId == viewer.Id) return true;
            if (KnownByReport(w, p.Id, d) != null) return true;
            int level = Level(viewer, d);
            string scope = d.Scouting.Coverage.Where(s => level >= s.Level).OrderByDescending(s => s.Level).Select(s => s.Scope).FirstOrDefault() ?? "league";
            string myLeague = w.ClubLeague.TryGetValue(viewer.Id, out var l) ? l : null;
            var club = p.ClubId == null ? null : w.Clubs.FirstOrDefault(c => c.Id == p.ClubId);
            string theirLeague = club != null && w.ClubLeague.TryGetValue(club.Id, out var tl) ? tl : null;
            string country = club?.CountryId ?? p.NationalityId;
            switch (scope)
            {
                case "world": return true;
                case "leagues": return theirLeague != null || club == null;
                case "confederation":
                    string mine = w.Countries.FirstOrDefault(c => c.Id == viewer.CountryId)?.Confederation;
                    return w.Countries.FirstOrDefault(c => c.Id == country)?.Confederation == mine;
                case "country": return country == viewer.CountryId;
                default: return theirLeague != null && theirLeague == myLeague || (club == null && p.NationalityId == viewer.CountryId);
            }
        }

        public static ScoutReport KnownByReport(GameWorld w, string playerId, GameData d)
            => w.ScoutReports.Where(r => r.PlayerId == playerId && w.SeasonStartYear - r.Season < d.Scouting.KnownSeasons).OrderByDescending(r => r.Id).FirstOrDefault();

        /// <summary>The user's view of a player: exact for his own squad, ranges otherwise (narrowed by scout reports).</summary>
        public static PlayerView View(GameWorld w, Club viewer, Player p, GameData d)
        {
            var c = d.Scouting;
            int level = Level(viewer, d);
            bool own = p.ClubId == viewer.Id;
            var report = own ? null : KnownByReport(w, p.Id, d);
            double rating = PositionRating.Base(p.Attributes, p.MainPosition, d.PositionRatings);
            int half = own ? 0 : c.RangeByLevel[level - 1];
            if (report != null) half = Math.Min(half, Math.Max(0, (report.RatingHigh - report.RatingLow) / 2));
            var noise = new GameRandom(w.Seed ^ Hash(viewer.Id) ^ (Hash(p.Id) * 31) ^ (ulong)w.SeasonStartYear);
            var attrs = p.Attributes.Values;
            var v = new PlayerView
            {
                Id = p.Id, Name = p.Name, NationalityId = p.NationalityId, ClubId = p.ClubId, Age = w.SeasonStartYear - p.BirthYear,
                MainPosition = p.MainPosition, Foot = p.Foot, Exact = half == 0,
                AttributeLow = new int[attrs.Length], AttributeHigh = new int[attrs.Length],
                Form = Squad.Stats.Form(p, d), Wage = p.Wage, ContractEndYear = p.ContractEndYear,
                Injured = p.Injured, Suspended = p.Bans.Count > 0, Energy = own ? p.Energy : 0,
            };
            var arch = d.Archetype(p.ArchetypeId);
            foreach (var pos in Positions.All)
            {
                if (pos == p.MainPosition) continue;
                var fit = OutOfPosition.Evaluate(arch, p.MainPosition, p.Foot, pos, d.PositionRules, d.OutOfPosition);
                if (fit.Fit == PositionFit.Comfortable || fit.Fit == PositionFit.Cover) v.OtherPositions.Add(pos);
            }
            for (int i = 0; i < attrs.Length; i++) (v.AttributeLow[i], v.AttributeHigh[i]) = Range(attrs[i], half, noise, 1, 99);
            (v.RatingLow, v.RatingHigh) = Range((int)Math.Round(rating), half, noise, 1, 99);

            int potHalf = level >= c.PotentialFromLevel ? c.PotentialRangeByLevel[level - 1] : -1;
            if (report != null && (potHalf < 0 || (report.PotentialHigh - report.PotentialLow) / 2 < potHalf)) { v.PotentialLow = report.PotentialLow; v.PotentialHigh = report.PotentialHigh; }
            else if (potHalf > 0) { var (lo, hi) = Range(p.Potential, potHalf, noise, 1, 99); v.PotentialLow = lo; v.PotentialHigh = hi; }

            bool personality = level >= c.PersonalityFromLevel
                || (own && (level >= c.OwnPersonalityFromLevel || p.Stats.Any(s => s.ClubId == viewer.Id && s.Season < w.SeasonStartYear)))
                || (report != null && report.PersonalityId != null);
            v.PersonalityKnown = personality;
            v.PersonalityId = personality ? p.PersonalityId : null;
            return v;
        }

        /// <summary>A range of +/- half around the true value, shifted at random but always containing it (never exact potential).</summary>
        private static (int, int) Range(int value, int half, GameRandom noise, int min, int max)
        {
            if (half <= 0) return (value, value);
            int shift = noise.NextInt(-half, half);
            int lo = Math.Max(min, value - half + shift / 2), hi = Math.Min(max, lo + 2 * half);
            if (value > hi) { hi = value; lo = Math.Max(min, hi - 2 * half); }
            return (lo, hi);
        }

        private static ulong Hash(string s)
        {
            ulong h = 1469598103934665603UL;
            foreach (char ch in s ?? "") { h ^= ch; h *= 1099511628211UL; }
            return h;
        }

        /// <summary>The market search: only players the user can see; filters work on what he sees (the estimated rating).</summary>
        public static List<PlayerView> Search(GameWorld w, Club viewer, GameData d, Position? position = null, int? maxAge = null, int? minRating = null,
                                              bool freeAgentsOnly = false, int limit = 200)
        {
            var list = new List<PlayerView>();
            foreach (var p in w.Players)
            {
                if (p.Retired || p.ClubId == viewer.Id) continue;
                if (freeAgentsOnly && p.ClubId != null) continue;
                if (position != null && p.MainPosition != position) continue;
                if (maxAge != null && w.SeasonStartYear - p.BirthYear > maxAge) continue;
                if (!Visible(w, viewer, p, d)) continue;
                var v = View(w, viewer, p, d);
                if (minRating != null && v.RatingHigh < minRating) continue;
                list.Add(v);
            }
            return list.OrderByDescending(v => (v.RatingLow + v.RatingHigh) / 2.0).ThenBy(v => v.Id, StringComparer.Ordinal).Take(limit).ToList();
        }

        // ---- scouts

        public static List<Scout> OfClub(GameWorld w, string clubId) => w.Scouts.Where(s => s.ClubId == clubId).ToList();

        public static Scout NewScout(GameWorld w, int rating, string nationality, GameRandom rng, GameData d)
        {
            var cc = d.Coaches;
            var lang = d.Names.Languages[d.Names.CountryLanguage[nationality]];
            int age = rng.NextInt(cc.AgeMin, cc.AgeMax);
            return new Scout
            {
                Id = "SCT-" + (++w.ScoutSeq).ToString("00000"), Rating = Math.Max(20, Math.Min(95, rating)), NationalityId = nationality,
                Name = lang.First[rng.NextInt(0, lang.First.Count - 1)] + " " + lang.Last[rng.NextInt(0, lang.Last.Count - 1)],
                BirthYear = w.SeasonStartYear - age, RetireAge = Math.Max(age + 1, rng.NextInt(cc.RetireAgeMin, cc.RetireAgeMax)),
            };
        }

        public static void RefillPool(GameWorld w, GameRandom rng, GameData d)
        {
            for (int i = w.Scouts.Count(s => s.ClubId == null); i < d.Scouting.PoolSize; i++)
                w.Scouts.Add(NewScout(w, (int)Math.Round(rng.Gaussian(52, 13)), w.Countries[rng.NextInt(0, w.Countries.Count - 1)].Id, rng, d));
        }

        /// <summary>Same price formula as coaches (one-time contract price, no wages).</summary>
        public static long Price(Scout s, int seasons, GameData d)
            => (long)Math.Round(d.Coaches.PriceBaseEur * Math.Pow(d.Coaches.PriceGrowth, s.Rating - d.Coaches.PriceBaseRating) * Math.Max(1, seasons));

        public static Squad.HireResult Hire(GameWorld w, Club club, Scout s, int seasons, GameData d)
        {
            if (s.ClubId != null) return Squad.HireResult.NotAvailable;
            if (OfClub(w, club.Id).Count >= d.Scouting.MaxScouts) return Squad.HireResult.LimitReached;
            seasons = Math.Max(d.Coaches.ContractSeasonsMin, Math.Min(d.Coaches.ContractSeasonsMax, seasons));
            long price = Price(s, seasons, d);
            if (!Money.Finance.TrySpend(club, price)) return Squad.HireResult.NotEnoughMoney;
            club.SpentOnCoaches += price;   // staff spending (coaches and scouts)
            s.ClubId = club.Id; s.ContractEndYear = w.SeasonStartYear + seasons;
            return Squad.HireResult.Done;
        }

        public static bool Renew(GameWorld w, Club club, Scout s, int seasons, GameData d)
        {
            if (s.ClubId != club.Id) return false;
            seasons = Math.Max(d.Coaches.ContractSeasonsMin, Math.Min(d.Coaches.ContractSeasonsMax, seasons));
            long price = Price(s, seasons, d);
            if (!Money.Finance.TrySpend(club, price)) return false;
            club.SpentOnCoaches += price;
            s.ContractEndYear = Math.Max(s.ContractEndYear, w.SeasonStartYear + 1) + seasons;
            return true;
        }

        /// <summary>
        /// A week of scouting for the user's scouts: each may file up to 2 reports on players matching his task. Better scouts lean
        /// toward high-potential players and are more accurate. Uses its own random stream (the game is unchanged).
        /// </summary>
        public static List<ScoutReport> Week(GameWorld w, GameData d)
        {
            var c = d.Scouting;
            var filed = new List<ScoutReport>();
            if (w.UserClubId == null) return filed;
            foreach (var s in OfClub(w, w.UserClubId).OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                var rng = new GameRandom(w.Seed ^ Hash(s.Id) ^ ((ulong)w.SeasonStartYear * 100 + (ulong)w.Calendar.Week) * 0x9E3779B97F4A7C15UL);
                if (!rng.Chance(c.ReportChance)) continue;
                var candidates = w.Players.Where(p => !p.Retired && p.ClubId != w.UserClubId && Matches(w, s.Task, p)).ToList();
                if (candidates.Count == 0) continue;
                int n = rng.NextInt(1, c.ReportsPerWeekMax);
                var recent = new HashSet<string>(w.ScoutReports.Where(r => r.ScoutId == s.Id && w.SeasonStartYear - r.Season < 1).Select(r => r.PlayerId));
                for (int k = 0; k < n; k++)
                {
                    var pool = candidates.Where(p => !recent.Contains(p.Id)).ToList();
                    if (pool.Count == 0) break;
                    // An eye for talent: better scouts lean toward players with more potential (they can't see it, they sense it).
                    double eye = c.TalentEye * s.Rating / 100.0;
                    double total = 0; var weights = new double[pool.Count];
                    for (int i = 0; i < pool.Count; i++) total += weights[i] = Math.Exp(eye * (pool[i].Potential - 60) / 5.0);
                    double roll = rng.NextDouble() * total, acc = 0; var p = pool[pool.Count - 1];
                    for (int i = 0; i < pool.Count; i++) { acc += weights[i]; if (roll < acc) { p = pool[i]; break; } }
                    recent.Add(p.Id);
                    int half = Math.Max(1, (int)Math.Round((100 - s.Rating) / c.AccuracyDivisor));
                    int rating = (int)Math.Round(PositionRating.Base(p.Attributes, p.MainPosition, d.PositionRatings));
                    var (rl, rh) = Range(rating, half, rng, 1, 99);
                    var (pl, ph) = Range(p.Potential, half, rng, 1, 99);
                    int age = w.SeasonStartYear - p.BirthYear;
                    var report = new ScoutReport
                    {
                        Id = ++w.ReportSeq, Season = w.SeasonStartYear, Week = w.Calendar.Week, ScoutId = s.Id, PlayerId = p.Id,
                        RatingLow = rl, RatingHigh = rh, PotentialLow = pl, PotentialHigh = ph,
                        PersonalityId = s.Rating >= c.RevealPersonalityFromRating ? p.PersonalityId : null,
                        Wonderkid = age <= c.WonderkidMaxAge && (pl + ph) / 2 >= c.WonderkidPotential,
                    };
                    w.ScoutReports.Add(report);
                    filed.Add(report);
                }
            }
            return filed;
        }

        public static bool Matches(GameWorld w, ScoutTask t, Player p)
        {
            if (t.Position != null && p.MainPosition != t.Position) return false;
            if (t.MaxAge != null && w.SeasonStartYear - p.BirthYear > t.MaxAge) return false;
            var club = p.ClubId == null ? null : w.Clubs.FirstOrDefault(c => c.Id == p.ClubId);
            string country = club?.CountryId ?? p.NationalityId;
            if (t.CountryId != null && country != t.CountryId) return false;
            if (t.Confederation != null && w.Countries.FirstOrDefault(c => c.Id == country)?.Confederation != t.Confederation) return false;
            return true;
        }

        /// <summary>Season end: scouts retire, contracts end (the user must renew before), old reports are dropped, the pool is refilled.</summary>
        public static void SeasonEnd(GameWorld w, GameRandom rng, GameData d)
        {
            w.Scouts.RemoveAll(s => w.SeasonStartYear - s.BirthYear >= s.RetireAge);
            foreach (var s in w.Scouts.Where(s => s.ClubId != null && s.ContractEndYear <= w.SeasonStartYear)) s.ClubId = null;
            w.ScoutReports.RemoveAll(r => w.SeasonStartYear - r.Season >= d.Scouting.KnownSeasons);
            RefillPool(w, rng, d);
        }

        /// <summary>Next-opponent report (Scouting Centre level 3+): their likely formation and best player.</summary>
        public static (string formation, Player best)? Opponent(GameWorld w, Club viewer, Club opponent, GameData d)
        {
            if (Level(viewer, d) < d.Scouting.OpponentReportFromLevel) return null;
            var squad = Transfers.Market.Squad(w, opponent.Id).Where(p => !p.Injured).ToList();
            if (squad.Count == 0) return null;
            var r = new Squad.RatingTable(d);
            string formation = Squad.Lineups.BestFormation(squad, r, d);
            var best = squad.OrderByDescending(r.Main).ThenBy(p => p.Id, StringComparer.Ordinal).First();
            return (formation, best);
        }
    }
}
