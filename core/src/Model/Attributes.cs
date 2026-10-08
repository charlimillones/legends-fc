using System;

namespace LegendsFC.Core.Model
{
    /// <summary>11 outfield + 4 goalkeeper attributes (confirmed Oct 7). Every player stores all 15:
    /// goalkeepers have hidden low outfield values and outfield players hidden low GK values (Oct 8).</summary>
    public enum Attr
    {
        Pace, Acceleration, Stamina, Strength, Dribbling, Passing, Crossing, Shooting, Heading, Tackling, Positioning,
        Reflexes, Handling, RushingOut, Distribution
    }

    public sealed class AttributeSet
    {
        public const int Count = 15;
        public const int Min = 1, Max = 99;
        private readonly int[] _values = new int[Count];

        public int this[Attr a]
        {
            get => _values[(int)a];
            set
            {
                if (value < Min || value > Max) throw new ArgumentOutOfRangeException(nameof(value), a + " must be 1-99");
                _values[(int)a] = value;
            }
        }

        public static AttributeSet Uniform(int value)
        {
            var s = new AttributeSet();
            foreach (Attr a in Enum.GetValues(typeof(Attr))) s[a] = value;
            return s;
        }
    }
}
