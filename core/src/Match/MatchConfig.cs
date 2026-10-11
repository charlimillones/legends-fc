using System.Collections.Generic;

namespace LegendsFC.Core.Match
{
    /// <summary>
    /// data/config/match.json: every tuning number of the real-time match (PROPOSAL, Master B brief Oct 11, unless a
    /// section's _source names a decision). Attribute-driven values are given as a range from attribute 1 (…Min/…Max order
    /// as written) to attribute 99.
    /// </summary>
    public sealed class MatchConfig
    {
        public PitchConfig Pitch = new PitchConfig();
        public ClockConfig Clock = new ClockConfig();
        public BallConfig Ball = new BallConfig();
        public MovementConfig Movement = new MovementConfig();
        public TouchConfig Touch = new TouchConfig();
        public KickConfig Kicks = new KickConfig();
        public ButtonConfig Buttons = new ButtonConfig();
        public TackleConfig Tackles = new TackleConfig();
        public KeeperConfig Keeper = new KeeperConfig();
        public SwitchConfig Switching = new SwitchConfig();
        public RestartConfig Restarts = new RestartConfig();
        public AiConfig Ai = new AiConfig();
        public List<DifficultyLevel> Difficulty = new List<DifficultyLevel>();
        public EnergyConfig Energy = new EnergyConfig();
        /// <summary>Base positions per formation, one per slot in squad.json order (team attacking +X, ball at the centre spot).</summary>
        public Dictionary<string, List<FormationSlot>> Formations = new Dictionary<string, List<FormationSlot>>();
        public TestTeamConfig TestTeams = new TestTeamConfig();

        /// <summary>The energy formula and penalty from squad.json (Carlos Oct 9), set by GameData.Load so there is one source.</summary>
        [Newtonsoft.Json.JsonIgnore] public LegendsFC.Core.Squad.SquadConfig Squad = new LegendsFC.Core.Squad.SquadConfig();

        /// <summary>The difficulty level (1-4), clamped.</summary>
        public DifficultyLevel Level(int level)
        {
            int i = level - 1;
            if (i < 0) i = 0;
            if (i >= Difficulty.Count) i = Difficulty.Count - 1;
            return Difficulty[i];
        }
    }

    public sealed class PitchConfig
    {
        public float Length = 105, Width = 68, GoalWidth = 7.32f, GoalHeight = 2.44f, PostRadius = 0.06f;
        public float PenaltyBoxDepth = 16.5f, PenaltyBoxWidth = 40.32f, GoalAreaDepth = 5.5f, GoalAreaWidth = 18.32f;
        /// <summary>How far outside the lines a player may run.</summary>
        public float PlayerMargin = 3f;
        /// <summary>A goal's net depth: the ball stops inside it.</summary>
        public float NetDepth = 2f;
    }

    /// <summary>Decided Oct 11 (match length): 5 real minutes for 90; extra time 30 match minutes in 100 real seconds.</summary>
    public sealed class ClockConfig
    {
        public float HalfSeconds = 150, HalfMinutes = 45, ExtraHalfSeconds = 50, ExtraHalfMinutes = 15;
        public float HalfTimeBreakSeconds = 3, ExtraTimeBreakSeconds = 3, GoalCelebrationSeconds = 2.5f;
    }

    public sealed class BallConfig
    {
        public float Radius = 0.11f, Gravity = 9.81f;
        /// <summary>Air drag: deceleration = AirDrag × speed² (1/m).</summary>
        public float AirDrag = 0.013f;
        /// <summary>Rolling friction deceleration (m/s²).</summary>
        public float RollingDecel = 2.2f;
        public float Restitution = 0.55f, BounceFriction = 0.85f;
        /// <summary>Below this vertical speed a bouncing ball starts rolling.</summary>
        public float RollVz = 1.0f;
        /// <summary>A rolling ball slower than this stops.</summary>
        public float StopSpeed = 0.2f;
        /// <summary>Largest ball move per physics sub-step (collisions with posts and players).</summary>
        public float MaxSubstep = 0.25f;
        public float PostRestitution = 0.5f;
    }

