using System;
using LegendsFC.Core.Model;

namespace LegendsFC.Core.Rules
{
    /// <summary>Rating at a position = weighted attributes for that position, then the out-of-position drop (Oct 8).</summary>
    public static class PositionRating
    {
        public static double Base(AttributeSet attrs, Position pos, PositionRatingConfig c)
        {
            var key = WeightKey(pos);
            if (!c.Weights.TryGetValue(key, out var weights)) throw new InvalidOperationException("No rating weights for " + key);
            double sum = 0;
            foreach (var kv in weights)
                sum += attrs[(Attr)Enum.Parse(typeof(Attr), kv.Key)] * kv.Value;
            return sum;
        }

        public static double AtPosition(AttributeSet attrs, Position pos, PositionFitResult fit, PositionRatingConfig c)
            => Base(attrs, pos, c) * (1 - fit.Drop);

        /// <summary>LB/RB, LM/RM and LW/RW share weights.</summary>
        public static string WeightKey(Position p)
        {
            switch (p)
            {
                case Position.LB: case Position.RB: return "FB";
                case Position.LM: case Position.RM: return "WM";
                case Position.LW: case Position.RW: return "W";
                default: return p.ToString();
            }
        }
    }
}
