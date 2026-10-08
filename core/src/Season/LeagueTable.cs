using System.Collections.Generic;
using System.Linq;

namespace LegendsFC.Core.Season
{
    public sealed class TableRow
    {
        public string ClubId;
        public int Played, Won, Drawn, Lost, GoalsFor, GoalsAgainst, Points;
        public int GoalDifference => GoalsFor - GoalsAgainst;
    }

    public sealed class LeagueTable
    {
        private readonly Dictionary<string, TableRow> _rows;
        private readonly MatchSimConfig _c;

        public LeagueTable(IEnumerable<string> clubIds, MatchSimConfig c)
        {
            _c = c;
            _rows = clubIds.ToDictionary(id => id, id => new TableRow { ClubId = id });
        }

        public void Add(MatchResult r)
        {
            var h = _rows[r.Home]; var a = _rows[r.Away];
            h.Played++; a.Played++;
            h.GoalsFor += r.HomeGoals; h.GoalsAgainst += r.AwayGoals;
            a.GoalsFor += r.AwayGoals; a.GoalsAgainst += r.HomeGoals;
            if (r.HomeGoals > r.AwayGoals) { h.Won++; a.Lost++; h.Points += _c.PointsWin; }
            else if (r.HomeGoals < r.AwayGoals) { a.Won++; h.Lost++; a.Points += _c.PointsWin; }
            else { h.Drawn++; a.Drawn++; h.Points += _c.PointsDraw; a.Points += _c.PointsDraw; }
        }

        /// <summary>Points, then goal difference, then goals scored, then club id (stable).</summary>
        public List<TableRow> Standings() => _rows.Values
            .OrderByDescending(r => r.Points).ThenByDescending(r => r.GoalDifference).ThenByDescending(r => r.GoalsFor)
            .ThenBy(r => r.ClubId, System.StringComparer.Ordinal).ToList();
    }
}
