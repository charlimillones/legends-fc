using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Util;

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

    /// <summary>Argentina's 2026 zone formats (Carlos, Oct 8: copy the zones; details adapted in league-formats.json).</summary>
    public static class Argentina
    {
        public static CompetitionOutcome PlayFirstDivision(IList<string> clubs, ArgentinaFirstConfig cfg, System.Func<string, double> strength,
            MatchSimConfig c, GameRandom rng)
        {
            var o = new CompetitionOutcome { CompetitionId = cfg.CompetitionId };
            var (zoneA, zoneB) = DrawZones(clubs, rng);
            int n = zoneA.Count;

            // Apertura fixtures: zone round-robin (once) + classic (A[i]-B[i]) + interzonal (A[i]-B[i+1]).
            var apertura = new List<Fixture>();
            apertura.AddRange(Fixtures.RoundRobin(zoneA, 1, rng).SelectMany(r => r));
            apertura.AddRange(Fixtures.RoundRobin(zoneB, 1, rng).SelectMany(r => r));
            for (int i = 0; i < n; i++)
            {
                apertura.Add(new Fixture(zoneA[i], zoneB[i]));
                apertura.Add(new Fixture(zoneB[(i + 1) % n], zoneA[i]));
            }
            var clausura = apertura.Select(f => new Fixture(f.Away, f.Home)).ToList(); // same fixtures, home/away swapped

            var annual = new LeagueTable(clubs, c);
            foreach (var (name, fixtures) in new[] { ("Apertura", apertura), ("Clausura", clausura) })
            {
                var table = new LeagueTable(clubs, c);
                foreach (var f in fixtures)
                {
                    var r = MatchSim.Play(f.Home, f.Away, strength(f.Home), strength(f.Away), c, rng);
                    table.Add(r); annual.Add(r);
                }
                var standings = table.Standings();
                var a = standings.Where(r => zoneA.Contains(r.ClubId)).ToList();
                var b = standings.Where(r => zoneB.Contains(r.ClubId)).ToList();
                o.Tables[name + " A"] = a; o.Tables[name + " B"] = b;
                o.Titles[name] = Playoffs(a, b, cfg, strength, c, rng, o.KnockoutMatches);
            }

            var annualRows = annual.Standings();
            o.Tables["Annual"] = annualRows;
            o.Titles["League"] = annualRows[0].ClubId;

            // Relegation: bottom of the annual table, then bottom of the averages table.
            // Averages = points per game over up to 3 seasons; only this season exists yet, so it follows the annual table.
            foreach (var row in Enumerable.Reverse(annualRows).Take(cfg.RelegatedByAnnualTable)) o.Relegated.Add(row.ClubId);
            var averages = annualRows.OrderBy(r => (double)r.Points / r.Played).ThenBy(r => r.GoalDifference).ToList();
            foreach (var row in averages.Where(r => !o.Relegated.Contains(r.ClubId)).Take(cfg.RelegatedByAverages)) o.Relegated.Add(row.ClubId);
            return o;
        }

        /// <summary>Round of 16 crossed (A1 v B8 ... A8 v B1), then quarters, semis and final. Single matches, draws → penalties.</summary>
        private static string Playoffs(List<TableRow> a, List<TableRow> b, ArgentinaFirstConfig cfg, System.Func<string, double> strength,
            MatchSimConfig c, GameRandom rng, List<MatchResult> log)
        {
            int q = cfg.PlayoffQualifiersPerZone;
            var seed = new Dictionary<string, int>();
            for (int i = 0; i < q; i++) { seed[a[i].ClubId] = i; seed[b[i].ClubId] = i; }
            var round = new List<string>();
            // Bracket order keeps zone leaders apart until the final.
            int[] order = { 0, 3, 1, 2 };
            foreach (var i in order) { round.Add(a[i].ClubId); round.Add(b[q - 1 - i].ClubId); }
            foreach (var i in order) { round.Add(b[i].ClubId); round.Add(a[q - 1 - i].ClubId); }
            while (round.Count > 1)
            {
                var next = new List<string>();
                for (int i = 0; i < round.Count; i += 2)
                {
                    next.Add(Knockout.Play(round[i], seed[round[i]], round[i + 1], seed[round[i + 1]], DrawRule.Penalties,
                        cfg.PenaltyHomeWinChance, strength, c, rng, out var r));
                    log.Add(r);
                }
                round = next;
            }
            return round[0];
        }

        public static CompetitionOutcome PlaySecondDivision(IList<string> clubs, ArgentinaSecondConfig cfg, ArgentinaFirstConfig penalties,
            System.Func<string, double> strength, MatchSimConfig c, GameRandom rng)
        {
            var o = new CompetitionOutcome { CompetitionId = cfg.CompetitionId };
            var (zoneA, zoneB) = DrawZones(clubs, rng);
            var tables = new List<List<TableRow>>();
            foreach (var (name, zone) in new[] { ("Zone A", zoneA), ("Zone B", zoneB) })
            {
                var t = new LeagueTable(zone, c);
                foreach (var f in Fixtures.RoundRobin(zone, 2, rng).SelectMany(r => r))
                    t.Add(MatchSim.Play(f.Home, f.Away, strength(f.Home), strength(f.Away), c, rng));
                var rows = t.Standings();
                o.Tables[name] = rows; tables.Add(rows);
            }
            var a = tables[0]; var b = tables[1];

            // Final between the zone winners: champion and 1st promotion.
            string champion = Knockout.Play(a[0].ClubId, 0, b[0].ClubId, 0, DrawRule.Penalties, penalties.PenaltyHomeWinChance, strength, c, rng, out var fr);
            o.KnockoutMatches.Add(fr);
            string finalLoser = champion == a[0].ClubId ? b[0].ClubId : a[0].ClubId;
            o.Titles["Champion"] = champion;
            o.Promoted.Add(champion);

            // Reducido: places 2-8 of each zone, crossed; draws → better-placed club advances.
            var rank = new Dictionary<string, (int place, int points)>();
            foreach (var zone in new[] { a, b })
                for (int i = 0; i < zone.Count; i++) rank[zone[i].ClubId] = (i + 1, zone[i].Points);
            int Seed(string id) => id == finalLoser ? 0 : rank[id].place * 1000 - rank[id].points; // lower = better

            int from = cfg.ReducidoFromPlace, to = cfg.ReducidoToPlace;
            var round1 = new List<(string, string)>();
            for (int p = from; p <= (from + to) / 2; p++) round1.Add((a[p - 1].ClubId, b[to + from - p - 1].ClubId));
            for (int p = from; p < (from + to) / 2; p++) round1.Add((b[p - 1].ClubId, a[to + from - p - 1].ClubId));
            var survivors = new List<string>();
            foreach (var (x, y) in round1)
            {
                survivors.Add(Knockout.Play(x, Seed(x), y, Seed(y), DrawRule.BetterSeedAdvances, 0.5, strength, c, rng, out var r));
                o.KnockoutMatches.Add(r);
            }
            survivors.Add(finalLoser);
            var round = survivors.OrderBy(Seed).ToList();
            while (round.Count > 1)
            {
                var next = new List<string>();
                int half = round.Count / 2;
                for (int i = 0; i < half; i++)
                {
                    string x = round[i], y = round[round.Count - 1 - i];
                    next.Add(Knockout.Play(x, Seed(x), y, Seed(y), DrawRule.BetterSeedAdvances, 0.5, strength, c, rng, out var r));
                    o.KnockoutMatches.Add(r);
                }
                round = next.OrderBy(Seed).ToList();
            }
            o.Titles["Reducido"] = round[0];
            o.Promoted.Add(round[0]);
            return o;
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
