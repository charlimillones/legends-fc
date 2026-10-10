using System.Collections.Generic;
using System.Linq;

namespace LegendsFC.Core.Squad
{
    /// <summary>data/config/squad.json (agreed Oct 9).</summary>
    public sealed class SquadConfig
    {
        public int Starters = 11, Bench = 9, Substitutions = 5, SavedLineups = 5;
        public Dictionary<string, List<string>> Formations = new Dictionary<string, List<string>>();
        public List<string> Mentalities = new List<string>();
        public double MentalityScoringPerStep = 0.08, MentalityConcedingPerStep = 0.06;
        public Dictionary<string, string> Lines = new Dictionary<string, string>();
        public Dictionary<string, double> AttackWeights = new Dictionary<string, double>(), DefenceWeights = new Dictionary<string, double>();
        public Dictionary<string, double> KmPer90 = new Dictionary<string, double>(), PassesPer90 = new Dictionary<string, double>(), ShotsPer90 = new Dictionary<string, double>();
        public double KmRandomMin = 0.92, KmRandomMax = 1.08, KmPerMentalityStep = 0.02, ActivityRandomMin = 0.8, ActivityRandomMax = 1.2;
        public double EnergyPerKm = 1.6, EnergyPerPass = 0.08, EnergyPerShot = 0.4, StaminaFactorBase = 1.25, StaminaFactorSlope = 0.5;
        public double EnergyPenaltyBelow = 80, EnergyPenaltyPerPoint = 0.004;
        public Dictionary<string, double> WeeklyRecovery = new Dictionary<string, double>();
        public Dictionary<string, double> PersonalityRecovery = new Dictionary<string, double>();
        public double AiLightRegimeBelowEnergy = 60, AiMentalityGap = 8, AiVeryMentalityGap = 15;
        /// <summary>The assistant puts a rested player back on his regime from this energy (Oct 10).</summary>
        public double AssistantBackAboveEnergy = 85;
    }

    /// <summary>data/config/match-events.json (PROPOSAL numbers, agreed Oct 9).</summary>
    public sealed class MatchEventConfig
    {
        public Dictionary<string, double> ScorerWeights = new Dictionary<string, double>(), AssistWeights = new Dictionary<string, double>(), CardWeights = new Dictionary<string, double>();
        public double BookedCaution = 0.75, AssistShare = 0.75, YellowsPerTeam = 1.8, StraightRedsPerTeam = 0.08, RedCardStrengthLoss = 0.25;
        public Dictionary<string, double> PersonalityCards = new Dictionary<string, double>(), PersonalityNoise = new Dictionary<string, double>(), PersonalityInjury = new Dictionary<string, double>();
        public double RatingBase = 6.4, RatingWin = 0.4, RatingGoal = 0.9, RatingAssist = 0.5, RatingCleanSheetGk = 0.7, RatingCleanSheetDef = 0.4;
        public double RatingPerConceded = 0.15, RatingPerPointAboveTeam = 0.04, RatingNoiseSd = 0.5, RatingYellow = 0.3, RatingRed = 1.5;
        public int MinMinutesForRating = 20, FormMatches = 10, AiSubsMin = 3, AiSubsMax = 5, SubMinuteMin = 55, SubMinuteMax = 85;
        public double InjuryPer90 = 0.036, InjuryFatigueBelow = 70, InjuryFatiguePerPoint = 0.02, RecoveryAheadChancePerWeek = 0.08;
    }

    public sealed class InjuryType
    {
        public string Id, Name;
        public double Share;
        public int WeeksMin, WeeksMax;
        public bool Serious;
    }

    /// <summary>data/rules/discipline.json: each competition's real suspension rules (Oct 9).</summary>
    public sealed class DisciplineData
    {
        public RedCardRules RedCards = new RedCardRules();
        public List<DisciplineRule> Rules = new List<DisciplineRule>();
        public DisciplineRule For(string competitionId) => Rules.FirstOrDefault(r => r.Competitions.Contains(competitionId));
    }

    public sealed class RedCardRules
    {
        public int SecondYellowBan = 1;
        public List<RedType> Straight = new List<RedType>();
    }

    public sealed class RedType { public string Name; public double Share; public int Ban; }

    public sealed class DisciplineRule
    {
        public List<string> Competitions = new List<string>();
        /// <summary>A one-match ban every N yellows (0 = use Thresholds).</summary>
        public int Every;
        public List<YellowThreshold> Thresholds = new List<YellowThreshold>();
        public List<string> ResetBeforeStages = new List<string>();
        public bool ResetOnStageChange;
        /// <summary>Where a red-card ban is served: competition, domestic or confederation.</summary>
        public string RedScope = "competition";
    }

    public sealed class YellowThreshold { public int At; public int? ByRound; public int Ban = 1; }
}
