using System;
using LegendsFC.Core.Data;
using LegendsFC.Core.Match;

/// <summary>Runs AI-vs-AI matches and prints the first tick where any position is NaN or outside the pitch bounds.</summary>
public static class MatchCheck
{
    public static void Run(GameData data, int n, ulong seed0)
    {
        var c = data.Match;
        float px = c.Pitch.Length / 2 + c.Pitch.PlayerMargin + 0.01f, py = c.Pitch.Width / 2 + c.Pitch.PlayerMargin + 0.01f;
        float bx = c.Pitch.Length / 2 + c.Pitch.NetDepth + 1f, by = c.Pitch.Width / 2 + 1f;
        int problems = 0;
        for (int k = 0; k < n; k++)
        {
            ulong seed = seed0 + (ulong)k;
            var setup = new MatchSetup
            {
                Home = LiveMatchFactory.TestTeam(c, "HOME", "4-4-2", 70, seed * 2 + 1),
                Away = LiveMatchFactory.TestTeam(c, "AWAY", "4-4-2", 70, seed * 2 + 2),
                Seed = seed, Settings = new MatchSettings { UserSide = -1 }
            };
            var m = new LiveMatch(setup, c);
            int t = 0;
            while (!m.IsOver)
            {
                m.Step(new ControlInput());
                t++;
                var b = m.Ball;
                string bad = null;
                for (int i = 0; i < 22; i++)
                {
                    var p = m.Players[i];
                    if (float.IsNaN(p.Pos.X) || float.IsNaN(p.Pos.Y) || MathF.Abs(p.Pos.X) > px || MathF.Abs(p.Pos.Y) > py) bad = $"player {i} at {p.Pos}";
                }
                if (float.IsNaN(b.Pos.X) || MathF.Abs(b.Pos.X) > bx || MathF.Abs(b.Pos.Y) > by || b.Pos.Z < 0 || b.Pos.Z > 60) bad = $"ball at {b.Pos} vel {b.Vel} owner {b.Owner}";
                if (bad != null)
                {
                    Console.WriteLine($"seed {seed} tick {t} phase {m.Phase}: {bad}");
                    if (++problems > 20) return;
                    break;
                }
            }
        }
        Console.WriteLine($"done, {problems} matches with problems");
    }
}
