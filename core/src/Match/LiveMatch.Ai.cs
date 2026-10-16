using System;

namespace LegendsFC.Core.Match
{
    public sealed partial class LiveMatch
    {
        private readonly Vec2[] _readNoise = new Vec2[2];
        private readonly float[] _readTimer = new float[2];

        // ------------------------------------------------------------------ team AI (same code for both teams)

        private bool TeamAttacking(int t) => (_owner >= 0 && _p[_owner].Team == t) || (_owner < 0 && _passReceiver >= 0 && _passTeam == t);
        private bool TeamDefending(int t) => (_owner >= 0 && _p[_owner].Team != t) || (_owner < 0 && _passReceiver >= 0 && _passTeam != t);

        private void RefreshReading(int t)
        {
            _readTimer[t] -= Dt;
            if (_readTimer[t] > 0) return;
            var lvl = _level[t];
            _readTimer[t] = Math.Max(_c.Ai.DecisionSeconds, lvl.ReactionSeconds);
            _readNoise[t] = new Vec2((float)_rng.Gaussian(0, lvl.ReadNoise), (float)_rng.Gaussian(0, lvl.ReadNoise));
        }

        private void TeamAi()
        {
            for (int t = 0; t < 2; t++)
            {
                RefreshReading(t);
                bool attacking = TeamAttacking(t), defending = TeamDefending(t);
                // the two outfield players who can reach the ball first
                int first = -1, second = -1; float t1 = float.MaxValue, t2 = float.MaxValue;
                for (int s = 0; s < 11; s++)
                {
                    int i = t * 11 + s;
                    if (_p[i].Keeper || i == _owner) continue;
                    float tt = TimeToBall(i, out _);
                    if (tt < t1) { second = first; t2 = t1; first = i; t1 = tt; }
                    else if (tt < t2) { second = i; t2 = tt; }
                }
                int sup1 = -1, sup2 = -1;
                if (attacking && _owner >= 0) NearestSupport(t, out sup1, out sup2);

                for (int s = 0; s < 11; s++)
                {
                    int i = t * 11 + s;
                    var p = _p[i];
                    if (p.Keeper) continue;
                    if (i == _controlled) continue;
                    p.UseDirect = false;
                    if (i == _owner) { CarrierAi(i); continue; }
                    if (attacking)
                    {
                        if (i == _passReceiver) { TimeToBall(i, out var pt); p.Target = pt; p.SpeedMode = 2; }
                        else AttackSpot(i, i == sup1 || i == sup2);
                    }
                    else if (defending)
                    {
                        if (_owner >= 0 && _p[_owner].Holding) DefendSpot(i);   // nobody presses a keeper with the ball in his hands
                        else if (i == first) Press(i);
                        else if (i == second && _owner >= 0) Cover(i);
                        else if (i == second) { TimeToBall(i, out var pt); p.Target = pt; p.SpeedMode = 1; }
                        else DefendSpot(i);
                    }
                    else
                    {
                        if (i == first) { TimeToBall(i, out var pt); p.Target = pt; p.SpeedMode = 2; }
                        else if (_lastPossTeam == t) AttackSpot(i, false);
                        else DefendSpot(i);
                    }
                }
            }
        }

        /// <summary>The two midfielders/defenders nearest the carrier move to give him passing angles.</summary>
        private void NearestSupport(int t, out int a, out int b)
        {
            a = -1; b = -1; float da = float.MaxValue, db = float.MaxValue;
            var cp = _ballPos.XY;
            for (int s = 0; s < 11; s++)
            {
                int i = t * 11 + s;
                if (i == _owner || _p[i].Keeper || _p[i].Line == 3) continue;
                float d = Vec2.DistanceSq(_p[i].Pos, cp);
                if (d < da) { b = a; db = da; a = i; da = d; }
                else if (d < db) { b = i; db = d; }
            }
        }

        /// <summary>Team t's frame: x towards the opponent's goal.</summary>
        private Vec2 ToFrame(int t, Vec2 w) { int d = Dir(t); return new Vec2(w.X * d, w.Y * d); }
        private Vec2 ToWorld(int t, Vec2 f) => ToFrame(t, f);

