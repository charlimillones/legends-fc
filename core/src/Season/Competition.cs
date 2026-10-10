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
        /// <summary>Extra time before penalties (cups, Oct 9). Only used with PenaltiesOnDraw.</summary>
        public bool ExtraTime;
        /// <summary>Second leg of a two-legged tie: the first-leg results, aligned with Fixtures (home and away swapped). The tie is decided on aggregate.</summary>
        public List<MatchResult> FirstLegs;
        /// <summary>Played at a neutral ground (finals, Argentine Cup): no home crowd.</summary>
        public bool Neutral;
    }

    /// <summary>What a competition's script sees: the results of the round it just asked for, and where it writes its outcome.</summary>
    public sealed class RoundResults
    {
        public List<MatchResult> Last = new List<MatchResult>();
        public CompetitionOutcome Outcome;
        /// <summary>Club seeds frozen at the start of the season (cups).</summary>
        public Dictionary<string, ClubSeed> Info;
        /// <summary>Clubs that arrive from another competition during the season (e.g. qualifying losers).</summary>
        public Func<string, List<string>> Input;
        /// <summary>Clubs this competition sends to others, e.g. "QualifyingLosers".</summary>
        public Dictionary<string, List<string>> Exports = new Dictionary<string, List<string>>();
        /// <summary>The entrants by pool: "Direct", "Qualifying", "Late".</summary>
        public Dictionary<string, List<string>> Pools = new Dictionary<string, List<string>>();
        /// <summary>True during the dry run that counts rounds (no real results).</summary>
        public bool DryRun;
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
        /// <summary>Cups: entrants by pool ("Direct", "Qualifying", "Late").</summary>
        public Dictionary<string, List<string>> Pools = new Dictionary<string, List<string>>();
        /// <summary>Cups: clubs received from other competitions during the season ("UEFA-CC:QualifyingLosers" → clubs).</summary>
        public Dictionary<string, List<string>> Inputs = new Dictionary<string, List<string>>();

        [Newtonsoft.Json.JsonIgnore] public Dictionary<string, ClubSeed> Info;
        /// <summary>What the format has sent out so far (rebuilt on replay).</summary>
        [Newtonsoft.Json.JsonIgnore] public Dictionary<string, List<string>> Exports => _ctx?.Exports ?? new Dictionary<string, List<string>>();

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
            _ctx = NewContext(Info, Pools, key => Inputs.TryGetValue(key, out var v) ? v
                : throw new InvalidOperationException(CompetitionId + " needs " + key + " before it has arrived."));
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
        public static int CountRounds(string competitionId, List<string> clubs, ulong seed, GameData d,
                                      Dictionary<string, ClubSeed> info = null, Dictionary<string, List<string>> pools = null)
        {
            var def = d.Cups.Cups.FirstOrDefault(c => c.Id == competitionId);
            var ctx = NewContext(info, pools, key => Enumerable.Range(0, def?.ImportCount(key) ?? 0).Select(i => Qualification.Placeholder(key, i)).ToList());
            ctx.DryRun = true;
            int n = 0;
            using (var it = Formats.Script(competitionId, clubs, new GameRandom(seed), ctx, d).GetEnumerator())
                while (it.MoveNext())
                {
                    n++;
                    ctx.Last = it.Current.Fixtures.Select(f => new MatchResult { Home = f.Home, Away = f.Away, PenaltyWinner = it.Current.PenaltiesOnDraw ? f.Home : null }).ToList();
                }
            return n;
        }

        private static RoundResults NewContext(Dictionary<string, ClubSeed> info, Dictionary<string, List<string>> pools, Func<string, List<string>> input)
            => new RoundResults { Info = info ?? new Dictionary<string, ClubSeed>(), Pools = pools ?? new Dictionary<string, List<string>>(), Input = input };
    }

    /// <summary>
    /// Plays a round in sim mode. A level knockout match (or a level aggregate in a second leg) goes to extra time
    /// when the round has it, then to penalties. The shootout is a coin flip until a shootout model exists.
    /// </summary>
    public static class RoundPlayer
    {
        /// <summary>Plays a round with real teams (squad and tactics, Oct 9). Knockout rounds decide every tie.</summary>
        public static List<MatchResult> Sim(Round round, string competitionId, Squad.MatchEngine.Context ctx)
        {
            var list = new List<MatchResult>();
            for (int i = 0; i < round.Fixtures.Count; i++)
            {
                var first = round.FirstLegs != null ? round.FirstLegs[i] : (MatchResult?)null;
                list.Add(Squad.MatchEngine.Play(competitionId, round.Fixtures[i], round.PenaltiesOnDraw, round.ExtraTime, first, ctx));
            }
            return list;
        }

        /// <summary>Level after this match: on the day, or on aggregate with the first leg (the second leg's home club was away in the first).</summary>
        public static bool Level(MatchResult r, MatchResult? firstLeg)
        {
            int home = r.HomeGoals, away = r.AwayGoals;
            if (firstLeg.HasValue) { home += firstLeg.Value.AwayGoals; away += firstLeg.Value.HomeGoals; }
            return home == away;
        }
    }
}
