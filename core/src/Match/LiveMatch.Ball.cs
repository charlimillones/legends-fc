using System;

namespace LegendsFC.Core.Match
{
    public sealed partial class LiveMatch
    {
        private enum KickKind : byte { Pass, LoftedPass, Cross, ThroughGround, ThroughLofted, Shot, Header, HeaderPass, Clearance, KeeperThrow, KeeperKick, ThrowIn }

        private static bool IsPassKind(KickKind k) => k != KickKind.Shot && k != KickKind.Header && k != KickKind.Clearance;

        // ------------------------------------------------------------------ ball each tick

        private void BallTick()
        {
            if (_owner >= 0)
            {
                AttachBall();
                var p = _p[_owner];
                if (!p.Holding && (MathF.Abs(_ballPos.X) > HalfX + _c.Ball.Radius || MathF.Abs(_ballPos.Y) > HalfY + _c.Ball.Radius))
                {
                    _lastToucher = _owner;
                    _owner = -1;
                    OutOfPlay();
                }
                return;
            }

            float dist = _ballVel.Length * Dt;
            int sub = 1 + (int)(dist / _c.Ball.MaxSubstep);
            if (sub > 8) sub = 8;
            float h = Dt / sub;
            for (int s = 0; s < sub; s++)
            {
                Vec3 prev = _ballPos;
                BallPhysics.Step(ref _ballPos, ref _ballVel, h, _c.Ball);
                Posts(prev);
                if (KeeperContact(prev)) return;
                if (PlayerContact(prev)) return;
                if (CheckLines(prev)) return;
            }
            if (_passReceiver >= 0 && _ballVel.LengthSq < 0.01f) _passReceiver = -1;
        }

        private void AttachBall()
        {
            var p = _p[_owner];
            var f = Vec2.FromAngle(p.Facing);
            if (p.Holding)
            {
                var hp = p.Pos + f * 0.3f;
                _ballPos = new Vec3(hp, 1.1f);
            }
            else
            {
                var bp = p.Pos + f * _c.Movement.DribbleOffset;
                _ballPos = new Vec3(bp, _c.Ball.Radius);
            }
            _ballVel = new Vec3(p.Vel, 0);
        }

        /// <summary>Posts and crossbar: bounce off thin cylinders.</summary>
        private void Posts(Vec3 prev)
        {
            float r = _c.Ball.Radius + _c.Pitch.PostRadius;
            float gx = HalfX, gy = GoalHalfWidth, gh = _c.Pitch.GoalHeight;
            if (MathF.Abs(_ballPos.X) < gx - 1f || MathF.Abs(_ballPos.X) > gx + 1f) return;
            float sx = _ballPos.X > 0 ? gx : -gx;
            // posts (vertical)
            if (_ballPos.Z < gh + r)
            {
                for (int k = -1; k <= 1; k += 2)
                {
                    var post = new Vec2(sx, k * gy);
                    var d = _ballPos.XY - post;
                    float l = d.Length;
                    if (l < r && l > 1e-5f)
                    {
                        var n = d / l;
                        float vn = _ballVel.X * n.X + _ballVel.Y * n.Y;
                        if (vn < 0)
                        {
                            _ballVel.X -= (1 + _c.Ball.PostRestitution) * vn * n.X;
                            _ballVel.Y -= (1 + _c.Ball.PostRestitution) * vn * n.Y;
                            _counters.PostHits++;
                        }
                        var np = post + n * r;
                        _ballPos.X = np.X; _ballPos.Y = np.Y;
                    }
                }
            }
            // crossbar (along Y at height gh)
            if (MathF.Abs(_ballPos.Y) < gy)
            {
                float dx = _ballPos.X - sx, dz = _ballPos.Z - gh;
                float l = MathF.Sqrt(dx * dx + dz * dz);
                if (l < r && l > 1e-5f)
                {
                    float nx = dx / l, nz = dz / l;
                    float vn = _ballVel.X * nx + _ballVel.Z * nz;
                    if (vn < 0)
                    {
                        _ballVel.X -= (1 + _c.Ball.PostRestitution) * vn * nx;
                        _ballVel.Z -= (1 + _c.Ball.PostRestitution) * vn * nz;
                        _counters.PostHits++;
                    }
                    _ballPos.X = sx + nx * r; _ballPos.Z = gh + nz * r;
                }
            }
        }