        /// <summary>The offside line in team t's frame (second-last opponent or the ball, whichever is further forward).</summary>
        private float OffsideLine(int t, float ballX)
        {
            float m1 = -99, m2 = -99;
            int d = Dir(t);
            for (int s = 0; s < 11; s++)
            {
                float x = _p[(1 - t) * 11 + s].Pos.X * d;
                if (x > m1) { m2 = m1; m1 = x; } else if (x > m2) m2 = x;
            }
            float line = Math.Max(m2, ballX);
            return Math.Min(line, HalfX - 1f);
        }

        private Vec2 ShapeSpot(int i, bool inPossession, out Vec2 ballFrame)
        {
            var p = _p[i];
            int t = p.Team;
            var a = _c.Ai;
            ballFrame = ToFrame(t, PerceivedBall(t) + _readNoise[t]);
            float x = p.Base.X + (inPossession ? a.PossessionPush : -a.DefendDrop) + a.FollowBallX * ballFrame.X + a.MentalityStep * _mentality[t];
            float y = p.Base.Y * (inPossession ? a.WidthAttack : a.WidthDefend) + a.FollowBallY * ballFrame.Y;
            if (p.Line == 1) x = MathUtil.Clamp(x, a.BackLineMin, a.BackLineMax);
            return new Vec2(x, y);
        }

        private void AttackSpot(int i, bool support)
        {
            var p = _p[i];
            int t = p.Team;
            var a = _c.Ai;
            var f = ShapeSpot(i, true, out var bf);
            float line = OffsideLine(t, bf.X);
            int mode = 1;
            bool carrierOk = _owner >= 0 && _p[_owner].Team == t && !_p[_owner].Keeper;
            if (p.Line == 3 || (p.Line == 2 && MathF.Abs(p.Base.Y) > 15f))
            {
                if (carrierOk && p.RunTimer <= 0 && p.RunCd <= 0)
                {
                    var c = _p[_owner];
                    float cx = c.Pos.X * Dir(t);
                    bool facing = Vec2.Dot(Vec2.FromAngle(c.Facing), new Vec2(Dir(t), 0)) > 0.3f;
                    if (facing && cx > line - a.RunTrigger && (float)_rng.NextDouble() < a.RunRate * Dt) p.RunTimer = a.RunSeconds;
                }
                if (p.Line == 3) f.X = line - a.OnsideGap;
                if (p.Line == 3 && carrierOk && p.RunTimer <= 0)
                {
                    // the ball is wide in the final third: forwards attack the box (near post, far post, penalty spot)
                    var cf = ToFrame(t, _ballPos.XY);
                    if (MathF.Abs(cf.Y) > a.CrossWideY && cf.X > a.CrossZoneX)
                    {
                        float side = cf.Y > 0 ? 1 : -1;
                        int k = p.Slot % 3;
                        f = k == 0 ? new Vec2(HalfX - 6f, side * 2.5f) : k == 1 ? new Vec2(HalfX - 8f, -side * 3.5f) : new Vec2(HalfX - 11f, 0);
                        mode = 2;
                    }
                }
                if (p.RunTimer > 0) { f.X = line + a.RunDepth; mode = 2; }
            }
            if (p.RunTimer <= 0 && f.X > line - a.OnsideGap) f.X = line - a.OnsideGap;
            var w = ToWorld(t, f);
            if (support && _owner >= 0)
            {
                var cp = _ballPos.XY;
                var to = w - cp;
                if (to.LengthSq < 1e-4f) to = new Vec2(-Dir(t), 0);
                w = cp + to.Normalized() * a.SupportDistance;
            }
            w = ClampInside(w, 1.5f);
            p.Target = w;
            float dist = Vec2.Distance(p.Pos, w);
            p.SpeedMode = mode == 2 ? 2 : dist > 10f ? 1 : dist > _c.Movement.ArriveRadius ? 3 : 0;
        }

