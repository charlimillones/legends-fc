using System;
using System.Collections.Generic;
using LegendsFC.Core.Util;

namespace LegendsFC.Core.Match
{
    /// <summary>
    /// The real-time 11v11 match (headless, deterministic). The Unity client calls <see cref="Step"/> every fixed tick
    /// (50 Hz) and draws <see cref="Players"/> and <see cref="Ball"/>. Same rules for both teams: the user's player uses the
    /// same physics, attributes and error model as the AI; difficulty changes only the opponent AI's reading, reaction
    /// time and positioning. No allocations inside Step.
    /// </summary>
    public sealed partial class LiveMatch
    {
        public const float TickSeconds = 0.02f;
        private const float Dt = TickSeconds;
        private const int N = 22;

        private readonly MatchConfig _c;
        private readonly MatchSetup _setup;
        private readonly GameRandom _rng;
        private readonly P[] _p = new P[N];
        private readonly PlayerState[] _st = new PlayerState[N];
        private readonly MatchCounters _counters = new MatchCounters();
        private readonly List<GoalEvent> _goals = new List<GoalEvent>(64);
        private readonly int[] _goalScorer = new int[64];
        private readonly DifficultyLevel[] _level = new DifficultyLevel[2];
        private readonly int[] _mentality = new int[2];
        private readonly int[] _keeper = new int[2];
        private readonly ButtonAction[] _attackMap = new ButtonAction[3], _defenceMap = new ButtonAction[3];

        // Ball
        private Vec3 _ballPos, _ballVel;
        private int _owner = -1;

        // Clock and phase
        private Phase _phase;
        private float _phaseTimer, _halfClock, _readyTimer;
        private int _half = 1, _minute = 1, _tick, _halfTicks;
        private int _homeGoals, _awayGoals, _goalCount;
        private int _firstKickTeam, _etFirstKickTeam;
        private bool _over, _extraTime, _needsShootout;
        private readonly float _kmScale;

        // Restarts
        private int _restartTeam, _taker = -1;
        private Vec2 _restartSpot;
        private bool _takerReady;

        // Control
        private readonly int _userTeam;
        private int _controlled = -1;
        private float _sinceSwitch;
        private readonly bool[] _btnPrev = new bool[3], _btnArmed = new bool[3], _btnPressed = new bool[3], _btnReleased = new bool[3];
        private readonly float[] _btnHold = new float[3], _btnReleasedHold = new float[3];

        // Possession and touches
        private int _passReceiver = -1, _passTeam = -1, _lastPossTeam;
        private int _lastToucher = -1, _lastKicker = -1;
        private bool _lastTouchWasPass, _lastTouchDeflect;
        private int _shooter = -1;
        private float _shotAge = 99f;
        private float _headerLock, _handsGuard;
        private int _handsGuardTeam = -1;

        public LiveMatch(MatchSetup setup, MatchConfig config)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (setup.Home?.Starters == null || setup.Away?.Starters == null || setup.Home.Starters.Length != 11 || setup.Away.Starters.Length != 11)
                throw new ArgumentException("Each team needs 11 starters.");
            _c = config;
            _setup = setup;
            var settings = setup.Settings ?? new MatchSettings();
            _rng = new GameRandom(setup.Seed);
            _userTeam = settings.UserSide == 0 || settings.UserSide == 1 ? settings.UserSide : -1;
            _kmScale = config.Clock.HalfMinutes * 60f / config.Clock.HalfSeconds;

            for (int t = 0; t < 2; t++)
            {
                var team = t == 0 ? setup.Home : setup.Away;
                if (!config.Formations.TryGetValue(team.Formation ?? "", out var slots) || slots.Count != 11)
                    throw new ArgumentException("No base positions in match.json for formation " + team.Formation);
                _mentality[t] = Math.Max(-2, Math.Min(2, team.Mentality));
                _keeper[t] = -1;
                for (int s = 0; s < 11; s++)
                {
                    int i = t * 11 + s;
                    var ps = team.Starters[s] ?? throw new ArgumentException("Empty starter slot " + s);
                    var p = new P(i, t, s, ps, slots[s], config);
                    _p[i] = p;
                    if (p.Keeper && _keeper[t] < 0) _keeper[t] = i;
                }
                if (_keeper[t] < 0) { _keeper[t] = t * 11; _p[t * 11].MakeKeeper(config); }
                for (int s = 0; s < 11; s++) { int i = t * 11 + s; if (i != _keeper[t] && _p[i].Keeper) _p[i].Keeper = false; }
            }