        /// <summary>Goal, goal line or touchline. Returns true when play stopped.</summary>
        private bool CheckLines(Vec3 prev)
        {
            float r = _c.Ball.Radius;
            if (MathF.Abs(_ballPos.X) > HalfX + r)
            {
                // where it crossed the line
                float sx = _ballPos.X > 0 ? HalfX + r : -HalfX - r;
                float t = MathF.Abs(_ballPos.X - prev.X) < 1e-6f ? 1 : (sx - prev.X) / (_ballPos.X - prev.X);
                t = MathUtil.Clamp01(t);
                float y = prev.Y + (_ballPos.Y - prev.Y) * t;
                float z = prev.Z + (_ballPos.Z - prev.Z) * t;
                if (MathF.Abs(y) < GoalHalfWidth - r && z < _c.Pitch.GoalHeight - r)
                {
                    int end = _ballPos.X > 0 ? 1 : -1;
                    int scoring = Dir(0) == end ? 0 : 1;
                    Goal(scoring, end);
                    return true;
                }
                OutOfPlay();
                return true;
            }
            if (MathF.Abs(_ballPos.Y) > HalfY + r) { OutOfPlay(); return true; }
            return false;
        }

        private int LastTouchTeam => _lastToucher >= 0 ? _p[_lastToucher].Team : _lastPossTeam;

        private void OutOfPlay()
        {
            int touchTeam = LastTouchTeam;
            ClearBallFlags();
            if (MathF.Abs(_ballPos.Y) > HalfY + _c.Ball.Radius && MathF.Abs(_ballPos.X) <= HalfX + _c.Ball.Radius)
            {
                var spot = new Vec2(MathUtil.Clamp(_ballPos.X, -HalfX + 1, HalfX - 1), _ballPos.Y > 0 ? HalfY : -HalfY);
                _counters.ThrowIns++;
                BeginRestart(Phase.ThrowIn, 1 - touchTeam, spot);
                return;
            }
            int end = _ballPos.X > 0 ? 1 : -1;
            int defending = Dir(0) == end ? 1 : 0;   // the team whose goal is at this end
            float sy = _ballPos.Y >= 0 ? 1 : -1;
            if (touchTeam == defending)
            {
                _counters.Corners++;
                BeginRestart(Phase.Corner, 1 - defending, new Vec2(end * (HalfX - 0.5f), sy * (HalfY - 0.5f)));
            }
            else
            {
                _counters.GoalKicks++;
                BeginRestart(Phase.GoalKick, defending, new Vec2(end * (HalfX - _c.Pitch.GoalAreaDepth), sy * (_c.Pitch.GoalAreaWidth * 0.5f - 1f)));
            }
        }

        private void ClearBallFlags()
        {
            _passReceiver = -1;
            _passTeam = -1;
            for (int i = 0; i < N; i++) { _p[i].Threat = false; _p[i].IgnoreShot = 0; }
        }

        private void Goal(int scoring, int end)
        {
            int conceding = 1 - scoring;
            if (scoring == 0) _homeGoals++; else _awayGoals++;
            _goalCount++;
            // Scorer: the last toucher if he attacks; a deflection or save off a defender goes to the shooter; otherwise own goal.
            int scorer = -1, assist = -1;
            bool own = false;
            if (_lastToucher >= 0 && _p[_lastToucher].Team == scoring) scorer = _lastToucher;
            else if (_lastToucher >= 0 && _lastTouchDeflect && _shooter >= 0 && _p[_shooter].Team == scoring && _shotAge < 4f) scorer = _shooter;
            else if (_lastToucher >= 0) { scorer = _lastToucher; own = true; }
            if (scorer >= 0 && !own)
            {
                _p[scorer].Goals++;
                int a = _p[scorer].AssistFrom;
                if (a >= 0 && a != scorer && _p[a].Team == scoring) { assist = a; _p[a].Assists++; }
                if (scorer == _shooter) _counters.ShotsOnTarget++;
            }
            if (_goals.Count < _goalScorer.Length) _goalScorer[_goals.Count] = scorer;
            _goals.Add(new GoalEvent
            {
                Minute = _minute, Team = (byte)scoring,
                ScorerId = scorer >= 0 ? _p[scorer].Id : null,
                AssistId = assist >= 0 ? _p[assist].Id : null,
                OwnGoal = own
            });
            ClearBallFlags();
            _owner = -1;
            _ballPos = new Vec3(end * (HalfX + _c.Pitch.NetDepth * 0.5f), MathUtil.Clamp(_ballPos.Y, -GoalHalfWidth + 0.3f, GoalHalfWidth - 0.3f), _c.Ball.Radius);
            _ballVel = Vec3.Zero;
            _phase = Phase.GoalScored;
            _phaseTimer = 0;
            _restartTeam = conceding;
            for (int i = 0; i < N; i++) { _p[i].Holding = false; _p[i].Diving = false; }
            SetKickOffTargets(conceding);
        }

