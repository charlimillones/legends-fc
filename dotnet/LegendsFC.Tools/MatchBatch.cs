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
            while (!match.IsOver) match.Step(input);
            res[m] = (match.HomeGoals, match.AwayGoals, match.Counters);
            var rr = match.Result();
            for (int i = 0; i < 22; i++) { km[m, i] = rr.Players[i].Km; en[m, i] = rr.Players[i].Energy; }
        });
        double goals = 0, out_ = 0, shots = 0, saves = 0, passes = 0, tackles = 0, blocks = 0, corners = 0, throws = 0, gk = 0, posts = 0, headers = 0, sot = 0;
        int hw = 0, aw = 0, dr = 0; double cat = 0, par = 0, tip = 0, tw = 0;
        foreach (var r in res)
        {
            goals += r.hg + r.ag;
            if (r.hg > r.ag) hw++; else if (r.ag > r.hg) aw++; else dr++;
            out_ += r.c.OutOfPlayTicks / (double)r.c.ClockTicks;
            shots += r.c.Shots; saves += r.c.Saves; passes += r.c.Passes; tackles += r.c.Tackles; blocks += r.c.Blocks;
            cat += r.c.Catches; par += r.c.Parries; tip += r.c.Tips; tw += r.c.TacklesWon;
            corners += r.c.Corners; throws += r.c.ThrowIns; gk += r.c.GoalKicks; posts += r.c.PostHits; headers += r.c.Headers; sot += r.c.ShotsOnTarget;
        }
        Console.WriteLine($"{n} matches, ratings {ratingHome} v {ratingAway}, {sw.Elapsed.TotalSeconds:F1} s");
        Console.WriteLine($"goals/match {goals / n:F2}  home {100.0 * hw / n:F0}%  draw {100.0 * dr / n:F0}%  away {100.0 * aw / n:F0}%  out of play {100 * out_ / n:F1}%");
        Console.WriteLine($"per match: shots {shots / n:F1} on target {sot / n:F1} saves {saves / n:F2} passes {passes / n:F0} tackles {tackles / n:F1} blocks {blocks / n:F1} headers {headers / n:F1} posts {posts / n:F2}");
        Console.Write("km by slot (home):");
        for (int i = 0; i < 11; i++) { double t = 0; for (int m = 0; m < n; m++) t += km[m, i]; Console.Write($" {t / n:F1}"); }
        Console.WriteLine();
        Console.Write("energy left (home):");
        for (int i = 0; i < 11; i++) { double t = 0; for (int m = 0; m < n; m++) t += en[m, i]; Console.Write($" {t / n:F0}"); }
        Console.WriteLine();
        Console.WriteLine($"restarts: corners {corners / n:F1} throw-ins {throws / n:F1} goal kicks {gk / n:F1}  catches {cat / n:F2} parries {par / n:F2} tips {tip / n:F2} tackles won {tw / n:F1}");
    }
}
