using System;

namespace LegendsFC.Core.Rules
{
    /// <summary>data/config/market-value.json (approved Oct 8).</summary>
    public sealed class MarketValueConfig
    {
        public double BaseEur = 20000, Growth = 1.2, PivotRating = 40;
        public double FormSlope = 0.15, FormNeutral = 6.5, FormMin = 0.7, FormMax = 1.3;
        public int AgeFlatUntil = 29; public double AgePerYear = 0.85;
        public int PotentialYoungUntil = 21; public double PotentialYoungFactor = 0.7;
        public int PotentialMidUntil = 23; public double PotentialMidFactor = 0.35;
        public double ContractBase = 0.4, ContractPerYear = 0.2;
    }

    /// <summary>Value comes only from rating and performance, shaped by age, potential and contract (design principle).</summary>
    public static class MarketValue
    {
        /// <param name="knownPotential">The potential the market believes in (scouted estimate), not the hidden true value.</param>
        public static double Eur(double rating, double form, int age, double knownPotential, int yearsLeft, MarketValueConfig c)
        {
            double v = c.BaseEur * Math.Pow(c.Growth, rating - c.PivotRating);
            v *= Math.Max(c.FormMin, Math.Min(c.FormMax, 1 + c.FormSlope * (form - c.FormNeutral)));
            v *= age <= c.AgeFlatUntil ? 1.0 : Math.Pow(c.AgePerYear, age - c.AgeFlatUntil);
            double gap = Math.Max(0, knownPotential - rating);
            if (age <= c.PotentialYoungUntil) v *= Math.Pow(c.Growth, c.PotentialYoungFactor * gap);
            else if (age <= c.PotentialMidUntil) v *= Math.Pow(c.Growth, c.PotentialMidFactor * gap);
            v *= Math.Min(1.0, c.ContractBase + c.ContractPerYear * yearsLeft);
            return v;
        }
    }
}
