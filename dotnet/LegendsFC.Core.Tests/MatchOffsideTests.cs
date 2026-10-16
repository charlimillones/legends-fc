using System;
using System.Collections.Generic;
using LegendsFC.Core.Match;
using Xunit;

/// <summary>Offside (Carlos Oct 15: standard rule, judged when the pass is played, indirect free kick restart) and the
/// match-event feed for the on-screen banners.</summary>
public class MatchOffsideTests
{
    private static MatchConfig C => TestData.Data.Match;
    private const int HomePasser = 6, HomeStriker = 9, HomeStriker2 = 10, HomeMid = 7;
    private const int AwayDefender = 13, AwayPasser = 16, AwayStriker = 20;

    /// <summary>Everyone to a line across the pitch at x (keepers too), far from the action we set up.</summary>
    private static void Park(LiveMatch m, float x)
    {
        for (int i = 0; i < 22; i++) m.TestSetPlayer(i, new Vec2(x + (i % 2) * 2f, -30f + i * 2.8f), 0f);
    }

    /// <summary>A match in play, home attacking +X (first half), everyone parked behind the halfway line at x = -25.</summary>
    private static LiveMatch Scene(int userSide = -1, float parkX = -25f)
    {
        var m = MatchEngineTests.NewMatch(userSide: userSide);
        m.TestStartPlay();
        Assert.Equal(1, m.AttackDirection(0));
        Park(m, parkX);
        return m;
    }

    /// <summary>Gives player i the ball at 'ball', facing 'facing'.</summary>
    private static void GiveBall(LiveMatch m, int i, Vec2 ball, float facing)
    {
        m.TestSetPlayer(i, ball - Vec2.FromAngle(facing) * C.Movement.DribbleOffset, facing);
        m.TestSetBall(new Vec3(ball, C.Ball.Radius), Vec3.Zero, i);
    }

    /// <summary>A ground ball from 'from' to 'to' that arrives at about 6 m/s.</summary>
    private static Vec3 GroundBall(Vec2 from, Vec2 to)
    {
        var d = to - from;
        float speed = MathF.Sqrt(36f + 2f * C.Ball.RollingDecel * d.Length) * 1.1f;
        return new Vec3(d.Normalized() * speed, 0);
    }

    /// <summary>Steps until 'until' is true (at most 'ticks' steps); returns whether player 'owner' had the ball at some point.</summary>
    private static bool StepUntil(LiveMatch m, Func<bool> until, int ticks, int owner = -1, ControlInput input = default)
    {
        bool had = false;
        for (int i = 0; i < ticks && !until(); i++)
        {
            m.Step(input);
            if (m.Ball.Owner == owner) had = true;
        }
        return had;
    }

    private static List<MatchEventInfo> EventsSince(LiveMatch m, int from)
    {
        var list = new List<MatchEventInfo>();
        for (int k = Math.Max(from, m.EventCount - 32); k < m.EventCount; k++) list.Add(m.GetEvent(k));
        return list;
    }

    // ------------------------------------------------------------------ the rule

    [Fact]
    public void AttackerBeyondTheSecondLastDefenderWhoReceivesIsOffside()
    {
        var m = Scene();
        var ball = new Vec2(10f, 0f);
        GiveBall(m, HomePasser, ball, 0f);
        m.TestSetPlayer(HomeStriker, new Vec2(20f, 0f), 0f);   // beyond the ball and every opponent, in their half
        int events = m.EventCount;
        m.TestKick(HomePasser, GroundBall(ball, new Vec2(20f, 0f)), HomeStriker);
        StepUntil(m, () => m.Counters.Offsides > 0, 200);

        Assert.Equal(1, m.Counters.Offsides);
        Assert.Equal(1, m.Counters.FreeKicks);
        Assert.Equal(Phase.FreeKick, m.Phase);
        Assert.Equal(1, m.RestartTeam);                         // indirect free kick to the defending team
        var at = m.Players[HomeStriker].Pos;                    // where he touched it
        Assert.InRange(at.X, 15f, 21f);
        Assert.InRange(m.Ball.Pos.X, at.X - 0.01f, at.X + 0.01f);
        Assert.InRange(m.Ball.Pos.Y, at.Y - 0.01f, at.Y + 0.01f);
        var ev = EventsSince(m, events);
        Assert.Equal(2, ev.Count);
        Assert.Equal(MatchEventKind.Offside, ev[0].Kind);
        Assert.Equal(0, ev[0].Team);
        Assert.Equal(HomeStriker, ev[0].Player);
        Assert.Equal(m.Minute, ev[0].Minute);
        Assert.Equal(MatchEventKind.FreeKick, ev[1].Kind);
        Assert.Equal(1, ev[1].Team);
        Assert.Equal(m.TestTaker, ev[1].Player);
        Assert.InRange(ev[1].Player, 11, 21);

        // opponents keep 9.15 m away during the wait, and the AI takes it after the usual delay
        int wait = (int)((C.Restarts.MaxWalk + C.Restarts.AiDelay) / LiveMatch.TickSeconds) + 5;
        for (int i = 0; i < wait && m.Phase == Phase.FreeKick; i++) m.Step(new ControlInput());
        Assert.Equal(Phase.Play, m.Phase);
        Assert.Equal(1, m.Counters.Offsides);
    }

