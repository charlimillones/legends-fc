using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Util;

namespace LegendsFC.Core.Season
{
    /// <summary>
    /// Cup formats (accepted Oct 9, competitions-draft.md) as scripts, like the league formats: each yields its rounds
    /// and reads the results before drawing the next. Structure randomness comes from the competition's own seed.
    /// </summary>
    public static class CupFormats
    {
        public const string Winner = "Winner", RunnerUp = "RunnerUp";

        public static IEnumerable<Round> Script(CupDef def, List<string> clubs, GameRandom rng, RoundResults ctx, GameData d)
        {
            switch (def.Kind)
            {
                case CupKind.DomesticKnockout: return Domestic(def, clubs, rng, ctx, d);
                case CupKind.LeaguePhase: return LeaguePhase(def, rng, ctx, d);
                case CupKind.Groups: return Groups(def, rng, ctx, d);
                case CupKind.Knockout: return Knockout(def, clubs, ctx);
                case CupKind.SuperCup: return SuperCup(def, clubs, ctx);
                case CupKind.SuperCupFour: return SuperCupFour(def, clubs, ctx);
                case CupKind.TwoLegSuperCup: return TwoLegSuperCup(def, clubs, ctx);
                case CupKind.Intercontinental: return Intercontinental(def, clubs, ctx);
                case CupKind.Ranking: return Ranking(def, clubs, rng, ctx, d);
                default: throw new ArgumentOutOfRangeException(nameof(def.Kind));
            }
        }

        // ------------------------------------------------------------------ ties (single match or two legs)

        private sealed class TieResult { public List<string> Winners = new List<string>(); public List<string> Losers = new List<string>(); }

        /// <summary>
        /// Knockout ties. Fixture.Home hosts the single match, or the first leg (the other club hosts the second leg).
        /// Two legs: aggregate goals, then extra time / penalties in the second leg (no away goals).
        /// </summary>
        private static IEnumerable<Round> Ties(List<Fixture> ties, string stage, bool twoLegs, DrawDecider draw, bool neutral,
                                               RoundResults ctx, CompetitionOutcome o, TieResult res)
        {
            if (ties.Count == 0) yield break;
            if (!twoLegs)
            {
                yield return new Round { Stage = stage, Fixtures = ties, PenaltiesOnDraw = true, ExtraTime = draw == DrawDecider.ExtraTime, Neutral = neutral };
                o.KnockoutMatches.AddRange(ctx.Last);
                foreach (var r in ctx.Last)
                {
                    string w = Formats.Winner(r, 0, 0, DrawRule.Penalties);
                    res.Winners.Add(w); res.Losers.Add(w == r.Home ? r.Away : r.Home);
                }
                yield break;
            }
            yield return new Round { Stage = stage, Fixtures = ties };
            var first = ctx.Last.ToList();
            o.KnockoutMatches.AddRange(first);
            var second = ties.Select(t => new Fixture(t.Away, t.Home)).ToList();
            yield return new Round { Stage = stage, Fixtures = second, FirstLegs = first, PenaltiesOnDraw = true, ExtraTime = draw == DrawDecider.ExtraTime };
            o.KnockoutMatches.AddRange(ctx.Last);
            for (int i = 0; i < ctx.Last.Count; i++)
            {
                var r = ctx.Last[i]; var f = first[i];
                int home = r.HomeGoals + f.AwayGoals, away = r.AwayGoals + f.HomeGoals;
                string w = home > away ? r.Home : away > home ? r.Away : (r.PenaltyWinner ?? r.Home);
                res.Winners.Add(w); res.Losers.Add(w == r.Home ? r.Away : r.Home);
            }
        }

        // ------------------------------------------------------------------ domestic cups

        private sealed class PoolState { public List<string> Pool; public int RoundNo; }

