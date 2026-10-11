using System;

namespace LegendsFC.Core.Match
{
    public sealed partial class LiveMatch
    {
        private const int PredCount = 30;
        private const float PredDt = 0.1f;
        private readonly Vec3[] _pred = new Vec3[PredCount + 1];
        private const int HistoryLen = 64;
        private readonly Vec2[] _history = new Vec2[HistoryLen];

        private void PlayTick(in ControlInput input)
        {
            UpdateControl();
            TeamAi();
            KeeperAi();
            UserControl(input);
            MovePlayers();
            BallTick();
        }

        // ------------------------------------------------------------------ prediction

        /// <summary>Where the loose ball will be over the next 3 seconds (0.1 s steps), for the AI and switching.</summary>
        private void PredictBall()
        {
            var pos = _ballPos; var vel = _ballVel;
            _pred[0] = pos;
            for (int k = 1; k <= PredCount; k++)
            {
                if (_owner < 0)
                {
                    BallPhysics.Step(ref pos, ref vel, PredDt * 0.5f, _c.Ball);
                    BallPhysics.Step(ref pos, ref vel, PredDt * 0.5f, _c.Ball);
                }
                _pred[k] = pos;
            }
        }

        private void RecordBall()
        {
            _history[_tick % HistoryLen] = _ballPos.XY;
        }

        /// <summary>The ball as team t's AI sees it: some reaction ticks old (difficulty).</summary>
        private Vec2 PerceivedBall(int t)
        {
            int delay = (int)(_level[t].ReactionSeconds / Dt);
            if (delay >= HistoryLen) delay = HistoryLen - 1;
            if (delay > _tick) delay = _tick;
            return _history[(_tick - delay + HistoryLen * 4) % HistoryLen];
        }

        /// <summary>Earliest time player i can reach the ball (loose: its predicted path; owned: the carrier).</summary>
        private readonly int[] _ttbTick = new int[N], _ttbOwner = new int[N];
        private readonly float[] _ttbTime = new float[N];
        private readonly Vec2[] _ttbPoint = new Vec2[N];

        /// <summary>Earliest time player i can reach the ball, cached for the tick (it's asked for many times).</summary>
        private float TimeToBall(int i, out Vec2 point)
        {
            if (_ttbTick[i] == _tick && _ttbOwner[i] == _owner) { point = _ttbPoint[i]; return _ttbTime[i]; }
            float t = TimeToBallRaw(i, out point);
            _ttbTick[i] = _tick; _ttbOwner[i] = _owner; _ttbTime[i] = t; _ttbPoint[i] = point;
            return t;
        }

        private float TimeToBallRaw(int i, out Vec2 point)
        {
            var p = _p[i];
            float speed = Math.Max(1f, p.SprintSpeed * EnergyFactor(p));
            if (_owner >= 0)
            {
                point = _ballPos.XY;
                return Vec2.Distance(p.Pos, point) / speed;
            }
            for (int k = 0; k <= PredCount; k++)
            {
                var b = _pred[k];
                if (b.Z > _c.Touch.HeaderMax) continue;
                float d = Vec2.Distance(p.Pos, b.XY) - p.Reach;
                float t = k * PredDt;
                if (d <= speed * t + 0.05f) { point = b.XY; return Math.Max(t, d / speed); }
            }
            point = _pred[PredCount].XY;
            return PredCount * PredDt + Vec2.Distance(p.Pos, point) / speed;
        }

        // ------------------------------------------------------------------ switching (user)

        private bool UserAttacking => _userTeam >= 0 && ((_owner >= 0 && _p[_owner].Team == _userTeam) || (_owner < 0 && _passReceiver >= 0 && _passTeam == _userTeam));