    public sealed class MovementConfig
    {
        /// <summary>Stick below this = walk; above = run. Holding the screen = sprint.</summary>
        public float WalkStick = 0.5f;
        /// <summary>AI only: jogging speed for small repositioning, and the distance at which a player stops adjusting.</summary>
        public float JogSpeed = 3.6f, ArriveRadius = 1.0f;
        public float WalkSpeed = 2.2f, RunSpeedMin = 5.0f, RunSpeedMax = 7.0f, SprintSpeedMin = 6.6f, SprintSpeedMax = 9.0f;
        public float AccelMin = 4.0f, AccelMax = 8.0f, Brake = 12f;
        /// <summary>Turning (rad/s) without and with the ball.</summary>
        public float TurnRate = 12f, DribbleTurnMin = 4.5f, DribbleTurnMax = 9f;
        /// <summary>Top speed share while dribbling.</summary>
        public float DribbleSpeedMin = 0.80f, DribbleSpeedMax = 0.94f;
        /// <summary>Ball distance in front of the carrier.</summary>
        public float DribbleOffset = 0.55f;
        public float PlayerRadius = 0.4f;
        /// <summary>Keeper running speed and acceleration come from Rushing Out and Reflexes (GKs have low hidden outfield values).</summary>
        public float KeeperSpeedMin = 4.8f, KeeperSpeedMax = 6.8f;
    }

    /// <summary>Getting and controlling the ball.</summary>
    public sealed class TouchConfig
    {
        /// <summary>Reach to win a loose ball, by Positioning.</summary>
        public float ReachMin = 0.55f, ReachMax = 0.85f;
        /// <summary>The fastest ball he can control, by Dribbling (faster balls may deflect off him).</summary>
        public float ControlSpeedMin = 13f, ControlSpeedMax = 23f;
        /// <summary>Feet and chest up to this height; headers between HeaderMin and HeaderMax.</summary>
        public float FootHeight = 1.0f, HeaderMin = 1.0f, HeaderMax = 2.5f, HeaderReach = 0.8f;
        /// <summary>The kicker can't touch his own kick for this long.</summary>
        public float KickerIgnoreSeconds = 0.3f;
        /// <summary>A ball too fast to control that passes this close may hit him.</summary>
        public float BlockReach = 0.5f, BlockChance = 0.55f, BlockHeight = 1.8f, BlockSpeedKeep = 0.35f, BlockSpreadDeg = 70f;
        /// <summary>Heading duel weights (Heading, Strength) and randomness.</summary>
        public float HeaderDuelHeading = 1.0f, HeaderDuelStrength = 0.4f, HeaderDuelNoise = 0.25f;
        /// <summary>After a header nobody can head the ball again for this long (no head tennis).</summary>
        public float HeaderLockSeconds = 0.3f;
    }