        private void DefendSpot(int i)
        {
            var p = _p[i];
            int t = p.Team;
            var a = _c.Ai;
            var f = ShapeSpot(i, false, out _);
            if (p.Line == 1 && _owner >= 0 && _p[_owner].Team != t)
            {
                // anti lob-spam (confirmed): drop deeper when the passer faces forward with space
                var c = _p[_owner];
                bool facing = Vec2.Dot(Vec2.FromAngle(c.Facing), new Vec2(-Dir(t), 0)) > 0.5f;
                if (facing && NearestOpponentOfTeam(c.Team, c.Pos) > a.LoftSpace) f.X -= a.LoftDrop;
                f.X = Math.Max(f.X, -HalfX + 2f);
            }
            var w = ToWorld(t, f);
            // marking: the nearest attacker to my spot, goal-side
            int own = -Dir(t);
            var goal = new Vec2(own * HalfX, 0);
            int mark = -1; float md = a.MarkRadius * (0.5f + 0.5f * p.Positioning);
            if (p.Line == 3) md = 0;   // forwards hold their place, they don't mark
            for (int s = 0; s < 11 && md > 0; s++)
            {
                int j = (1 - t) * 11 + s;
                if (_p[j].Keeper || j == _owner) continue;
                float d = Vec2.Distance(_p[j].Pos, w);
                if (d < md) { md = d; mark = j; }
            }
            if (mark >= 0)
            {
                var o = _p[mark].Pos;
                var mp = o + (goal - o).Normalized() * a.MarkGoalSide;
                float q = _level[t].PositioningQuality * (0.6f + 0.4f * p.Positioning);
                w = Vec2.Lerp(w, mp, q);
            }
            w = ClampInside(w, 1f);
            p.Target = w;
            float dist = Vec2.Distance(p.Pos, w);
            p.SpeedMode = dist > 15f ? 2 : dist > 6f ? 1 : dist > _c.Movement.ArriveRadius ? 3 : 0;
        }

        private void Press(int i)
        {
            var p = _p[i];
            int t = p.Team;
            if (_owner >= 0)
            {
                var b = PerceivedBall(t);
                var own = new Vec2(-Dir(t) * HalfX, 0);
                p.Target = b + (own - b).Normalized() * 0.4f;
                p.SpeedMode = 2;
                var o = _p[_owner];
                if (!o.Holding && Vec2.Distance(p.Pos, _ballPos.XY) < _c.Tackles.Range && (float)_rng.NextDouble() < _c.Ai.TackleRate * Dt)
                    TryTackle(i);
            }
            else { TimeToBall(i, out var pt); p.Target = pt; p.SpeedMode = 2; }
        }

        private void Cover(int i)
        {
            var p = _p[i];
            var own = new Vec2(-Dir(p.Team) * HalfX, 0);
            var b = PerceivedBall(p.Team);
            p.Target = ClampInside(b + (own - b).Normalized() * _c.Ai.CoverDepth, 1f);
            p.SpeedMode = 1;
        }

        private float NearestOpponentOfTeam(int team, Vec2 at)
        {
            float best = float.MaxValue;
            for (int s = 0; s < 11; s++)
            {
                float d = Vec2.Distance(_p[(1 - team) * 11 + s].Pos, at);
                if (d < best) best = d;
            }
            return best;
        }

        // ------------------------------------------------------------------ the carrier

        private float Noise(int t) => (float)_rng.Gaussian(0, _level[t].DecisionNoise);

