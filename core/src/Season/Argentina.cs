using System.Collections.Generic;
using System.Linq;

namespace LegendsFC.Core.Season
{
    public sealed class ArgentinaFirstConfig
    {
        public string CompetitionId = "ARG-1";
        public int Zones = 2, PlayoffQualifiersPerZone = 8, RelegatedByAnnualTable = 1, RelegatedByAverages = 1, AveragesSeasons = 3;
        public double PenaltyHomeWinChance = 0.5;
    }

    public sealed class ArgentinaSecondConfig
    {
        public string CompetitionId = "ARG-2";
        public int Zones = 2, ReducidoFromPlace = 2, ReducidoToPlace = 8;
    }

    /// <summary>Everything a competition produced in one season.</summary>
    public sealed class CompetitionOutcome
    {
        public string CompetitionId;
        /// <summary>Named tables, e.g. "League", "Apertura A", "Annual".</summary>
        public Dictionary<string, List<TableRow>> Tables = new Dictionary<string, List<TableRow>>();
        /// <summary>Title name → club id, e.g. "Apertura" → CLB-000123.</summary>
        public Dictionary<string, string> Titles = new Dictionary<string, string>();
        public List<string> Promoted = new List<string>();
        public List<string> Relegated = new List<string>();
        public List<MatchResult> KnockoutMatches = new List<MatchResult>();
    }

}
