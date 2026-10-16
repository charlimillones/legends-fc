using System;
using System.Diagnostics;
using System.Threading.Tasks;
using LegendsFC.Core.Data;
using LegendsFC.Core.Match;

/// <summary>Headless batch of AI-vs-AI real-time matches: goals, results, out-of-play time, shots and saves.</summary>
public static class MatchBatch
{
    public static void Run(GameData data, int n, int ratingHome, int ratingAway, ulong seed)
    {
        var sw = Stopwatch.StartNew();
        var res = new (int hg, int ag, MatchCounters c)[n];
        var km = new double[n, 22]; var en = new double[n, 22];
        // compactness while defending in its own third (opponent has the ball): sums of depth/width and the tick count
        var blk = new double[n, 8];
        Parallel.For(0, n, m =>
        {
            ulong s = seed + (ulong)m * 7919UL;
            var setup = new MatchSetup
            {
                Home = LiveMatchFactory.TestTeam(data.Match, "HOME", "4-4-2", ratingHome, s * 3 + 1),
                Away = LiveMatchFactory.TestTeam(data.Match, "AWAY", "4-4-2", ratingAway, s * 3 + 2),
                Seed = s,
                Settings = new MatchSettings { UserSide = -1 }
            };
            var match = new LiveMatch(setup, data.Match);
            var input = new ControlInput();
            while (!match.IsOver)
            {
                match.Step(input);
                var ball = match.Ball;
                if (match.Phase != Phase.Play || ball.Owner < 0) continue;
                int def = ball.Owner < 11 ? 1 : 0;
                float bx = ball.Pos.X * match.AttackDirection(def);
                if (bx > -data.Match.Pitch.Length / 6f) continue;   // only while the ball is in the third nearest its own goal
                match.BlockSize(def, false, out float d, out float w);
                match.BlockSize(def, true, out float d10, out float w10);
                blk[m, 0] += d; blk[m, 1] += w; blk[m, 2] += d10; blk[m, 3] += w10; blk[m, 4] += 1;
                // openness: attackers in the defenders' box with no defender within 3 m, and the carrier's space
                var ps = match.Players;
                int free = 0; float carrierSpace = float.MaxValue;
                float gx = -match.AttackDirection(def) * data.Match.Pitch.Length / 2f;
                for (int a = 0; a < 11; a++)
                {
                    int ai = (1 - def) * 11 + a;
                    var ap = ps[ai].Pos;
                    float nearest = float.MaxValue;
                    for (int q = 0; q < 11; q++)
                    {
                        int di = def * 11 + q;
                        if (ps[di].IsKeeper) continue;
                        float dd = (ps[di].Pos - ap).Length;
                        if (dd < nearest) nearest = dd;
                    }
                    if (ai == ball.Owner) { carrierSpace = nearest; continue; }
                    bool inBox = MathF.Abs(ap.X - gx) < data.Match.Pitch.PenaltyBoxDepth && MathF.Abs(ap.Y) < data.Match.Pitch.PenaltyBoxWidth / 2f && ap.X * gx > 0;
                    if (inBox && nearest > 3f) free++;
                }
                blk[m, 5] += free; blk[m, 6] += carrierSpace;
            }
            res[m] = (match.HomeGoals, match.AwayGoals, match.Counters);
            var rr = match.Result();
            for (int i = 0; i < 22; i++) { km[m, i] = rr.Players[i].Km; en[m, i] = rr.Players[i].Energy; }
        });
        double lunges = 0, goals = 0, out_ = 0, shots = 0, saves = 0, passes = 0, tackles = 0, blocks = 0, corners = 0, throws = 0, gk = 0, posts = 0, headers = 0, sot = 0;
        int hw = 0, aw = 0, dr = 0; double cat = 0, par = 0, tip = 0, tw = 0, cr = 0, th = 0, lth = 0, lp = 0, offs = 0, fks = 0;
        foreach (var r in res)
        {
            goals += r.hg + r.ag;
            if (r.hg > r.ag) hw++; else if (r.ag > r.hg) aw++; else dr++;
            out_ += r.c.OutOfPlayTicks / (double)r.c.ClockTicks;
            lunges += r.c.TackleLunges; shots += r.c.Shots; saves += r.c.Saves; passes += r.c.Passes; tackles += r.c.Tackles; blocks += r.c.Blocks;
            cat += r.c.Catches; par += r.c.Parries; tip += r.c.Tips; tw += r.c.TacklesWon; cr += r.c.Crosses; th += r.c.ThroughBalls; lth += r.c.LoftedThroughBalls; lp += r.c.LoftedPasses;
            offs += r.c.Offsides; fks += r.c.FreeKicks; corners += r.c.Corners; throws += r.c.ThrowIns; gk += r.c.GoalKicks; posts += r.c.PostHits; headers += r.c.Headers; sot += r.c.ShotsOnTarget;
        }
        Console.WriteLine($"{n} matches, ratings {ratingHome} v {ratingAway}, {sw.Elapsed.TotalSeconds:F1} s");
        Console.WriteLine($"goals/match {goals / n:F2}  home {100.0 * hw / n:F0}%  draw {100.0 * dr / n:F0}%  away {100.0 * aw / n:F0}%  out of play {100 * out_ / n:F1}%");
        Console.WriteLine($"per match: shots {shots / n:F1} on target {sot / n:F1} saves {saves / n:F2} passes {passes / n:F0} tackles {tackles / n:F1} blocks {blocks / n:F1} headers {headers / n:F1} posts {posts / n:F2}");
        Console.WriteLine($"pass kinds per match: crosses {cr / n:F1} through {th / n:F1} lofted through {lth / n:F1} lofted {lp / n:F1}");
        Console.Write("km by slot (home):");
        for (int i = 0; i < 11; i++) { double t = 0; for (int m = 0; m < n; m++) t += km[m, i]; Console.Write($" {t / n:F1}"); }
        Console.WriteLine();
        Console.Write("energy left (home):");
        for (int i = 0; i < 11; i++) { double t = 0; for (int m = 0; m < n; m++) t += en[m, i]; Console.Write($" {t / n:F0}"); }
        Console.WriteLine();
        double bd = 0, bw = 0, bd10 = 0, bw10 = 0, bt = 0, bf = 0, bs = 0;
        for (int m = 0; m < n; m++) { bd += blk[m, 0]; bw += blk[m, 1]; bd10 += blk[m, 2]; bw10 += blk[m, 3]; bt += blk[m, 4]; bf += blk[m, 5]; bs += blk[m, 6]; }
        if (bt > 0) Console.WriteLine($"defending in own third ({bt * LiveMatch.TickSeconds / n:F0} s per match): block (defenders + midfielders) depth {bd / bt:F1} m width {bw / bt:F1} m; all 10 outfield depth {bd10 / bt:F1} m width {bw10 / bt:F1} m; unmarked attackers in the box (nobody within 3 m) {bf / bt:F2}; carrier's space to the nearest defender {bs / bt:F1} m");
        Console.WriteLine($"restarts: corners {corners / n:F1} throw-ins {throws / n:F1} goal kicks {gk / n:F1} offsides {offs / n:F2} free kicks {fks / n:F2}  catches {cat / n:F2} parries {par / n:F2} tips {tip / n:F2} tackles won {tw / n:F1} tackle lunges {lunges / n:F1}");
    }
}
