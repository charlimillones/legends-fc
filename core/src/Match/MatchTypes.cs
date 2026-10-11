using System.Collections.Generic;

namespace LegendsFC.Core.Match
{
    /// <summary>Settings the player picks (not rules): difficulty, auto-switch eagerness and which side the user controls.</summary>
    public sealed class MatchSettings
    {
        /// <summary>1-4 (match.json difficulty levels). Changes only the opponent AI's reading, reaction time and positioning.</summary>
        public int Difficulty = 2;
        /// <summary>0 = auto-switch off, 1 = most eager.</summary>
        public float SwitchSensitivity = 0.5f;
        /// <summary>0 home, 1 away, -1 none (AI vs AI).</summary>
        public int UserSide = 0;
    }

    /// <summary>One starter, with attributes already reduced by the out-of-position drop.</summary>
    public sealed class PlayerSetup
    {
        public string PlayerId;
        public string Name;
        public string SlotLabel;
        public LegendsFC.Core.Model.Position Position;
        public bool IsKeeper;
        public int Pace, Acceleration, Stamina, Strength, Dribbling, Passing, Crossing, Shooting, Heading, Tackling, Positioning, Reflexes, Handling, RushingOut, Distribution;
        /// <summary>0-100.</summary>
        public double Energy = 100;
        public int ShirtNumber;
    }

    public sealed class TeamSetup
    {
        public string ClubId;
        public string Name;
        public string Formation;
        /// <summary>-2..+2.</summary>
        public int Mentality;
        /// <summary>11 players in slot order (the formation's slot order in squad.json).</summary>
        public PlayerSetup[] Starters;
    }

    public sealed class MatchSetup
    {
        public string CompetitionId;
        public TeamSetup Home, Away;
        public bool ExtraTimeIfLevel;
        /// <summary>The first leg's score, from the first leg's point of view (its home team is this match's away team),
        /// like RoundPlayer.Level. -1 = no first leg.</summary>
        public int FirstLegHomeGoals = -1, FirstLegAwayGoals = -1;
        public ulong Seed;
        public MatchSettings Settings = new MatchSettings();
    }

    /// <summary>The user's controls for one tick. Stick in pitch terms: +X = towards the opponent's goal for the user's team,
    /// +Y = to the user's left when facing that goal.</summary>
    public struct ControlInput
    {
        public float MoveX, MoveY;
        public bool Sprint;
        /// <summary>Held this tick.</summary>
        public bool A, B, C;
    }

    public enum Phase { KickOff, Play, ThrowIn, GoalKick, Corner, GoalScored, HalfTime, ExtraTimeBreak, FullTime }

    /// <summary>Animation hint in PlayerState.Action.</summary>
    public static class PlayerAction
    {
        public const byte None = 0, Kick = 1, Tackle = 2, Header = 3, Dive = 4, ThrowIn = 5;
    }

    public struct PlayerState
    {
        public Vec2 Pos;
        public Vec2 Vel;
        /// <summary>Radians.</summary>
        public float Facing;
        /// <summary>0 home, 1 away.</summary>
        public byte Team;
        public byte Slot;
        public bool HasBall;
        public bool UserControlled;
        public bool IsKeeper;
        public float Energy;
        /// <summary>See <see cref="PlayerAction"/>.</summary>
        public byte Action;
    }

    public struct BallState
    {
        public Vec3 Pos;
        public Vec3 Vel;
        /// <summary>Player index 0-21, or -1 when loose.</summary>
        public sbyte Owner;
    }

    public struct GoalEvent
    {
        public int Minute;
        /// <summary>The team the goal counts for.</summary>
        public byte Team;
        public string ScorerId;
        public string AssistId;
        public bool OwnGoal;
    }

    public sealed class PlayerStats
    {
        public string PlayerId;
        public byte Team;
        public int Minutes, Goals, Assists, Passes, Shots;
        public double Km;
        /// <summary>Energy at the end of the match (0-100).</summary>
        public double Energy;
    }

    public sealed class LiveMatchResult
    {
        public string HomeClubId, AwayClubId;
        public int HomeGoals, AwayGoals;
        public bool ExtraTimePlayed, NeedsShootout;
        public List<GoalEvent> Goals;
        public List<PlayerStats> Players;
    }

    /// <summary>Running match counters (for tests, tuning and a debug overlay). Added to the contract.</summary>
    public sealed class MatchCounters
    {
        /// <summary>Ticks with the clock running, and the ones of those with the ball out of play (any phase but Play).</summary>
        public int ClockTicks, OutOfPlayTicks;
        public int Shots, ShotsOnTarget, Saves, Catches, Parries, Tips, Blocks, Passes, Tackles, TacklesWon, Headers;
        public int ThrowIns, GoalKicks, Corners, KickOffs;
        public int PostHits;
        /// <summary>Ticks each team owned the ball (home, away).</summary>
        public int HomePossessionTicks, AwayPossessionTicks;

        public void Clear()
        {
            ClockTicks = OutOfPlayTicks = Shots = ShotsOnTarget = Saves = Catches = Parries = Tips = Blocks = Passes = Tackles = TacklesWon = Headers = 0;
            ThrowIns = GoalKicks = Corners = KickOffs = PostHits = HomePossessionTicks = AwayPossessionTicks = 0;
        }
    }
}
