using System;
using System.Linq;
using LegendsFC.Core.Match;
using LegendsFC.Core.Squad;
using LegendsFC.Core.World;
using Xunit;

/// <summary>The real-time match: controls, switching, restarts, clock, result and determinism.</summary>
public class MatchEngineTests
{
    private static MatchConfig C => TestData.Data.Match;

    internal static LiveMatch NewMatch(int userSide = 0, float sensitivity = 0f, ulong seed = 1, bool extraTime = false, int firstLegHome = -1, int firstLegAway = -1)
    {
        var c = TestData.Data.Match;
        var setup = new MatchSetup
        {
            CompetitionId = "TEST",
            Home = LiveMatchFactory.TestTeam(c, "HOME", "4-4-2", 70, 11),
            Away = LiveMatchFactory.TestTeam(c, "AWAY", "4-3-3", 70, 12),
            ExtraTimeIfLevel = extraTime, FirstLegHomeGoals = firstLegHome, FirstLegAwayGoals = firstLegAway,
            Seed = seed,
            Settings = new MatchSettings { UserSide = userSide, SwitchSensitivity = sensitivity }
        };
        return new LiveMatch(setup, c);
    }

    private static void Run(LiveMatch m, ControlInput input, int ticks)
    {
        for (int i = 0; i < ticks && !m.IsOver; i++) m.Step(input);
    }

    /// <summary>Everyone to a line across the middle, keepers too (far from any action we set up).</summary>
    private static void ParkEveryone(LiveMatch m, float x)
    {
        for (int i = 0; i < 22; i++) m.TestSetPlayer(i, new Vec2(x + (i % 2) * 2f, -30f + i * 2.8f), 0f);
    }

    [Fact]
    public void ScriptedShotFromElevenMetresIntoAnEmptyGoalScores()
    {
        var m = NewMatch(userSide: 0);
        m.TestStartPlay();
        Assert.Equal(1, m.AttackDirection(0));   // home attacks +X in the first half
        ParkEveryone(m, -25f);
        int st = 9;                                // 4-4-2 slot 9 = ST
        float ballX = 52.5f - 11f;
        m.TestSetPlayer(st, new Vec2(ballX - C.Movement.DribbleOffset, 0), 0f);
        m.TestSetBall(new Vec3(ballX, 0, C.Ball.Radius), Vec3.Zero, st);
        m.Step(new ControlInput());
        Assert.Equal(st, m.ControlledIndex);
        var hold = new ControlInput { MoveX = 1, B = true };
        Run(m, hold, 15);                          // hold B 0.3 s: a third of full power
        Run(m, new ControlInput { MoveX = 1 }, 1); // release = shoot
        Assert.Equal(1, m.Counters.Shots);
        for (int i = 0; i < 150 && m.HomeGoals == 0; i++) m.Step(new ControlInput());
        Assert.Equal(1, m.HomeGoals);
        Assert.Equal(1, m.LastGoalCount);
        Assert.Equal(Phase.GoalScored, m.Phase);
        Run(m, new ControlInput(), (int)(C.Clock.GoalCelebrationSeconds / LiveMatch.TickSeconds) + 2);
        Assert.Equal(Phase.KickOff, m.Phase);
        Assert.Equal(1, m.RestartTeam);            // the team that conceded kicks off
        var r = m.Result();
        Assert.Single(r.Goals);
        Assert.Equal("HOME-P10", r.Goals[0].ScorerId);
        Assert.False(r.Goals[0].OwnGoal);
    }

    [Fact]
    public void ControlledPlayerWalksRunsAndSprintsWithTheStick()
    {
        var m = NewMatch(userSide: 0, sensitivity: 0f);
        m.TestStartPlay();
        int awayKeeper = m.TestKeeper(1);
        m.TestKeeperHolds(awayKeeper, 60f);       // the away keeper holds the ball: nobody can win it
        m.Step(new ControlInput());
        int c = m.ControlledIndex;
        Assert.InRange(c, 0, 10);
        Assert.NotEqual(m.TestKeeper(0), c);
        m.TestSetPlayer(c, new Vec2(-30f, -20f), 0f);

        Run(m, new ControlInput { MoveX = 0.3f }, 100);
        Assert.Equal(c, m.ControlledIndex);
        float walk = m.Players[c].Vel.Length;
        Assert.True(walk > 0.5f && walk <= m.TestWalkSpeed + 0.01f, "walk " + walk);
        Assert.True(m.Players[c].Vel.X > 0);       // +X on the stick = towards the opponent's goal

        Run(m, new ControlInput { MoveX = 1f }, 150);
        float run = m.Players[c].Vel.Length;
        Assert.InRange(run, m.TestRunSpeed(c) - 0.3f, m.TestRunSpeed(c) + 0.01f);
        Assert.True(run > walk + 1f);

        Run(m, new ControlInput { MoveX = 1f, Sprint = true }, 150);
        float sprint = m.Players[c].Vel.Length;
        Assert.True(sprint > run + 0.5f, $"sprint {sprint} run {run}");
        Assert.True(sprint <= m.TestSprintSpeed(c) + 0.01f);
    }

