using System;

namespace LegendsFC.Core.Match
{
    public sealed partial class LiveMatch
    {
        // ------------------------------------------------------------------ kick-off

        private void StartKickOff(int team)
        {
            ClearBallFlags();
            _phase = Phase.KickOff;
            _phaseTimer = 0;
            _readyTimer = 0;
            _restartTeam = team;
            _restartSpot = Vec2.Zero;
            _owner = -1;
            _ballPos = new Vec3(0, 0, _c.Ball.Radius);
            _ballVel = Vec3.Zero;
            _counters.KickOffs++;
            _lastPossTeam = team;
            _lastToucher = -1;
            SetKickOffTargets(team);
            ClearTackles();
            Emit(MatchEventKind.KickOff, team, _taker);
            for (int i = 0; i < N; i++)
            {
                var p = _p[i];
                p.Pos = p.Target; p.Vel = Vec2.Zero; p.Holding = false; p.Diving = false; p.Stun = 0; p.Action = 0;
                p.RunTimer = 0; p.Threat = false; p.UseDirect = false;
                p.Facing = new Vec2(Dir(p.Team), 0).Angle;
            }
            _takerReady = true;
            _owner = _taker;
            if (_userTeam >= 0)
            {
                if (team == _userTeam) SetControlled(_taker);
                else { int b = NearestOutfield(_userTeam, Vec2.Zero); if (b >= 0) SetControlled(b); }
            }
            for (int k = 0; k < 3; k++) _btnArmed[k] = false;
        }

        /// <summary>Kick-off positions: everyone in his own half; the kicking team's most advanced player at the ball.</summary>
        private void SetKickOffTargets(int team)
        {
            float circle = _c.Restarts.Distance;
            int taker = -1; float bx = float.MinValue;
            for (int s = 0; s < 11; s++)
            {
                int i = team * 11 + s;
                if (_p[i].Keeper) continue;
                if (_p[i].Base.X > bx) { bx = _p[i].Base.X; taker = i; }
            }
            _taker = taker;
            for (int i = 0; i < N; i++)
            {
                var p = _p[i];
                var f = new Vec2(Math.Min(p.Base.X * 0.9f, -1.0f), p.Base.Y);
                if (i == taker) f = new Vec2(-0.5f, 0);
                else if (p.Team != team && f.Length < circle + 0.5f) f = f.Normalized() * (circle + 0.5f);
                if (f.X > -0.5f) f.X = -0.5f;
                p.Target = ToWorld(p.Team, f);
                p.SpeedMode = 1;
            }
        }

