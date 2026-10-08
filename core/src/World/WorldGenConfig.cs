using System.Collections.Generic;

namespace LegendsFC.Core.World
{
    /// <summary>data/config/world-generation.json. Values are PROPOSALS unless noted in the file.</summary>
    public sealed class WorldGenConfig
    {
        public int CupOnlyClubsPerCountry = 5;
        public ClubRatingSettings ClubRatings = new ClubRatingSettings();
        public SquadSettings Squad = new SquadSettings();
        public FeetSettings Feet = new FeetSettings();
        public AttributeGenSettings Attributes = new AttributeGenSettings();
        public PotentialSettings Potential = new PotentialSettings();
        public AgeDecaySettings AgeDecay = new AgeDecaySettings();
        public ContractSettings Contracts = new ContractSettings();
        public NationalitySettings Nationality = new NationalitySettings();
        public FacilityGenSettings Facilities = new FacilityGenSettings();
    }

    public sealed class ClubRatingSettings
    {
        public double CurveExponent = 1.5;
        /// <summary>Key: competition id (e.g. "ENG-1") or cup-only country id (e.g. "POR"). Value: [best, worst] average squad rating.</summary>
        [Newtonsoft.Json.JsonExtensionData]
        public IDictionary<string, Newtonsoft.Json.Linq.JToken> Ranges = new Dictionary<string, Newtonsoft.Json.Linq.JToken>();

        public (double best, double worst) Range(string key)
        {
            var arr = Ranges[key];
            return ((double)arr[0], (double)arr[1]);
        }
    }

    public sealed class SquadSettings
    {
        public Dictionary<string, int> Slots = new Dictionary<string, int>();
        public Dictionary<string, int> StarterSlots = new Dictionary<string, int>();
        public double RatingSpread = 4, BenchPenalty = 3, ReservePenalty = 6;
        public int AcademyPlayers = 5;
        public double AgeMean = 26, AgeSd = 4;
        public int YoungUntilAge = 23, OldFromAge = 31;
        public double YoungPenaltyPerYear = 1.5, OldPenaltyPerYear = 1.0;
        public int AgeMin = 18, AgeMax = 35;
    }

    public sealed class FeetSettings { public double CentralLeftShare = 0.25, InvertedOppositeFootShare = 0.75; }

    public sealed class AttributeGenSettings
    {
        public int PlusMin = 8, PlusMax = 14, MinusMin = 10, MinusMax = 16;
        public double NoiseSd = 4, BalancedNoiseSd = 3;
        public int[] GkBackupForOutfield = { 10, 30 }, OutfieldBackupForGk = { 20, 45 }, AKeeperGkBackup = { 45, 60 };
    }

    public sealed class PotentialSettings
    {
        public double YoungGapPerYearUnder24 = 2.2, YoungGapSd = 4;
        public int MidAgeGapMax = 3;
        public double AcademyBase = 52, AcademyPerLevel = 3.5, AcademySd = 6;
        public int AcademyMin = 45, AcademyMax = 95;
        public int[] AcademyCurrentBelowPotential = { 15, 30 }, AcademyAge = { 16, 18 };
    }

    public sealed class AgeDecaySettings
    {
        public int StartMin = 28, StartMax = 32, RetireMin = 32, RetireMax = 40, MinYearsBetween = 2;
        public double DropMin = 15, DropMax = 25, Exponent = 1.5;
        public int GoalkeeperExtraYears = 2;
    }

    public sealed class ContractSettings { public int EndYearMin = 2027, EndYearMax = 2031; }
    public sealed class NationalitySettings { public double HomeShare = 0.85; }
    public sealed class FacilityGenSettings { public double ConditionMin = 70, ConditionMax = 100; }

    /// <summary>data/names/names.json.</summary>
    public sealed class NameBank
    {
        public Dictionary<string, string> CountryLanguage = new Dictionary<string, string>();
        public Dictionary<string, LanguageNames> Languages = new Dictionary<string, LanguageNames>();
    }

    public sealed class LanguageNames
    {
        public List<string> First = new List<string>(), Last = new List<string>(), TownA = new List<string>(), TownB = new List<string>(), Patterns = new List<string>();
    }
}