        private void UpdateControl()
        {
            if (_userTeam < 0) { _controlled = -1; return; }
            if (_owner >= 0 && _p[_owner].Team == _userTeam)
            {
                if (!_p[_owner].Keeper) SetControlled(_owner);
                return;
            }
            if (_owner < 0 && _passReceiver >= 0 && _passTeam == _userTeam)
            {
                if (!_p[_passReceiver].Keeper) SetControlled(_passReceiver);
                return;
            }
            // defending or loose ball
            bool switchPressed = false;
            for (int k = 0; k < 3; k++) if (_defenceMap[k] == ButtonAction.Switch && _btnPressed[k]) switchPressed = true;
            if (switchPressed || _controlled < 0)
            {
                int best = BestDefender(_controlled, out _);
                if (best >= 0) SetControlled(best);
                return;
            }
            float sens = _setup.Settings == null ? 0.5f : _setup.Settings.SwitchSensitivity;
            if (sens <= 0 || _sinceSwitch < _c.Switching.DwellSeconds) return;
            int cand = BestDefender(-1, out float tb);
            if (cand < 0 || cand == _controlled) return;
            float tc = TimeToBall(_controlled, out _);
            float margin = MathUtil.Lerp(_c.Switching.MarginAtLow, _c.Switching.MarginAtHigh, MathUtil.Clamp01(sens));
            if (tc - tb > margin) SetControlled(cand);
        }

        private void SetControlled(int i)
        {
            if (i == _controlled) return;
            _controlled = i;
            _sinceSwitch = 0;
        }

        /// <summary>The user's defender who can reach the ball's path soonest (never the keeper), excluding one player.</summary>
        private int BestDefender(int exclude, out float time)
        {
            int best = -1; time = float.MaxValue;
            for (int s = 0; s < 11; s++)
            {
                int i = _userTeam * 11 + s;
                if (i == exclude || _p[i].Keeper) continue;
                float t = TimeToBall(i, out _);
                if (t < time) { time = t; best = i; }
            }
            return best;
        }

        // ------------------------------------------------------------------ the user's player

        private Vec2 StickWorld(in ControlInput input, out float mag)
        {
            var s = new Vec2(MathUtil.Clamp(input.MoveX, -1, 1), MathUtil.Clamp(input.MoveY, -1, 1));
            mag = s.Length;
            if (mag > 1) { s = s / mag; mag = 1; }
            return s * Dir(_userTeam);
        }

        private void UserControl(in ControlInput input)
        {
            if (_controlled < 0) return;
            var p = _p[_controlled];
            var stick = StickWorld(input, out float mag);
            var dir = mag > 0.05f ? stick / mag : Vec2.FromAngle(p.Facing);
            float speed = 0;
            if (mag > 0.05f)
            {
                if (input.Sprint) speed = p.SprintSpeed;
                else if (mag < _c.Movement.WalkStick) speed = _c.Movement.WalkSpeed * Math.Min(1f, mag / _c.Movement.WalkStick);
                else speed = p.RunSpeed;
            }
            p.UseDirect = true;
            p.DirectVel = dir * speed;
            if (mag <= 0.05f && _owner < 0 && _controlled == _passReceiver)
            {
                // stick centred while a pass comes to him: he goes to meet it like any AI receiver
                p.UseDirect = false;
                TimeToBall(_controlled, out var pt);
                p.Target = pt; p.SpeedMode = 2;
            }

            bool carrier = _owner == _controlled && !p.Holding;
            if (carrier)
            {
                for (int k = 0; k < 3; k++)
                {
                    if (_btnPressed[k]) _btnArmed[k] = true;
                    if (!_btnArmed[k]) continue;
                    bool fire = _btnReleased[k] || (_btnPrev[k] && _btnHold[k] >= _c.Kicks.HoldCapSeconds);
                    if (!fire) continue;
                    float held = _btnReleased[k] ? _btnReleasedHold[k] : _btnHold[k];
                    _btnArmed[k] = false;
                    UserKick(_attackMap[k], dir, held);
                    return;
                }
                return;
            }
            for (int k = 0; k < 3; k++) _btnArmed[k] = false;
            if (UserAttacking) return;
            for (int k = 0; k < 3; k++)
            {
                var a = _defenceMap[k];
                bool held = k == 0 ? input.A : k == 1 ? input.B : input.C;
                if (a == ButtonAction.Tackle && _btnPressed[k]) TryTackle(_controlled);
                if (a == ButtonAction.Pressure && held && _owner >= 0 && _p[_owner].Team != p.Team)
                {
                    var to = _ballPos.XY - p.Pos;
                    p.DirectVel = to.Normalized() * (input.Sprint ? p.SprintSpeed : p.RunSpeed);
                }
            }
        }