            int userLevel = config.Ai.TeammateLevel;
            for (int t = 0; t < 2; t++)
                _level[t] = config.Level(_userTeam < 0 || t != _userTeam ? settings.Difficulty : userLevel);
            ParseButtons(config.Buttons.Attack, _attackMap, true);
            ParseButtons(config.Buttons.Defence, _defenceMap, false);

            for (int i = 0; i < N; i++) _ttbTick[i] = -1;
            _firstKickTeam = _rng.NextInt(0, 1);
            _etFirstKickTeam = _rng.NextInt(0, 1);
            StartKickOff(_firstKickTeam);
            Publish();
        }

        // ------------------------------------------------------------------ public API

        public PlayerState[] Players => _st;
        public BallState Ball => new BallState { Pos = _ballPos, Vel = _ballVel, Owner = (sbyte)_owner };
        public int HomeGoals => _homeGoals;
        public int AwayGoals => _awayGoals;
        public Phase Phase => _phase;
        public int Minute => _minute;
        public float PhaseTimer => _phaseTimer;
        public int Half => _half;
        public int ControlledIndex => _controlled;
        public int LastGoalCount => _goalCount;
        public bool IsOver => _over;
        /// <summary>Running counters (shots, saves, out-of-play time...). Added to the contract.</summary>
        public MatchCounters Counters => _counters;
        /// <summary>+1 when this team attacks towards +X in the current half, -1 otherwise. Added to the contract.</summary>
        public int AttackDirection(int team) => Dir(team);
        /// <summary>Seconds played in the current half. Added to the contract.</summary>
        public float HalfClock => _halfClock;
        /// <summary>The team taking the current restart (kick-off, throw-in, goal kick, corner, free kick). Added to the contract.</summary>
        public int RestartTeam => _restartTeam;

        public void Step(in ControlInput input)
        {
            if (_over) return;
            _tick++;
            ReadButtons(input);
            if (_phase == Phase.HalfTime || _phase == Phase.ExtraTimeBreak) BreakTick();
            else RunningTick(input);
            Publish();
        }

        public LiveMatchResult Result()
        {
            var r = new LiveMatchResult
            {
                CompetitionId = _setup.CompetitionId, HomeClubId = _setup.Home.ClubId, AwayClubId = _setup.Away.ClubId,
                HomeGoals = _homeGoals, AwayGoals = _awayGoals,
                ExtraTimePlayed = _extraTime, NeedsShootout = _needsShootout,
                Goals = new List<GoalEvent>(_goals), Players = new List<PlayerStats>(N)
            };
            int minutes = _extraTime ? 120 : 90;
            if (!_over) minutes = Math.Max(0, _minute);
            for (int i = 0; i < N; i++)
            {
                var p = _p[i];
                if (string.IsNullOrEmpty(p.Id)) continue;
                r.Players.Add(new PlayerStats
                {
                    PlayerId = p.Id, Team = (byte)p.Team, Minutes = minutes, Goals = p.Goals, Assists = p.Assists,
                    Passes = p.Passes, Shots = p.Shots, Km = Math.Round(p.Km, 2), Energy = Math.Max(0, p.Energy)
                });
            }
            return r;
        }