        private int NearestOutfield(int team, Vec2 at)
        {
            int best = -1; float bd = float.MaxValue;
            for (int s = 0; s < 11; s++)
            {
                int i = team * 11 + s;
                if (_p[i].Keeper) continue;
                float d = Vec2.DistanceSq(_p[i].Pos, at);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        // ------------------------------------------------------------------ throw-ins, goal kicks, corners

        private void BeginRestart(Phase phase, int team, Vec2 spot)
        {
            _phase = phase;
            _phaseTimer = 0;
            _readyTimer = 0;
            _restartTeam = team;
            _restartSpot = spot;
            _owner = -1;
            _ballPos = new Vec3(spot, _c.Ball.Radius);
            _ballVel = Vec3.Zero;
            _lastPossTeam = team;
            for (int i = 0; i < N; i++) { var p = _p[i]; p.Holding = false; p.Diving = false; p.Threat = false; p.RunTimer = 0; }
            ClearTackles();
            _taker = phase == Phase.GoalKick ? _keeper[team] : NearestOutfield(team, spot);
            _takerReady = false;
            if (team == _userTeam && !_p[_taker].Keeper) SetControlled(_taker);
            for (int k = 0; k < 3; k++) _btnArmed[k] = false;
        }

        /// <summary>Where the taker stands: just behind the ball, facing the pitch.</summary>
        private Vec2 TakerStand(out float facing)
        {
            var into = new Vec2(-_restartSpot.X, -_restartSpot.Y);
            if (_phase == Phase.ThrowIn) into = new Vec2(0, -MathF.Sign(_restartSpot.Y));
            else if (_phase == Phase.FreeKick) into = new Vec2(Dir(_restartTeam), 0);
            into = into.Normalized();
            if (into.LengthSq < 0.5f) into = new Vec2(Dir(_restartTeam), 0);
            facing = into.Angle;
            return _restartSpot - into * 0.4f;
        }

        private void RestartTick(in ControlInput input)
        {
            if (_phase == Phase.GoalScored)
            {
                for (int i = 0; i < N; i++) { _p[i].UseDirect = false; }
                MovePlayers();
                if (_phaseTimer >= _c.Clock.GoalCelebrationSeconds) StartKickOff(_restartTeam);
                return;
            }
            var taker = _p[_taker];
            var stand = TakerStand(out float face);
            if (!_takerReady)
            {
                if (Vec2.Distance(taker.Pos, stand) < 0.6f || _phaseTimer >= _c.Restarts.MaxWalk)
                {
                    _takerReady = true;
                    _readyTimer = 0;
                    _owner = _taker;
                    if (_phase == Phase.ThrowIn) _ballPos = new Vec3(_restartSpot, 2.0f);
                }
            }
            if (_takerReady)
            {
                _readyTimer += Dt;
                taker.Pos = stand; taker.Vel = Vec2.Zero; taker.Target = stand; taker.SpeedMode = 0;
                if (_phase != Phase.KickOff) taker.Facing = face;
            }
            else { taker.Target = stand; taker.SpeedMode = 2; taker.UseDirect = false; }

            for (int i = 0; i < N; i++)
            {
                if (i == _taker) continue;
                var p = _p[i];
                if (i == _controlled) continue;
                p.UseDirect = false;
                RestartTarget(i);
            }
            if (_controlled >= 0 && _controlled != _taker) UserMoveOnly(input);
            else if (_controlled == _taker) taker.UseDirect = false;
            MovePlayers();
            if (_takerReady) { taker.Pos = stand; taker.Vel = Vec2.Zero; }
            if (!_takerReady) return;

            if (_restartTeam == _userTeam && !taker.Keeper)
            {
                for (int k = 0; k < 3; k++)
                {
                    if (!_btnReleased[k]) continue;
                    var stick = StickWorld(input, out float mag);
                    var dir = mag > 0.05f ? stick / mag : Vec2.FromAngle(taker.Facing);
                    UserRestartKick(dir, PowerFromHold(_btnReleasedHold[k]));
                    return;
                }
                if (_readyTimer >= _c.Restarts.UserTimeout) AiRestartKick();
            }
            else if (_readyTimer >= _c.Restarts.AiDelay) AiRestartKick();
        }

        private void UserMoveOnly(in ControlInput input)
        {
            var p = _p[_controlled];
            var stick = StickWorld(input, out float mag);
            float speed = 0;
            if (mag > 0.05f) speed = input.Sprint ? p.SprintSpeed : mag < _c.Movement.WalkStick ? _c.Movement.WalkSpeed * Math.Min(1f, mag / _c.Movement.WalkStick) : p.RunSpeed;
            p.UseDirect = true;
            p.DirectVel = mag > 0.05f ? stick / mag * speed : Vec2.Zero;
        }

        /// <summary>Restart positions: shape for throw-ins, goal kicks and kick-offs; box positions for corners; distance rules.</summary>
        private void RestartTarget(int i)
        {
            var p = _p[i];
            int t = p.Team;
            bool att = t == _restartTeam;
            if (_phase == Phase.KickOff) return;   // targets set at the kick-off
            if (p.Keeper)
            {
                int own = -Dir(t);
                p.Target = new Vec2(own * (HalfX - 1.5f), 0);
                p.SpeedMode = 1;
                if (_phase == Phase.GoalKick && t == _restartTeam) return;
                return;
            }
            if (_phase == Phase.Corner) { CornerTarget(i, att); }
            else if (att) AttackSpot(i, false);
            else DefendSpot(i);
            // distance from the ball
            float need = _phase == Phase.ThrowIn ? _c.Restarts.ThrowInDistance : _c.Restarts.Distance;
            if (!att)
            {
                var d = p.Target - _restartSpot;
                if (d.Length < need) p.Target = _restartSpot + (d.LengthSq > 1e-4f ? d.Normalized() : new Vec2(-Dir(_restartTeam), 0)) * need;
                if (_phase == Phase.GoalKick && InOwnBox(_restartTeam, p.Target, 1f))
                {
                    int own = -Dir(_restartTeam);
                    p.Target = new Vec2(own * (HalfX - _c.Pitch.PenaltyBoxDepth - 2f), p.Target.Y);
                }
            }
            p.Target = ClampInside(p.Target, 0.5f);
        }

        /// <summary>Corner positions: the best headers attack the box, the defending team marks them goal-side and guards the six-yard line.</summary>
        private void CornerTarget(int i, bool att)
        {
            var p = _p[i];
            int at = _restartTeam;
            int dir = Dir(at);
            float gx = dir * HalfX;
            var r = _c.Restarts;
            if (att)
            {
                int rank = HeaderRank(i, at);
                if (rank < r.CornerAttackers)
                {
                    float x = gx - dir * (6f + (rank % 2) * (r.CornerZoneDepth - 6f));
                    float y = (rank - (r.CornerAttackers - 1) * 0.5f) * 3.5f;
                    p.Target = new Vec2(x, y); p.SpeedMode = 1;
                }
                else if (p.Line == 1) { p.Target = ToWorld(at, new Vec2(-5f, p.Base.Y * 0.6f)); p.SpeedMode = 1; }
                else { p.Target = new Vec2(gx - dir * 22f, p.Base.Y * 0.8f); p.SpeedMode = 1; }
                return;
            }
            // defending: mark an attacker in the box (same rank), or zone on the six-yard line
            int myRank = HeaderRank(i, 1 - at);
            if (myRank < r.CornerAttackers)
            {
                int target = AttackerWithRank(at, myRank);
                if (target >= 0)
                {
                    var o = _p[target].Pos;
                    p.Target = o + (new Vec2(gx, 0) - o).Normalized() * 1.0f;
                    p.SpeedMode = 1;
                    return;
                }
            }
            if (p.Line == 3 && myRank >= r.CornerAttackers + 4) { p.Target = new Vec2(gx - dir * 30f, p.Base.Y * 0.5f); p.SpeedMode = 1; return; }
            float zy = ((i % 5) - 2) * 3f;
            p.Target = new Vec2(gx - dir * (_c.Pitch.GoalAreaDepth + (i % 2) * 4f), zy);
            p.SpeedMode = 1;
        }

        /// <summary>Rank of player i in his team by Heading (0 = best), keepers and the taker excluded. Ties by index.</summary>
        private int HeaderRank(int i, int team)
        {
            int rank = 0;
            var me = _p[i];
            for (int s = 0; s < 11; s++)
            {
                int j = team * 11 + s;
                if (j == i || _p[j].Keeper || j == _taker) continue;
                if (_p[j].Heading > me.Heading || (_p[j].Heading == me.Heading && j < i)) rank++;
            }
            return rank;
        }

        private int AttackerWithRank(int team, int rank)
        {
            for (int s = 0; s < 11; s++)
            {
                int j = team * 11 + s;
                if (_p[j].Keeper || j == _taker) continue;
                if (HeaderRank(j, team) == rank) return j;
            }
            return -1;
        }

        // ------------------------------------------------------------------ taking the restart

        private void AiRestartKick()
        {
            int i = _taker;
            var p = _p[i];
            int t = p.Team;
            var cp = _ballPos.XY;
            if (_phase == Phase.GoalKick) { KeeperDistribute(i); return; }
            if (_phase == Phase.Corner)
            {
                int best = -1; float bh = -1;
                for (int s = 0; s < 11; s++)
                {
                    int j = t * 11 + s;
                    if (j == i || _p[j].Keeper || !InOwnBox(1 - t, _p[j].Pos, 1f)) continue;
                    float h = _p[j].Heading + Noise(t);
                    if (h > bh) { bh = h; best = j; }
                }
                var target = best >= 0 ? _p[best].Pos : new Vec2(Dir(t) * (HalfX - 8f), 0);
                PassTo(i, target, best, KickKind.Cross, 1f);
                return;
            }
            bool throwIn = _phase == Phase.ThrowIn;
            float maxD = throwIn ? 22f : _c.Ai.GroundPassMax;
            int r = -1; float bv = float.MinValue;
            for (int s = 0; s < 11; s++)
            {
                int j = t * 11 + s;
                if (j == i || _p[j].Keeper) continue;
                var rp = _p[j].Pos;
                float d = Vec2.Distance(cp, rp);
                if (d < 3f || d > maxD) continue;
                if (AiOffsideTarget(t, j)) continue;
                float v = PassValue(t, cp, rp) * LaneSafety(t, cp, rp, d / _c.Ai.PassAvgSpeed) + Noise(t);
                if (v > bv) { bv = v; r = j; }
            }
            var tp = r >= 0 ? _p[r].Pos : ClampInside(cp + new Vec2(Dir(t) * 10f, 0), 2f);
            PassTo(i, tp, r, throwIn ? KickKind.ThrowIn : KickKind.Pass, 1f);
        }

        private void UserRestartKick(Vec2 dir, float power)
        {
            int i = _taker;
            var k = _c.Kicks;
            int r = PickReceiver(i, dir, false);
            Vec2 target = r >= 0 ? _p[r].Pos : ClampInside(_ballPos.XY + dir * MathUtil.Lerp(k.FreePassMin, k.FreePassMax, power), 1f);
            KickKind kind = _phase == Phase.ThrowIn ? KickKind.ThrowIn
                : _phase == Phase.Corner ? KickKind.Cross
                : KickKind.Pass;
            float mul = kind == KickKind.Pass ? MathUtil.Lerp(k.PassPowerMin, k.PassPowerMax, power) : 1f;
            PassTo(i, target, r, kind, mul);
        }
    }
}
