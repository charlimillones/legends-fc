using System;

namespace LegendsFC.Core.Util
{
    /// <summary>
    /// The only source of randomness in the core (architecture rule 4). xoshiro256** seeded with SplitMix64:
    /// identical results on every platform (Unity/iOS, dotnet, CI) for the same seed.
    /// </summary>
    public sealed class GameRandom
    {
        private ulong _s0, _s1, _s2, _s3;

        public GameRandom(ulong seed)
        {
            ulong x = seed;
            _s0 = SplitMix(ref x); _s1 = SplitMix(ref x); _s2 = SplitMix(ref x); _s3 = SplitMix(ref x);
        }

        private static ulong SplitMix(ref ulong x)
        {
            ulong z = (x += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        public ulong NextUInt64()
        {
            ulong result = Rotl(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;
            _s2 ^= _s0; _s3 ^= _s1; _s1 ^= _s2; _s0 ^= _s3; _s2 ^= t; _s3 = Rotl(_s3, 45);
            return result;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

        /// <summary>Uniform integer in [min, maxInclusive].</summary>
        public int NextInt(int min, int maxInclusive)
        {
            if (maxInclusive < min) throw new ArgumentException("max < min");
            ulong range = (ulong)((long)maxInclusive - min + 1);
            return (int)(min + (long)(NextUInt64() % range));
        }

        public double Uniform(double min, double max) => min + (max - min) * NextDouble();

        public bool Chance(double p) => NextDouble() < p;

        /// <summary>Normal distribution (Box-Muller).</summary>
        public double Gaussian(double mean, double sd)
        {
            double u1 = 1.0 - NextDouble(), u2 = NextDouble();
            return mean + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
