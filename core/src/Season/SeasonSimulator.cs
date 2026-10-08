using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Rules;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Season
{
    /// <summary>data/config/league-formats.json.</summary>
    public sealed class LeagueFormatConfig
    {
        public Dictionary<string, int> Legs = new Dictionary<string, int>();
    }

    /// <summary>Plays every league season in sim mode (no cups, transfers or development yet).</summary>
    public sealed class SeasonSimulator
    {
        private readonly GameData _d;
        public SeasonSimulator(GameData d) { _d = d; }

        /// <summary>Team strength = average main-position rating of the best eleven (simple placeholder until lineups exist).</summary>
        public Dictionary<string, double> Strengths(GameWorld w)
            => w.Players.Where(p => p.ClubId != null).GroupBy(p => p.ClubId).ToDictionary(g => g.Key,
                g => g.Select(p => PositionRating.Base(p.Attributes, p.MainPosition, _d.PositionRatings))
                      .OrderByDescending(x => x).Take(_d.MatchSim.StartingElevenSize).Average());

        public Dictionary<string, List<TableRow>> PlayLeagues(GameWorld w, GameRandom rng)
        {
            var strength = Strengths(w);
            var result = new Dictionary<string, List<TableRow>>();
            foreach (var league in w.Competitions.Where(c => c.Type == Model.CompetitionType.League).OrderBy(c => c.Id, System.StringComparer.Ordinal))
            {
                var clubs = w.Clubs.Where(c => w.ClubLeague[c.Id] == league.Id).Select(c => c.Id).ToList();
                int legs = _d.LeagueFormats.Legs.TryGetValue(league.Id, out var l) ? l : 2;
                var table = new LeagueTable(clubs, _d.MatchSim);
                foreach (var round in Fixtures.RoundRobin(clubs, legs, rng))
                    foreach (var f in round)
                        table.Add(MatchSim.Play(f.Home, f.Away, strength[f.Home], strength[f.Away], _d.MatchSim, rng));
                result[league.Id] = table.Standings();
            }
            return result;
        }
    }
}