        // ------------------------------------------------------------------ touches

        /// <summary>Records a touch by player i (for possession, assists and own goals).</summary>
        private void Touch(int i, bool isPass, bool deflect)
        {
            var p = _p[i];
            if (i != _lastToucher)
            {
                // a new touch: the previous toucher assists if he passed to a teammate
                p.AssistFrom = _lastToucher >= 0 && _p[_lastToucher].Team == p.Team && _lastTouchWasPass && !deflect ? _lastToucher : -1;
            }
            _lastToucher = i;
            _lastTouchWasPass = isPass;
            _lastTouchDeflect = deflect;
        }

        /// <summary>Gives the ball to player i (control, catch or tackle).</summary>
        private void TakeBall(int i, bool hands)
        {
            var p = _p[i];
            Touch(i, false, false);
            _owner = i;
            _lastPossTeam = p.Team;
            _passReceiver = -1;
            _passTeam = -1;
            p.HoldTime = 0;
            p.Decision = Math.Max(_c.Ai.MinHoldSeconds, _level[p.Team].ReactionSeconds * 0.5f);
            p.Holding = hands;
            p.DribbleDir = Vec2.FromAngle(p.Facing);
            if (hands) p.HoldLeft = MathUtil.Lerp(_c.Keeper.HoldMin, _c.Keeper.HoldMax, (float)_rng.NextDouble());
            for (int k = 0; k < 3; k++) _btnArmed[k] = false;
            for (int j = 0; j < N; j++) { _p[j].Threat = false; }
            AttachBall();
        }

        /// <summary>Keepers' hands: saves, catches and parries inside their own box.</summary>
        private bool KeeperContact(Vec3 prev)
        {
            for (int t = 0; t < 2; t++)
            {
                int ki = _keeper[t];
                var k = _p[ki];
                if (k.Stun > 0 && !k.Diving) continue;
                if (k.IgnoreShot > 0) continue;
                if (ki == _lastKicker && k.KickIgnore > 0) continue;
                if (!InOwnBox(t, k.Pos, 0.5f)) continue;
                float speed = _ballVel.Length;
                int own = -Dir(t);
                bool towardGoal = _ballVel.X * own > _c.Keeper.ThreatMinSpeed;
                bool onTarget = towardGoal && OnTarget(t, 0f);
                bool shot = towardGoal && OnTarget(t, _c.Keeper.ThreatMargin) && _lastToucher >= 0 && _p[_lastToucher].Team != t;
                float reach = k.Diving ? _c.Keeper.DiveReach : _c.Keeper.StandReach;
                if (!shot && !k.Diving) reach = Math.Max(reach, _c.Keeper.CollectReach);
                float d = MathUtil.SegmentDistance(k.Pos, prev.XY, _ballPos.XY, out _);
                if (d > reach || _ballPos.Z > _c.Keeper.ReachHeight) continue;
                if (shot)
                {
                    float miss = MathUtil.Lerp(_c.Keeper.MissMax, _c.Keeper.MissMin, k.Reflexes);
                    if ((float)_rng.NextDouble() < miss) { k.IgnoreShot = 0.4f; continue; }
                }
                float catchP = _c.Keeper.CatchBase + _c.Keeper.CatchHandling * k.Handling
                               - _c.Keeper.CatchSpeedPenalty * Math.Max(0, speed - _c.Keeper.CatchEasySpeed)
                               - (k.Diving ? _c.Keeper.CatchStretchPenalty : 0);
                if (!shot && speed < _c.Keeper.CatchEasySpeed) catchP = 1;   // collecting a slow ball
                if (onTarget) { _counters.Saves++; _counters.ShotsOnTarget++; }
                if ((float)_rng.NextDouble() < catchP)
                {
                    if (onTarget) _counters.Catches++;
                    _ballVel = Vec3.Zero;
                    TakeBall(ki, true);
                    k.Diving = false;
                    return true;
                }
                Touch(ki, false, true);
                k.IgnoreShot = 0.5f;
                _passReceiver = -1;
                if (_ballPos.Z > _c.Keeper.TipHeight && towardGoal)
                {
                    if (onTarget) _counters.Tips++;
                    // tipped over the bar
                    _ballVel = new Vec3(_ballVel.X * 0.4f, _ballVel.Y * 0.4f, Math.Max(3f, _ballVel.Z + 3f));
                }
                else
                {
                    if (onTarget) _counters.Parries++;
                    float spread = ((float)_rng.NextDouble() * 2 - 1) * _c.Keeper.ParrySpreadDeg * MathUtil.Deg;
                    var away = new Vec2(-own, 0).Rotated(spread);
                    if (away.X * -own < -0.15f) away = new Vec2(-own * 0.15f, away.Y).Normalized();   // never back into the net
                    float sp = Math.Max(3f, speed * _c.Keeper.ParrySpeedKeep);
                    _ballVel = new Vec3(away * sp, (float)_rng.NextDouble() * 2.5f);
                }
                return false;
            }
            return false;
        }