        /// <summary>
        /// Every league club. Lower-division clubs play the early rounds until the bracket is a power of two
        /// (or until the late entrants join at their round). Then a fresh random draw every round.
        /// </summary>
        private static IEnumerable<Round> Domestic(CupDef def, List<string> clubs, GameRandom rng, RoundResults ctx, GameData d)
        {
            var o = new CompetitionOutcome { CompetitionId = def.Id };
            var late = ctx.Pools.TryGetValue("Late", out var lp) ? lp.ToList() : new List<string>();
            var state = new PoolState { Pool = Shuffled(clubs.Where(c => !late.Contains(c)), rng) };

            int target = late.Count > 0 && def.Late != null
                ? Math.Max(1, Math.Min(state.Pool.Count, def.Late.JoinAtSize - late.Count))
                : PowerOfTwoAtMost(state.Pool.Count);
            foreach (var r in Reduce(def, state, target, rng, ctx, o)) yield return r;
            state.Pool.AddRange(late);
            foreach (var r in Reduce(def, state, PowerOfTwoAtMost(state.Pool.Count), rng, ctx, o)) yield return r;

            string runnerUp = null;
            while (state.Pool.Count > 1)
            {
                int size = state.Pool.Count;
                bool final = size == 2, semi = size == 4;
                bool twoLegs = final ? def.TwoLegFinal : semi ? def.TwoLegSemis || def.TwoLegsFromSize >= 4 : def.TwoLegsFromSize >= size;
                var draw = twoLegs ? def.TieDraw : final ? def.FinalDraw : def.SingleDraw;
                bool neutral = !twoLegs && ((final && def.FinalNeutral) || (semi && def.SemisNeutral) || def.Home == HomeRule.Neutral);
                var drawn = Shuffled(state.Pool, rng);
                var ties = new List<Fixture>();
                for (int i = 0; i + 1 < drawn.Count; i += 2) ties.Add(HomeFor(def, drawn[i], drawn[i + 1], twoLegs, ctx));
                var res = new TieResult();
                foreach (var r in Ties(ties, StageName(size), twoLegs, draw, neutral, ctx, o, res)) yield return r;
                if (final) runnerUp = res.Losers[0];
                state.Pool = res.Winners;
            }
            if (state.Pool.Count == 1) o.Titles[Winner] = state.Pool[0];
            if (runnerUp != null) o.Titles[RunnerUp] = runnerUp;
            ctx.Outcome = o;
        }

        /// <summary>Early rounds: the lowest-division clubs play until <paramref name="target"/> clubs are left; the rest get byes.</summary>
        private static IEnumerable<Round> Reduce(CupDef def, PoolState s, int target, GameRandom rng, RoundResults ctx, CompetitionOutcome o)
        {
            while (s.Pool.Count > target && s.Pool.Count > 1)
            {
                int m = s.Pool.Count;
                int matches = m <= 2 * target ? m - target : m / 2;
                var ordered = s.Pool.Select((c, i) => (c, i)).OrderByDescending(x => Division(ctx, x.c)).ThenBy(x => x.i).Select(x => x.c).ToList();
                var playing = Shuffled(ordered.Take(2 * matches), rng);
                var byes = ordered.Skip(2 * matches).ToList();
                var ties = new List<Fixture>();
                for (int i = 0; i + 1 < playing.Count; i += 2) ties.Add(HomeFor(def, playing[i], playing[i + 1], false, ctx));
                s.RoundNo++;
                var res = new TieResult();
                foreach (var r in Ties(ties, "Round " + s.RoundNo, false, def.SingleDraw, def.Home == HomeRule.Neutral, ctx, o, res)) yield return r;
                s.Pool = byes.Concat(res.Winners).ToList();
            }
        }

