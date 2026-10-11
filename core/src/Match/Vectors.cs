using System;

namespace LegendsFC.Core.Match
{
    /// <summary>A point or direction on the pitch plane (metres). X along the length (-52.5..52.5), Y across (-34..34).</summary>
    public struct Vec2
    {
        public float X, Y;

        public Vec2(float x, float y) { X = x; Y = y; }

        public static readonly Vec2 Zero = new Vec2(0, 0);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float k) => new Vec2(a.X * k, a.Y * k);
        public static Vec2 operator *(float k, Vec2 a) => new Vec2(a.X * k, a.Y * k);
        public static Vec2 operator /(Vec2 a, float k) => new Vec2(a.X / k, a.Y / k);

        public float LengthSq => X * X + Y * Y;
        public float Length => MathF.Sqrt(X * X + Y * Y);

        /// <summary>Unit vector in the same direction (zero stays zero).</summary>
        public Vec2 Normalized()
        {
            float l = Length;
            return l > 1e-6f ? new Vec2(X / l, Y / l) : Zero;
        }

        /// <summary>Angle in radians (atan2(Y, X)).</summary>
        public float Angle => MathF.Atan2(Y, X);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static float DistanceSq(Vec2 a, Vec2 b) => (a - b).LengthSq;
        public static Vec2 FromAngle(float radians) => new Vec2(MathF.Cos(radians), MathF.Sin(radians));
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        /// <summary>Rotates by an angle in radians (counter-clockwise).</summary>
        public Vec2 Rotated(float radians)
        {
            float c = MathF.Cos(radians), s = MathF.Sin(radians);
            return new Vec2(X * c - Y * s, X * s + Y * c);
        }

        /// <summary>Same direction, length at most max.</summary>
        public Vec2 ClampLength(float max)
        {
            float l2 = LengthSq;
            if (l2 <= max * max || l2 < 1e-12f) return this;
            float k = max / MathF.Sqrt(l2);
            return new Vec2(X * k, Y * k);
        }

        public override string ToString() => "(" + X.ToString("F2") + ", " + Y.ToString("F2") + ")";
    }

    /// <summary>A point or velocity in 3D (metres). Z = height above the grass.</summary>
    public struct Vec3
    {
        public float X, Y, Z;

        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public Vec3(Vec2 xy, float z) { X = xy.X; Y = xy.Y; Z = z; }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);

        /// <summary>The pitch-plane part.</summary>
        public Vec2 XY => new Vec2(X, Y);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, float k) => new Vec3(a.X * k, a.Y * k, a.Z * k);
        public static Vec3 operator *(float k, Vec3 a) => new Vec3(a.X * k, a.Y * k, a.Z * k);

        public float LengthSq => X * X + Y * Y + Z * Z;
        public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);
        public float HorizontalLength => MathF.Sqrt(X * X + Y * Y);

        public override string ToString() => "(" + X.ToString("F2") + ", " + Y.ToString("F2") + ", " + Z.ToString("F2") + ")";
    }

    /// <summary>Small float helpers (no allocations).</summary>
    public static class MathUtil
    {
        public const float Deg = MathF.PI / 180f;

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Wraps an angle to -pi..pi.</summary>
        public static float WrapAngle(float a)
        {
            while (a > MathF.PI) a -= 2 * MathF.PI;
            while (a < -MathF.PI) a += 2 * MathF.PI;
            return a;
        }

        /// <summary>Turns angle 'from' towards 'to' by at most maxStep radians.</summary>
        public static float TurnTowards(float from, float to, float maxStep)
        {
            float d = WrapAngle(to - from);
            if (d > maxStep) d = maxStep; else if (d < -maxStep) d = -maxStep;
            return WrapAngle(from + d);
        }

        /// <summary>Distance from point p to the segment a-b, and the segment parameter (0..1) of the closest point.</summary>
        public static float SegmentDistance(Vec2 p, Vec2 a, Vec2 b, out float t)
        {
            Vec2 ab = b - a;
            float l2 = ab.LengthSq;
            t = l2 < 1e-9f ? 0 : Clamp01(Vec2.Dot(p - a, ab) / l2);
            return Vec2.Distance(p, a + ab * t);
        }

        public static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));
    }
}