    [Theory]
    [InlineData(20.0f, false)]   // level with the second-last defender: onside
    [InlineData(20.2f, true)]    // just beyond him: offside
    public void LevelIsOnside(float attackerX, bool offside)
    {
        var m = Scene();
        int keeper = m.TestKeeper(1);
        m.TestSetPlayer(keeper, new Vec2(50f, 0f), MathF.PI);            // last opponent
        m.TestSetPlayer(AwayDefender, new Vec2(20f, -22f), MathF.PI);    // second-last opponent at x = 20
        var ball = new Vec2(10f, 0f);
        GiveBall(m, HomePasser, ball, 0f);
        m.TestSetPlayer(HomeStriker, new Vec2(attackerX, 0f), 0f);
        m.TestKick(HomePasser, GroundBall(ball, new Vec2(attackerX, 0f)), HomeStriker);
        bool had = StepUntil(m, () => m.Counters.Offsides > 0 || m.Ball.Owner == HomeStriker, 200, HomeStriker);
        m.Step(new ControlInput());
        Assert.Equal(offside ? 1 : 0, m.Counters.Offsides);
        Assert.Equal(offside ? Phase.FreeKick : Phase.Play, m.Phase);
        if (!offside) Assert.True(had || m.Ball.Owner == HomeStriker, "he received it");
    }

    [Theory]
    [InlineData(-15f, 0f, -3f, 0f)]    // in his own half (beyond the ball and every opponent): onside
    [InlineData(20f, 0f, 15f, 5f)]     // behind the ball when it is played: onside
    public void OwnHalfOrBehindTheBallIsOnside(float bx, float by, float ax, float ay)
    {
        var m = Scene();
        var ball = new Vec2(bx, by);
        var to = new Vec2(ax, ay);
        GiveBall(m, HomePasser, ball, (to - ball).Angle);
        m.TestSetPlayer(HomeStriker, to, 0f);
        m.TestKick(HomePasser, GroundBall(ball, to), HomeStriker);
        bool had = StepUntil(m, () => m.Ball.Owner == HomeStriker || m.Counters.Offsides > 0, 200, HomeStriker);
        Assert.True(had || m.Ball.Owner == HomeStriker);
        Run(m, 25);
        Assert.Equal(0, m.Counters.Offsides);
    }

    [Theory]
    [InlineData(Phase.ThrowIn, 20f, 34f, 30f)]     // receiver 10 m nearer the goal than the ball and every opponent
    [InlineData(Phase.Corner, 52f, 33.5f, 42f)]    // (at a corner nobody can be beyond the ball; beyond every outfield opponent)
    public void NoOffsideFromAThrowInOrACorner(Phase phase, float sx, float sy, float ax)
    {
        var m = Scene();
        var spot = new Vec2(sx, sy);
        m.TestBeginRestart(phase, 0, spot);
        Park(m, -25f);
        var to = new Vec2(ax, 22f);
        m.TestSetPlayer(HomeStriker, to, 0f);
        int taker = m.TestTaker;
        Assert.InRange(taker, 0, 10);
        m.TestKick(taker, GroundBall(spot, to), HomeStriker);
        Assert.Equal(Phase.Play, m.Phase);
        bool had = StepUntil(m, () => m.Ball.Owner == HomeStriker || m.Counters.Offsides > 0, 200, HomeStriker);
        Assert.True(had || m.Ball.Owner == HomeStriker);
        Run(m, 10);
        Assert.Equal(0, m.Counters.Offsides);
    }