        /// <summary>
        /// Single match: as drawn, or the lower-division club at home (Spain, Brazil early rounds).
        /// Two legs: the stronger club (higher division, then seed) hosts the second leg.
        /// </summary>
        private static Fixture HomeFor(CupDef def, string a, string b, bool twoLegs, RoundResults ctx)
        {
            int da = Division(ctx, a), db = Division(ctx, b);
            if (twoLegs)
            {
                bool aStronger = da != db ? da < db : Score(ctx, a) >= Score(ctx, b);
                return aStronger ? new Fixture(b, a) : new Fixture(a, b);
            }
            if (def.Home == HomeRule.LowerDivision && da != db) return da > db ? new Fixture(a, b) : new Fixture(b, a);
            return new Fixture(a, b);
        }

        public static string StageName(int clubsLeft)
        {
            switch (clubsLeft)
            {
                case 2: return "Final";
                case 4: return "Semi-final";
                case 8: return "Quarter-final";
                default: return "Round of " + clubsLeft;
            }
        }

        // ------------------------------------------------------------------ continental: league phase (Champions Cup, Europa Cup)

        private static IEnumerable<Round> LeaguePhase(CupDef def, GameRandom rng, RoundResults ctx, GameData d)
        {
            var o = new CompetitionOutcome { CompetitionId = def.Id };
            var direct = Pool(ctx, "Direct");
            foreach (var r in QualifyingRound(def, Pool(ctx, "Qualifying"), ctx, o, direct)) yield return r;

            var teams = Resolve(direct, ctx).OrderByDescending(c => Score(ctx, c)).ThenBy(c => c, StringComparer.Ordinal).ToList();
            var table = new LeagueTable(teams, d.MatchSim);
            foreach (var fixtures in SwissDraw(teams, def.Pots, c => Country(ctx, c), rng))
            {
                yield return new Round { Stage = "League phase", Fixtures = fixtures };
                foreach (var r in ctx.Last) table.Add(r);
            }
            var rows = table.Standings();
            o.Tables["League phase"] = rows;
            var pos = rows.Select(r => r.ClubId).ToList();

            // 1st-4th to the quarter-finals; 5th-12th play off (5v12, 6v11, 7v10, 8v9), the better-placed club hosting the 2nd leg.
            var quarter = new List<Fixture>();
            if (pos.Count >= 12)
            {
                var playoff = Enumerable.Range(0, 4).Select(i => new Fixture(pos[11 - i], pos[4 + i])).ToList();
                var res = new TieResult();
                foreach (var r in Ties(playoff, "Knockout play-off", true, def.TieDraw, false, ctx, o, res)) yield return r;
                // 1 v winner of 8/9, 2 v 7/10, 3 v 6/11, 4 v 5/12.
                for (int i = 0; i < 4; i++) quarter.Add(new Fixture(res.Winners[3 - i], pos[i]));
            }
            else
                for (int i = 0; i < Math.Min(4, pos.Count / 2); i++) quarter.Add(new Fixture(pos[pos.Count - 1 - i], pos[i]));
            int Rank(string c) => pos.IndexOf(c);
            foreach (var r in Bracket(def, quarter, Rank, ctx, o)) yield return r;
            ctx.Outcome = o;
        }

        /// <summary>Two-legged qualifying ties in pre-season: seeds 1vN, 2vN-1 ... (same-country pairs swapped where possible). Losers are exported.</summary>
        private static IEnumerable<Round> QualifyingRound(CupDef def, List<string> qualifying, RoundResults ctx, CompetitionOutcome o, List<string> direct)
        {
            if (qualifying.Count < 2) yield break;
            var q = Resolve(qualifying, ctx).OrderByDescending(c => Score(ctx, c)).ThenBy(c => c, StringComparer.Ordinal).ToList();
            int half = q.Count / 2;
            var seeded = q.Take(half).ToList(); var unseeded = q.Skip(half).Reverse().ToList();
            for (int i = 0; i < half; i++)
                if (Country(ctx, seeded[i]) != null && Country(ctx, seeded[i]) == Country(ctx, unseeded[i]))
                    for (int j = 0; j < half; j++)
                        if (j != i && Country(ctx, seeded[i]) != Country(ctx, unseeded[j]) && Country(ctx, seeded[j]) != Country(ctx, unseeded[i]))
                        { (unseeded[i], unseeded[j]) = (unseeded[j], unseeded[i]); break; }
            var ties = Enumerable.Range(0, half).Select(i => new Fixture(unseeded[i], seeded[i])).ToList();
            var res = new TieResult();
            foreach (var r in Ties(ties, "Qualifying", true, def.TieDraw, false, ctx, o, res)) yield return r;
            ctx.Exports["QualifyingLosers"] = res.Losers;
            direct.AddRange(res.Winners);
        }

