using System.Collections.Generic;

namespace LegendsFC.Core.Model
{
    public enum Facility { Stadium, TrainingGround, Academy, MedicalCentre, ClubStore, ScoutingCentre }

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
        public long Balance;             // whole units of the country's currency
        public Dictionary<Facility, FacilityState> Facilities = new Dictionary<Facility, FacilityState>();
        public double FanMood = 50;      // 0-100 (Oct 8)
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
        public long Wage;                // per season, club currency
        public List<double> RecentMatchRatings = new List<double>(); // form score = average of the last ~10
        /// <summary>Fractional training progress per attribute (hidden), so small weekly gains add up.</summary>
        public double[] AttributeProgress = new double[AttributeSet.Count];
        public bool Retired;
    }
}