        private void CarrierAi(int i)
        {
            var p = _p[i];
            int t = p.Team;
            var a = _c.Ai;
            p.HoldTime += Dt;
            p.Decision -= Dt;
            var cp = _ballPos.XY;
            int dir = Dir(t);
            var goal = new Vec2(dir * HalfX, 0);
            float press = NearestOpponent(i, cp);
            bool pressed = press < a.PressedRadius;
            if (p.Decision > 0 && !(pressed && p.HoldTime > a.MinHoldSeconds))
            {
                p.Target = ClampInside(p.Pos + p.DribbleDir * 5f, 1.5f);
                return;
            }
            p.Decision = a.DecisionSeconds + _level[t].ReactionSeconds * 0.5f;

            // dribble: towards goal, away from the nearest opponent in front
            var fwd = (goal - cp).Normalized();
            float space = SpaceAhead(t, cp, fwd, out var avoid);
            var ddir = (fwd + avoid).Normalized();
            if (ddir.LengthSq < 0.5f) ddir = fwd;
            float bestV = a.DribbleBase + a.DribbleProgress * Math.Min(space, a.DribbleSpace) / a.DribbleSpace
                          + a.GoalWeight * 0.5f * MathF.Exp(-Vec2.Distance(cp, goal) / a.GoalScale);
            if (pressed) bestV *= 0.6f;
            bestV += Noise(t);
            int bestKind = 0; Vec2 bestTarget = cp + ddir * 5f; int bestR = -1; KickKind bestKick = KickKind.Pass;

            // shot
            if (p.HoldTime >= a.MinHoldSeconds * 0.5f || pressed)
            {
                float xg = ShotXg(t, cp);
                if (xg > 0)
                {
                    float sv = xg * a.ShotValueWeight + (xg > a.ShootAlways ? 1f : 0f) + Noise(t);
                    if (sv > bestV) { bestV = sv; bestKind = 1; }
                }
            }

            // passes
            if (p.HoldTime >= a.MinHoldSeconds || pressed)
            {
                bool wide = MathF.Abs(cp.Y) > a.CrossWideY && cp.X * dir > a.CrossZoneX;
                float obx = cp.X * dir, osl = SecondLastOpponent(t);
                for (int s = 0; s < 11; s++)
                {
                    int j = t * 11 + s;
                    if (j == i || OffsidePosition(j, obx, osl)) continue;   // never pass to a player in an offside position
                    var rp = Lead(i, j);
                    float d = Vec2.Distance(cp, rp);
                    if (d < a.PassMin) continue;
                    float val = PassValue(t, cp, rp);
                    if (d <= a.GroundPassMax)
                    {
                        float v = val * LaneSafety(t, cp, rp, d / a.PassAvgSpeed) + Noise(t);
                        if (v > bestV) { bestV = v; bestKind = 2; bestTarget = rp; bestR = j; bestKick = KickKind.Pass; }
                    }
                    if (d >= 12f && d <= a.LoftedPassMax && !_p[j].Keeper && !wide)
                    {
                        float v = val * LandingSafety(t, j, rp, d / a.LoftAvgSpeed) - a.LoftPenalty + Noise(t);
                        if (v > bestV) { bestV = v; bestKind = 2; bestTarget = rp; bestR = j; bestKick = KickKind.LoftedPass; }
                    }
                    if (wide && _p[j].Line >= 2)
                    {
                        // cross: into the box where he is arriving (between the six-yard line and the penalty spot)
                        var bf = ToFrame(t, rp);
                        var cf = new Vec2(MathUtil.Clamp(bf.X, HalfX - 12f, HalfX - 5f), MathUtil.Clamp(bf.Y, -7f, 7f));
                        var ct = ToWorld(t, cf);
                        float cd = Vec2.Distance(cp, ct);
                        float tr = Vec2.Distance(_p[j].Pos, ct) / _p[j].SprintSpeed;
                        float v = PassValue(t, cp, ct) * LandingSafety(t, j, ct, Math.Max(cd / a.LoftAvgSpeed, tr)) + a.CrossBonus + Noise(t);
                        if (v > bestV) { bestV = v; bestKind = 2; bestTarget = ct; bestR = j; bestKick = KickKind.Cross; }
                    }
                    if (_p[j].RunTimer > 0)
                    {
                        float lead = (_c.Kicks.ThroughLeadMin + _c.Kicks.ThroughLeadMax) * 0.5f;
                        var tp = ClampInside(_p[j].Pos + new Vec2(dir, 0) * lead, 2f);
                        float td = Vec2.Distance(cp, tp);
                        if (td >= a.PassMin && td <= a.LoftedPassMax)
                        {
                            float tv = PassValue(t, cp, tp);
                            float tr = Vec2.Distance(_p[j].Pos, tp) / _p[j].SprintSpeed;
                            float g = tv * LaneSafety(t, cp, tp, Math.Max(td / a.PassAvgSpeed, tr)) + Noise(t);
                            if (td <= a.GroundPassMax && g > bestV) { bestV = g; bestKind = 2; bestTarget = tp; bestR = j; bestKick = KickKind.ThroughGround; }
                            float l = tv * LandingSafety(t, j, tp, Math.Max(td / a.LoftAvgSpeed, tr)) - a.LoftPenalty + Noise(t);
                            if (l > bestV) { bestV = l; bestKind = 2; bestTarget = tp; bestR = j; bestKick = KickKind.ThroughLofted; }
                        }
                    }
                }
            }

            // clearance from our own box under pressure
            if (InOwnBox(t, cp, 2f) && press < a.ClearPressRadius)
            {
                float v = 0.75f + Noise(t);
                if (v > bestV)
                {
                    bestV = v; bestKind = 2; bestR = -1; bestKick = KickKind.Clearance;
                    bestTarget = ToWorld(t, new Vec2(5f, (cp.Y * dir >= 0 ? 1 : -1) * 22f));
                }
            }

            switch (bestKind)
            {
                case 1: AiShot(i); return;
                case 2: PassTo(i, bestTarget, bestR, bestKick, 1f); return;
                default:
                    p.DribbleDir = ddir;
                    p.Target = ClampInside(cp + ddir * 5f, 1.5f);
                    p.SpeedMode = space > 8f ? 2 : 1;
                    return;
            }
        }