        /// <summary>Quarter-finals (as given: Home hosts the 1st leg), then semi-finals QF1 v QF4 and QF2 v QF3, then the final.</summary>
        private static IEnumerable<Round> Bracket(CupDef def, List<Fixture> quarter, Func<string, int> rank, RoundResults ctx, CompetitionOutcome o)
        {
            var qf = new TieResult();
            bool qfTwoLegs = def.Kind != CupKind.Knockout || def.TwoLegsFromSize >= 8;
            foreach (var r in Ties(quarter, "Quarter-final", qfTwoLegs, def.TieDraw, false, ctx, o, qf)) yield return r;
            var w = qf.Winners;
            var semis = new List<Fixture>();
            if (w.Count == 4) { semis.Add(Second(w[0], w[3], rank)); semis.Add(Second(w[1], w[2], rank)); }
            else if (w.Count == 2) semis.Add(Second(w[0], w[1], rank));
            var sf = new TieResult();
            bool sfTwoLegs = def.Kind != CupKind.Knockout || def.TwoLegSemis;
            if (w.Count == 4) foreach (var r in Ties(semis, "Semi-final", sfTwoLegs, def.TieDraw, false, ctx, o, sf)) yield return r;
            var finalists = w.Count == 4 ? sf.Winners : w;
            if (finalists.Count == 2)
            {
                var fin = new TieResult();
                var f = Second(finalists[0], finalists[1], rank);
                var fixture = def.FinalNeutral ? f : new Fixture(f.Away, f.Home);   // not neutral: the better club hosts
                foreach (var r in Ties(new List<Fixture> { fixture }, "Final", false, def.FinalDraw, def.FinalNeutral, ctx, o, fin)) yield return r;
                o.Titles[Winner] = fin.Winners[0]; o.Titles[RunnerUp] = fin.Losers[0];
            }
            else if (finalists.Count == 1) o.Titles[Winner] = finalists[0];
        }

        /// <summary>A tie where the better-ranked club (lower rank number) hosts the second leg.</summary>
        private static Fixture Second(string a, string b, Func<string, int> rank) => rank(a) <= rank(b) ? new Fixture(b, a) : new Fixture(a, b);

        /// <summary>
        /// League-phase draw: clubs in pots by seed; each club plays 2 from every pot (1 home, 1 away), never a club from its
        /// own country; 2 × pots rounds where every club plays once. Random restarts; the country rule is relaxed only if no draw works.
        /// </summary>
        public static List<List<Fixture>> SwissDraw(List<string> teams, int pots, Func<string, string> country, GameRandom rng)
        {
            int n = teams.Count;
            if (n < 2) return new List<List<Fixture>>();
            pots = Math.Max(1, Math.Min(pots, n / 2));
            int potSize = (int)Math.Ceiling(n / (double)pots), rounds = 2 * pots;
            var pot = Enumerable.Range(0, n).Select(i => Math.Min(pots - 1, i / potSize)).ToArray();
            var ctry = teams.Select(country).ToArray();
            foreach (bool strict in new[] { true, false })
                for (int attempt = 0; attempt < 400; attempt++)
                {
                    var played = new bool[n, n]; var cnt = new int[n, pots];
                    var schedule = new List<int[]>();
                    bool ok = true;
                    for (int r = 0; r < rounds && ok; r++)
                    {
                        var mate = Enumerable.Repeat(-1, n).ToArray();
                        if (!Match(0, mate, played, cnt, pot, ctry, strict, rng, n)) { ok = false; break; }
                        for (int i = 0; i < n; i++)
                            if (mate[i] > i) { played[i, mate[i]] = played[mate[i], i] = true; cnt[i, pot[mate[i]]]++; cnt[mate[i], pot[i]]++; }
                        schedule.Add(mate);
                    }
                    if (!ok) continue;
                    var homeOf = Orient(schedule, pot, pots, n);
                    return schedule.Select(mate => Enumerable.Range(0, n).Where(i => mate[i] > i)
                        .Select(i => homeOf[i, mate[i]] ? new Fixture(teams[i], teams[mate[i]]) : new Fixture(teams[mate[i]], teams[i])).ToList()).ToList();
                }
            return Fixtures.RoundRobin(teams, 1, rng).Take(rounds).ToList();   // last resort
        }

