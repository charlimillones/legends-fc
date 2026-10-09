using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Util;

namespace LegendsFC.Core.Season
{
    /// <summary>
    /// Competition formats as scripts: each yields its rounds in order and reads the results before going on,
    /// so the season can be played week by week (and replayed from saved results).
    /// </summary>
    public static class Formats
    {
        public static IEnumerable<Round> Script(string competitionId, List<string> clubs, GameRandom rng, RoundResults ctx, GameData d)
        {
            var f = d.LeagueFormats;
            if (competitionId == f.ArgentinaFirst.CompetitionId) return ArgentinaFirst(competitionId, clubs, f.ArgentinaFirst, rng, ctx, d);
            if (competitionId == f.ArgentinaSecond.CompetitionId) return ArgentinaSecond(competitionId, clubs, f.ArgentinaSecond, rng, ctx, d);
            var comp = d.Competitions.First(c => c.Id == competitionId);
            int legs = f.Legs.TryGetValue(competitionId, out var l) ? l : 2;
            return League(comp, clubs, legs, rng, ctx, d);
        }

        // ------------------------------------------------------------------ round-robin league

        private static IEnumerable<Round> League(Model.Competition comp, List<string> clubs, int legs, GameRandom rng, RoundResults ctx, GameData d)
        {
            var table = new LeagueTable(clubs, d.MatchSim);
            foreach (var fixtures in Fixtures.RoundRobin(clubs, legs, rng))
            {
                yield return new Round { Stage = "League", Fixtures = fixtures };
                foreach (var r in ctx.Last) table.Add(r);
            }
            var rows = table.Standings();
            var o = new CompetitionOutcome { CompetitionId = comp.Id };
            o.Tables["League"] = rows;
            o.Titles["Champion"] = rows[0].ClubId;
            o.Relegated.AddRange(Enumerable.Reverse(rows).Take(comp.Relegation).Select(r => r.ClubId));
            o.Promoted.AddRange(rows.Take(comp.Promotion).Select(r => r.ClubId));
            ctx.Outcome = o;
        }

        // ------------------------------------------------------------------ Argentina, first division

        /// <summary>Apertura and Clausura: 2 zones of 15 (zone round-robin + classic + interzonal), top 8 of each zone into crossed playoffs; annual table; 2 relegated.</summary>
        private static IEnumerable<Round> ArgentinaFirst(string id, List<string> clubs, ArgentinaFirstConfig cfg, GameRandom rng, RoundResults ctx, GameData d)
        {
            var o = new CompetitionOutcome { CompetitionId = id };
            var (zoneA, zoneB) = DrawZones(clubs, rng);
            int n = zoneA.Count;
            var rrA = Fixtures.RoundRobin(zoneA, 1, rng); var rrB = Fixtures.RoundRobin(zoneB, 1, rng);
            var apertura = new List<List<Fixture>>();
            for (int i = 0; i < System.Math.Max(rrA.Count, rrB.Count); i++)
                apertura.Add((i < rrA.Count ? rrA[i] : new List<Fixture>()).Concat(i < rrB.Count ? rrB[i] : new List<Fixture>()).ToList());
            apertura.Add(Enumerable.Range(0, n).Select(i => new Fixture(zoneA[i], zoneB[i])).ToList());              // classic
            apertura.Add(Enumerable.Range(0, n).Select(i => new Fixture(zoneB[(i + 1) % n], zoneA[i])).ToList());    // interzonal
            var clausura = apertura.Select(r => r.Select(f => new Fixture(f.Away, f.Home)).ToList()).ToList();

            var annual = new LeagueTable(clubs, d.MatchSim);
            foreach (var (name, rounds) in new[] { ("Apertura", apertura), ("Clausura", clausura) })
            {
                var table = new LeagueTable(clubs, d.MatchSim);
                foreach (var fixtures in rounds)
                {
                    yield return new Round { Stage = name, Fixtures = fixtures };
                    foreach (var r in ctx.Last) { table.Add(r); annual.Add(r); }
                }
                var standings = table.Standings();
                var a = standings.Where(r => zoneA.Contains(r.ClubId)).ToList();
                var b = standings.Where(r => zoneB.Contains(r.ClubId)).ToList();
                o.Tables[name + " A"] = a; o.Tables[name + " B"] = b;

                // Playoffs: crossed (A1 v B8 ...), zone leaders kept apart until the final. Single matches, penalties on a draw.
                int q = cfg.PlayoffQualifiersPerZone;
                var seed = new Dictionary<string, int>();
                for (int i = 0; i < q; i++) { seed[a[i].ClubId] = i; seed[b[i].ClubId] = i; }
                var bracket = new List<string>();
                int[] order = { 0, 3, 1, 2 };
                foreach (var i in order) { bracket.Add(a[i].ClubId); bracket.Add(b[q - 1 - i].ClubId); }
                foreach (var i in order) { bracket.Add(b[i].ClubId); bracket.Add(a[q - 1 - i].ClubId); }
                while (bracket.Count > 1)
                {
                    var ties = new List<Fixture>();
                    for (int i = 0; i < bracket.Count; i += 2)
                    {
                        string x = bracket[i], y = bracket[i + 1];
                        ties.Add(seed[x] <= seed[y] ? new Fixture(x, y) : new Fixture(y, x));   // better seed at home
                    }
                    yield return new Round { Stage = name + " playoffs", Fixtures = ties, PenaltiesOnDraw = true };
                    o.KnockoutMatches.AddRange(ctx.Last);
                    bracket = ties.Select((t, i) => Winner(ctx.Last[i], seed[t.Home], seed[t.Away], DrawRule.Penalties)).ToList();
                }
                o.Titles[name] = bracket[0];
            }

            var annualRows = annual.Standings();
            o.Tables["Annual"] = annualRows;
            o.Titles["League"] = annualRows[0].ClubId;
            foreach (var row in Enumerable.Reverse(annualRows).Take(cfg.RelegatedByAnnualTable)) o.Relegated.Add(row.ClubId);
            var averages = annualRows.OrderBy(r => (double)r.Points / r.Played).ThenBy(r => r.GoalDifference).ToList();
            foreach (var row in averages.Where(r => !o.Relegated.Contains(r.ClubId)).Take(cfg.RelegatedByAverages)) o.Relegated.Add(row.ClubId);
            ctx.Outcome = o;
        }