        /// <summary>Free space in front of the carrier, and a sideways nudge away from the nearest opponent there.</summary>
        private float SpaceAhead(int t, Vec2 cp, Vec2 fwd, out Vec2 avoid)
        {
            float best = 2f * _c.Ai.DribbleSpace;
            avoid = Vec2.Zero;
            for (int s = 0; s < 11; s++)
            {
                var o = _p[(1 - t) * 11 + s].Pos;
                var to = o - cp;
                float d = to.Length;
                if (d < 1e-3f) continue;
                if (Vec2.Dot(to / d, fwd) < 0.4f) continue;
                if (d < best)
                {
                    best = d;
                    float side = Vec2.Cross(fwd, to) > 0 ? -1 : 1;   // go to the other side
                    avoid = new Vec2(-fwd.Y, fwd.X) * side * MathUtil.Clamp01(1.2f - d / (2f * _c.Ai.DribbleSpace));
                }
            }
            // stay inside the pitch
            if (MathF.Abs(cp.Y) > HalfY - 4f) avoid = avoid + new Vec2(0, -MathF.Sign(cp.Y) * 0.8f);
            return best;
        }

        private float PassValue(int t, Vec2 cp, Vec2 rp)
        {
            var a = _c.Ai;
            int dir = Dir(t);
            var goal = new Vec2(dir * HalfX, 0);
            float progress = (rp.X - cp.X) * dir;
            float open = Math.Min(10f, NearestOpponentOfTeam(t, rp));
            float v = a.PassBase + a.ProgressWeight * progress / 20f + a.GoalWeight * MathF.Exp(-Vec2.Distance(rp, goal) / a.GoalScale) + a.OpenWeight * open / 10f;
            if (progress < 0) v -= a.BackPassPenalty;
            return v;
        }

        /// <summary>Chance a ground pass gets through: every opponent's time to the ball's path against the ball's time there.</summary>
        private float LaneSafety(int t, Vec2 from, Vec2 to, float ballTime)
        {
            float minMargin = 9f;
            for (int s = 0; s < 11; s++)
            {
                var o = _p[(1 - t) * 11 + s];
                float d = MathUtil.SegmentDistance(o.Pos, from, to, out float u);
                float tb = ballTime * u;
                float to_ = Math.Max(0, d - o.Reach) / Math.Max(1f, o.SprintSpeed) + _c.Ai.InterceptReact;
                float m = to_ - tb;
                if (m < minMargin) minMargin = m;
            }
            return MathUtil.Sigmoid(minMargin * _c.Ai.SafetySharpness);
        }

        /// <summary>Chance a lofted ball is won by the receiver where it lands.</summary>
        private float LandingSafety(int t, int j, Vec2 at, float flight)
        {
            float tr = Math.Max(flight, Vec2.Distance(_p[j].Pos, at) / Math.Max(1f, _p[j].SprintSpeed));
            float minMargin = 9f;
            for (int s = 0; s < 11; s++)
            {
                var o = _p[(1 - t) * 11 + s];
                float to_ = Math.Max(0, Vec2.Distance(o.Pos, at) - o.Reach) / Math.Max(1f, o.SprintSpeed) + _c.Ai.InterceptReact;
                float m = to_ - tr;
                if (m < minMargin) minMargin = m;
            }
            return MathUtil.Sigmoid(minMargin * _c.Ai.SafetySharpness);
        }

        /// <summary>A rough chance of scoring from here: opening angle, distance and blockers.</summary>
        private float ShotXg(int t, Vec2 cp)
        {
            var a = _c.Ai;
            int dir = Dir(t);
            var goal = new Vec2(dir * HalfX, 0);
            float d = Vec2.Distance(cp, goal);
            if (d > a.ShootRange || (goal.X - cp.X) * dir < 0.5f) return 0;
            var p1 = new Vec2(goal.X, GoalHalfWidth) - cp;
            var p2 = new Vec2(goal.X, -GoalHalfWidth) - cp;
            float open = MathF.Abs(MathUtil.WrapAngle(p1.Angle - p2.Angle));
            int blockers = 0;
            for (int s = 0; s < 11; s++)
            {
                var o = _p[(1 - t) * 11 + s];
                if (o.Keeper) continue;
                float sd = MathUtil.SegmentDistance(o.Pos, cp, goal, out float u);
                if (sd < 1.0f && u > 0.03f && u < 0.97f) blockers++;
            }
            return a.ShotScale * open * MathF.Exp(-d / a.ShotDistanceScale) * MathF.Pow(a.BlockFactor, blockers);
        }