        private static bool Match(int from, int[] mate, bool[,] played, int[,] cnt, int[] pot, string[] ctry, bool strict, GameRandom rng, int n)
        {
            int a = from;
            while (a < n && mate[a] >= 0) a++;
            if (a >= n) return true;
            var cands = Enumerable.Range(a + 1, n - a - 1).Where(b => mate[b] < 0 && !played[a, b]
                && cnt[a, pot[b]] < 2 && cnt[b, pot[a]] < 2
                && (!strict || ctry[a] == null || ctry[a] != ctry[b])).ToList();
            for (int i = cands.Count - 1; i > 0; i--) { int j = rng.NextInt(0, i); (cands[i], cands[j]) = (cands[j], cands[i]); }
            foreach (var b in cands)
            {
                mate[a] = b; mate[b] = a;
                if (Match(a + 1, mate, played, cnt, pot, ctry, strict, rng, n)) return true;
                mate[a] = -1; mate[b] = -1;
            }
            return false;
        }

        /// <summary>Home and away: around each cycle of opponents between two pots, so every club gets 1 home and 1 away per pot.</summary>
        private static bool[,] Orient(List<int[]> schedule, int[] pot, int pots, int n)
        {
            var home = new bool[n, n];
            var adj = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
            foreach (var mate in schedule) for (int i = 0; i < n; i++) if (mate[i] >= 0) adj[i].Add(mate[i]);
            var done = new bool[n, n];
            for (int p = 0; p < pots; p++)
                for (int q = p; q < pots; q++)
                {
                    bool InPair(int x, int y) => (pot[x] == p && pot[y] == q) || (pot[x] == q && pot[y] == p);
                    for (int start = 0; start < n; start++)
                    {
                        if (pot[start] != p && pot[start] != q) continue;
                        int cur = start;
                        while (true)
                        {
                            int next = adj[cur].Where(y => InPair(cur, y) && !done[cur, y]).DefaultIfEmpty(-1).First();
                            if (next < 0) break;
                            done[cur, next] = done[next, cur] = true;
                            home[cur, next] = true;
                            cur = next;
                        }
                    }
                }
            return home;
        }

        // ------------------------------------------------------------------ continental: groups (South American cups)