        /// <summary>FNV-1a over the whole simulated state (positions, velocities, energy, ball, score, clock).</summary>
        public ulong StateHash()
        {
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < N; i++)
            {
                var p = _p[i];
                h = Mix(h, p.Pos.X); h = Mix(h, p.Pos.Y); h = Mix(h, p.Vel.X); h = Mix(h, p.Vel.Y);
                h = Mix(h, p.Facing); h = Mix(h, p.Energy);
                h = MixInt(h, p.Goals); h = MixInt(h, p.Passes); h = MixInt(h, p.Shots);
            }
            h = Mix(h, _ballPos.X); h = Mix(h, _ballPos.Y); h = Mix(h, _ballPos.Z);
            h = Mix(h, _ballVel.X); h = Mix(h, _ballVel.Y); h = Mix(h, _ballVel.Z);
            h = MixInt(h, _owner); h = MixInt(h, _homeGoals); h = MixInt(h, _awayGoals);
            h = MixInt(h, (int)_phase); h = MixInt(h, _half); h = MixInt(h, _tick); h = Mix(h, _halfClock);
            return h;
        }

        private static ulong Mix(ulong h, float v) => MixInt(h, BitConverter.SingleToInt32Bits(v));

        private static ulong MixInt(ulong h, int v)
        {
            unchecked
            {
                for (int k = 0; k < 4; k++) { h ^= (byte)(v >> (8 * k)); h *= 1099511628211UL; }
            }
            return h;
        }

        // ------------------------------------------------------------------ clock and phases

        private int Dir(int team) => (team == 0 ? 1 : -1) * (_half % 2 == 1 ? 1 : -1);
        private float HalfLength => _half <= 2 ? _c.Clock.HalfSeconds : _c.Clock.ExtraHalfSeconds;
        private float HalfX => _c.Pitch.Length * 0.5f;
        private float HalfY => _c.Pitch.Width * 0.5f;
        private float GoalHalfWidth => _c.Pitch.GoalWidth * 0.5f;

        private void RunningTick(in ControlInput input)
        {
            _phaseTimer += Dt;
            _halfTicks++;
            _halfClock = _halfTicks * Dt;
            _counters.ClockTicks++;
            if (_phase != Phase.Play) _counters.OutOfPlayTicks++;
            if (_owner >= 0) { if (_p[_owner].Team == 0) _counters.HomePossessionTicks++; else _counters.AwayPossessionTicks++; }
            UpdateMinute();
            _shotAge += Dt;
            if (_headerLock > 0) _headerLock -= Dt;
            if (_handsGuard > 0) _handsGuard -= Dt;
            UpdateTimers();
            RecordBall();
            PredictBall();
            if (_phase == Phase.Play) PlayTick(input);
            else RestartTick(input);
            if (_halfTicks >= (int)MathF.Round(HalfLength / Dt) && !_over) EndHalf();
        }

        private void UpdateMinute()
        {
            float len = HalfLength;
            float minutes = _half <= 2 ? _c.Clock.HalfMinutes : _c.Clock.ExtraHalfMinutes;
            int start = _half == 1 ? 0 : _half == 2 ? (int)_c.Clock.HalfMinutes : _half == 3 ? (int)(2 * _c.Clock.HalfMinutes) : (int)(2 * _c.Clock.HalfMinutes + _c.Clock.ExtraHalfMinutes);
            int m = (int)(_halfClock / len * minutes);
            if (m > (int)minutes - 1) m = (int)minutes - 1;
            _minute = start + m + 1;
        }

        private bool AggregateLevel()
        {
            int home = _homeGoals, away = _awayGoals;
            if (_setup.FirstLegHomeGoals >= 0 && _setup.FirstLegAwayGoals >= 0)
            {
                home += _setup.FirstLegAwayGoals;
                away += _setup.FirstLegHomeGoals;
            }
            return home == away;
        }

        private void EndHalf()
        {
            ClearBallFlags();
            if (_half == 1) { EnterBreak(Phase.HalfTime); Emit(MatchEventKind.HalfTime, -1, -1); return; }
            if (_half == 2)
            {
                if (_setup.ExtraTimeIfLevel && AggregateLevel()) { _extraTime = true; EnterBreak(Phase.ExtraTimeBreak); Emit(MatchEventKind.ExtraTime, -1, -1); return; }
                Finish();
                return;
            }
            if (_half == 3) { EnterBreak(Phase.ExtraTimeBreak); return; }
            _needsShootout = AggregateLevel();
            Finish();
        }

        private void EnterBreak(Phase ph)
        {
            _phase = ph;
            _phaseTimer = 0;
            _owner = -1;
            _ballPos = new Vec3(0, 0, _c.Ball.Radius);
            _ballVel = Vec3.Zero;
            for (int i = 0; i < N; i++) { _p[i].Holding = false; _p[i].Diving = false; _p[i].Action = 0; }
            ClearTackles();
        }

        private void Finish()
        {
            _phase = Phase.FullTime;
            _phaseTimer = 0;
            _over = true;
            _owner = -1;
            _ballVel = Vec3.Zero;
            for (int i = 0; i < N; i++) _p[i].Vel = Vec2.Zero;
            ClearTackles();
            Emit(MatchEventKind.FullTime, -1, -1);
        }

        private void BreakTick()
        {
            _phaseTimer += Dt;
            float len = _phase == Phase.HalfTime ? _c.Clock.HalfTimeBreakSeconds : _c.Clock.ExtraTimeBreakSeconds;
            for (int i = 0; i < N; i++) _p[i].Vel = Vec2.Zero;
            if (_phaseTimer < len) return;
            _half++;
            _halfClock = 0;
            _halfTicks = 0;
            UpdateMinute();
            int team = _half == 2 ? 1 - _firstKickTeam : _half == 3 ? _etFirstKickTeam : 1 - _etFirstKickTeam;
            StartKickOff(team);
        }

        private void UpdateTimers()
        {
            _sinceSwitch += Dt;
            _switchAge += Dt;
            for (int i = 0; i < N; i++)
            {
                var p = _p[i];
                if (p.Stun > 0) p.Stun -= Dt;
                if (p.RecoverTimer > 0) p.RecoverTimer -= Dt;
                if (p.LungeLeft > 0 && --p.LungeLeft == 0) EndLunge(i);
                if (p.KickIgnore > 0) p.KickIgnore -= Dt;
                if (p.Protect > 0) p.Protect -= Dt;
                if (p.SmotherCd > 0) p.SmotherCd -= Dt;
                if (p.IgnoreShot > 0) p.IgnoreShot -= Dt;
                if (p.RunTimer > 0) { p.RunTimer -= Dt; if (p.RunTimer <= 0) p.RunCd = _c.Ai.RunCooldown; }
                else if (p.RunCd > 0) p.RunCd -= Dt;
                if (p.ActionTimer > 0) { p.ActionTimer -= Dt; if (p.ActionTimer <= 0) p.Action = 0; }
            }
        }

        private void Publish()
        {
            for (int i = 0; i < N; i++)
            {
                var p = _p[i];
                _st[i] = new PlayerState
                {
                    Pos = p.Pos, Vel = p.Vel, Facing = p.Facing, Team = (byte)p.Team, Slot = (byte)p.Slot,
                    HasBall = _owner == i, UserControlled = _controlled == i, IsKeeper = p.Keeper,
                    Energy = p.Energy, Action = p.Action
                };
            }
        }

        // ------------------------------------------------------------------ buttons

        private enum ButtonAction { None, Pass, Shoot, Through, Switch, Tackle, Pressure }

        private static void ParseButtons(Dictionary<string, string> map, ButtonAction[] into, bool attack)
        {
            string[] keys = { "A", "B", "C" };
            for (int k = 0; k < 3; k++)
            {
                into[k] = ButtonAction.None;
                if (map == null || !map.TryGetValue(keys[k], out var v) || v == null) continue;
                if (Enum.TryParse(v, true, out ButtonAction a)) into[k] = a;
            }
            if (attack && into[0] == ButtonAction.None && into[1] == ButtonAction.None && into[2] == ButtonAction.None)
            { into[0] = ButtonAction.Pass; into[1] = ButtonAction.Shoot; into[2] = ButtonAction.Through; }
            if (!attack && into[0] == ButtonAction.None && into[1] == ButtonAction.None && into[2] == ButtonAction.None)
            { into[0] = ButtonAction.Switch; into[1] = ButtonAction.Tackle; into[2] = ButtonAction.Pressure; }
        }

        private void ReadButtons(in ControlInput input)
        {
            for (int k = 0; k < 3; k++)
            {
                bool now = k == 0 ? input.A : k == 1 ? input.B : input.C;
                _btnPressed[k] = now && !_btnPrev[k];
                _btnReleased[k] = !now && _btnPrev[k];
                if (_btnPressed[k]) _btnHold[k] = 0;
                if (now) _btnHold[k] += Dt;
                if (_btnReleased[k]) _btnReleasedHold[k] = _btnHold[k];
                _btnPrev[k] = now;
            }
        }

        /// <summary>Kick power 0-1 from how long the button was held (D-07).</summary>
        private float PowerFromHold(float held)
        {
            float x = MathUtil.Clamp01(held / _c.Kicks.HoldCapSeconds);
            return MathF.Pow(x, _c.Kicks.PowerCurve);
        }

        // ------------------------------------------------------------------ per-player runtime data

        private sealed class P
        {
            public readonly int Index, Team, Slot;
            public bool Keeper;
            public readonly string Id, Name;
            /// <summary>0 GK, 1 defence, 2 midfield, 3 attack.</summary>
            public int Line;
            public readonly Vec2 Base;
            public float Pace, Accel, Stamina, Strength, Dribbling, Passing, Crossing, Shooting, Heading, Tackling, Positioning, Reflexes, Handling, Rushing, Distribution;
            public float RunSpeed, SprintSpeed, AccelRate, Reach, ControlSpeed, TurnWithBall, DribbleFactor, StaminaFactor;

            public Vec2 Pos, Vel, Target, DirectVel;
            public float Facing;
            public float Energy;
            public int SpeedMode;          // 0 walk, 1 run, 2 sprint, 3 jog
            public bool UseDirect;
            public float Stun, KickIgnore, Protect, SmotherCd, ActionTimer, RunTimer, RunCd;
            // tackle lunge (Oct 15): time left, direction and speed; then recovery time left
            public int LungeLeft;          // ticks
            public float LungeSpeed, RecoverTimer;
            public Vec2 LungeDir;
            public bool LungeDone, LungeWon;
            public byte Action;
            public float Decision, HoldTime;
            public Vec2 DribbleDir;
            // keeper
            public bool Holding, Diving, Threat;
            public float HoldLeft, React, DiveTimer, IgnoreShot;
            public Vec2 DiveVel;
            // stats
            public int Goals, Assists, Passes, Shots;
            public double Km;
            public int AssistFrom = -1;

            public P(int index, int team, int slot, PlayerSetup s, FormationSlot f, MatchConfig c)
            {
                Index = index; Team = team; Slot = slot;
                Id = s.PlayerId; Name = s.Name;
                Keeper = s.IsKeeper || s.Position == LegendsFC.Core.Model.Position.GK;
                Base = new Vec2(f.X, f.Y);
                Line = LineOf(s.SlotLabel ?? f.Slot);
                if (Keeper) Line = 0;
                Pace = A(s.Pace); Accel = A(s.Acceleration); Stamina = A(s.Stamina); Strength = A(s.Strength);
                Dribbling = A(s.Dribbling); Passing = A(s.Passing); Crossing = A(s.Crossing); Shooting = A(s.Shooting);
                Heading = A(s.Heading); Tackling = A(s.Tackling); Positioning = A(s.Positioning);
                Reflexes = A(s.Reflexes); Handling = A(s.Handling); Rushing = A(s.RushingOut); Distribution = A(s.Distribution);
                Energy = (float)Math.Max(0, Math.Min(100, s.Energy));
                Derive(c);
            }

            public void MakeKeeper(MatchConfig c) { Keeper = true; Line = 0; Derive(c); }

            /// <summary>Speeds and skills derived from attributes (same formula for every player).</summary>
            public void Derive(MatchConfig c)
            {
                var m = c.Movement;
                RunSpeed = MathUtil.Lerp(m.RunSpeedMin, m.RunSpeedMax, Pace);
                SprintSpeed = MathUtil.Lerp(m.SprintSpeedMin, m.SprintSpeedMax, Pace);
                AccelRate = MathUtil.Lerp(m.AccelMin, m.AccelMax, Accel);
                Reach = MathUtil.Lerp(c.Touch.ReachMin, c.Touch.ReachMax, Positioning);
                ControlSpeed = MathUtil.Lerp(c.Touch.ControlSpeedMin, c.Touch.ControlSpeedMax, Dribbling);
                TurnWithBall = MathUtil.Lerp(m.DribbleTurnMin, m.DribbleTurnMax, Dribbling);
                DribbleFactor = MathUtil.Lerp(m.DribbleSpeedMin, m.DribbleSpeedMax, Dribbling);
                StaminaFactor = (float)(c.Squad.StaminaFactorBase - c.Squad.StaminaFactorSlope * Stamina);
                if (Keeper)
                {
                    // Keepers' outfield values are hidden and low: they move by Rushing Out and react by Reflexes.
                    RunSpeed = MathUtil.Lerp(m.KeeperSpeedMin, m.KeeperSpeedMax, Rushing);
                    SprintSpeed = RunSpeed;
                    AccelRate = MathUtil.Lerp(m.AccelMin, m.AccelMax, Reflexes);
                }
            }

            private static float A(int v) => MathUtil.Clamp(v, 1, 99) / 100f;

            private static int LineOf(string slot)
            {
                switch (slot)
                {
                    case "GK": return 0;
                    case "CB": case "LB": case "RB": case "LWB": case "RWB": return 1;
                    case "DM": case "CM": case "LM": case "RM": return 2;
                    default: return 3;
                }
            }
        }
    }
}
