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
        private int[] _values = new int[Count];

        /// <summary>Raw values in <see cref="Attr"/> order (used by saves/JSON). Always 15 values, each 1-99.</summary>
        public int[] Values
        {
            get => (int[])_values.Clone();
            set
            {
                if (value == null || value.Length != Count) throw new ArgumentException("Need exactly 15 attribute values");
                foreach (var v in value) if (v < Min || v > Max) throw new ArgumentOutOfRangeException(nameof(value), "Attributes must be 1-99");
                _values = (int[])value.Clone();
            }
        }

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