    public sealed class KickConfig
    {
        /// <summary>Kick power from how long the button is held (D-07): power = (held / HoldCapSeconds) ^ PowerCurve, capped at 1.</summary>
        public float HoldCapSeconds = 0.9f, PowerCurve = 1.0f;
        /// <summary>Pass speed = speed needed to reach the receiver × lerp(PassPowerMin, PassPowerMax, power).</summary>
        public float PassPowerMin = 0.9f, PassPowerMax = 1.25f;
        /// <summary>Pass target search: teammates within this half-angle of the stick direction.</summary>
        public float PassConeDeg = 35f;
        /// <summary>A pass with nobody in the cone travels this far, by power.</summary>
        public float FreePassMin = 8f, FreePassMax = 35f;
        /// <summary>Ground pass: the speed it should arrive with.</summary>
        public float PassArrivalSpeed = 5f, MaxGroundSpeed = 27f, MinGroundSpeed = 6f;
        /// <summary>Lofted pass and cross launch angle; lofted through ball angle.</summary>
        public float LoftAngleDeg = 32f, ThroughLoftAngleDeg = 40f, CrossAngleDeg = 22f, ClearAngleDeg = 35f;
        /// <summary>Through ball: holding C past this makes it lofted (anti lob-spam rule, confirmed).</summary>
        public float ThroughLoftHoldSeconds = 0.35f;
        /// <summary>Through ball lead in front of the runner, by power.</summary>
        public float ThroughLeadMin = 4f, ThroughLeadMax = 12f;
        /// <summary>Shot speed by Shooting; power 0 gives ShotPowerFloor of it.</summary>
        public float ShotSpeedMin = 17f, ShotSpeedMax = 31f, ShotPowerFloor = 0.55f;
        /// <summary>User shots: launch angle by power.</summary>
        public float ShotLoftMinDeg = 2f, ShotLoftMaxDeg = 12f;
        /// <summary>Direction error (standard deviation, degrees) by skill 1→99.</summary>
        public float PassErrorMaxDeg = 9f, PassErrorMinDeg = 1.2f;
        public float ShotErrorMaxDeg = 9f, ShotErrorMinDeg = 1.8f;
        public float HeaderErrorMaxDeg = 16f, HeaderErrorMinDeg = 4f;
        /// <summary>Power error (standard deviation, share of speed) by skill 1→99.</summary>
        public float PowerErrorMax = 0.14f, PowerErrorMin = 0.03f;
        /// <summary>Elevation error (degrees) for lofted balls and shots.</summary>
        public float ElevationErrorMaxDeg = 5f, ElevationErrorMinDeg = 1f;
        /// <summary>Error multipliers: lofted pass or cross, lofted through ball (riskier), long-hold shots (power above ShotWildPower).</summary>
        public float LoftedErrorScale = 1.4f, ThroughLoftedErrorScale = 2.0f, ShotWildPower = 0.85f, ShotWildScale = 1.5f;
        /// <summary>An opponent within PressureRadius raises errors by up to PressureScale.</summary>
        public float PressureRadius = 2.5f, PressureScale = 0.6f;
        /// <summary>Headers: speed by Heading; launch angle.</summary>
        public float HeaderSpeedMin = 7f, HeaderSpeedMax = 15f, HeaderAngleDeg = 8f;
        /// <summary>Throw-ins: the fastest throw.</summary>
        public float ThrowSpeedMax = 15f, ThrowAngleDeg = 25f;
        /// <summary>The fastest launch the lofted-ball solver may use.</summary>
        public float LoftSolveMaxSpeed = 34f;
    }

    /// <summary>Button mapping (PROPOSAL D-07). Values: Pass, Shoot, Through; Switch, Tackle, Pressure.</summary>
    public sealed class ButtonConfig
    {
        public Dictionary<string, string> Attack = new Dictionary<string, string>();
        public Dictionary<string, string> Defence = new Dictionary<string, string>();
    }

    public sealed class TackleConfig
    {
        public float Range = 1.7f;
        /// <summary>Success = Base + SkillWeight × (Tackling - Dribbling) + StrengthWeight × (Strength - Strength), on 0-1 attributes, clamped to MinChance..MaxChance.</summary>
        public float Base = 0.5f, SkillWeight = 0.6f, StrengthWeight = 0.25f, MinChance = 0.1f, MaxChance = 0.9f;
        public float CooldownSeconds = 0.8f, FailStunSeconds = 0.5f, StunSpeed = 0.35f;
        /// <summary>A won tackle pokes the ball away at this speed (the tackler often keeps it).</summary>
        public float PokeSpeed = 3.5f, KeepChance = 0.5f;
        /// <summary>The carrier is protected for this long after winning the ball.</summary>
        public float ProtectSeconds = 0.35f;
    }