        private void UserKick(ButtonAction a, Vec2 dir, float held)
        {
            int i = _controlled;
            float power = PowerFromHold(held);
            var k = _c.Kicks;
            switch (a)
            {
                case ButtonAction.Shoot:
                {
                    float speed = ShotSpeed(_p[i]) * MathUtil.Lerp(k.ShotPowerFloor, 1, power);
                    float elev = MathUtil.Lerp(k.ShotLoftMinDeg, k.ShotLoftMaxDeg, power) * MathUtil.Deg;
                    var v = new Vec3(dir * (speed * MathF.Cos(elev)), speed * MathF.Sin(elev));
                    float scale = power > k.ShotWildPower ? k.ShotWildScale : 1f;
                    v = WithError(i, v, _p[i].Shooting, k.ShotErrorMaxDeg, k.ShotErrorMinDeg, scale, true);
                    Release(i, v, KickKind.Shot, -1);
                    return;
                }
                case ButtonAction.Through:
                {
                    bool lofted = held >= k.ThroughLoftHoldSeconds;
                    int r = PickReceiver(i, dir, true);
                    float lead = MathUtil.Lerp(k.ThroughLeadMin, k.ThroughLeadMax, power);
                    Vec2 target = r >= 0 ? _p[r].Pos + dir * lead : _ballPos.XY + dir * MathUtil.Lerp(k.FreePassMin, k.FreePassMax, power);
                    target = ClampInside(target, 1f);
                    PassTo(i, target, r, lofted ? KickKind.ThroughLofted : KickKind.ThroughGround, 1f);
                    return;
                }
                default:
                {
                    int r = PickReceiver(i, dir, false);
                    if (r >= 0)
                    {
                        var target = Lead(i, r);
                        PassTo(i, target, r, KickKind.Pass, MathUtil.Lerp(k.PassPowerMin, k.PassPowerMax, power));
                    }
                    else
                    {
                        var target = ClampInside(_ballPos.XY + dir * MathUtil.Lerp(k.FreePassMin, k.FreePassMax, power), 0f);
                        PassTo(i, target, -1, KickKind.Pass, 1f);
                    }
                    return;
                }
            }
        }

        /// <summary>The teammate in the stick's cone (closest to the stick line, then nearest).</summary>
        private int PickReceiver(int i, Vec2 dir, bool through)
        {
            var me = _p[i];
            float cone = _c.Kicks.PassConeDeg * MathUtil.Deg;
            int best = -1; float bestScore = float.MaxValue;
            for (int s = 0; s < 11; s++)
            {
                int j = me.Team * 11 + s;
                if (j == i) continue;
                var to = _p[j].Pos - me.Pos;
                float d = to.Length;
                if (d < 2f) continue;
                float cos = MathUtil.Clamp(Vec2.Dot(to / d, dir), -1, 1);
                float ang = MathF.Acos(cos);
                if (ang > cone) continue;
                float score = ang * 2f + d * 0.02f;
                if (through) score -= (_p[j].Pos.X - me.Pos.X) * Dir(me.Team) * 0.02f;
                if (score < bestScore) { bestScore = score; best = j; }
            }
            return best;
        }

        // ------------------------------------------------------------------ movement

        private float EnergyFactor(P p)
            => 1f - _c.Energy.SpeedLossPerPoint * (float)Math.Max(0, _c.Squad.EnergyPenaltyBelow - p.Energy);