    [Fact]
    public void AnOffsidePlayerWhoNeverTouchesTheBallIsNotPenalised()
    {
        var m = Scene();
        var ball = new Vec2(10f, 0f);
        var mate = new Vec2(8f, -8f);                    // behind the ball: onside
        GiveBall(m, HomePasser, ball, (mate - ball).Angle);
        m.TestSetPlayer(HomeStriker, new Vec2(30f, 10f), 0f);   // offside position, not involved
        m.TestSetPlayer(HomeMid, mate, 0f);
        m.TestKick(HomePasser, GroundBall(ball, mate), HomeMid);
        bool had = StepUntil(m, () => m.Ball.Owner == HomeMid || m.Counters.Offsides > 0, 200, HomeMid);
        Assert.True(had || m.Ball.Owner == HomeMid);
        Run(m, 50);
        Assert.Equal(0, m.Counters.Offsides);
    }

    [Fact]
    public void AKeeperSaveDoesNotResetOffside()
    {
        var m = Scene();
        int keeper = m.TestKeeper(1);
        m.TestSetPlayer(keeper, new Vec2(51f, 0f), MathF.PI);
        var ball = new Vec2(35f, 0f);
        GiveBall(m, HomeStriker, ball, 0f);
        var rebound = new Vec2(47f, 5f);
        m.TestSetPlayer(HomeStriker2, rebound, 0f);     // beyond the ball and the second-last opponent when the shot is struck
        m.TestKick(HomeStriker, new Vec3(25f, 0f, 0.5f), -1, shot: true);
        m.TestKeeperParry(keeper, GroundBall(new Vec2(51f, 0f), rebound));
        StepUntil(m, () => m.Counters.Offsides > 0 || m.HomeGoals > 0 || m.Phase != Phase.Play, 200);
        Assert.Equal(1, m.Counters.Offsides);
        Assert.Equal(0, m.HomeGoals);
        Assert.Equal(Phase.FreeKick, m.Phase);
        Assert.Equal(1, m.RestartTeam);
    }

    [Theory]
    [InlineData(true)]    // the defender controls it, then passes
    [InlineData(false)]   // the defender plays it first time
    public void ADefendersDeliberatePassPutsTheAttackerOnside(bool controls)
    {
        var m = Scene();
        var ball = new Vec2(10f, 0f);
        GiveBall(m, HomePasser, ball, 0f);
        m.TestSetPlayer(AwayDefender, new Vec2(17f, 0f), MathF.PI);
        var att = new Vec2(25f, 6f);
        m.TestSetPlayer(HomeStriker, att, 0f);           // offside position when the home pass is played
        m.TestKick(HomePasser, GroundBall(ball, new Vec2(17f, 0f)), -1);
        if (controls)
        {
            StepUntil(m, () => m.Ball.Owner == AwayDefender, 200);
            Assert.Equal(AwayDefender, m.Ball.Owner);     // the defender wins it
        }
        else
        {
            StepUntil(m, () => m.Ball.Pos.X > 14f, 100);   // on its way to him, away from the passer
            Assert.Equal(-1, m.Ball.Owner);
            m.TestSetPlayer(AwayDefender, m.Ball.Pos.XY + new Vec2(0.3f, 0f), MathF.PI);
        }
        m.TestSetPlayer(HomeStriker, att, 0f);
        var from = m.Ball.Pos.XY;
        m.TestKick(AwayDefender, GroundBall(from, att), -1);   // ...and plays it straight to the attacker
        bool had = StepUntil(m, () => m.Ball.Owner == HomeStriker || m.Counters.Offsides > 0, 200, HomeStriker);
        Assert.True(had || m.Ball.Owner == HomeStriker);
        Run(m, 10);
        Assert.Equal(0, m.Counters.Offsides);
    }

    [Fact]
    public void TheUsersPassToAnOffsideTeammateIsCalled()
    {
        var m = Scene(userSide: 0);
        var ball = new Vec2(10f, 0f);
        GiveBall(m, HomePasser, ball, 0f);
        m.TestSetPlayer(HomeStriker, new Vec2(20f, 0f), 0f);
        m.Step(new ControlInput());
        Assert.Equal(HomePasser, m.ControlledIndex);
        for (int i = 0; i < 5; i++) m.Step(new ControlInput { MoveX = 1, A = true });
        m.Step(new ControlInput { MoveX = 1 });         // release = pass in the stick direction
        Assert.Equal(1, m.Counters.Passes);
        StepUntil(m, () => m.Counters.Offsides > 0, 200);
        Assert.Equal(1, m.Counters.Offsides);
        Assert.Equal(Phase.FreeKick, m.Phase);
        Assert.Equal(1, m.RestartTeam);
        Assert.Equal(MatchEventKind.Offside, m.GetEvent(m.EventCount - 2).Kind);
        Assert.Equal(HomeStriker, m.GetEvent(m.EventCount - 2).Player);
    }

