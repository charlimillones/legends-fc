using System;

namespace LegendsFC.Core.Rules
{
    public static class Probability
    {
        /// <summary>Clamp so nothing is ever certain (5-95% by default).</summary>
        public static double Clamp(double p, ProbabilityConfig c) => Math.Max(c.Min, Math.Min(c.Max, p));

        /// <summary>What the player sees: rounded to the nearest 5%.</summary>
        public static int DisplayPercent(double p, ProbabilityConfig c)
        {
            double clamped = Clamp(p, c);
            double steps = Math.Round(clamped / c.DisplayStep, MidpointRounding.AwayFromZero);
            return (int)Math.Round(steps * c.DisplayStep * 100, MidpointRounding.AwayFromZero);
        }
    }
}
