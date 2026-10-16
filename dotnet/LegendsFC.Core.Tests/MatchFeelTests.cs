using System;
using LegendsFC.Core.Match;
using Xunit;

/// <summary>Carlos Oct 15 playtest: switching picks the right defender, B makes the player tackle, the defence is compact.
/// Home attacks +X in the first half, so the home team's own goal is at x = -52.5.</summary>
public class MatchFeelTests
{
    private static MatchConfig C => TestData.Data.Match;

    private static LiveMatch NewMatch(int userSide = 0, float sensitivity = 0f, ulong seed = 1, Action<TeamSetup, TeamSetup> tweak = null)
    {
        var c = C;
        var home = LiveMatchFactory.TestTeam(c, "HOME", "4-4-2", 70, 11);
        var away = LiveMatchFactory.TestTeam(c, "AWAY", "4-4-2", 70, 12);
        tweak?.Invoke(home, away);
        var m = new LiveMatch(new MatchSetup
        {
            CompetitionId = "TEST", Home = home, Away = away, Seed = seed,
            Settings = new MatchSettings { UserSide = userSide, SwitchSensitivity = sensitivity }
        }, c);
        m.TestStartPlay();
        return m;
    }

    /// <summary>Everyone far away from the action, in a line (keepers included).</summary>
    private static void Park(LiveMatch m, float homeX, float awayX)
    {
        for (int i = 0; i < 22; i++) m.TestSetPlayer(i, new Vec2(i < 11 ? homeX : awayX, -30f + (i % 11) * 6f), 0f);
    }

    private static void Run(LiveMatch m, ControlInput input, int ticks)
    {
        for (int i = 0; i < ticks && !m.IsOver; i++) m.Step(input);
    }

    private static void PressA(LiveMatch m)
    {
        m.Step(new ControlInput { A = true });
        m.Step(new ControlInput());
    }

    // ------------------------------------------------------------------ switching

    [Fact]
    public void PressingADefendingPicksTheDefenderGoalSideOfTheBallNotTheNearestOneBehindThePlay()
    {
        var m = NewMatch();
        Park(m, 40f, 45f);                                  // home players parked far behind the play
        m.TestSetPlayer(20, new Vec2(-20f, 0f), MathF.PI); // away carrier in our half, running at our goal
        m.TestSetBall(new Vec3(-20f - C.Movement.DribbleOffset, 0, C.Ball.Radius), Vec3.Zero, 20);
        m.TestSetPlayer(6, new Vec2(-15.5f, 1f), 0f);      // nearest to the ball, but 4.5 m behind it (behind the play)
        m.TestSetPlayer(3, new Vec2(-29f, 4f), 0f);        // further away, but between the ball and our goal
        m.TestSetPlayer(9, new Vec2(30f, 0f), 0f);
        m.Step(new ControlInput());
        int before = m.ControlledIndex;
        Assert.NotEqual(3, before);
        PressA(m);
        Assert.Equal(3, m.ControlledIndex);
        Assert.True(m.Players[3].Pos.X < m.Ball.Pos.X, "the chosen player is goal-side of the ball");
    }

    [Fact]
    public void PressingAAgainCyclesToTheNextCandidateInsteadOfBouncingBack()
    {
        var m = NewMatch();
        Park(m, 40f, 45f);
        m.TestSetPlayer(20, new Vec2(-15f, 0f), MathF.PI);
        m.TestSetBall(new Vec3(-15f - C.Movement.DribbleOffset, 0, C.Ball.Radius), Vec3.Zero, 20);
        m.TestSetPlayer(2, new Vec2(-22f, 2f), 0f);        // best placed
        m.TestSetPlayer(3, new Vec2(-25f, -6f), 0f);       // second
        m.TestSetPlayer(7, new Vec2(-30f, 12f), 0f);       // third
        m.TestSetPlayer(9, new Vec2(30f, 0f), 0f);
        m.Step(new ControlInput());
        PressA(m);
        int first = m.ControlledIndex;
        PressA(m);
        int second = m.ControlledIndex;
        PressA(m);
        int third = m.ControlledIndex;
        Assert.Equal(2, first);
        Assert.NotEqual(first, second);
        Assert.Equal(3, second);
        Assert.NotEqual(second, third);
        Assert.Equal(7, third);
        for (int k = 0; k < 12; k++) { PressA(m); Assert.NotEqual(m.TestKeeper(0), m.ControlledIndex); }
    }

    [Fact]
    public void AutoSwitchOnAnOpponentPassLooksAtTheReceiverNotWhereThePassCameFrom()
    {
        var m = NewMatch(sensitivity: 1f);
        Park(m, 40f, 45f);
        m.TestSetPlayer(19, new Vec2(-8f, 22f), MathF.PI);   // away passer, on our left
        m.TestSetPlayer(20, new Vec2(-34f, -16f), MathF.PI); // away receiver, deep on our right
        m.TestSetPlayer(5, new Vec2(-11f, 20f), 0f);         // home: next to the passer
        m.TestSetPlayer(4, new Vec2(-41f, -13f), 0f);        // home: goal-side of the receiver
        m.TestSetBall(new Vec3(-8.6f, 22f, C.Ball.Radius), Vec3.Zero, 19);
        m.Step(new ControlInput());
        PressA(m);
        Assert.Equal(5, m.ControlledIndex);                  // best placed while the passer has the ball
        var to = new Vec2(-34f, -16f) - new Vec2(-8.6f, 22f);
        var v = to.Normalized() * 20f;
        m.TestKick(19, new Vec3(v.X, v.Y, 0), receiver: 20);
        Run(m, new ControlInput(), 25);
        Assert.Equal(4, m.ControlledIndex);
    }

