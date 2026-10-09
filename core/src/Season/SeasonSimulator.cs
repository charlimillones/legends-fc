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
        public ArgentinaFirstConfig ArgentinaFirst = new ArgentinaFirstConfig();
        public ArgentinaSecondConfig ArgentinaSecond = new ArgentinaSecondConfig();
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

        /// <summary>Main league table per competition (Argentina: the annual / zone tables are in the outcome).</summary>
        public Dictionary<string, List<TableRow>> PlayLeagues(GameWorld w, GameRandom rng)
            => PlaySeason(w, rng).ToDictionary(o => o.CompetitionId,
                o => o.Tables.TryGetValue("League", out var t) ? t : o.Tables.TryGetValue("Annual", out var a) ? a : o.Tables.Values.First());

        public List<CompetitionOutcome> PlaySeason(GameWorld w, GameRandom rng)
        {
            var strength = Strengths(w);
            double S(string id) => strength[id];
            var f = _d.LeagueFormats;
            var outcomes = new List<CompetitionOutcome>();
            foreach (var league in w.Competitions.Where(c => c.Type == Model.CompetitionType.League).OrderBy(c => c.Id, System.StringComparer.Ordinal))
            {
                var clubs = w.Clubs.Where(c => w.ClubLeague[c.Id] == league.Id).Select(c => c.Id).ToList();
                if (league.Id == f.ArgentinaFirst.CompetitionId) { outcomes.Add(Argentina.PlayFirstDivision(clubs, f.ArgentinaFirst, S, _d.MatchSim, rng)); continue; }
                if (league.Id == f.ArgentinaSecond.CompetitionId) { outcomes.Add(Argentina.PlaySecondDivision(clubs, f.ArgentinaSecond, f.ArgentinaFirst, S, _d.MatchSim, rng)); continue; }

                int legs = f.Legs.TryGetValue(league.Id, out var l) ? l : 2;
                var table = new LeagueTable(clubs, _d.MatchSim);
                foreach (var round in Fixtures.RoundRobin(clubs, legs, rng))
                    foreach (var fx in round)
                        table.Add(MatchSim.Play(fx.Home, fx.Away, S(fx.Home), S(fx.Away), _d.MatchSim, rng));
                var rows = table.Standings();
                var o = new CompetitionOutcome { CompetitionId = league.Id };
                o.Tables["League"] = rows;
                o.Titles["Champion"] = rows[0].ClubId;
                o.Relegated.AddRange(Enumerable.Reverse(rows).Take(league.Relegation).Select(r => r.ClubId));
                o.Promoted.AddRange(rows.Take(league.Promotion).Select(r => r.ClubId));
                outcomes.Add(o);
            }
            return outcomes;
        }
    }
}