    [Fact]
    public void InPossessionControlFollowsTheCarrier()
    {
        var m = NewMatch(userSide: 0);
        m.TestStartPlay();
        m.TestSetBall(new Vec3(-10, 0, 0.11f), Vec3.Zero, 5);
        m.Step(new ControlInput());
        Assert.Equal(5, m.ControlledIndex);
        m.TestSetBall(new Vec3(0, 10, 0.11f), Vec3.Zero, 8);
        m.Step(new ControlInput());
        Assert.Equal(8, m.ControlledIndex);
        Assert.True(m.Players[8].UserControlled && m.Players[8].HasBall);
    }

    [Fact]
    public void DefendingTheAButtonSwitchesToAnotherOutfieldPlayer()
    {
        var m = NewMatch(userSide: 0, sensitivity: 0f);
        m.TestStartPlay();
        m.TestSetBall(new Vec3(-20, 5, 0.11f), Vec3.Zero, 20);   // an away player has the ball in our half
        m.Step(new ControlInput());
        int before = m.ControlledIndex;
        m.Step(new ControlInput { A = true });
        m.Step(new ControlInput());
        int after = m.ControlledIndex;
        Assert.NotEqual(before, after);
        Assert.InRange(after, 0, 10);
        Assert.NotEqual(m.TestKeeper(0), after);
        for (int k = 0; k < 20; k++)
        {   // pressing again and again never selects the keeper
            m.Step(new ControlInput { A = true });
            m.Step(new ControlInput());
            Assert.NotEqual(m.TestKeeper(0), m.ControlledIndex);
        }
    }

    [Fact]
    public void AutoSwitchMovesControlToTheDefenderNearTheBall()
    {
        var m = NewMatch(userSide: 0, sensitivity: 1f);
        m.TestStartPlay();
        m.TestSetBall(new Vec3(-30, 0, 0.11f), Vec3.Zero, 20);
        m.Step(new ControlInput());
        int near = m.ControlledIndex;
        // move the controlled player far away: someone else is now much closer to the ball
        m.TestSetPlayer(near, new Vec2(40, 30), 0f);
        Run(m, new ControlInput(), 30);
        Assert.NotEqual(near, m.ControlledIndex);
        Assert.NotEqual(m.TestKeeper(0), m.ControlledIndex);

        var off = NewMatch(userSide: 0, sensitivity: 0f);
        off.TestStartPlay();
        off.TestSetBall(new Vec3(-30, 0, 0.11f), Vec3.Zero, 20);
        off.Step(new ControlInput());
        int c = off.ControlledIndex;
        off.TestSetPlayer(c, new Vec2(40, 30), 0f);
        Run(off, new ControlInput(), 30);
        Assert.Equal(c, off.ControlledIndex);       // sensitivity 0 = auto-switch off
    }

    [Fact]
    public void BallOverTheTouchlineGivesAThrowInToTheOtherTeam()
    {
        var m = NewMatch(userSide: -1);
        m.TestStartPlay();
        ParkEveryone(m, -5f);
        m.TestSetBall(new Vec3(20, 33.0f, 0.11f), new Vec3(0, 6, 0), -1);
        m.TestSetLastTouch(3);                    // a home defender touched it last
        Run(m, new ControlInput(), 25);
        Assert.Equal(Phase.ThrowIn, m.Phase);
        Assert.Equal(1, m.RestartTeam);
        Assert.InRange(MathF.Abs(m.Ball.Pos.Y), 33.99f, 34.01f);
        Assert.Equal(1, m.Counters.ThrowIns);
        // the AI takes it after the short delay and play goes on
        Run(m, new ControlInput(), (int)((C.Restarts.MaxWalk + C.Restarts.AiDelay) / LiveMatch.TickSeconds) + 5);
        Assert.NotEqual(Phase.ThrowIn, m.Phase);
    }