    [Fact]
    public void WithNoInputTheControlledDefenderStaysStill()
    {
        var m = NewMatch(sensitivity: 0f);
        Park(m, 40f, 45f);
        m.TestSetPlayer(20, new Vec2(-10f, 0f), MathF.PI);
        m.TestSetBall(new Vec3(-10.6f, 0, C.Ball.Radius), Vec3.Zero, 20);
        m.TestSetPlayer(3, new Vec2(-30f, 15f), 0f);
        m.Step(new ControlInput());
        PressA(m);
        int c = m.ControlledIndex;
        var at = m.Players[c].Pos;
        Run(m, new ControlInput(), 100);
        Assert.Equal(c, m.ControlledIndex);
        Assert.True(Vec2.Distance(at, m.Players[c].Pos) < 0.01f, "moved to " + m.Players[c].Pos);
    }

    // ------------------------------------------------------------------ tackles

    /// <summary>A home user defender facing an away carrier 1 m away (or further), everyone else parked far away.</summary>
    private static (LiveMatch m, int tackler) TackleSetup(ulong seed, float gap, Action<TeamSetup, TeamSetup> tweak = null)
    {
        var m = NewMatch(seed: seed, tweak: tweak);
        Park(m, 40f, 45f);
        int tackler = 3, carrier = 20;
        m.TestSetPlayer(carrier, new Vec2(-20f, 0f), MathF.PI);
        m.TestSetBall(new Vec3(-20f - C.Movement.DribbleOffset, 0, C.Ball.Radius), Vec3.Zero, carrier);
        m.TestSetPlayer(tackler, new Vec2(-20f - C.Movement.DribbleOffset - gap, 0f), 0f);
        m.TestSetPlayer(9, new Vec2(30f, 0f), 0f);
        m.Step(new ControlInput());
        if (m.ControlledIndex != tackler) PressA(m);
        Assert.Equal(tackler, m.ControlledIndex);
        return (m, tackler);
    }

    [Fact]
    public void PressingBInReachLungesWithActionTackleAndResolvesTheDuel()
    {
        var (m, i) = TackleSetup(3, 1.0f);
        var at = m.Players[i].Pos;
        m.Step(new ControlInput { B = true });
        Assert.Equal(1, m.Counters.TackleLunges);
        Assert.Equal(PlayerAction.Tackle, m.Players[i].Action);
        Assert.Equal(1, m.Counters.Tackles);                 // in reach on the first tick: the duel is resolved
        int lungeTicks = (int)MathF.Round(C.Tackles.LungeSeconds / LiveMatch.TickSeconds);
        for (int k = 1; k < lungeTicks; k++)
        {
            m.Step(new ControlInput());
            Assert.Equal(PlayerAction.Tackle, m.Players[i].Action);
        }
        Assert.Equal(1, m.Counters.Tackles);                 // one duel per lunge
        Assert.True(Vec2.Distance(at, m.Players[i].Pos) > 0.5f, "he lunged");
        Run(m, new ControlInput(), 3);
        Assert.NotEqual(PlayerAction.Tackle, m.Players[i].Action);
    }

    [Fact]
    public void TheLungeIsFasterThanHisRun()
    {
        var (m, i) = TackleSetup(4, 6f);
        m.Step(new ControlInput { B = true });
        float speed = m.Players[i].Vel.Length;
        Assert.True(speed > m.TestRunSpeed(i) + 0.5f, $"lunge {speed}, run {m.TestRunSpeed(i)}");
        Assert.True(speed > m.TestSprintSpeed(i), $"lunge {speed}, sprint {m.TestSprintSpeed(i)}");
    }

    private static void SetDuel(TeamSetup home, TeamSetup away, int tackling, int tStrength, int dribbling, int dStrength)
    {
        home.Starters[3].Tackling = tackling; home.Starters[3].Strength = tStrength;
        away.Starters[9].Dribbling = dribbling; away.Starters[9].Strength = dStrength;
    }

    private static int DuelsWon(int tackling, int tStrength, int dribbling, int dStrength)
    {
        int won = 0;
        for (int trial = 0; trial < 100; trial++)
        {
            var (m, i) = TackleSetup(1000 + (ulong)trial, 1.0f, (h, a) => SetDuel(h, a, tackling, tStrength, dribbling, dStrength));
            m.Step(new ControlInput { B = true });
            Assert.Equal(1, m.Counters.Tackles);
            won += m.Counters.TacklesWon;
        }
        return won;
    }