        private static IEnumerable<Round> Groups(CupDef def, GameRandom rng, RoundResults ctx, GameData d)
        {
            var o = new CompetitionOutcome { CompetitionId = def.Id };
            var direct = Pool(ctx, "Direct");
            foreach (var r in QualifyingRound(def, Pool(ctx, "Qualifying"), ctx, o, direct)) yield return r;

            var teams = Resolve(direct, ctx).OrderByDescending(c => Score(ctx, c)).ThenBy(c => c, StringComparer.Ordinal).ToList();
            var groups = DrawGroups(teams, Math.Max(1, teams.Count / 4), c => Country(ctx, c), rng);
            var tables = groups.Select(g => new LeagueTable(g, d.MatchSim)).ToList();
            var groupOf = new Dictionary<string, int>();
            for (int g = 0; g < groups.Count; g++) foreach (var c in groups[g]) groupOf[c] = g;
            var rr = groups.Select(g => Fixtures.RoundRobin(g, 2, rng)).ToList();
            int rounds = rr.Max(x => x.Count);
            for (int i = 0; i < rounds; i++)
            {
                yield return new Round { Stage = "Group stage", Fixtures = rr.SelectMany(x => i < x.Count ? x[i] : new List<Fixture>()).ToList() };
                foreach (var r in ctx.Last) tables[groupOf[r.Home]].Add(r);
            }
            var st = tables.Select(t => t.Standings()).ToList();
            for (int g = 0; g < st.Count; g++) o.Tables["Group " + (char)('A' + g)] = st[g];
            // Group rank for hosting: winners first, then by points and goal difference.
            var all = st.SelectMany((rows, g) => rows.Select((row, i) => (row, i))).OrderBy(x => x.i)
                        .ThenByDescending(x => x.row.Points).ThenByDescending(x => x.row.GoalDifference).ThenBy(x => x.row.ClubId, StringComparer.Ordinal)
                        .Select(x => x.row.ClubId).ToList();
            int Rank(string c) { int i = all.IndexOf(c); return i < 0 ? 1000 : i; }
            var winners = st.Select(s => s[0].ClubId).ToList();
            var runners = st.Select(s => s.Count > 1 ? s[1].ClubId : null).Where(c => c != null).ToList();

            var quarter = new List<Fixture>();
            if (def.GroupThirdsDropDown)
            {
                ctx.Exports["GroupThirds"] = st.Where(s => s.Count > 2).Select(s => s[2].ClubId).ToList();
                // Winners v runners-up of the paired group (A1 v B2, B1 v A2, C1 v D2, D1 v C2): the winner hosts the 2nd leg.
                for (int g = 0; g + 1 < winners.Count; g += 2)
                {
                    quarter.Add(new Fixture(runners[g + 1], winners[g]));
                    quarter.Add(new Fixture(runners[g], winners[g + 1]));
                }
                if (quarter.Count == 4) quarter = new List<Fixture> { quarter[0], quarter[2], quarter[1], quarter[3] };
            }
            else
            {
                // Runners-up v the other competition's 3rd-placed clubs (the 3rds host the 2nd leg); winners wait in the quarter-finals.
                var key = def.Imports.Keys.FirstOrDefault();
                var thirds = key == null ? new List<string>() : Shuffled(ctx.Input(key), rng);
                var playoff = runners.Zip(thirds, (a, b) => new Fixture(a, b)).ToList();
                var res = new TieResult();
                foreach (var r in Ties(playoff, "Knockout play-off", true, def.TieDraw, false, ctx, o, res)) yield return r;
                var through = Shuffled(res.Winners, rng);
                for (int i = 0; i < Math.Min(winners.Count, through.Count); i++) quarter.Add(new Fixture(through[i], winners[i]));
            }
            foreach (var r in Bracket(def, quarter, Rank, ctx, o)) yield return r;
            ctx.Outcome = o;
        }

        /// <summary>Groups from pots of seeds (one club per pot in each group), keeping same-country clubs apart as far as possible.</summary>
        public static List<List<string>> DrawGroups(List<string> teams, int groupCount, Func<string, string> country, GameRandom rng)
        {
            var groups = Enumerable.Range(0, groupCount).Select(_ => new List<string>()).ToList();
            var perms = Permutations(groupCount);
            for (int p = 0; p * groupCount < teams.Count; p++)
            {
                var potClubs = teams.Skip(p * groupCount).Take(groupCount).ToList();
                int best = int.MaxValue; var bestPerms = new List<int[]>();
                foreach (var perm in perms)
                {
                    int cost = 0;
                    for (int i = 0; i < potClubs.Count; i++)
                    {
                        var c = country(potClubs[i]);
                        if (c != null) cost += groups[perm[i]].Count(x => country(x) == c);
                    }
                    if (cost < best) { best = cost; bestPerms.Clear(); }
                    if (cost == best) bestPerms.Add(perm);
                }
                var pick = bestPerms[rng.NextInt(0, bestPerms.Count - 1)];
                for (int i = 0; i < potClubs.Count; i++) groups[pick[i]].Add(potClubs[i]);
            }
            return groups;
        }