        private void MovePlayers()
        {
            var m = _c.Movement;
            for (int i = 0; i < N; i++)
            {
                var p = _p[i];
                if (p.Diving)
                {
                    p.DiveTimer -= Dt;
                    p.Vel = p.DiveVel;
                    p.Pos = p.Pos + p.Vel * Dt;
                    if (p.DiveTimer <= 0) { p.Diving = false; p.Stun = 0.35f; p.Vel = Vec2.Zero; }
                    ClampPlayer(p);
                    continue;
                }
                float ef = EnergyFactor(p);
                bool carrier = _owner == i && !p.Holding;
                Vec2 dv;
                if (p.UseDirect) dv = p.DirectVel;
                else
                {
                    var to = p.Target - p.Pos;
                    float dist = to.Length;
                    float cap = p.SpeedMode == 2 ? p.SprintSpeed : p.SpeedMode == 1 ? p.RunSpeed : p.SpeedMode == 3 ? Math.Min(m.JogSpeed, p.RunSpeed) : m.WalkSpeed;
                    float sp = Math.Min(cap, dist * 2.5f);
                    if (dist < 0.15f || (p.SpeedMode == 0 && dist < m.ArriveRadius)) sp = 0;
                    dv = dist > 1e-4f ? to / dist * sp : Vec2.Zero;
                }
                float top = p.SprintSpeed * ef * (carrier ? p.DribbleFactor : 1f);
                if (p.Holding) top = m.WalkSpeed;
                if (p.Stun > 0) top *= _c.Tackles.StunSpeed;
                dv = dv.ClampLength(top);
                if (carrier && dv.LengthSq > 0.01f)
                {
                    // with the ball he turns at a rate set by Dribbling and runs where he faces
                    float want = dv.Angle;
                    p.Facing = MathUtil.TurnTowards(p.Facing, want, p.TurnWithBall * Dt);
                    float along = Math.Max(0, Vec2.Dot(dv, Vec2.FromAngle(p.Facing)));
                    dv = Vec2.FromAngle(p.Facing) * Math.Max(along, dv.Length * 0.3f);
                }
                var delta = dv - p.Vel;
                bool braking = Vec2.Dot(dv, p.Vel) < 0 || dv.LengthSq < p.Vel.LengthSq;
                float maxDelta = (braking ? m.Brake : p.AccelRate * ef) * Dt;
                p.Vel = p.Vel + delta.ClampLength(maxDelta);
                p.Pos = p.Pos + p.Vel * Dt;
                ClampPlayer(p);
                float spd = p.Vel.Length;
                if (!carrier)
                {
                    if (spd > 0.4f) p.Facing = MathUtil.TurnTowards(p.Facing, p.Vel.Angle, m.TurnRate * Dt);
                    else p.Facing = MathUtil.TurnTowards(p.Facing, (_ballPos.XY - p.Pos).Angle, m.TurnRate * Dt);
                }
                // distance and energy (squad.json formula, Carlos Oct 9): match km = real metres x clock scale
                double km = spd * Dt * _kmScale * _c.Energy.DistanceScale / 1000.0;
                p.Km += km;
                p.Energy -= (float)(km * _c.Squad.EnergyPerKm * p.StaminaFactor);
                if (p.Energy < 0) p.Energy = 0;
            }
            Separate();
        }

        private void ClampPlayer(P p)
        {
            float mx = HalfX + _c.Pitch.PlayerMargin, my = HalfY + _c.Pitch.PlayerMargin;
            if (p.Pos.X > mx) p.Pos.X = mx; else if (p.Pos.X < -mx) p.Pos.X = -mx;
            if (p.Pos.Y > my) p.Pos.Y = my; else if (p.Pos.Y < -my) p.Pos.Y = -my;
        }

