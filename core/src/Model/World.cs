using System.Collections.Generic;

namespace LegendsFC.Core.Model
{
    /// <summary>
    /// Stable facility ids (saved by name, never renamed). Display names live in data/rules/facilities.json
    /// (Oct 9): Stadium, Training Grounds, Youth Academy, Medical Building, Club Store, Scouting Centre.
    /// </summary>
    public enum Facility { Stadium, TrainingGround, Academy, MedicalCentre, ClubStore, ScoutingCentre }

    /// <summary>data/rules/facilities.json: display name for each stable facility id.</summary>
    public sealed class FacilityInfo { public Facility Id; public string Name; }

    public enum CompetitionType { League, Cup, Continental, International }

    /// <summary>data/world/countries.json. Stable IDs, e.g. "ENG".</summary>
    public sealed class Country
    {
        public string Id;
        public string Name;
        public string Currency;      // ISO code: GBP, EUR, BRL, ARS (real currencies, Oct 7)
        public string Confederation; // UEFA, CONMEBOL, CAF, AFC, ...
        public bool HasLeague;       // false = cup-only country (clubs exist for continental cups)
        public int Divisions;
    }

    /// <summary>data/world/competitions.json. Stable IDs, e.g. "ENG-1".</summary>
    public sealed class Competition
    {
        public string Id;
        public string Name;
        public string CountryId;     // null for continental/international
        public CompetitionType Type;
        public int Level;            // 1 = top division
        public int Teams;
        public string FormatTemplate;
        public int Promotion;
        public int Relegation;
    }

    public sealed class FacilityState
    {
        public int Level = 1;            // 1-10
        public double Condition = 100;   // 0-100 %, shown per facility and as a total (Oct 8)
        /// <summary>The facility's named manager (a story character with no abilities, Oct 7).</summary>
        public FacilityManager Manager;
    }

    /// <summary>A facility manager: random per club, ages, retires and is replaced (Oct 7-9).</summary>
    public sealed class FacilityManager
    {
        public string Name, NationalityId;
        public int BirthYear, RetireAge;
    }

    /// <summary>A club. Stable IDs, e.g. "CLB-000123".</summary>
    public sealed class Club
    {
        public string Id;
        public string Name;
        public string ShortName;
        public string CountryId;
        public int Division;
        public int Reputation;           // 1-100
        public int StadiumCapacity;
        public string PrimaryColor;
        public string SecondaryColor;
        public long Balance;             // EUR (displayed in the world's currency)
        public Dictionary<Facility, FacilityState> Facilities = new Dictionary<Facility, FacilityState>();
        public double FanMood = 50;      // 0-100 (Oct 8)
        /// <summary>Running total spent on facility upgrades and repairs (EUR), for reports.</summary>
        public long SpentOnFacilities;
        public Tactics Tactics = new Tactics();
        /// <summary>Running total spent on coach contracts (EUR), for reports.</summary>
        public long SpentOnCoaches;
        /// <summary>Running total of money from random events and board bonuses (EUR; negative = fines), for reports.</summary>
        public long OtherMoney;
    }

    /// <summary>A player. Hidden fields are never shown directly (architecture rule 7).</summary>
    public sealed class Player
    {
        public string Id;                // e.g. "PLY-0001234"
        public string Name;
        public int BirthYear;
        public int BirthDayOfYear;
        public string NationalityId;
        public string ClubId;            // null = free agent
        public Position MainPosition;
        public Foot Foot;
        public AttributeSet Attributes = new AttributeSet();

        // Hidden
        public string ArchetypeId;
        public int Potential;            // fixed (Oct 8); scouts reveal an estimate
        public int DeclineStartAge;      // 28-32, random per player (Oct 8)
        public int RetireAge;            // 32-40, random per player (Oct 8)
        public double DeclineAmount;     // 15-25 ceiling points by retirement (PROPOSAL)

        // Visible once revealed by the Scouting Centre
        public string PersonalityId;     // null = no personality (about 55% of players)

        public int ContractEndYear;
        public long Wage;                // per season, in EUR (displayed in the world's currency)
        public List<double> RecentMatchRatings = new List<double>(); // form score = average of the last ~10
        /// <summary>Fractional training progress per attribute (hidden), so small weekly gains add up.</summary>
        public double[] AttributeProgress = new double[AttributeSet.Count];
        public bool Retired;
        /// <summary>Club whose academy produced him (null for generated senior players). Gives the academy/ex-club signing bonus.</summary>
        public string AcademyClubId;
        /// <summary>Clubs he has left (released or out of contract). Also gives the ex-club bonus.</summary>
        public List<string> FormerClubIds = new List<string>();
        /// <summary>On loan: the club that owns him (ClubId is the borrowing club). Null when not on loan.</summary>
        public string LoanFromClubId;
        /// <summary>Season ends in a row he finished without a club (0 when he has one).</summary>
        public int UnsignedSeasons;
        /// <summary>Share of his wage the borrowing club pays while on loan (0, 0.5 or 1).</summary>
        public double LoanWageShare;

