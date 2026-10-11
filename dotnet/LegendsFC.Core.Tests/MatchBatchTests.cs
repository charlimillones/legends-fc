using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LegendsFC.Core.Match;
using Xunit;
using Xunit.Abstractions;

/// <summary>Done checks 2 and 3 (brief Oct 11): 200-match batches, strength, speed and zero allocations.</summary>
public class MatchBatchTests
{
    private readonly ITestOutputHelper _out;
    public MatchBatchTests(ITestOutputHelper output) { _out = output; }

    private static MatchConfig C => TestData.Data.Match;

    private sealed class Outcome
    {
        public int Home, Away, Saves, BadTicks;
        public double OutOfPlay;
        public bool FullTime;
    }

    private static Outcome Play(MatchConfig c, int homeRating, int awayRating, ulong seed)
    {
        var setup = new MatchSetup
        {
            Home = LiveMatchFactory.TestTeam(c, "HOME", "4-4-2", homeRating, seed * 2 + 1),
            Away = LiveMatchFactory.TestTeam(c, "AWAY", "4-4-2", awayRating, seed * 2 + 2),
            Seed = seed,
            Settings = new MatchSettings { UserSide = -1 }
        };
        var m = new LiveMatch(setup, c);
        var input = new ControlInput();
        int bad = 0;
        float px = c.Pitch.Length / 2 + c.Pitch.PlayerMargin + 0.01f, py = c.Pitch.Width / 2 + c.Pitch.PlayerMargin + 0.01f;
        float bx = c.Pitch.Length / 2 + c.Pitch.NetDepth + 1f, by = c.Pitch.Width / 2 + 1f;
        for (int t = 0; t < 40000 && !m.IsOver; t++)
        {
            m.Step(input);
            var ps = m.Players;
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                if (!Finite(p.Pos.X) || !Finite(p.Pos.Y) || !Finite(p.Vel.X) || !Finite(p.Vel.Y) || !Finite(p.Facing) || MathF.Abs(p.Pos.X) > px || MathF.Abs(p.Pos.Y) > py) bad++;
            }
            var b = m.Ball;
            if (!Finite(b.Pos.X) || !Finite(b.Pos.Y) || !Finite(b.Pos.Z) || !Finite(b.Vel.X) || !Finite(b.Vel.Y) || !Finite(b.Vel.Z)
                || MathF.Abs(b.Pos.X) > bx || MathF.Abs(b.Pos.Y) > by || b.Pos.Z < 0 || b.Pos.Z > 60) bad++;
        }
        return new Outcome
        {
            Home = m.HomeGoals, Away = m.AwayGoals, Saves = m.Counters.Saves, BadTicks = bad, FullTime = m.Phase == Phase.FullTime && m.IsOver,
            OutOfPlay = m.Counters.OutOfPlayTicks / (double)m.Counters.ClockTicks
        };
    }

    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    [Fact]
    public void TwoHundredMatchesBetweenEqualTeamsLookLikeFootball()
    {
        var c = C;
        const int n = 200;
        var res = new Outcome[n];
        var sw = Stopwatch.StartNew();
        Parallel.For(0, n, i => res[i] = Play(c, 70, 70, 1000 + (ulong)i));
        sw.Stop();
        int fullTime = 0, home = 0, away = 0, bad = 0; double goals = 0, outOfPlay = 0, saves = 0;
        foreach (var r in res)
        {
            if (r.FullTime) fullTime++;
            if (r.Home > r.Away) home++; else if (r.Away > r.Home) away++;
            goals += r.Home + r.Away; outOfPlay += r.OutOfPlay; saves += r.Saves; bad += r.BadTicks;
        }
        _out.WriteLine($"{n} matches in {sw.Elapsed.TotalSeconds:F1} s: full time {fullTime}, goals/match {goals / n:F2}, home wins {100.0 * home / n:F1}%, away wins {100.0 * away / n:F1}%, draws {100.0 * (n - home - away) / n:F1}%, out of play {100 * outOfPlay / n:F1}%, saves/match {saves / n:F2}, bad positions {bad}");
        Assert.Equal(n, fullTime);
        Assert.InRange(goals / n, 1.5, 4.5);
        Assert.InRange(home / (double)n, 0.30, 0.50);
        Assert.InRange(away / (double)n, 0.30, 0.50);
        Assert.True(outOfPlay / n < 0.35);
        Assert.True(saves / n >= 1.0);
        Assert.Equal(0, bad);
    }

    [Fact]
    public void AFifteenPointsStrongerTeamWinsMostMatches()
    {
        var c = C;
        const int n = 200;
        int strong = 0, weak = 0;
        Parallel.For(0, n, i =>
        {
            bool strongHome = i % 2 == 0;
            var r = Play(c, strongHome ? 77 : 62, strongHome ? 62 : 77, 5000 + (ulong)i);
            int s = strongHome ? r.Home : r.Away, w = strongHome ? r.Away : r.Home;
            if (s > w) Interlocked.Increment(ref strong); else if (w > s) Interlocked.Increment(ref weak);
        });
        _out.WriteLine($"77 v 62 over {n} matches: stronger wins {100.0 * strong / n:F1}%, weaker wins {100.0 * weak / n:F1}%, draws {100.0 * (n - strong - weak) / n:F1}%");
        Assert.True(strong / (double)n > 0.55);
    }

    [Fact]
    public void AFullMatchRunsFastAndStepDoesNotAllocate()
    {
        var c = C;
        Play(c, 70, 70, 1);   // warm up the JIT
        // best of 3 (other test classes run in parallel and can steal the CPU)
        double best = double.MaxValue; int ticks = 0;
        for (int run = 0; run < 3; run++)
        {
            var m = new LiveMatch(new MatchSetup
            {
                Home = LiveMatchFactory.TestTeam(c, "HOME", "4-3-3", 70, 3),
                Away = LiveMatchFactory.TestTeam(c, "AWAY", "3-5-2", 70, 4),
                Seed = 42, Settings = new MatchSettings { UserSide = 0 }
            }, c);
            var input = new ControlInput { MoveX = 0.7f, MoveY = 0.2f };
            var sw = Stopwatch.StartNew();
            ticks = 0;
            while (!m.IsOver) { m.Step(input); ticks++; }
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalSeconds);
        }
        _out.WriteLine($"full match: {ticks} ticks in {best * 1000:F0} ms (best of 3)");
        Assert.True(best < 1.5);

        var a = new LiveMatch(new MatchSetup
        {
            Home = LiveMatchFactory.TestTeam(c, "HOME", "4-4-2", 70, 5),
            Away = LiveMatchFactory.TestTeam(c, "AWAY", "4-2-3-1", 70, 6),
            Seed = 7, Settings = new MatchSettings { UserSide = 0 }
        }, c);
        for (int i = 0; i < 1000; i++) a.Step(new ControlInput { MoveX = MathF.Sin(i * 0.05f), A = i % 50 < 5, B = i % 170 < 12 });
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) a.Step(new ControlInput { MoveX = MathF.Sin(i * 0.05f), MoveY = 0.3f, Sprint = i % 2 == 0, A = i % 50 < 5, B = i % 170 < 12, C = i % 90 < 30 });
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        _out.WriteLine($"allocated across 1,000 steps after warm-up: {allocated} bytes");
        Assert.Equal(0, allocated);
    }
}