        /// <summary>Players don't overlap: pairs closer than two radii are pushed apart.</summary>
        private void Separate()
        {
            float r2 = 2 * _c.Movement.PlayerRadius;
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    var d = _p[j].Pos - _p[i].Pos;
                    float l2 = d.LengthSq;
                    if (l2 >= r2 * r2 || l2 < 1e-8f) continue;
                    float l = MathF.Sqrt(l2);
                    var push = d * ((r2 - l) * 0.5f / l);
                    if (!_p[i].Holding && !_p[i].Diving) { _p[i].Pos = _p[i].Pos - push; ClampPlayer(_p[i]); }
                    if (!_p[j].Holding && !_p[j].Diving) { _p[j].Pos = _p[j].Pos + push; ClampPlayer(_p[j]); }
                }
            }
        }

        private Vec2 ClampInside(Vec2 v, float margin)
        {
            float mx = HalfX - margin, my = HalfY - margin;
            return new Vec2(MathUtil.Clamp(v.X, -mx, mx), MathUtil.Clamp(v.Y, -my, my));
        }

        // ------------------------------------------------------------------ tackles

        /// <summary>A tackle attempt on the carrier (same duel for the user and the AI). Out of range = a lunge that misses.</summary>
        private void TryTackle(int i)
        {
            var p = _p[i];
            var t = _c.Tackles;
            if (p.TackleCd > 0 || p.Stun > 0) return;
            p.TackleCd = t.CooldownSeconds;
            p.Action = PlayerAction.Tackle; p.ActionTimer = 0.3f;
            if (_owner < 0 || _p[_owner].Team == p.Team || _p[_owner].Holding) { p.Stun = t.FailStunSeconds * 0.5f; return; }
            var o = _p[_owner];
            if (Vec2.Distance(p.Pos, _ballPos.XY) > t.Range || o.Protect > 0) { p.Stun = t.FailStunSeconds; return; }
            _counters.Tackles++;
            float chance = t.Base + t.SkillWeight * (p.Tackling - o.Dribbling) + t.StrengthWeight * (p.Strength - o.Strength);
            chance = MathUtil.Clamp(chance, t.MinChance, t.MaxChance);
            if ((float)_rng.NextDouble() < chance)
            {
                _counters.TacklesWon++;
                int victim = _owner;
                if ((float)_rng.NextDouble() < t.KeepChance)
                {
                    _owner = -1;
                    TakeBall(i, false);
                    p.Protect = t.ProtectSeconds;
                }
                else
                {
                    _owner = -1;
                    var away = (_ballPos.XY - p.Pos).Normalized().Rotated(((float)_rng.NextDouble() * 2 - 1) * 0.7f);
                    if (away.LengthSq < 0.1f) away = Vec2.FromAngle(p.Facing);
                    _ballVel = new Vec3(away * t.PokeSpeed, 0);
                    Touch(i, false, true);
                    _lastKicker = i;
                    p.KickIgnore = 0.1f;
                    _passReceiver = -1;
                }
                _p[victim].Stun = t.FailStunSeconds * 0.6f;
                _p[victim].KickIgnore = 0.3f;
            }
            else p.Stun = t.FailStunSeconds;
        }

        // ------------------------------------------------------------------ kicks (same error model for everyone)

        private float ShotSpeed(P p) => MathUtil.Lerp(_c.Kicks.ShotSpeedMin, _c.Kicks.ShotSpeedMax, p.Shooting);

        private float NearestOpponent(int i, Vec2 at)
        {
            int t = _p[i].Team;
            float best = float.MaxValue;
            for (int s = 0; s < 11; s++)
            {
                int j = (1 - t) * 11 + s;
                float d = Vec2.Distance(_p[j].Pos, at);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>Applies direction, power and (optionally) elevation errors by skill, pressure and a kind scale.</summary>
        private Vec3 WithError(int i, Vec3 v, float skill, float maxDeg, float minDeg, float scale, bool elevation)
        {
            var k = _c.Kicks;
            float pressure = 1f + k.PressureScale * MathUtil.Clamp01(1f - NearestOpponent(i, _p[i].Pos) / k.PressureRadius);
            float s = scale * pressure;
            float dirSd = MathUtil.Lerp(maxDeg, minDeg, skill) * s;
            float powSd = MathUtil.Lerp(k.PowerErrorMax, k.PowerErrorMin, skill) * s;
            float h = v.HorizontalLength;
            float elev = MathF.Atan2(v.Z, Math.Max(1e-4f, h));
            float speed = v.Length;
            float yaw = MathF.Atan2(v.Y, v.X) + (float)_rng.Gaussian(0, dirSd) * MathUtil.Deg;
            speed *= MathUtil.Clamp(1f + (float)_rng.Gaussian(0, powSd), 0.6f, 1.4f);
            if (elevation)
            {
                float eSd = MathUtil.Lerp(k.ElevationErrorMaxDeg, k.ElevationErrorMinDeg, skill) * s;
                elev += (float)_rng.Gaussian(0, eSd) * MathUtil.Deg;
                if (elev < -0.05f) elev = -0.05f;
            }
            float c = MathF.Cos(elev);
            return new Vec3(MathF.Cos(yaw) * c * speed, MathF.Sin(yaw) * c * speed, MathF.Sin(elev) * speed);
        }

        /// <summary>Where to aim a pass to teammate r: his position plus his run.</summary>
        private Vec2 Lead(int i, int r)
        {
            float d = Vec2.Distance(_p[i].Pos, _p[r].Pos);
            var target = _p[r].Pos + _p[r].Vel * (d / _c.Ai.PassAvgSpeed);
            return ClampInside(target, 1f);
        }

        /// <summary>Kicks a pass of the given kind from the ball to target. powerMul scales a ground pass's speed.</summary>
        private void PassTo(int i, Vec2 target, int receiver, KickKind kind, float powerMul)
        {
            var p = _p[i];
            var k = _c.Kicks;
            var from = _ballPos.XY;
            var to = target - from;
            float d = Math.Max(0.5f, to.Length);
            var dir = to / d;
            float z0 = _ballPos.Z;
            float skill = kind == KickKind.Cross ? p.Crossing : p.Keeper ? p.Distribution : p.Passing;
            float maxDeg = k.PassErrorMaxDeg, minDeg = k.PassErrorMinDeg;
            Vec3 v;
            float scale = 1f;
            bool lofted = kind == KickKind.LoftedPass || kind == KickKind.Cross || kind == KickKind.ThroughLofted || kind == KickKind.Clearance || kind == KickKind.KeeperKick || kind == KickKind.ThrowIn;
            if (lofted)
            {
                float ang = kind == KickKind.ThroughLofted ? k.ThroughLoftAngleDeg
                    : kind == KickKind.Cross ? k.CrossAngleDeg
                    : kind == KickKind.Clearance ? k.ClearAngleDeg
                    : kind == KickKind.KeeperKick ? _c.Keeper.LongKickAngleDeg
                    : kind == KickKind.ThrowIn ? k.ThrowAngleDeg
                    : k.LoftAngleDeg;
                float a = ang * MathUtil.Deg;
                float speed = BallPhysics.LoftSpeedFor(d, a, z0, _c.Ball, k.LoftSolveMaxSpeed, out _);
                if (kind == KickKind.ThrowIn) speed = Math.Min(speed, k.ThrowSpeedMax);
                v = new Vec3(dir * (speed * MathF.Cos(a)), speed * MathF.Sin(a));
                scale = kind == KickKind.ThroughLofted ? k.ThroughLoftedErrorScale : kind == KickKind.ThrowIn ? 1f : k.LoftedErrorScale;
            }
            else
            {
                float speed = MathUtil.Clamp(BallPhysics.GroundSpeedFor(d, k.PassArrivalSpeed, _c.Ball) * powerMul, k.MinGroundSpeed, k.MaxGroundSpeed);
                float vz = kind == KickKind.KeeperThrow ? 1f : 0f;
                v = new Vec3(dir * speed, vz);
            }
            v = WithError(i, v, skill, maxDeg, minDeg, scale, lofted);
            Release(i, v, kind, receiver);
        }

        /// <summary>The ball leaves player i with velocity v.</summary>
        private void Release(int i, Vec3 v, KickKind kind, int receiver)
        {
            var p = _p[i];
            _owner = -1;
            // a ball released from the keeper's hands can't be taken by an opponent straight away (same for both teams)
            if (p.Holding) { _handsGuardTeam = 1 - p.Team; _handsGuard = _c.Keeper.ReleaseGuardSeconds; }
            p.Holding = false;
            _ballVel = v;
            if (_ballPos.Z < _c.Ball.Radius) _ballPos.Z = _c.Ball.Radius;
            if (v.Z > 0 && _ballPos.Z <= _c.Ball.Radius) _ballPos.Z = _c.Ball.Radius + 0.001f;
            p.KickIgnore = _c.Touch.KickerIgnoreSeconds;
            p.Action = kind == KickKind.ThrowIn ? PlayerAction.ThrowIn : (kind == KickKind.Header || kind == KickKind.HeaderPass) ? PlayerAction.Header : PlayerAction.Kick;
            p.ActionTimer = 0.3f;
            _lastKicker = i;
            bool pass = IsPassKind(kind);
            Touch(i, pass, false);
            if (pass)
            {
                if (kind == KickKind.Cross) _counters.Crosses++;
                else if (kind == KickKind.ThroughGround) _counters.ThroughBalls++;
                else if (kind == KickKind.ThroughLofted) _counters.LoftedThroughBalls++;
                else if (kind == KickKind.LoftedPass) _counters.LoftedPasses++;
                p.Passes++;
                _counters.Passes++;
                p.Energy -= (float)(_c.Squad.EnergyPerPass * p.StaminaFactor);
                _passReceiver = receiver;
                _passTeam = receiver >= 0 ? p.Team : -1;
            }
            else
            {
                _passReceiver = -1; _passTeam = -1;
            }
            if (kind == KickKind.Shot || kind == KickKind.Header)
            {
                p.Shots++;
                _counters.Shots++;
                p.Energy -= (float)(_c.Squad.EnergyPerShot * p.StaminaFactor);
                _shooter = i;
                _shotAge = 0;
            }
            if (p.Energy < 0) p.Energy = 0;
            for (int k = 0; k < 3; k++) _btnArmed[k] = false;
            if (_phase != Phase.Play) { _phase = Phase.Play; _phaseTimer = 0; }
        }
    }
}
