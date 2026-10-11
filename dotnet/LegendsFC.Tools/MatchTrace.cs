using System;
using LegendsFC.Core.Data;
using LegendsFC.Core.Match;

/// <summary>Prints what happens in one AI-vs-AI match (possession changes, phases, goals) for tuning.</summary>
public static class MatchTrace
{
    public static void Run(GameData data, ulong seed, int maxLines)
    {
        var setup = new MatchSetup
        {
            Home = LiveMatchFactory.TestTeam(data.Match, "HOME", "4-4-2", 70, seed * 3 + 1),
            Away = LiveMatchFactory.TestTeam(data.Match, "AWAY", "4-4-2", 70, seed * 3 + 2),
            Seed = seed,
            Settings = new MatchSettings { UserSide = -1 }
        };
        var m = new LiveMatch(setup, data.Match);
        var input = new ControlInput();
        int lastOwner = -2; Phase lastPhase = (Phase)(-1); int lines = 0; int lastShots = 0, lastHeaders = 0, lastSaves = 0, lastBlocks = 0, lastPasses = 0;
        int tick = 0;
        while (!m.IsOver && lines < maxLines)
        {
            m.Step(input);
            tick++;
            var b = m.Ball;
            var c = m.Counters;
            string ev = null;
            if (m.Phase != lastPhase) ev = "phase " + m.Phase + " team " + m.RestartTeam;
            else if (c.Shots != lastShots) ev = "SHOT by " + lastOwner;
            else if (c.Headers != lastHeaders) ev = "header";
            else if (c.Saves != lastSaves) ev = "SAVE";
            else if (c.Blocks != lastBlocks) ev = "block";
            else if (c.Passes != lastPasses) ev = "pass by " + lastOwner;
            else if (b.Owner != lastOwner && b.Owner >= 0) ev = "owner " + b.Owner + (b.Owner < 11 ? " (H)" : " (A)");
            lastShots = c.Shots; lastHeaders = c.Headers; lastSaves = c.Saves; lastBlocks = c.Blocks; lastPasses = c.Passes;
            if (b.Owner >= 0 || m.Phase != lastPhase) lastOwner = b.Owner;
            lastPhase = m.Phase;
            if (Environment.GetEnvironmentVariable("TRACE_FROM") is string from && tick * LiveMatch.TickSeconds >= float.Parse(from)
                && tick * LiveMatch.TickSeconds <= float.Parse(from) + 1.5f)
                ev = (ev ?? "") + $" | tick owner {b.Owner} k11 {m.Players[11].Pos} act {m.Players[11].Action}";
            if (ev != null)
            {
                Console.WriteLine($"{tick * LiveMatch.TickSeconds,6:F2}s m{m.Minute,3} {m.HomeGoals}-{m.AwayGoals} ball ({b.Pos.X,6:F1},{b.Pos.Y,6:F1},{b.Pos.Z,4:F1}) v({b.Vel.X,5:F1},{b.Vel.Y,5:F1},{b.Vel.Z,5:F1}) {ev}");
                lines++;
            }
        }
    }
}