        private void AiShot(int i)
        {
            var p = _p[i];
            int t = p.Team;
            var a = _c.Ai;
            var k = _c.Kicks;
            int dir = Dir(t);
            var keeper = _p[_keeper[1 - t]];
            float side = keeper.Pos.Y >= 0 ? -1f : 1f;
            var aim = new Vec2(dir * HalfX, side * (GoalHalfWidth - a.AimInset));
            float aimZ = (float)_rng.NextDouble() < a.AimHighChance ? a.AimHigh : a.AimLow;
            float speed = ShotSpeed(p) * MathUtil.Lerp(k.ShotPowerFloor, 1f, a.ShotPower);
            var to = aim - _ballPos.XY;
            float d = to.Length;
            float time = d / (speed * 0.93f);
            float vz = (aimZ - _ballPos.Z + 0.5f * _c.Ball.Gravity * time * time) / time;
            float vh = MathF.Sqrt(Math.Max(1f, speed * speed - vz * vz));
            var v = new Vec3(to / d * vh, vz);
            v = WithError(i, v, p.Shooting, k.ShotErrorMaxDeg, k.ShotErrorMinDeg, 1f, true);
            Release(i, v, KickKind.Shot, -1);
        }

        /// <summary>A header (automatic for everyone): at goal in the box, a clearance in our half, otherwise to a teammate.</summary>
        private void DoHeader(int i)
        {
            var p = _p[i];
            int t = p.Team;
            int dir = Dir(t);
            var k = _c.Kicks;
            var cp = _ballPos.XY;
            var f = ToFrame(t, cp);
            float hs = MathUtil.Lerp(k.HeaderSpeedMin, k.HeaderSpeedMax, p.Heading);
            _counters.Headers++;
            _headerLock = _c.Touch.HeaderLockSeconds;
            p.Facing = (_ballPos.XY - p.Pos).LengthSq > 1e-4f ? (new Vec2(dir, 0)).Angle : p.Facing;
            if (InOwnBox(1 - t, cp, 2f))
            {
                var keeper = _p[_keeper[1 - t]];
                float side = keeper.Pos.Y >= 0 ? -1f : 1f;
                var aim = new Vec2(dir * HalfX, side * (GoalHalfWidth - _c.Ai.AimInset));
                var to = aim - cp;
                float d = to.Length;
                float elev = MathF.Atan2(0.5f - _ballPos.Z, d);
                var v = new Vec3(to / d * (hs * MathF.Cos(elev)), hs * MathF.Sin(elev));
                v = WithError(i, v, p.Heading, k.HeaderErrorMaxDeg, k.HeaderErrorMinDeg, 1f, true);
                Release(i, v, KickKind.Header, -1);
                return;
            }
            if (f.X < -10f)
            {
                float a = k.ClearAngleDeg * MathUtil.Deg;
                var hdir = ToWorld(t, new Vec2(1f, f.Y >= 0 ? 0.5f : -0.5f)).Normalized();
                float sp = hs * 1.3f;
                var v = new Vec3(hdir * (sp * MathF.Cos(a)), sp * MathF.Sin(a));
                v = WithError(i, v, p.Heading, k.HeaderErrorMaxDeg, k.HeaderErrorMinDeg, 1f, true);
                Release(i, v, KickKind.Clearance, -1);
                return;
            }
            // flick on to the best-placed teammate in front
            int r = -1; float best = float.MaxValue;
            for (int s = 0; s < 11; s++)
            {
                int j = t * 11 + s;
                if (j == i || _p[j].Keeper || AiOffsideTarget(t, j)) continue;
                var to = _p[j].Pos - cp;
                float d = to.Length;
                if (d < 4f || d > 20f) continue;
                float score = d - (to.X * dir) * 0.5f;
                if (score < best) { best = score; r = j; }
            }
            var target = r >= 0 ? _p[r].Pos : cp + new Vec2(dir * 10f, 0);
            var dv = target - cp;
            float dd = Math.Max(1f, dv.Length);
            float ang = k.HeaderAngleDeg * MathUtil.Deg;
            float spd = Math.Min(hs, 4f + dd * 0.6f);
            var vv = new Vec3(dv / dd * (spd * MathF.Cos(ang)), spd * MathF.Sin(ang));
            vv = WithError(i, vv, p.Heading, k.HeaderErrorMaxDeg, k.HeaderErrorMinDeg, 1f, true);
            Release(i, vv, KickKind.HeaderPass, r);
        }