    /// <summary>Save model (decided Oct 11): Reflexes = reaction, Handling = catch vs parry, Rushing Out = coming off the line, Distribution = kicks and throws.</summary>
    public sealed class KeeperConfig
    {
        public float ReactionMax = 0.42f, ReactionMin = 0.12f;
        public float DiveSpeedMin = 4.5f, DiveSpeedMax = 7.5f;
        public float StandReach = 0.9f, DiveReach = 1.4f, ReachHeight = 2.55f;
        /// <summary>Catch chance = CatchBase + CatchHandling × Handling - CatchSpeedPenalty × max(0, speed - CatchEasySpeed) - CatchStretchPenalty (when diving).</summary>
        public float CatchBase = 0.3f, CatchHandling = 0.6f, CatchSpeedPenalty = 0.035f, CatchEasySpeed = 12f, CatchStretchPenalty = 0.25f;
        /// <summary>Above this height a save is tipped over.</summary>
        public float TipHeight = 2.15f;
        public float ParrySpeedKeep = 0.35f, ParrySpreadDeg = 60f;
        /// <summary>Base save-chance fumble: even a reached ball beats him this often, less with Reflexes.</summary>
        public float MissMax = 0.3f, MissMin = 0.04f;
        public float HoldMin = 0.8f, HoldMax = 1.6f;
        /// <summary>Off his line: depth from the goal = LineBase + LinePerMetre × ball distance, at most LineMax (+ RushingOut × LineRushBonus).</summary>
        public float LineBase = 1.0f, LinePerMetre = 0.08f, LineMax = 5f, LineRushBonus = 3f;
        /// <summary>Charges a carrier who is through within this distance of goal, by Rushing Out.</summary>
        public float RushMin = 8f, RushMax = 18f;
        /// <summary>Claims crosses landing within this distance of him, by Rushing Out.</summary>
        public float ClaimMin = 3f, ClaimMax = 9f;
        public float SmotherRange = 1.3f, SmotherBase = 0.4f, SmotherSkill = 0.5f, SmotherCooldown = 0.7f;
        /// <summary>A shot is a threat when predicted to cross his line within the frame plus this margin, within ThreatSeconds.</summary>
        public float ThreatMargin = 0.6f, ThreatSeconds = 2.0f, ThreatMinSpeed = 7f;
        /// <summary>Hand reach to collect loose balls in his box.</summary>
        public float CollectReach = 1.1f;
        /// <summary>Distribution: a throw goes to an open teammate within ThrowRange; otherwise a long kick (angle).</summary>
        public float ThrowRange = 30f, ThrowOpenDistance = 7f, LongKickAngleDeg = 38f;
    }

    public sealed class SwitchConfig
    {
        /// <summary>Auto-switch margin (seconds sooner) at sensitivity 0+ and 1.</summary>
        public float MarginAtLow = 1.6f, MarginAtHigh = 0.15f;
        /// <summary>No auto-switch within this time of the last switch.</summary>
        public float DwellSeconds = 0.4f;
    }

    public sealed class RestartConfig
    {
        /// <summary>The AI takes a restart this long after the taker is in place.</summary>
        public float AiDelay = 0.6f;
        /// <summary>The user's team: wait this long for a button, then the AI takes it.</summary>
        public float UserTimeout = 5f;
        /// <summary>The taker is placed on the spot if he isn't there after this long.</summary>
        public float MaxWalk = 2.0f;
        /// <summary>Opponents keep this far away (kick-offs, corners, goal kicks) and at throw-ins.</summary>
        public float Distance = 9.15f, ThrowInDistance = 2f;
        /// <summary>Corner: attackers placed in the box (count) and the near-post zone depth.</summary>
        public int CornerAttackers = 4;
        public float CornerZoneDepth = 9f;
    }

