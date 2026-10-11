using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using LegendsFC.Core.Match;
using Newtonsoft.Json.Linq;
using Xunit;

/// <summary>Ball physics and the match config (data/config/match.json).</summary>
public class MatchPhysicsTests
{
    private static MatchConfig C => TestData.Data.Match;
    private const float Tick = 0.02f;   // LiveMatch.TickSeconds

    /// <summary>Steps a loose ball like the engine (50 Hz, sub-steps) until it first touches the grass after flying.</summary>
    private static float FlyToFirstBounce(Vec3 pos, Vec3 vel, out float time)
    {
        time = 0;
        for (int i = 0; i < 2000; i++)
        {
            for (int s = 0; s < 4; s++)
            {
                if (BallPhysics.Step(ref pos, ref vel, Tick / 4, C.Ball)) return pos.X;
            }
            time += Tick;
        }
        return float.NaN;
    }

    [Theory]
    [InlineData(20f, 32f)]
    [InlineData(30f, 32f)]
    [InlineData(45f, 40f)]
    public void LoftedBallLandsWhereAimed(float distance, float angleDeg)
    {
        float a = angleDeg * MathUtil.Deg;
        float speed = BallPhysics.LoftSpeedFor(distance, a, C.Ball.Radius, C.Ball, C.Kicks.LoftSolveMaxSpeed, out float flight);
        float landed = FlyToFirstBounce(new Vec3(0, 0, C.Ball.Radius + 0.001f), new Vec3(speed * MathF.Cos(a), 0, speed * MathF.Sin(a)), out float t);
        Assert.InRange(landed, distance - 0.75f, distance + 0.75f);
        Assert.InRange(t, flight - 0.1f, flight + 0.1f);
        // drag makes it need more speed than a vacuum throw would (v^2 = d g / sin 2a)
        float vacuum = MathF.Sqrt(distance * C.Ball.Gravity / MathF.Sin(2 * a));
        Assert.True(speed > vacuum, $"speed {speed} vs vacuum {vacuum}");
    }

    [Fact]
    public void RollingBallSlowsAndStops()
    {
        var pos = new Vec3(0, 0, C.Ball.Radius);
        var vel = new Vec3(10, 0, 0);
        float t = 0, lastSpeed = 10;
        while (vel.LengthSq > 0 && t < 30)
        {
            BallPhysics.Step(ref pos, ref vel, Tick, C.Ball);
            float sp = vel.Length;
            Assert.True(sp <= lastSpeed + 1e-4f);
            lastSpeed = sp;
            t += Tick;
        }
        Assert.Equal(0f, vel.Length);
        Assert.True(t < 8f, "stops within 8 s, took " + t);
        float expected = BallPhysics.RollDistance(10, C.Ball);
        Assert.InRange(pos.X, expected * 0.95f, expected * 1.02f);
        Assert.Equal((double)C.Ball.Radius, (double)pos.Z, 4);
    }

    [Fact]
    public void GroundPassArrivesWithTheWantedSpeed()
    {
        float d = 25f, arrive = C.Kicks.PassArrivalSpeed;
        float v0 = BallPhysics.GroundSpeedFor(d, arrive, C.Ball);
        var pos = new Vec3(0, 0, C.Ball.Radius);
        var vel = new Vec3(v0, 0, 0);
        while (pos.X < d && vel.X > 0) BallPhysics.Step(ref pos, ref vel, 0.005f, C.Ball);
        Assert.InRange(vel.X, arrive - 0.3f, arrive + 0.3f);
    }

    [Fact]
    public void DroppedBallBouncesLowerAndEndsRolling()
    {
        var pos = new Vec3(0, 0, 2f);
        var vel = new Vec3(1, 0, 0);
        float peak = 2f, cur = 0; int bounces = 0; bool rising = false;
        for (int i = 0; i < 1000; i++)
        {
            if (BallPhysics.Step(ref pos, ref vel, Tick, C.Ball)) bounces++;
            if (vel.Z > 0) { rising = true; cur = Math.Max(cur, pos.Z); }
            else if (rising) { Assert.True(cur < peak); peak = cur; cur = 0; rising = false; }
        }
        Assert.True(bounces >= 2);
        Assert.True(BallPhysics.IsRolling(pos, vel, C.Ball));
    }

    [Fact]
    public void ConfigLoadsWithEightFormationsMatchingSquadSlots()
    {
        var d = TestData.Data;
        var C = d.Match;   // the same loaded instance throughout (TestData may load twice when tests run in parallel)
        Assert.Equal(d.Squad.Formations.Count, C.Formations.Count);
        foreach (var kv in d.Squad.Formations)
        {
            Assert.True(C.Formations.ContainsKey(kv.Key), kv.Key);
            Assert.Equal(kv.Value, C.Formations[kv.Key].Select(s => s.Slot).ToList());
            Assert.All(C.Formations[kv.Key], s => Assert.InRange(s.X, -52.5f, 0f));
        }
        Assert.Equal(4, C.Difficulty.Count);
        Assert.Equal(2, new MatchSettings().Difficulty);
        Assert.Equal(150f, C.Clock.HalfSeconds);
        Assert.Equal(50f, C.Clock.ExtraHalfSeconds);
        Assert.Equal("Pass", C.Buttons.Attack["A"]);
        Assert.Equal("Switch", C.Buttons.Defence["A"]);
        Assert.Same(d.Squad, C.Squad);
        Assert.False(string.IsNullOrEmpty(JObject.Parse(File.ReadAllText(Path.Combine(TestData.Root, "config", "match.json")))["_source"]?.ToString()));
    }
}
