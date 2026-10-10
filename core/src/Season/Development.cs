using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;

namespace LegendsFC.Core.Season
{
    /// <summary>data/config/development.json.</summary>
    public sealed class DevelopmentConfig
    {
        public int TrainingWeeksPerSeason = 40;
        public double BaseGrowth = 0.08, CrowdPerExtraPlayer = 0.08, ArchetypePlusMultiplier = 1.5, ArchetypeMinusMultiplier = 0.5;
        public Dictionary<string, double> Regimes = new Dictionary<string, double>();
        public Dictionary<string, double> PersonalityGrowth = new Dictionary<string, double>();
        public List<AgeBand> AgeCurve = new List<AgeBand>();
        public double RoomDivisor = 10, FacilityBase = 0.7, FacilityPerLevel = 0.06;
        public double FormNeutral = 6.5, FormSlope = 0.5, FormFactorMax = 1.5, BadFormThreshold = 5.5, BadFormWeeklyLoss = 0.04;
        public double DeclineWeeklyMax = 0.1, DefaultCoachQuality = 1.0;
        public string DefaultRegime = "moderate";
        public int AcademyIntakeMin = 2, AcademyIntakeMax = 4, MinSquadSize = 16, MaxSquadSize = 32, AiMinGoalkeepers = 2;
        /// <summary>The user's club starts with at most this many players, so the first protégé fits (Carlos, Oct 10).</summary>
        public int UserStartMaxSquad = 31;
    }

    public sealed class AgeBand { public int MaxAge; public double Factor; }

    /// <summary>
    /// Weekly player development (fixed potential, confirmed Oct 8): training grows attributes toward the
    /// age-decayed ceiling; form speeds or slows growth, and consistent bad form lowers the rating;
    /// past the decline start the ceiling falls and the rating follows it down.
    /// </summary>
    public static class Development
    {
        /// <summary>Hidden ceiling = potential minus age decay: D × ((age − start) / (retire − start))^1.5 (confirmed Oct 8).</summary>
        public static double Ceiling(Player p, int age, double exponent = 1.5)
        {
            if (age < p.DeclineStartAge) return p.Potential;
            double span = Math.Max(1, p.RetireAge - p.DeclineStartAge);
            double t = Math.Min(1.0, (age - p.DeclineStartAge) / span);
            return p.Potential - p.DeclineAmount * Math.Pow(t, exponent);
        }

        public static double FormFactor(double form, DevelopmentConfig c)
            => Math.Max(0, Math.Min(c.FormFactorMax, 1 + c.FormSlope * (form - c.FormNeutral)));

        public static double AgeFactor(int age, DevelopmentConfig c)
            => c.AgeCurve.OrderBy(b => b.MaxAge).First(b => age <= b.MaxAge).Factor;

        /// <summary>One training week for one player. Same rules for every club.</summary>
        public static void TrainWeek(Player p, int age, Archetype arch, double coachQuality, string regime, int playersOnCoach,
            int trainingGroundEffectiveLevel, double form, GameData d)
        {
            var c = d.Development;
            var main = p.MainPosition;
            double rating = PositionRating.Base(p.Attributes, main, d.PositionRatings);
            double ceiling = Ceiling(p, age);
            var core = main == Position.GK ? GkAttrs : OutfieldAttrs;

            if (rating > ceiling)
            {
                // Age decay: the rating follows the falling ceiling down.
                Shift(p, core, -Math.Min(c.DeclineWeeklyMax, rating - ceiling));
                return;
            }

            double room = Math.Max(0, Math.Min(1, (ceiling - rating) / c.RoomDivisor));
            double crowd = 1 / (1 + c.CrowdPerExtraPlayer * Math.Max(0, playersOnCoach - 1));
            double pers = p.PersonalityId != null && c.PersonalityGrowth.TryGetValue(p.PersonalityId, out var pg) ? pg : 1.0;
            double fac = c.FacilityBase + c.FacilityPerLevel * trainingGroundEffectiveLevel;
            double delta = c.BaseGrowth * coachQuality * c.Regimes[regime] * crowd * pers * AgeFactor(age, c) * room * fac * FormFactor(form, c);

            foreach (var a in core)
            {
                string name = a.ToString();
                double m = arch.Plus.Contains(name) ? c.ArchetypePlusMultiplier : arch.Minus.Contains(name) ? c.ArchetypeMinusMultiplier : 1.0;
                p.AttributeProgress[(int)a] += delta * m;
            }
            if (form < c.BadFormThreshold)
                foreach (var a in core) p.AttributeProgress[(int)a] -= c.BadFormWeeklyLoss * (c.BadFormThreshold - form);
            Commit(p, core);
        }

        private static void Shift(Player p, Attr[] core, double amount)
        {
            foreach (var a in core) p.AttributeProgress[(int)a] += amount;
            Commit(p, core);
        }

        /// <summary>Whole points move into the 1-99 attributes; fractions are kept for next week.</summary>
        private static void Commit(Player p, Attr[] core)
        {
            foreach (var a in core)
            {
                int i = (int)a;
                double frac = p.AttributeProgress[i];
                int whole = (int)Math.Truncate(frac);
                if (whole == 0) continue;
                int v = Math.Max(AttributeSet.Min, Math.Min(AttributeSet.Max, p.Attributes[a] + whole));
                p.Attributes[a] = v;
                p.AttributeProgress[i] = frac - whole;
            }
        }

        public static readonly Attr[] OutfieldAttrs = { Attr.Pace, Attr.Acceleration, Attr.Stamina, Attr.Strength, Attr.Dribbling, Attr.Passing, Attr.Crossing, Attr.Shooting, Attr.Heading, Attr.Tackling, Attr.Positioning };
        public static readonly Attr[] GkAttrs = { Attr.Reflexes, Attr.Handling, Attr.RushingOut, Attr.Distribution };
    }
}
