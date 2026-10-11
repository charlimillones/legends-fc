using System;

namespace LegendsFC.Core.Match
{
    /// <summary>
    /// Ball flight and roll: gravity, air drag (∝ speed²), bounce with restitution and grass friction, rolling friction.
    /// Pure functions on value types, so the engine and the AI's predictions share them (no allocations).
    /// </summary>
    public static class BallPhysics
    {
        /// <summary>True when the ball sits on the grass and doesn't move up or down.</summary>
        public static bool IsRolling(in Vec3 pos, in Vec3 vel, BallConfig c) => pos.Z <= c.Radius + 1e-4f && vel.Z == 0f;

        /// <summary>Advances a loose ball by dt (one sub-step). Returns true if it bounced.</summary>
        public static bool Step(ref Vec3 pos, ref Vec3 vel, float dt, BallConfig c)
        {
            if (IsRolling(pos, vel, c))
            {
                float h = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
                if (h < c.StopSpeed) { vel = Vec3.Zero; pos.Z = c.Radius; return false; }
                float dec = c.RollingDecel + c.AirDrag * h * h;
                float nh = h - dec * dt;
                if (nh < 0) nh = 0;
                float k = nh / h;
                float avg = (h + nh) * 0.5f / h;
                pos.X += vel.X * avg * dt; pos.Y += vel.Y * avg * dt;
                vel.X *= k; vel.Y *= k;
                return false;
            }
            float speed = vel.Length;
            float drag = c.AirDrag * speed;
            vel.X -= drag * vel.X * dt;
            vel.Y -= drag * vel.Y * dt;
            vel.Z -= (drag * vel.Z + c.Gravity) * dt;
            pos.X += vel.X * dt; pos.Y += vel.Y * dt; pos.Z += vel.Z * dt;
            if (pos.Z <= c.Radius)
            {
                pos.Z = c.Radius;
                if (-vel.Z > c.RollVz)
                {
                    vel.Z = -vel.Z * c.Restitution;
                    vel.X *= c.BounceFriction; vel.Y *= c.BounceFriction;
                }
                else vel.Z = 0f;
                return true;
            }
            return false;
        }

        /// <summary>Ground pass: the launch speed that arrives at distance s with speed arrive (closed form for roll + drag).</summary>
        public static float GroundSpeedFor(float s, float arrive, BallConfig c)
        {
            float a = c.RollingDecel, k = c.AirDrag;
            float v2 = ((a + k * arrive * arrive) * MathF.Exp(2 * k * s) - a) / k;
            return MathF.Sqrt(Math.Max(0, v2));
        }

        /// <summary>How far a ball rolling at v0 travels before it stops (closed form, ignoring the stop threshold).</summary>
        public static float RollDistance(float v0, BallConfig c)
        {
            float a = c.RollingDecel, k = c.AirDrag;
            return MathF.Log((a + k * v0 * v0) / a) / (2 * k);
        }

        /// <summary>Horizontal distance to the first bounce of a ball launched at speed and elevation (radians) from height z0.</summary>
        public static float CarryDistance(float speed, float elevation, float z0, BallConfig c, out float flightTime)
        {
            var pos = new Vec3(0, 0, Math.Max(z0, c.Radius + 0.001f));
            var vel = new Vec3(speed * MathF.Cos(elevation), 0, speed * MathF.Sin(elevation));
            const float dt = 0.02f;
            flightTime = 0;
            for (int i = 0; i < 1000; i++)
            {
                flightTime += dt;
                if (Step(ref pos, ref vel, dt, c)) return pos.X;
                if (vel.Z == 0 && pos.Z <= c.Radius + 1e-4f) return pos.X;
            }
            return pos.X;
        }

        /// <summary>The launch speed for a lofted ball at this elevation to land (first bounce) at distance (bisection).</summary>
        public static float LoftSpeedFor(float distance, float elevation, float z0, BallConfig c, float maxSpeed, out float flightTime)
        {
            float lo = 1f, hi = maxSpeed;
            flightTime = 0;
            for (int i = 0; i < 16; i++)
            {
                float mid = (lo + hi) * 0.5f;
                float d = CarryDistance(mid, elevation, z0, c, out flightTime);
                if (d < distance) lo = mid; else hi = mid;
            }
            float v = (lo + hi) * 0.5f;
            CarryDistance(v, elevation, z0, c, out flightTime);
            return v;
        }
    }
}