    [Theory]
    [InlineData(9, Phase.GoalKick, 1)]   // off a home attacker: goal kick to the away team
    [InlineData(14, Phase.Corner, 0)]   // off an away defender: corner to the home team
    public void BallOverTheGoalLine(int lastTouch, Phase expected, int team)
    {
        var m = NewMatch(userSide: -1);
        m.TestStartPlay();
        ParkEveryone(m, -5f);
        m.TestSetBall(new Vec3(50, 15, 0.11f), new Vec3(9, 0, 0), -1);
        m.TestSetLastTouch(lastTouch);
        Run(m, new ControlInput(), 25);
        Assert.Equal(expected, m.Phase);
        Assert.Equal(team, m.RestartTeam);
    }

    [Fact]
    public void UserRestartWaitsForAButtonThenTheAiTakesIt()
    {
        var m = NewMatch(userSide: 0);
        m.TestStartPlay();
        ParkEveryone(m, -5f);
        m.TestSetBall(new Vec3(-20, -33.0f, 0.11f), new Vec3(0, -6, 0), -1);
        m.TestSetLastTouch(15);                   // away touched it: home (the user) throws
        Run(m, new ControlInput(), 25);
        Assert.Equal(Phase.ThrowIn, m.Phase);
        Assert.Equal(0, m.RestartTeam);
        Run(m, new ControlInput(), (int)((C.Restarts.MaxWalk + C.Restarts.AiDelay + 1f) / LiveMatch.TickSeconds));
        Assert.Equal(Phase.ThrowIn, m.Phase);     // still waiting for the user (an AI team would have thrown by now)
        m.Step(new ControlInput { MoveX = 1, A = true });
        m.Step(new ControlInput { MoveX = 1 });   // release = throw in the stick direction
        Assert.Equal(Phase.Play, m.Phase);

        var t = NewMatch(userSide: 0);
        t.TestStartPlay();
        ParkEveryone(t, -5f);
        t.TestSetBall(new Vec3(-20, -33.0f, 0.11f), new Vec3(0, -6, 0), -1);
        t.TestSetLastTouch(15);
        Run(t, new ControlInput(), 25);
        Run(t, new ControlInput(), (int)((C.Restarts.MaxWalk + C.Restarts.UserTimeout) / LiveMatch.TickSeconds) + 5);
        Assert.NotEqual(Phase.ThrowIn, t.Phase);  // timeout: the AI took it
    }

    [Fact]
    public void ClockHalvesAndFullTime()
    {
        var m = NewMatch(userSide: -1, seed: 5);
        int ticks = 0;
        while (m.Phase != Phase.HalfTime) { m.Step(new ControlInput()); ticks++; }
        Assert.Equal((int)(C.Clock.HalfSeconds / LiveMatch.TickSeconds), ticks);
        Assert.Equal(45, m.Minute);
        Run(m, new ControlInput(), (int)(C.Clock.HalfTimeBreakSeconds / LiveMatch.TickSeconds) + 1);
        Assert.Equal(2, m.Half);
        Assert.Equal(-1, m.AttackDirection(0));  // ends switched
        Assert.Equal(Phase.KickOff, m.Phase);
        while (!m.IsOver) m.Step(new ControlInput());
        Assert.Equal(Phase.FullTime, m.Phase);
        Assert.Equal(90, m.Minute);
        var r = m.Result();
        Assert.False(r.ExtraTimePlayed);
        Assert.Equal(r.HomeGoals + r.AwayGoals, r.Goals.Count);
        Assert.Equal(22, r.Players.Count);
        Assert.All(r.Players, p => Assert.Equal(90, p.Minutes));
        Assert.All(r.Players, p => Assert.True(p.Energy < 100, "energy is spent"));
        Assert.All(r.Players.Where(p => !p.PlayerId.EndsWith("P01")), p => Assert.InRange(p.Km, 3, 16));
        Assert.Equal(m.Counters.Passes, r.Players.Sum(p => p.Passes));
        Assert.Equal(m.Counters.Shots, r.Players.Sum(p => p.Shots));
        Assert.Equal(r.Goals.Count(g => !g.OwnGoal), r.Players.Sum(p => p.Goals));
        Assert.Equal(r.Goals.Count(g => g.AssistId != null), r.Players.Sum(p => p.Assists));
    }