        // ------------------------------------------------------------------ Argentina, second division

        /// <summary>2 zones home and away; the zone winners play a final (champion, 1st promotion); the reducido decides the 2nd promotion.</summary>
        private static IEnumerable<Round> ArgentinaSecond(string id, List<string> clubs, ArgentinaSecondConfig cfg, GameRandom rng, RoundResults ctx, GameData d)
        {
            var o = new CompetitionOutcome { CompetitionId = id };
            var (zoneA, zoneB) = DrawZones(clubs, rng);
            var rrA = Fixtures.RoundRobin(zoneA, 2, rng); var rrB = Fixtures.RoundRobin(zoneB, 2, rng);
            var tA = new LeagueTable(zoneA, d.MatchSim); var tB = new LeagueTable(zoneB, d.MatchSim);
            var setA = new HashSet<string>(zoneA);
            for (int i = 0; i < System.Math.Max(rrA.Count, rrB.Count); i++)
            {
                var fixtures = (i < rrA.Count ? rrA[i] : new List<Fixture>()).Concat(i < rrB.Count ? rrB[i] : new List<Fixture>()).ToList();
                yield return new Round { Stage = "Zones", Fixtures = fixtures };
                foreach (var r in ctx.Last) (setA.Contains(r.Home) ? tA : tB).Add(r);
            }
            var a = tA.Standings(); var b = tB.Standings();
            o.Tables["Zone A"] = a; o.Tables["Zone B"] = b;

            yield return new Round { Stage = "Final", Fixtures = { new Fixture(a[0].ClubId, b[0].ClubId) }, PenaltiesOnDraw = true };
            o.KnockoutMatches.AddRange(ctx.Last);
            string champion = Winner(ctx.Last[0], 0, 0, DrawRule.Penalties);
            string finalLoser = champion == a[0].ClubId ? b[0].ClubId : a[0].ClubId;
            o.Titles["Champion"] = champion;
            o.Promoted.Add(champion);

            var rank = new Dictionary<string, (int place, int points)>();
            foreach (var zone in new[] { a, b })
                for (int i = 0; i < zone.Count; i++) rank[zone[i].ClubId] = (i + 1, zone[i].Points);
            int Seed(string c) => c == finalLoser ? 0 : rank[c].place * 1000 - rank[c].points;   // lower = better

            int from = cfg.ReducidoFromPlace, to = cfg.ReducidoToPlace;
            var first = new List<Fixture>();
            for (int p = from; p <= (from + to) / 2; p++) first.Add(Home(a[p - 1].ClubId, b[to + from - p - 1].ClubId, Seed));
            for (int p = from; p < (from + to) / 2; p++) first.Add(Home(b[p - 1].ClubId, a[to + from - p - 1].ClubId, Seed));
            yield return new Round { Stage = "Reducido", Fixtures = first };
            o.KnockoutMatches.AddRange(ctx.Last);
            var survivors = first.Select((t, i) => Winner(ctx.Last[i], Seed(t.Home), Seed(t.Away), DrawRule.BetterSeedAdvances)).ToList();
            survivors.Add(finalLoser);
            var round = survivors.OrderBy(Seed).ToList();
            while (round.Count > 1)
            {
                var ties = new List<Fixture>();
                int half = round.Count / 2;
                for (int i = 0; i < half; i++) ties.Add(Home(round[i], round[round.Count - 1 - i], Seed));
                yield return new Round { Stage = "Reducido", Fixtures = ties };
                o.KnockoutMatches.AddRange(ctx.Last);
                round = ties.Select((t, i) => Winner(ctx.Last[i], Seed(t.Home), Seed(t.Away), DrawRule.BetterSeedAdvances)).OrderBy(Seed).ToList();
            }
            o.Titles["Reducido"] = round[0];
            o.Promoted.Add(round[0]);
            ctx.Outcome = o;
        }

        // ------------------------------------------------------------------ helpers

        private static Fixture Home(string x, string y, System.Func<string, int> seed) => seed(x) <= seed(y) ? new Fixture(x, y) : new Fixture(y, x);

        /// <summary>Knockout winner: the goals, then the shootout (Penalties) or the better seed (lower number).</summary>
        public static string Winner(MatchResult r, int seedHome, int seedAway, DrawRule rule)
        {
            if (r.HomeGoals > r.AwayGoals) return r.Home;
            if (r.AwayGoals > r.HomeGoals) return r.Away;
            if (rule == DrawRule.BetterSeedAdvances) return seedHome <= seedAway ? r.Home : r.Away;
            return r.PenaltyWinner ?? r.Home;
        }

        private static (List<string>, List<string>) DrawZones(IList<string> clubs, GameRandom rng)
        {
            var list = clubs.OrderBy(x => x, System.StringComparer.Ordinal).ToList();
            for (int i = list.Count - 1; i > 0; i--) { int j = rng.NextInt(0, i); (list[i], list[j]) = (list[j], list[i]); }
            int half = list.Count / 2;
            return (list.Take(half).ToList(), list.Skip(half).ToList());
        }
    }
}