        private static List<int[]> Permutations(int n)
        {
            var result = new List<int[]>();
            void Go(int[] a, int k)
            {
                if (k == n) { result.Add((int[])a.Clone()); return; }
                for (int i = k; i < n; i++) { (a[k], a[i]) = (a[i], a[k]); Go(a, k + 1); (a[k], a[i]) = (a[i], a[k]); }
            }
            Go(Enumerable.Range(0, n).ToArray(), 0);
            return result;
        }

        // ------------------------------------------------------------------ North American Champions Cup

        private static IEnumerable<Round> Knockout(CupDef def, List<string> clubs, RoundResults ctx)
        {
            var o = new CompetitionOutcome { CompetitionId = def.Id };
            var seeds = Resolve(clubs, ctx).OrderByDescending(c => Score(ctx, c)).ThenBy(c => c, StringComparer.Ordinal).ToList();
            int Rank(string c) => seeds.IndexOf(c);
            var quarter = new List<Fixture>();
            if (seeds.Count >= 8)
                foreach (var i in new[] { 0, 3, 1, 2 }) quarter.Add(new Fixture(seeds[7 - i], seeds[i]));   // 1v8, 4v5, 2v7, 3v6
            foreach (var r in Bracket(def, quarter, Rank, ctx, o)) yield return r;
            ctx.Outcome = o;
        }

        // ------------------------------------------------------------------ super cups

        private static IEnumerable<Round> SuperCup(CupDef def, List<string> clubs, RoundResults ctx)
        {
            var o = new CompetitionOutcome { CompetitionId = def.Id };
            if (clubs.Count >= 2)
            {
                var res = new TieResult();
                foreach (var r in Ties(new List<Fixture> { new Fixture(clubs[0], clubs[1]) }, "Final", false, def.FinalDraw, true, ctx, o, res)) yield return r;
                o.Titles[Winner] = res.Winners[0]; o.Titles[RunnerUp] = res.Losers[0];
            }
            ctx.Outcome = o;
        }

        /// <summary>Spain: semi-finals 1st v 4th entrant and 2nd v 3rd (league champion v cup runner-up, league runner-up v cup winner), then the final.</summary>
        private static IEnumerable<Round> SuperCupFour(CupDef def, List<string> clubs, RoundResults ctx)
        {
            var o = new CompetitionOutcome { CompetitionId = def.Id };
            if (clubs.Count >= 4)
            {
                var sf = new TieResult();
                foreach (var r in Ties(new List<Fixture> { new Fixture(clubs[0], clubs[3]), new Fixture(clubs[1], clubs[2]) }, "Semi-final", false, def.FinalDraw, true, ctx, o, sf)) yield return r;
                var fin = new TieResult();
                foreach (var r in Ties(new List<Fixture> { new Fixture(sf.Winners[0], sf.Winners[1]) }, "Final", false, def.FinalDraw, true, ctx, o, fin)) yield return r;
                o.Titles[Winner] = fin.Winners[0]; o.Titles[RunnerUp] = fin.Losers[0];
            }
            ctx.Outcome = o;
        }