    [Fact]
    public void LevelKnockoutGoesToExtraTimeThenNeedsAShootout()
    {
        var m = NewMatch(userSide: -1, extraTime: true);
        m.TestSetClock(2, C.Clock.HalfSeconds - 0.01f);
        m.Step(new ControlInput());
        Assert.Equal(Phase.ExtraTimeBreak, m.Phase);
        Run(m, new ControlInput(), (int)(C.Clock.ExtraTimeBreakSeconds / LiveMatch.TickSeconds) + 1);
        Assert.Equal(3, m.Half);
        Assert.Equal(91, m.Minute);
        Assert.Equal(Phase.KickOff, m.Phase);
        m.TestSetClock(4, C.Clock.ExtraHalfSeconds - 0.01f);
        m.Step(new ControlInput());
        Assert.True(m.IsOver);
        var r = m.Result();
        Assert.True(r.ExtraTimePlayed);
        Assert.Equal(r.HomeGoals == r.AwayGoals, r.NeedsShootout);
        Assert.All(r.Players, p => Assert.Equal(120, p.Minutes));

        // second leg: the away team won the first leg 1-0 at home, so 0-0 now is not level on aggregate
        var leg = NewMatch(userSide: -1, extraTime: true, firstLegHome: 1, firstLegAway: 0);
        leg.TestSetClock(2, C.Clock.HalfSeconds - 0.01f);
        leg.Step(new ControlInput());
        Assert.Equal(leg.HomeGoals - leg.AwayGoals == 1, leg.Phase == Phase.ExtraTimeBreak);
    }

    [Fact]
    public void SameSeedAndInputsGiveTheSameMatch()
    {
        ulong a = PlayScripted(77), b = PlayScripted(77), c = PlayScripted(78);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    private static ulong PlayScripted(ulong seed)
    {
        var m = NewMatch(userSide: 0, sensitivity: 0.5f, seed: seed);
        int t = 0;
        while (!m.IsOver)
        {
            var input = new ControlInput
            {
                MoveX = MathF.Sin(t * 0.011f), MoveY = MathF.Cos(t * 0.017f),
                Sprint = (t / 200) % 2 == 0,
                A = t % 97 < 10, B = t % 301 < 20, C = t % 173 < 25
            };
            m.Step(input);
            t++;
        }
        return m.StateHash();
    }

    [Fact]
    public void FactoryBuildsSetupsFromTeamSheetsWithTheOutOfPositionDrop()
    {
        var d = TestData.Data;
        var w = new WorldGenerator(d).Generate(3);
        var r = new RatingTable(d);
        var clubs = w.Clubs.Where(c => w.ClubLeague.TryGetValue(c.Id, out var l) && l == "ENG-1").Take(2).ToList();
        var home = Lineups.Pick(w, clubs[0], "ENG-1", r, d);
        var away = Lineups.Pick(w, clubs[1], "ENG-1", r, d);
        // play someone out of position: the first CB's slot gets a striker
        var st = home.Starters.First(p => p != null && p.MainPosition == LegendsFC.Core.Model.Position.ST);
        int cbSlot = home.SlotPositions.IndexOf(LegendsFC.Core.Model.Position.CB);
        int stSlot = home.Starters.IndexOf(st);
        (home.Starters[cbSlot], home.Starters[stSlot]) = (home.Starters[stSlot], home.Starters[cbSlot]);
        var setup = LiveMatchFactory.FromSheets(w, d, "ENG-1", home, away, new MatchSettings { UserSide = -1 }, 9);
        Assert.Equal(11, setup.Home.Starters.Length);
        Assert.Equal(home.Formation, setup.Home.Formation);
        var ps = setup.Home.Starters[cbSlot];
        Assert.Equal(st.Id, ps.PlayerId);
        var drop = LegendsFC.Core.Rules.OutOfPosition.Evaluate(d.Archetype(st.ArchetypeId), st.MainPosition, st.Foot, LegendsFC.Core.Model.Position.CB, d.PositionRules, d.OutOfPosition).Drop;
        Assert.True(drop > 0);
        Assert.Equal((int)Math.Round(st.Attributes[LegendsFC.Core.Model.Attr.Shooting] * (1 - drop)), ps.Shooting);
        Assert.Equal(st.Energy, ps.Energy);
        Assert.True(setup.Home.Starters[0].IsKeeper);
        var m = new LiveMatch(setup, d.Match);
        Run(m, new ControlInput(), 500);
        Assert.False(m.IsOver);
    }
}