        // Fitness, discipline and stats (squad and tactics, Oct 9)
        /// <summary>0-100. Matches cost energy; the training regime sets the weekly rest (light = full).</summary>
        public double Energy = 100;
        /// <summary>"light", "moderate" or "heavy" (development.json regimes).</summary>
        public string Regime = "moderate";
        public Injury Injury;
        public List<Ban> Bans = new List<Ban>();
        /// <summary>Yellow cards this season per competition (wiped by each competition's rules).</summary>
        public Dictionary<string, int> Yellows = new Dictionary<string, int>();
        public List<StatLine> Stats = new List<StatLine>();
        /// <summary>The coach who trains him (null = he develops naturally, without a coach).</summary>
        public string CoachId;
        public bool Injured => Injury != null;
    }

    /// <summary>A scout (decided Oct 7): a rating, a contract price only, and a task. Reports depend only on his rating.</summary>
    public sealed class Scout
    {
        public string Id, Name, NationalityId;
        public int BirthYear, RetireAge, Rating;
        public string ClubId;
        public int ContractEndYear;
        public ScoutTask Task = new ScoutTask();
    }

    /// <summary>Where and what a scout looks for: a country or confederation (null = anywhere), a position (null = any), an age limit.</summary>
    public sealed class ScoutTask
    {
        public string CountryId, Confederation;
        public Position? Position;
        public int? MaxAge;
    }

    /// <summary>A scout's report on a player: estimated ranges (never exact potential), personality if he saw it.</summary>
    public sealed class ScoutReport
    {
        public int Id, Season, Week;
        public string ScoutId, PlayerId, PersonalityId;
        public int RatingLow, RatingHigh, PotentialLow, PotentialHigh;
        public bool Wonderkid;
    }

    /// <summary>A coach (decided Oct 7): one of 4 groups, a rating, a contract in seasons, a one-time price. No wages.</summary>
    public sealed class Coach
    {
        public string Id, Name, NationalityId, Group;   // Group: GK, DEF, MID, FWD
        public int BirthYear, RetireAge, Rating;
        /// <summary>Null = in the free pool.</summary>
        public string ClubId;
        public int ContractEndYear;
        /// <summary>Set when he was a player: his clubs (cheaper to hire there).</summary>
        public string FormerPlayerId;
        public List<string> FormerClubIds = new List<string>();
    }

    public sealed class Injury
    {
        public string TypeId, Name;
        public double WeeksLeft;
        public int TotalWeeks;
        public bool Serious;
    }

    /// <summary>A suspension: matches left to miss, in one competition, a country's competitions, or a confederation's cups.</summary>
    public sealed class Ban
    {
        public string Scope, Key;
        public int MatchesLeft;
        public string Reason;
    }

    /// <summary>A player's numbers for one competition in one season at one club (older seasons are merged per club).</summary>
    public sealed class StatLine
    {
        public int Season;
        public string CompetitionId, ClubId;
        public int Apps, Starts, SubApps, Minutes, Goals, Assists, CleanSheets, Yellows, Reds, Rated, PlayerOfTheMatch;
        public double RatingSum;
        public double AverageRating => Rated == 0 ? 0 : RatingSum / Rated;
    }

    /// <summary>A club's team choices. The user sets them; AI clubs pick by the same rules.</summary>
    public sealed class Tactics
    {
        public string Formation = "4-4-2";
        /// <summary>-2 very defensive ... +2 very attacking.</summary>
        public int Mentality;
        /// <summary>Player ids by formation slot (11) and the bench (up to 9). Empty = pick automatically.</summary>
        public List<string> Lineup = new List<string>();
        public List<string> Bench = new List<string>();
        public string Captain, PenaltyTaker, FreeKickTaker, CornerTaker;
        public List<SavedLineup> Saved = new List<SavedLineup>();
    }

    public sealed class SavedLineup
    {
        public string Name, Formation;
        public int Mentality;
        public List<string> Lineup = new List<string>(), Bench = new List<string>();
    }
}
