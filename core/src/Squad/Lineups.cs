using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Squad
{
    /// <summary>
    /// Each player's rating at each of the 12 positions this week (main-position formula, out-of-position drop
    /// and wrong foot, confirmed Oct 8). Attributes only change at the weekly training, so one table serves a week.
    /// </summary>
    public sealed class RatingTable
    {
        private readonly GameData _d;
        private readonly Dictionary<string, double[]> _cache = new Dictionary<string, double[]>();
        private readonly Dictionary<(string, Position, Foot), double[]> _drops = new Dictionary<(string, Position, Foot), double[]>();
        public RatingTable(GameData d) { _d = d; }

        /// <summary>The out-of-position drop to each position: depends only on archetype, main position and foot.</summary>
        private double[] Drops(Player p)
        {
            var key = (p.ArchetypeId, p.MainPosition, p.Foot);
            if (_drops.TryGetValue(key, out var row)) return row;
            row = new double[Positions.All.Length];
            var arch = _d.Archetype(p.ArchetypeId);
            foreach (var target in Positions.All)
                row[(int)target] = OutOfPosition.Evaluate(arch, p.MainPosition, p.Foot, target, _d.PositionRules, _d.OutOfPosition).Drop;
            return _drops[key] = row;
        }

        public double At(Player p, Position pos)
        {
            if (!_cache.TryGetValue(p.Id, out var row))
            {
                row = new double[Positions.All.Length];
                var drops = Drops(p);
                foreach (var target in Positions.All)
                    row[(int)target] = PositionRating.Base(p.Attributes, target, _d.PositionRatings) * (1 - drops[(int)target]);
                _cache[p.Id] = row;
            }
            return row[(int)pos];
        }

        public double Main(Player p) => At(p, p.MainPosition);

        /// <summary>AI formations chosen this week (a club keeps its formation for the week).</summary>
        public readonly Dictionary<string, string> Formations = new Dictionary<string, string>();
    }

    /// <summary>A team for one match: who plays in each formation slot, the bench and the mentality.</summary>
    public sealed class TeamSheet
    {
        public string ClubId, Formation;
        public int Mentality;
        /// <summary>Slot labels from squad.json (e.g. "LWB") and the position each one uses (LWB = LB).</summary>
        public List<string> Slots = new List<string>();
        public List<Position> SlotPositions = new List<Position>();
        /// <summary>The starter in each slot (null if the club can't field 11).</summary>
        public List<Player> Starters = new List<Player>();
        public List<Player> Bench = new List<Player>();
    }

    /// <summary>
    /// Picking teams (agreed Oct 9). The user's lineup is used as set (unavailable players are replaced by the best fit);
    /// AI clubs, and the auto-pick button, choose the formation that fits the squad best and the best 11 for it, counting energy.
    /// Unrelated positions (-30%) are used only when nothing else is left.
    /// </summary>
    public static class Lineups
    {
        public static Position SlotPosition(string slot)
        {
            switch (slot)
            {
                case "LWB": return Position.LB;
                case "RWB": return Position.RB;
                default: return (Position)Enum.Parse(typeof(Position), slot);
            }
        }

        /// <summary>Playing fitness: below 80 energy he plays 0.4% worse per point (agreed Oct 9).</summary>
        public static double EnergyFactor(double energy, SquadConfig c)
            => 1 - c.EnergyPenaltyPerPoint * Math.Max(0, c.EnergyPenaltyBelow - energy);

        public static double Effective(Player p, Position pos, RatingTable r, SquadConfig c) => r.At(p, pos) * EnergyFactor(p.Energy, c);

        /// <summary>Can he play in this competition today? Not injured, not suspended for it.</summary>
        public static bool Available(GameWorld w, Player p, string competitionId, GameData d)
            => !p.Injured && !Discipline.IsSuspended(w, p, competitionId, d);

        /// <summary>The team a club sends out for a match.</summary>
        public static TeamSheet Pick(GameWorld w, Club club, string competitionId, RatingTable r, GameData d)
        {
            var c = d.Squad;
            var squad = Transfers.Market.Squad(w, club.Id).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
            var available = squad.Where(p => Available(w, p, competitionId, d)).ToList();
            var t = club.Tactics;
            bool user = club.Id == w.UserClubId;
            string formation;
            if (user && c.Formations.ContainsKey(t.Formation)) formation = t.Formation;
            else if (!r.Formations.TryGetValue(club.Id, out formation)) r.Formations[club.Id] = formation = BestFormation(available, r, d);
            if (!user) t.Formation = formation;

            var sheet = new TeamSheet { ClubId = club.Id, Formation = formation, Mentality = t.Mentality };
            sheet.Slots = c.Formations[formation].ToList();
            sheet.SlotPositions = sheet.Slots.Select(SlotPosition).ToList();
            var chosen = new HashSet<string>();
            var starters = new Player[sheet.Slots.Count];
            if (user && t.Lineup.Count == sheet.Slots.Count)
                for (int i = 0; i < starters.Length; i++)
                {
                    var p = available.FirstOrDefault(x => x.Id == t.Lineup[i]);
                    if (p != null && chosen.Add(p.Id)) starters[i] = p;
                }
            Fill(starters, sheet.SlotPositions, available, chosen, r, c);
            sheet.Starters = starters.ToList();

            // Bench: the user's choices first (if available), then the best of the rest, with a goalkeeper if there is one.
            var bench = new List<Player>();
            if (user) foreach (var id in t.Bench) { var p = available.FirstOrDefault(x => x.Id == id); if (p != null && bench.Count < c.Bench && chosen.Add(p.Id)) bench.Add(p); }
            var rest = available.Where(p => !chosen.Contains(p.Id)).OrderByDescending(p => Effective(p, p.MainPosition, r, c)).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
            if (bench.All(p => p.MainPosition != Position.GK))
            {
                var gk = rest.FirstOrDefault(p => p.MainPosition == Position.GK);
                if (gk != null && bench.Count < c.Bench) { bench.Add(gk); chosen.Add(gk.Id); rest.Remove(gk); }
            }
            foreach (var p in rest) { if (bench.Count >= c.Bench) break; bench.Add(p); chosen.Add(p.Id); }
            sheet.Bench = bench;
            return sheet;
        }

        /// <summary>Fills empty slots: first with natural players for the slot, then with the best fit left.</summary>
        public static void Fill(Player[] starters, List<Position> positions, List<Player> available, HashSet<string> chosen, RatingTable r, SquadConfig c)
        {
            var order = Enumerable.Range(0, positions.Count).OrderBy(i => positions[i] == Position.GK ? 0 : 1).ThenBy(i => i).ToList();
            foreach (var i in order)
            {
                if (starters[i] != null) continue;
                var best = BestFor(available, chosen, positions[i], true, r, c);
                if (best != null) { starters[i] = best; chosen.Add(best.Id); }
            }
            foreach (var i in order)
            {
                if (starters[i] != null) continue;
                var best = BestFor(available, chosen, positions[i], false, r, c);
                if (best != null) { starters[i] = best; chosen.Add(best.Id); }
            }
        }

        /// <summary>The best unchosen player for a position (ties: lower id). naturalOnly: only players whose main position it is.</summary>
        private static Player BestFor(List<Player> available, HashSet<string> chosen, Position pos, bool naturalOnly, RatingTable r, SquadConfig c)
        {
            Player best = null; double bestV = double.MinValue;
            foreach (var p in available)
            {
                if (chosen.Contains(p.Id) || (naturalOnly && p.MainPosition != pos)) continue;
                double v = Effective(p, pos, r, c);
                if (v > bestV || (v == bestV && string.CompareOrdinal(p.Id, best.Id) < 0)) { best = p; bestV = v; }
            }
            return best;
        }

        /// <summary>The formation whose best 11 adds up to the most (ties: the first in squad.json).</summary>
        public static string BestFormation(List<Player> available, RatingTable r, GameData d)
        {
            var c = d.Squad;
            string best = null; double bestTotal = double.MinValue;
            foreach (var kv in c.Formations)
            {
                var positions = kv.Value.Select(SlotPosition).ToList();
                var starters = new Player[positions.Count];
                Fill(starters, positions, available, new HashSet<string>(), r, c);
                double total = 0;
                for (int i = 0; i < starters.Length; i++) total += starters[i] == null ? 30 : Effective(starters[i], positions[i], r, c);
                if (total > bestTotal + 1e-9) { bestTotal = total; best = kv.Key; }
            }
            return best;
        }

        /// <summary>Attack and defence ratings of a team sheet (agreed Oct 9). An empty slot counts as 30.</summary>
        public static (double attack, double defence, double overall) Strength(TeamSheet s, RatingTable r, SquadConfig c)
        {
            double aSum = 0, aW = 0, dSum = 0, dW = 0, all = 0;
            for (int i = 0; i < s.Slots.Count; i++)
            {
                var pos = s.SlotPositions[i];
                double v = s.Starters[i] == null ? 30 : Effective(s.Starters[i], pos, r, c);
                all += v;
                string line = c.Lines[pos.ToString()];
                if (c.AttackWeights.TryGetValue(line, out var aw)) { aSum += v * aw; aW += aw; }
                if (c.DefenceWeights.TryGetValue(line, out var dw)) { dSum += v * dw; dW += dw; }
            }
            return (aW == 0 ? 30 : aSum / aW, dW == 0 ? 30 : dSum / dW, all / Math.Max(1, s.Slots.Count));
        }

        /// <summary>AI mentality (agreed Oct 9): defensive as a clear underdog, attacking as a clear favourite.</summary>
        public static int AiMentality(double us, double them, SquadConfig c)
        {
            double gap = us - them;
            if (gap >= c.AiVeryMentalityGap) return 2;
            if (gap >= c.AiMentalityGap) return 1;
            if (gap <= -c.AiVeryMentalityGap) return -2;
            if (gap <= -c.AiMentalityGap) return -1;
            return 0;
        }
    }
}