    [Fact]
    public void AStrongTacklerUsuallyWinsTheBallFromAWeakDribblerAndRarelyTheOtherWayRound()
    {
        int strong = DuelsWon(92, 85, 30, 40);
        int weak = DuelsWon(30, 40, 92, 85);
        Assert.True(strong >= 70, "strong tackler won " + strong + " of 100");
        Assert.True(weak <= 25, "weak tackler won " + weak + " of 100");
    }

    [Fact]
    public void PressingBOutOfReachIsAWhiffFollowedByASlowRecovery()
    {
        var (m, i) = TackleSetup(5, 6f);
        m.Step(new ControlInput { B = true });
        Assert.Equal(PlayerAction.Tackle, m.Players[i].Action);
        int lungeTicks = (int)MathF.Round(C.Tackles.LungeSeconds / LiveMatch.TickSeconds);
        Run(m, new ControlInput(), lungeTicks);
        Assert.Equal(1, m.Counters.TackleLunges);
        Assert.Equal(0, m.Counters.Tackles);                 // never in reach: no duel
        Assert.NotEqual(PlayerAction.Tackle, m.Players[i].Action);
        // in recovery he is slower even at full sprint, and B does nothing
        var away = new ControlInput { MoveX = -1f, Sprint = true };
        int recTicks = (int)(C.Tackles.RecoverSeconds / LiveMatch.TickSeconds) - 2;
        float top = 0;
        for (int k = 0; k < recTicks; k++)
        {
            m.Step(new ControlInput { MoveX = -1f, Sprint = true, B = k % 4 == 0 });
            top = Math.Max(top, m.Players[i].Vel.Length);
        }
        Assert.Equal(1, m.Counters.TackleLunges);
        Assert.True(top <= m.TestSprintSpeed(i) * C.Tackles.RecoverSpeed + 0.01f, $"top {top} in recovery");
        Run(m, away, 60);
        Assert.True(m.Players[i].Vel.Length > m.TestSprintSpeed(i) * C.Tackles.RecoverSpeed + 1f, "full speed again after the recovery");
    }

    [Fact]
    public void AiDefendersUseTheSameLungeOnlyWhenInReachAndNotFromBehind()
    {
        // the home team carries the ball towards +X; the away team's goal is at +52.5
        int front = 0, fromBehind = 0, behindLunges = 0;
        for (int trial = 0; trial < 10; trial++)
        {
            front += AiLunges(1.6f, 200 + (ulong)trial, out _);
            fromBehind += AiLunges(-1.0f, 300 + (ulong)trial, out int bad);
            behindLunges += bad;
        }
        Assert.True(front >= 8, "goal-side AI defender lunged in " + front + " of 10");
        Assert.Equal(0, behindLunges);                       // never a lunge while still behind the carrier
    }

    /// <summary>An away AI defender starts dx metres from a home carrier who keeps the ball on the spot. Returns 1 if he
    /// lunged within 60 ticks; badLunges counts lunges started while he was behind the carrier.</summary>
    private static int AiLunges(float dx, ulong seed, out int badLunges)
    {
        badLunges = 0;
        var m = NewMatch(userSide: -1, seed: seed);
        Park(m, -45f, 48f);
        m.TestSetPlayer(14, new Vec2(dx, 0f), dx > 0 ? MathF.PI : 0f);
        for (int k = 0; k < 60; k++)
        {
            m.TestSetPlayer(9, new Vec2(0f, 0f), 0f);
            m.TestSetBall(new Vec3(C.Movement.DribbleOffset, 0, C.Ball.Radius), Vec3.Zero, 9);
            bool behind = m.Players[14].Pos.X < -0.3f;
            int before = m.Counters.TackleLunges;
            m.Step(new ControlInput());
            if (m.Counters.TackleLunges > before)
            {
                Assert.Equal(PlayerAction.Tackle, m.Players[14].Action);
                if (behind) badLunges++;
                return 1;
            }
        }
        return 0;
    }

    // ------------------------------------------------------------------ compact defence

    [Fact]
    public void DefendingInItsOwnThirdTheTeamMarksTheRunnerInTheBoxAndStaysCompact()
    {
        var m = NewMatch(userSide: -1, seed: 9);
        int carrier = 19, runner = 20;
        var cp = new Vec2(-28f, 18f);
        var rp = new Vec2(-44f, -6f);
        for (int k = 0; k < 150; k++)
        {
            // the away carrier keeps the ball wide in our third; the runner waits in the box
            m.TestSetPlayer(carrier, cp, MathF.PI);
            m.TestSetBall(new Vec3(cp.X - C.Movement.DribbleOffset, cp.Y, C.Ball.Radius), Vec3.Zero, carrier);
            m.TestSetPlayer(runner, rp, MathF.PI);
            m.Step(new ControlInput());
        }
        float nearest = float.MaxValue;
        for (int i = 1; i < 11; i++) nearest = Math.Min(nearest, Vec2.Distance(m.Players[i].Pos, rp));
        Assert.True(nearest < 3f, "nearest home player to the runner in the box: " + nearest + " m");
        m.BlockSize(0, false, out float depth, out float width);
        Assert.InRange(depth, 0f, 30f);
        Assert.InRange(width, 0f, 45f);
    }
}