    [Fact]
    public void AnAiOffsideGivesTheUserAFreeKickThatWaitsForAButton()
    {
        var m = Scene(userSide: 0, parkX: 25f);           // away attacks -X: park everyone deep in the away half
        var ball = new Vec2(-10f, 0f);
        GiveBall(m, AwayPasser, ball, MathF.PI);
        m.TestSetPlayer(AwayStriker, new Vec2(-20f, 0f), MathF.PI);
        m.TestKick(AwayPasser, GroundBall(ball, new Vec2(-20f, 0f)), AwayStriker);
        StepUntil(m, () => m.Counters.Offsides > 0, 200);
        Assert.Equal(1, m.Counters.Offsides);
        Assert.Equal(Phase.FreeKick, m.Phase);
        Assert.Equal(0, m.RestartTeam);
        Assert.Equal(1, m.GetEvent(m.EventCount - 2).Team);
        Assert.Equal(AwayStriker, m.GetEvent(m.EventCount - 2).Player);
        Assert.Equal(0, m.GetEvent(m.EventCount - 1).Team);
        Assert.Equal(m.TestTaker, m.ControlledIndex);       // the user takes it

        Run(m, (int)((C.Restarts.MaxWalk + C.Restarts.AiDelay + 1f) / LiveMatch.TickSeconds));
        Assert.Equal(Phase.FreeKick, m.Phase);               // still waiting for the user
        m.Step(new ControlInput { MoveX = 1, A = true });
        m.Step(new ControlInput { MoveX = 1 });
        Assert.Equal(Phase.Play, m.Phase);
    }

    private static void Run(LiveMatch m, int ticks)
    {
        for (int i = 0; i < ticks && !m.IsOver; i++) m.Step(new ControlInput());
    }

    // ------------------------------------------------------------------ the event feed

    [Fact]
    public void AScriptedGoalGivesExactlyOneGoalEvent()
    {
        var m = Scene(userSide: 0);
        Assert.Equal(MatchEventKind.KickOff, m.GetEvent(0).Kind);
        float ballX = 52.5f - 11f;
        GiveBall(m, HomeStriker, new Vec2(ballX, 0f), 0f);
        int events = m.EventCount;
        m.Step(new ControlInput());
        for (int i = 0; i < 15; i++) m.Step(new ControlInput { MoveX = 1, B = true });
        m.Step(new ControlInput { MoveX = 1 });
        for (int i = 0; i < 150 && m.HomeGoals == 0; i++) m.Step(new ControlInput());
        Assert.Equal(1, m.HomeGoals);
        var ev = EventsSince(m, events);
        Assert.Single(ev);
        Assert.Equal(MatchEventKind.Goal, ev[0].Kind);
        Assert.Equal(0, ev[0].Team);
        Assert.Equal(HomeStriker, ev[0].Player);
        Assert.Equal(m.Minute, ev[0].Minute);
        Run(m, (int)(C.Clock.GoalCelebrationSeconds / LiveMatch.TickSeconds) + 2);
        var after = m.GetEvent(m.EventCount - 1);
        Assert.Equal(MatchEventKind.KickOff, after.Kind);   // then the kick-off, by the team that conceded
        Assert.Equal(1, after.Team);
    }