        // ------------------------------------------------------------------ keepers (always AI)

        private void KeeperAi()
        {
            var kc = _c.Keeper;
            for (int t = 0; t < 2; t++)
            {
                int ki = _keeper[t];
                var k = _p[ki];
                k.UseDirect = false;
                int own = -Dir(t);
                var goal = new Vec2(own * HalfX, 0);
                if (_owner == ki) { KeeperWithBall(ki); continue; }
                if (k.Diving) continue;

                bool threat = _owner < 0 && _ballVel.X * own > kc.ThreatMinSpeed && OnTarget(t, kc.ThreatMargin)
                              && _lastToucher >= 0 && _p[_lastToucher].Team != t;
                if (threat)
                {
                    if (!k.Threat) { k.Threat = true; k.React = MathUtil.Lerp(kc.ReactionMax, kc.ReactionMin, k.Reflexes); }
                    k.React -= Dt;
                    if (k.React > 0 || k.Stun > 0) { k.Target = k.Pos; k.SpeedMode = 0; continue; }
                    float tk = MathF.Abs(_ballVel.X) > 0.1f ? (k.Pos.X - _ballPos.X) / _ballVel.X : 0f;
                    if (tk < 0) tk = 0;
                    float yk = _ballPos.Y + _ballVel.Y * tk;
                    float need = yk - k.Pos.Y;
                    float dive = MathUtil.Lerp(kc.DiveSpeedMin, kc.DiveSpeedMax, k.Reflexes);
                    if (MathF.Abs(need) > kc.StandReach * 0.7f && tk < 0.8f)
                    {
                        // dive so he gets to the ball's line as it arrives (as fast as Reflexes allow)
                        float sp = Math.Min(dive, MathF.Abs(need) / Math.Max(0.05f, tk));
                        k.Diving = true;
                        k.DiveVel = new Vec2(0, MathF.Sign(need) * sp);
                        k.DiveTimer = Math.Min(tk + 0.2f, 1.0f);
                        k.Action = PlayerAction.Dive; k.ActionTimer = k.DiveTimer + 0.3f;
                    }
                    else
                    {
                        k.Target = new Vec2(k.Pos.X, MathUtil.Clamp(yk, -GoalHalfWidth - 0.5f, GoalHalfWidth + 0.5f));
                        k.SpeedMode = 2;
                    }
                    continue;
                }
                k.Threat = false;

                // rush out at a carrier who is through
                if (_owner >= 0 && _p[_owner].Team != t)
                {
                    var c = _p[_owner];
                    float dc = Vec2.Distance(c.Pos, goal);
                    float rush = MathUtil.Lerp(kc.RushMin, kc.RushMax, k.Rushing);
                    float dk = Vec2.Distance(k.Pos, _ballPos.XY);
                    if (dc < rush && !DefenderCloser(t, dk))
                    {
                        k.Target = _ballPos.XY; k.SpeedMode = 2;
                        if (dk < kc.SmotherRange && k.SmotherCd <= 0 && !c.Holding)
                        {
                            k.SmotherCd = kc.SmotherCooldown;
                            k.Action = PlayerAction.Dive; k.ActionTimer = 0.4f;
                            float chance = kc.SmotherBase + kc.SmotherSkill * (k.Rushing - c.Dribbling);
                            if ((float)_rng.NextDouble() < chance)
                            {
                                c.Stun = _c.Tackles.FailStunSeconds;
                                _owner = -1;
                                TakeBall(ki, InOwnBox(t, k.Pos, 0.5f));
                            }
                        }
                        continue;
                    }
                }
                // claim a loose ball or a cross landing near him in his box
                if (_owner < 0)
                {
                    float tk = TimeToBall(ki, out var pt);
                    float claim = MathUtil.Lerp(kc.ClaimMin, kc.ClaimMax, k.Rushing);
                    if (InOwnBox(t, pt, 0f) && (Vec2.Distance(pt, k.Pos) < claim || FastestTo(ki, tk)))
                    {
                        k.Target = pt; k.SpeedMode = 2;
                        continue;
                    }
                }
                // position on the line between the ball and the goal centre
                var bp = _ballPos.XY;
                var tob = bp - goal;
                float dist = tob.Length;
                float depth = Math.Min(kc.LineBase + kc.LinePerMetre * dist, kc.LineMax + k.Rushing * kc.LineRushBonus);
                var dirv = tob.LengthSq > 1e-4f ? tob / dist : new Vec2(-own, 0);
                if (dirv.X * own > -0.15f) dirv = new Vec2(-own * 0.3f, MathF.Sign(dirv.Y)).Normalized();
                var spot = goal + dirv * depth;
                spot.Y = MathUtil.Clamp(spot.Y, -GoalHalfWidth, GoalHalfWidth);
                k.Target = spot;
                k.SpeedMode = Vec2.Distance(k.Pos, spot) > 3f ? 2 : 1;
            }
        }