        /// <summary>Would the loose ball cross team t's goal line inside the frame (plus margin)?</summary>
        private bool OnTarget(int t, float margin)
        {
            int own = -Dir(t);
            float gx = own * HalfX;
            if (MathF.Abs(_ballVel.X) < 1e-3f) return false;
            float time = (gx - _ballPos.X) / _ballVel.X;
            if (time < 0 || time > _c.Keeper.ThreatSeconds) return false;
            float y = _ballPos.Y + _ballVel.Y * time;
            float z = _ballPos.Z + _ballVel.Z * time - 0.5f * _c.Ball.Gravity * time * time;
            if (z < 0) z = 0.2f; // bounced: still low
            return MathF.Abs(y) < GoalHalfWidth + margin && z < _c.Pitch.GoalHeight + margin;
        }

        private bool InOwnBox(int t, Vec2 pos, float margin)
        {
            int own = -Dir(t);
            float bx = own * HalfX;
            return MathF.Abs(pos.X - bx) < _c.Pitch.PenaltyBoxDepth + margin && MathF.Abs(pos.Y) < _c.Pitch.PenaltyBoxWidth * 0.5f + margin
                   && pos.X * own > 0;
        }

        /// <summary>Outfield (and keepers outside their box): control, headers and blocks. Returns true when someone took the ball.</summary>
        private bool PlayerContact(Vec3 prev)
        {
            int ctrl = -1, head = -1, block = -1;
            float ctrlD = float.MaxValue, headScore = float.MinValue, blockD = float.MaxValue;
            float z = _ballPos.Z;
            for (int i = 0; i < N; i++)
            {
                var p = _p[i];
                if (p.KickIgnore > 0 && i == _lastKicker) continue;
                if (p.Stun > 0 || p.Diving || p.IgnoreShot > 0) continue;
                if (p.Keeper && InOwnBox(p.Team, p.Pos, 0.5f)) continue;   // hands: KeeperContact
                float d = MathUtil.SegmentDistance(p.Pos, prev.XY, _ballPos.XY, out _);
                if (z < _c.Touch.FootHeight)
                {
                    if (d > p.Reach && d > _c.Touch.BlockReach) continue;
                    var rel = _ballVel.XY - p.Vel;
                    float rs = rel.Length;
                    if (d <= p.Reach && rs <= p.ControlSpeed) { if (d < ctrlD) { ctrlD = d; ctrl = i; } }
                    else if (d <= _c.Touch.BlockReach && z < _c.Touch.BlockHeight && d < blockD) { blockD = d; block = i; }
                }
                else if (z >= _c.Touch.HeaderMin && z <= _c.Touch.HeaderMax && d <= _c.Touch.HeaderReach && !p.Keeper && _headerLock <= 0)
                {
                    float score = _c.Touch.HeaderDuelHeading * p.Heading + _c.Touch.HeaderDuelStrength * p.Strength
                                  + _c.Touch.HeaderDuelNoise * (float)_rng.NextDouble();
                    if (score > headScore) { headScore = score; head = i; }
                }
                else if (z < _c.Touch.BlockHeight && d <= _c.Touch.BlockReach && d < blockD) { blockD = d; block = i; }
            }
            if (ctrl >= 0)
            {
                TakeBall(ctrl, false);
                return true;
            }
            if (head >= 0) { DoHeader(head); return false; }
            if (block >= 0)
            {
                var p = _p[block];
                if ((float)_rng.NextDouble() < _c.Touch.BlockChance)
                {
                    float spread = ((float)_rng.NextDouble() * 2 - 1) * _c.Touch.BlockSpreadDeg * MathUtil.Deg;
                    var h = _ballVel.XY.Rotated(spread) * _c.Touch.BlockSpeedKeep;
                    _ballVel = new Vec3(h, Math.Abs(_ballVel.Z) * 0.3f + (float)_rng.NextDouble() * 1.5f);
                    _counters.Blocks++;
                    Touch(block, false, true);
                    _passReceiver = -1;
                    p.KickIgnore = 0.25f;
                    _lastKicker = block;
                }
                else p.IgnoreShot = 0.3f;
            }
            return false;
        }
    }
}