    public sealed class AiConfig
    {
        public float DecisionSeconds = 0.15f;
        /// <summary>The user's 10 AI teammates use this difficulty level (same AI as the opponent; difficulty is for the opponent only).</summary>
        public int TeammateLevel = 2;
        /// <summary>AI shots use this share of full power (0-1).</summary>
        public float ShotPower = 0.8f;
        /// <summary>The carrier holds the ball at least this long before passing unless pressed.</summary>
        public float MinHoldSeconds = 0.25f;
        /// <summary>Shape: push up in possession, drop when defending, follow the ball, width, mentality step (m per step).</summary>
        public float PossessionPush = 9f, DefendDrop = 3f, FollowBallX = 0.5f, FollowBallY = 0.3f, WidthAttack = 1.1f, WidthDefend = 0.75f, MentalityStep = 3f;
        /// <summary>Deepest and highest the back line goes.</summary>
        public float BackLineMin = -46f, BackLineMax = 12f;
        /// <summary>Anti lob-spam (confirmed): defenders drop this much deeper when the passer faces forward with this much space.</summary>
        public float LoftDrop = 4f, LoftSpace = 6f;
        /// <summary>Marking: an attacker within MarkRadius of a defender's spot is marked from MarkGoalSide metres goal-side.</summary>
        public float MarkRadius = 9f, MarkGoalSide = 1.5f;
        /// <summary>Second defender covers this far behind the presser.</summary>
        public float CoverDepth = 5f;
        /// <summary>Forwards wait this far onside of the last defender (the AI plays as if offside existed).</summary>
        public float OnsideGap = 0.8f;
        /// <summary>Two nearest teammates move to support angles at this distance.</summary>
        public float SupportDistance = 11f;
        /// <summary>Pass evaluation.</summary>
        public float PassMin = 5f, GroundPassMax = 38f, LoftedPassMax = 50f, PassAvgSpeed = 14f, LoftAvgSpeed = 17f;
        public float SafetySharpness = 4f, InterceptReact = 0.25f;
        public float PassBase = 0.35f, ProgressWeight = 0.5f, GoalWeight = 0.6f, OpenWeight = 0.25f, LoftPenalty = 0.12f, BackPassPenalty = 0.15f;
        public float GoalScale = 22f;
        /// <summary>Shot evaluation: xG ≈ ShotScale × opening angle × e^(-distance / ShotDistanceScale) × BlockFactor^blockers.</summary>
        public float ShootRange = 27f, ShotScale = 1.2f, ShotDistanceScale = 14f, BlockFactor = 0.55f, ShotValueWeight = 2.2f, ShootAlways = 0.3f;
        /// <summary>Dribble evaluation.</summary>
        public float DribbleBase = 0.3f, DribbleSpace = 6f, DribbleProgress = 0.35f, PressedRadius = 2.0f;
        /// <summary>Cross from wide in the final third.</summary>
        public float CrossWideY = 15f, CrossZoneX = 33f;
        /// <summary>The carrier aims shots this far inside a post, and this high.</summary>
        public float AimInset = 0.7f, AimLow = 0.35f, AimHigh = 1.7f, AimHighChance = 0.35f;
        /// <summary>Tackle: the AI tries when in range, at most this often per second.</summary>
        public float TackleRate = 2.5f;
        /// <summary>Runs in behind start when the carrier is within this distance of the back line and faces forward.</summary>
        public float RunTrigger = 30f, RunDepth = 8f, RunSeconds = 2.0f, RunCooldown = 2.5f, RunRate = 0.8f;
        /// <summary>Clearance: own-box carrier under pressure kicks it long.</summary>
        public float ClearPressRadius = 3f;
    }

    /// <summary>One difficulty level: only the opponent AI's reading, reaction time and positioning (decided).</summary>
    public sealed class DifficultyLevel
    {
        public string Name;
        public float ReactionSeconds, ReadNoise, PositioningQuality, DecisionNoise;
    }

    public sealed class EnergyConfig
    {
        /// <summary>Top speed and acceleration drop per energy point below squad.json energyPenaltyBelow.</summary>
        public float SpeedLossPerPoint = 0.002f;
        /// <summary>Match km = real metres × (90 match minutes / real minutes) × DistanceScale. The real-time match is busier
        /// than real football, so this keeps a match's km (and energy cost) close to sim mode's (about 10-12 km).</summary>
        public float DistanceScale = 0.5f;
    }

    public sealed class FormationSlot
    {
        public string Slot;
        public float X, Y;
    }

    /// <summary>Test teams (headless tests and the quick-match button): attribute offsets per position from the team rating.</summary>
    public sealed class TestTeamConfig
    {
        public float Noise = 4f;
        /// <summary>Goalkeepers' hidden outfield values sit this far below the rating; outfield players' GK values too.</summary>
        public float HiddenDrop = 35f;
        public Dictionary<string, Dictionary<string, float>> Profiles = new Dictionary<string, Dictionary<string, float>>();
    }
}