        private bool DefenderCloser(int t, float keeperDist)
        {
            for (int s = 0; s < 11; s++)
            {
                int i = t * 11 + s;
                if (_p[i].Keeper) continue;
                if (Vec2.Distance(_p[i].Pos, _ballPos.XY) < keeperDist * 0.8f) return true;
            }
            return false;
        }

        private bool FastestTo(int i, float time)
        {
            for (int j = 0; j < N; j++)
            {
                if (j == i) continue;
                if (TimeToBall(j, out _) < time) return false;
            }
            return true;
        }

        private void KeeperWithBall(int ki)
        {
            var k = _p[ki];
            k.Target = k.Pos;
            k.SpeedMode = 0;
            k.Facing = new Vec2(Dir(k.Team), 0).Angle;
            if (k.Holding)
            {
                k.HoldLeft -= Dt;
                if (k.HoldLeft > 0) return;
            }
            else
            {
                k.HoldTime += Dt;
                if (k.HoldTime < _c.Ai.MinHoldSeconds) return;
            }
            KeeperDistribute(ki);
        }

        /// <summary>Throw or pass to an open defender nearby, otherwise a long kick to the most advanced forward (Distribution).</summary>
        private void KeeperDistribute(int ki)
        {
            var k = _p[ki];
            var kc = _c.Keeper;
            int t = k.Team;
            int dir = Dir(t);
            var cp = _ballPos.XY;
            int best = -1; float bestV = 0.45f;
            for (int s = 0; s < 11; s++)
            {
                int j = t * 11 + s;
                if (j == ki || AiOffsideTarget(t, j)) continue;
                float d = Vec2.Distance(cp, _p[j].Pos);
                if (d < _c.Ai.PassMin || d > kc.ThrowRange) continue;
                float open = NearestOpponentOfTeam(t, _p[j].Pos);
                if (open < kc.ThrowOpenDistance) continue;
                float v = LaneSafety(t, cp, _p[j].Pos, d / _c.Ai.PassAvgSpeed) + Noise(t) * 0.5f;
                if (v > bestV) { bestV = v; best = j; }
            }
            if (best >= 0)
            {
                PassTo(ki, Lead(ki, best), best, k.Holding ? KickKind.KeeperThrow : KickKind.Pass, 1f);
                return;
            }
            int far = -1; float fx = float.MinValue;
            for (int s = 0; s < 11; s++)
            {
                int j = t * 11 + s;
                if (j == ki || _p[j].Line < 2 || AiOffsideTarget(t, j)) continue;
                float x = _p[j].Pos.X * dir + NearestOpponentOfTeam(t, _p[j].Pos) * 0.5f + Noise(t) * 5f;
                if (x > fx) { fx = x; far = j; }
            }
            var target = far >= 0 ? _p[far].Pos : cp + new Vec2(dir * 45f, 0);
            if (Vec2.Distance(cp, target) > _c.Ai.LoftedPassMax + 15f) target = cp + (target - cp).Normalized() * (_c.Ai.LoftedPassMax + 15f);
            PassTo(ki, ClampInside(target, 2f), far, KickKind.KeeperKick, 1f);
        }
    }
}
