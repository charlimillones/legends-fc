namespace LegendsFC.Core.Match
{
    /// <summary>Test hooks (internal: visible to the dotnet test project only) to set up match situations.</summary>
    public sealed partial class LiveMatch
    {
        /// <summary>Skips the kick-off wait: play is on, the ball is where it is.</summary>
        internal void TestStartPlay()
        {
            _phase = Phase.Play;
            _phaseTimer = 0;
            _owner = -1;
        }

        internal void TestSetPlayer(int i, Vec2 pos, float facing)
        {
            var p = _p[i];
            p.Pos = pos; p.Vel = Vec2.Zero; p.Target = pos; p.Facing = facing; p.Stun = 0; p.Diving = false; p.Holding = false;
            Publish();
        }

        /// <summary>Puts the ball somewhere: owner -1 = loose with this velocity, else at that player's feet.</summary>
        internal void TestSetBall(Vec3 pos, Vec3 vel, int owner)
        {
            ClearBallFlags();
            _ballPos = pos; _ballVel = vel;
            _owner = -1;
            if (owner >= 0) TakeBall(owner, false);
            Publish();
        }

        /// <summary>Gives the ball to a keeper in his hands for this long.</summary>
        internal void TestKeeperHolds(int keeper, float seconds)
        {
            ClearBallFlags();
            _owner = -1;
            TakeBall(keeper, true);
            _p[keeper].HoldLeft = seconds;
            Publish();
        }

        /// <summary>Jumps the clock: this half, this many seconds played.</summary>
        internal void TestSetClock(int half, float halfClock)
        {
            _half = half;
            _halfTicks = (int)System.MathF.Round(halfClock / Dt);
            _halfClock = _halfTicks * Dt;
            UpdateMinute();
        }

        internal void TestSetLastTouch(int i)
        {
            _lastToucher = i;
            _lastPossTeam = _p[i].Team;
        }

        internal float TestWalkSpeed => _c.Movement.WalkSpeed;
        internal float TestRunSpeed(int i) => _p[i].RunSpeed;
        internal float TestSprintSpeed(int i) => _p[i].SprintSpeed;
        internal int TestKeeper(int team) => _keeper[team];
    }
}