        /// <summary>South American Super Cup: two legs; the second entrant (the Champions Cup winner) hosts the second leg.</summary>
        private static IEnumerable<Round> TwoLegSuperCup(CupDef def, List<string> clubs, RoundResults ctx)
        {
            var o = new CompetitionOutcome { CompetitionId = def.Id };
            if (clubs.Count >= 2)
            {
                var res = new TieResult();
                foreach (var r in Ties(new List<Fixture> { new Fixture(clubs[0], clubs[1]) }, "Final", true, def.TieDraw, false, ctx, o, res)) yield return r;
                o.Titles[Winner] = res.Winners[0]; o.Titles[RunnerUp] = res.Losers[0];
            }
            ctx.Outcome = o;
        }

        /// <summary>North American v South American champion, then the winner v the European champion. Neutral venues.</summary>
        private static IEnumerable<Round> Intercontinental(CupDef def, List<string> clubs, RoundResults ctx)
        {
            var o = new CompetitionOutcome { CompetitionId = def.Id };
            if (clubs.Count >= 3)
            {
                var derby = new TieResult();
                foreach (var r in Ties(new List<Fixture> { new Fixture(clubs[0], clubs[1]) }, "Derby of the Americas", false, def.FinalDraw, true, ctx, o, derby)) yield return r;
                var fin = new TieResult();
                foreach (var r in Ties(new List<Fixture> { new Fixture(derby.Winners[0], clubs[2]) }, "Final", false, def.FinalDraw, true, ctx, o, fin)) yield return r;
                o.Titles[Winner] = fin.Winners[0]; o.Titles[RunnerUp] = fin.Losers[0];
            }
            ctx.Outcome = o;
        }

        // ------------------------------------------------------------------ cup-only countries

        /// <summary>Background league (home and away, not playable): its table sets the continental places.</summary>
        private static IEnumerable<Round> Ranking(CupDef def, List<string> clubs, GameRandom rng, RoundResults ctx, GameData d)
        {
            var table = new LeagueTable(clubs, d.MatchSim);
            foreach (var fixtures in Fixtures.RoundRobin(clubs, 2, rng))
            {
                yield return new Round { Stage = "League", Fixtures = fixtures };
                foreach (var r in ctx.Last) table.Add(r);
            }
            var o = new CompetitionOutcome { CompetitionId = def.Id };
            var rows = table.Standings();
            o.Tables["League"] = rows;
            if (rows.Count > 0) o.Titles["Champion"] = rows[0].ClubId;
            ctx.Outcome = o;
        }

        // ------------------------------------------------------------------ helpers

        private static List<string> Pool(RoundResults ctx, string name) => ctx.Pools.TryGetValue(name, out var p) ? p.ToList() : new List<string>();

        /// <summary>Placeholders ("?UEFA-CC:QualifyingLosers#0") become the clubs that arrived from the other competition.</summary>
        private static List<string> Resolve(List<string> clubs, RoundResults ctx)
            => clubs.Select(c =>
            {
                if (!Qualification.IsPlaceholder(c)) return c;
                int hash = c.LastIndexOf('#');
                string key = c.Substring(1, hash - 1);
                int i = int.Parse(c.Substring(hash + 1));
                var list = ctx.Input(key);
                return i < list.Count ? list[i] : null;
            }).Where(c => c != null).ToList();

        private static List<string> Shuffled(IEnumerable<string> items, GameRandom rng)
        {
            var list = items.ToList();
            for (int i = list.Count - 1; i > 0; i--) { int j = rng.NextInt(0, i); (list[i], list[j]) = (list[j], list[i]); }
            return list;
        }

        private static int PowerOfTwoAtMost(int n) { int p = 1; while (p * 2 <= n) p *= 2; return p; }
        private static int Division(RoundResults ctx, string c) => ctx.Info.TryGetValue(c ?? "", out var s) ? s.Division : 1;
        private static double Score(RoundResults ctx, string c) => ctx.Info.TryGetValue(c ?? "", out var s) ? s.Score : 0;
        private static string Country(RoundResults ctx, string c) => ctx.Info.TryGetValue(c ?? "", out var s) ? s.CountryId : null;
    }
}
