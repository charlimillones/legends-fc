using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Util;

namespace LegendsFC.Core.Season
{
    /// <summary>One round of matches a competition wants played (a matchday, or a knockout round).</summary>
    public sealed class Round
    {
        public string Stage;                 // e.g. "Apertura", "Apertura playoffs", "League", "Final"
        public List<Fixture> Fixtures = new List<Fixture>();
        /// <summary>Knockout round decided by penalties on a draw: the match player decides the shootout (MatchResult.PenaltyWinner).</summary>
        public bool PenaltiesOnDraw;
    }

    /// <summary>What a competition's script sees: the results of the round it just asked for, and where it writes its outcome.</summary>
    public sealed class RoundResults
    {
        public List<MatchResult> Last = new List<MatchResult>();
        public CompetitionOutcome Outcome;
    }

    /// <summary>
    /// A competition being played week by week. The format is a script (an iterator) that yields rounds and reads
    /// their results. Only the seed and the results are saved; on load the script is replayed from them, so a
    /// half-played season survives a save.
    /// </summary>
    public sealed class CompetitionRun
    {
        public string CompetitionId;
        public ulong Seed;                          // structure randomness (zones, fixture order): independent of match luck
        public List<string> Clubs = new List<string>();
        public List<List<MatchResult>> PlayedRounds = new List<List<MatchResult>>();
        public List<string> PlayedStages = new List<string>();
        public int PlannedRounds;
        public CompetitionOutcome Outcome;

        [Newtonsoft.Json.JsonIgnore] private IEnumerator<Round> _script;
        [Newtonsoft.Json.JsonIgnore] private RoundResults _ctx;
        [Newtonsoft.Json.JsonIgnore] private Round _pending;

        public bool Finished => Outcome != null;

        /// <summary>The next round to play (null when finished). Rebuilds the script from the saved results if needed.</summary>
        public Round Next(GameData d)
        {
            if (Finished) return null;
            Ensure(d);
            return _pending;
        }

        /// <summary>Record the results of the round returned by Next.</summary>
        public void Record(List<MatchResult> results, GameData d)
        {
            Ensure(d);
            if (_pending == null) throw new InvalidOperationException("No round is waiting for results.");
            PlayedRounds.Add(results);
            PlayedStages.Add(_pending.Stage);
            Advance(results);
        }

        private void Ensure(GameData d)
        {
            if (_script != null) return;
            _ctx = new RoundResults();
            _script = Formats.Script(CompetitionId, Clubs, new GameRandom(Seed), _ctx, d).GetEnumerator();
            _pending = _script.MoveNext() ? _script.Current : null;
            foreach (var played in PlayedRounds) Advance(played, replay: true);
            if (_pending == null) Outcome = _ctx.Outcome;
        }

        private void Advance(List<MatchResult> results, bool replay = false)
        {
            _ctx.Last = results;
            _pending = _script.MoveNext() ? _script.Current : null;
            if (_pending == null) Outcome = _ctx.Outcome;
        }

        /// <summary>How many rounds the format has (it doesn't depend on results): a dry run with 0-0 draws.</summary>
        public static int CountRounds(string competitionId, List<string> clubs, ulong seed, GameData d)
        {
            var ctx = new RoundResults();
            int n = 0;
            using (var it = Formats.Script(competitionId, clubs, new GameRandom(seed), ctx, d).GetEnumerator())
                while (it.MoveNext())
                {
                    n++;
                    ctx.Last = it.Current.Fixtures.Select(f => new MatchResult { Home = f.Home, Away = f.Away, PenaltyWinner = it.Current.PenaltiesOnDraw ? f.Home : null }).ToList();
                }
            return n;
        }
    }

    /// <summary>Plays a round in sim mode. The penalty shootout is a coin flip until a shootout model exists.</summary>
    public static class RoundPlayer
    {
        public static List<MatchResult> Sim(Round round, Func<string, double> strength, GameData d, GameRandom rng)
        {
            var list = new List<MatchResult>();
            foreach (var f in round.Fixtures)
            {
                var r = MatchSim.Play(f.Home, f.Away, strength(f.Home), strength(f.Away), d.MatchSim, rng);
                if (round.PenaltiesOnDraw && r.HomeGoals == r.AwayGoals)
                    r.PenaltyWinner = rng.Chance(d.LeagueFormats.ArgentinaFirst.PenaltyHomeWinChance) ? f.Home : f.Away;
                list.Add(r);
            }
            return list;
        }
    }
}