    [Fact]
    public void AFullMatchsEventsAgreeWithTheCountersAndTheScore()
    {
        int offsides = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var m = MatchEngineTests.NewMatch(userSide: -1, seed: seed);
            var n = new int[16];
            var goals = new int[2];
            int seen = 0, lastMinute = 0;
            void Read()
            {
                for (; seen < m.EventCount; seen++)
                {
                    var e = m.GetEvent(seen);
                    n[(int)e.Kind]++;
                    Assert.True(e.Minute >= lastMinute, "minutes never go back");
                    lastMinute = e.Minute;
                    Assert.InRange(e.Minute, 1, 90);
                    if (e.Kind == MatchEventKind.Goal || e.Kind == MatchEventKind.OwnGoal) goals[e.Team]++;
                    if (e.Kind == MatchEventKind.HalfTime || e.Kind == MatchEventKind.FullTime || e.Kind == MatchEventKind.ExtraTime) Assert.Equal(255, e.Team);
                    else Assert.InRange(e.Team, 0, 1);
                    if (e.Kind == MatchEventKind.Save) Assert.Equal(m.TestKeeper(e.Team), e.Player);
                }
            }
            Read();
            while (!m.IsOver) { m.Step(new ControlInput()); Read(); }
            var c = m.Counters;
            Assert.Equal(c.KickOffs, n[(int)MatchEventKind.KickOff]);
            Assert.Equal(c.Corners, n[(int)MatchEventKind.Corner]);
            Assert.Equal(c.ThrowIns, n[(int)MatchEventKind.ThrowIn]);
            Assert.Equal(c.GoalKicks, n[(int)MatchEventKind.GoalKick]);
            Assert.Equal(c.Offsides, n[(int)MatchEventKind.Offside]);
            Assert.Equal(c.FreeKicks, n[(int)MatchEventKind.FreeKick]);
            Assert.Equal(c.Saves, n[(int)MatchEventKind.Save]);
            Assert.InRange(n[(int)MatchEventKind.Woodwork], c.PostHits > 0 ? 1 : 0, c.PostHits);
            Assert.Equal(m.HomeGoals, goals[0]);
            Assert.Equal(m.AwayGoals, goals[1]);
            Assert.Equal(m.HomeGoals + m.AwayGoals, n[(int)MatchEventKind.Goal] + n[(int)MatchEventKind.OwnGoal]);
            Assert.Equal(1, n[(int)MatchEventKind.HalfTime]);
            Assert.Equal(1, n[(int)MatchEventKind.FullTime]);
            Assert.Equal(0, n[(int)MatchEventKind.ExtraTime]);
            Assert.Equal(MatchEventKind.FullTime, m.GetEvent(m.EventCount - 1).Kind);
            offsides += c.Offsides;
        }
        Assert.True(offsides > 0, "at least one offside in these matches, so the check means something");
    }

    [Fact]
    public void ExtraTimeAndHalfTimeEvents()
    {
        var m = MatchEngineTests.NewMatch(userSide: -1, extraTime: true);
        m.TestSetClock(1, C.Clock.HalfSeconds - 0.01f);
        m.Step(new ControlInput());
        Assert.Equal(MatchEventKind.HalfTime, m.GetEvent(m.EventCount - 1).Kind);
        Assert.Equal(45, m.GetEvent(m.EventCount - 1).Minute);
        Run(m, (int)(C.Clock.HalfTimeBreakSeconds / LiveMatch.TickSeconds) + 1);
        Assert.Equal(MatchEventKind.KickOff, m.GetEvent(m.EventCount - 1).Kind);
        m.TestSetClock(2, C.Clock.HalfSeconds - 0.01f);
        m.Step(new ControlInput());
        Assert.Equal(m.HomeGoals, m.AwayGoals);   // level after the clock jumps: extra time
        Assert.Equal(MatchEventKind.ExtraTime, m.GetEvent(m.EventCount - 1).Kind);
        Assert.Equal(255, m.GetEvent(m.EventCount - 1).Team);
        Assert.Equal(-1, m.GetEvent(m.EventCount - 1).Player);
    }

    [Fact]
    public void OnlyTheLast32EventsCanBeRead()
    {
        var m = Scene();
        Assert.Throws<ArgumentOutOfRangeException>(() => m.GetEvent(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.GetEvent(m.EventCount));
        for (int k = 0; k < 40; k++)
        {
            m.TestStartPlay();
            Park(m, -5f);
            m.TestSetBall(new Vec3(20, 33.5f, 0.11f), new Vec3(0, 6, 0), -1);
            m.TestSetLastTouch(3);
            StepUntil(m, () => m.Phase == Phase.ThrowIn, 30);
            Assert.Equal(Phase.ThrowIn, m.Phase);
        }
        Assert.Equal(41, m.EventCount);                     // the kick-off and 40 throw-ins
        Assert.Equal(MatchEventKind.ThrowIn, m.GetEvent(m.EventCount - 32).Kind);
        Assert.Equal(MatchEventKind.ThrowIn, m.GetEvent(m.EventCount - 1).Kind);
        Assert.Equal(1, m.GetEvent(m.EventCount - 1).Team);
        Assert.Throws<ArgumentOutOfRangeException>(() => m.GetEvent(m.EventCount - 33));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.GetEvent(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.GetEvent(m.EventCount));
    }
}
