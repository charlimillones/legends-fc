using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Squad
{
    /// <summary>
    /// Energy and injuries (Carlos, Oct 9). A match costs energy for the distance covered and for being active (passes and
    /// shots), less for players with more stamina; goals and assists don't matter. The training regime sets the weekly rest:
    /// light recovers fully, moderate and heavy less (but they grow faster). Injuries come with a type and a recovery time;
    /// the Medical Building speeds recovery.
    /// </summary>
    public static class Fitness
    {
        public static double StaminaFactor(Player p, SquadConfig c) => c.StaminaFactorBase - c.StaminaFactorSlope * p.Attributes[Attr.Stamina] / 100.0;

        /// <summary>Energy cost of a match. Sim mode estimates distance, passes and shots; the 3D match will pass in what it measured.</summary>
        public static void MatchEnergy(Player p, PlayerMatch line, int mentality, MatchEngine.Context x)
        {
            var c = x.Data.Squad; var rng = x.Rng;
            if (line.Minutes <= 0) return;
            string pos = line.Position.ToString();
            double share = line.Minutes / 90.0;
            if (line.Km <= 0)
            {
                line.Km = Math.Round(c.KmPer90[pos] * rng.Uniform(c.KmRandomMin, c.KmRandomMax) * (1 + c.KmPerMentalityStep * mentality) * share, 1);
                line.Passes = (int)Math.Round(c.PassesPer90[pos] * rng.Uniform(c.ActivityRandomMin, c.ActivityRandomMax) * share);
                line.Shots = Math.Max(line.Goals, (int)Math.Round(c.ShotsPer90[pos] * rng.Uniform(c.ActivityRandomMin, c.ActivityRandomMax) * share * (1 + 0.1 * mentality)));
            }
            double cost = (line.Km * c.EnergyPerKm + line.Passes * c.EnergyPerPass + line.Shots * c.EnergyPerShot) * StaminaFactor(p, c);
            p.Energy = Math.Max(0, Math.Round(p.Energy - cost, 1));
        }

        /// <summary>A match injury: type by share, length × the club's Medical Building recovery multiplier.</summary>
        public static Injury Injure(Player p, Club club, MatchEngine.Context x)
        {
            var d = x.Data; var rng = x.Rng;
            double roll = rng.NextDouble() * d.Injuries.Sum(i => i.Share), acc = 0;
            var type = d.Injuries.Last();
            foreach (var i in d.Injuries) { acc += i.Share; if (roll < acc) { type = i; break; } }
            double medical = club == null ? 1 : Facilities.FacilityRules.RecoveryTime(club.Facilities[Facility.MedicalCentre], d.FacilityRules);
            int weeks = Math.Max(1, (int)Math.Round(rng.NextInt(type.WeeksMin, type.WeeksMax) * medical));
            p.Injury = new Injury { TypeId = type.Id, Name = type.Name, WeeksLeft = weeks, TotalWeeks = weeks, Serious = type.Serious };
            return p.Injury;
        }

        /// <summary>
        /// End of the week: injuries heal a week (sometimes two: ahead of schedule); injured players rest fully;
        /// everyone else recovers by his regime (light = full) and personality.
        /// </summary>
        public static void WeekEnd(GameWorld w, GameData d, GameRandom rng, Season.WeekReport report)
        {
            var c = d.Squad; var e = d.MatchEvents;
            foreach (var p in w.Players)
            {
                if (p.Retired) continue;
                if (p.Injury != null)
                {
                    p.Energy = 100;
                    p.Injury.WeeksLeft -= 1;
                    if (p.Injury.WeeksLeft >= 2 && rng.Chance(e.RecoveryAheadChancePerWeek))
                    {
                        p.Injury.WeeksLeft -= 1;
                        if (report != null && p.ClubId != null && p.ClubId == w.UserClubId) report.AheadOfSchedule.Add(p.Id);
                    }
                    if (p.Injury.WeeksLeft <= 0)
                    {
                        p.Injury = null;
                        if (report != null && p.ClubId != null && p.ClubId == w.UserClubId) report.Recovered.Add(p.Id);
                    }
                    continue;
                }
                if (p.ClubId == null) { p.Energy = 100; continue; }
                double rest = c.WeeklyRecovery.TryGetValue(p.Regime ?? "moderate", out var r) ? r : 40;
                if (p.PersonalityId != null && c.PersonalityRecovery.TryGetValue(p.PersonalityId, out var m)) rest *= m;
                p.Energy = Math.Min(100, Math.Round(p.Energy + rest, 1));
            }
        }

        /// <summary>AI clubs set regimes by the same rules the user can use: light below 60 energy, otherwise moderate.</summary>
        public static void AiRegimes(GameWorld w, GameData d)
        {
            foreach (var p in w.Players)
            {
                if (p.ClubId == null || p.ClubId == w.UserClubId || p.Retired) continue;
                p.Regime = p.Energy < d.Squad.AiLightRegimeBelowEnergy ? "light" : d.Development.DefaultRegime;
            }
        }
    }

    /// <summary>Player stats (DLS-level tracking, agreed Oct 9) and form = average of the last 10 match ratings (decided).</summary>
    public static class Stats
    {
        public static void Record(Player p, PlayerMatch m, int season, string competitionId, bool playerOfTheMatch, GameData d)
        {
            var line = p.Stats.LastOrDefault(s => s.Season == season && s.CompetitionId == competitionId && s.ClubId == m.ClubId);
            if (line == null) { line = new StatLine { Season = season, CompetitionId = competitionId, ClubId = m.ClubId }; p.Stats.Add(line); }
            line.Apps++;
            if (m.Started) line.Starts++; else line.SubApps++;
            line.Minutes += m.Minutes; line.Goals += m.Goals; line.Assists += m.Assists; line.Yellows += m.Yellows; line.Reds += m.Reds;
            if (m.CleanSheet) line.CleanSheets++;
            if (playerOfTheMatch) line.PlayerOfTheMatch++;
            if (m.Rating > 0)
            {
                line.Rated++; line.RatingSum += m.Rating;
                p.RecentMatchRatings.Add(m.Rating);
                while (p.RecentMatchRatings.Count > d.MatchEvents.FormMatches) p.RecentMatchRatings.RemoveAt(0);
            }
        }

        /// <summary>Form: the average of his last 10 match ratings (neutral until he has played).</summary>
        public static double Form(Player p, GameData d) => p.RecentMatchRatings.Count == 0 ? d.Development.FormNeutral : p.RecentMatchRatings.Average();

        /// <summary>Season end: lines older than last season are merged into one line per season and club (keeps saves small).</summary>
        public static void Compact(GameWorld w, int currentSeason)
        {
            foreach (var p in w.Players)
            {
                if (p.Stats.Count == 0) continue;
                var old = p.Stats.Where(s => s.Season < currentSeason - 1 && s.CompetitionId != null).ToList();
                if (old.Count == 0) continue;
                foreach (var g in old.GroupBy(s => (s.Season, s.ClubId)))
                {
                    var merged = p.Stats.FirstOrDefault(s => s.Season == g.Key.Season && s.ClubId == g.Key.ClubId && s.CompetitionId == null)
                                 ?? AddMerged(p, g.Key.Season, g.Key.ClubId);
                    foreach (var s in g)
                    {
                        merged.Apps += s.Apps; merged.Starts += s.Starts; merged.SubApps += s.SubApps; merged.Minutes += s.Minutes; merged.Goals += s.Goals;
                        merged.Assists += s.Assists; merged.CleanSheets += s.CleanSheets; merged.Yellows += s.Yellows; merged.Reds += s.Reds;
                        merged.Rated += s.Rated; merged.RatingSum += s.RatingSum; merged.PlayerOfTheMatch += s.PlayerOfTheMatch;
                        p.Stats.Remove(s);
                    }
                }
            }
        }

        /// <summary>One line per season and club (for the archive of retired players).</summary>
        public static List<StatLine> Merged(List<StatLine> lines)
        {
            var list = new List<StatLine>();
            foreach (var g in lines.GroupBy(s => (s.Season, s.ClubId)))
            {
                var m = new StatLine { Season = g.Key.Season, ClubId = g.Key.ClubId };
                foreach (var s in g)
                {
                    m.Apps += s.Apps; m.Starts += s.Starts; m.SubApps += s.SubApps; m.Minutes += s.Minutes; m.Goals += s.Goals;
                    m.Assists += s.Assists; m.CleanSheets += s.CleanSheets; m.Yellows += s.Yellows; m.Reds += s.Reds;
                    m.Rated += s.Rated; m.RatingSum += s.RatingSum; m.PlayerOfTheMatch += s.PlayerOfTheMatch;
                }
                list.Add(m);
            }
            return list;
        }

        private static StatLine AddMerged(Player p, int season, string club)
        {
            var s = new StatLine { Season = season, ClubId = club };
            p.Stats.Add(s);
            return s;
        }
    }
}
