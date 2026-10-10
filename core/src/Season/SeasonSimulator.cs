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

        /// <summary>Home league matches per club per season (gate income). Cup-only clubs have no league games yet.</summary>
        public static int HomeLeagueMatches(string leagueOrCountry, int teams, GameData d)
        {
            var f = d.LeagueFormats;
            if (leagueOrCountry == f.ArgentinaFirst.CompetitionId) return 16;           // 2 tournaments × 8
            if (leagueOrCountry == f.ArgentinaSecond.CompetitionId) return (teams / 2) - 1; // zone home games
            if (!f.Legs.TryGetValue(leagueOrCountry, out var legs)) return 0;
            return legs * (teams - 1) / 2;
        }

        /// <summary>Main league table per competition (Argentina: the annual / zone tables are in the outcome).</summary>
        public Dictionary<string, List<TableRow>> PlayLeagues(GameWorld w, GameRandom rng)
            => PlaySeason(w, rng).ToDictionary(o => o.CompetitionId,
                o => o.Tables.TryGetValue("League", out var t) ? t : o.Tables.TryGetValue("Annual", out var a) ? a : o.Tables.Values.First());

        /// <summary>A season's league competitions, ready to play round by round (structure seeds drawn from rng).</summary>
        public List<CompetitionRun> NewRuns(GameWorld w, GameRandom rng)
        {
            var runs = new List<CompetitionRun>();
            foreach (var league in w.Competitions.Where(c => c.Type == Model.CompetitionType.League).OrderBy(c => c.Id, System.StringComparer.Ordinal))
            {
                var clubs = w.Clubs.Where(c => w.ClubLeague[c.Id] == league.Id).Select(c => c.Id).ToList();
                var run = new CompetitionRun { CompetitionId = league.Id, Seed = rng.NextUInt64(), Clubs = clubs };
                run.PlannedRounds = CompetitionRun.CountRounds(run.CompetitionId, clubs, run.Seed, _d);
                runs.Add(run);
            }
            return runs;
        }

        /// <summary>Plays every league season at once in sim mode (headless tools and tests). The weekly calendar uses the same runs.</summary>
        public List<CompetitionOutcome> PlaySeason(GameWorld w, GameRandom rng)
        {
            var runs = NewRuns(w, rng);
            var ctx = new Squad.MatchEngine.Context { World = w, Data = _d, Ratings = new Squad.RatingTable(_d), Rng = rng };
            // Round by round across the leagues, with a week's rest after each (energy and injuries, Oct 9).
            while (true)
            {
                bool any = false;
                foreach (var run in runs)
                {
                    var round = run.Next(_d);
                    if (round == null) continue;
                    any = true;
                    run.Record(RoundPlayer.Sim(round, run.CompetitionId, ctx), _d);
                }
                if (!any) break;
                Squad.Fitness.WeekEnd(w, _d, rng, null);
            }
            return runs.Select(r => r.Outcome).ToList();
        }
    }
}
