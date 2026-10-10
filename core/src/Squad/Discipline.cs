using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Season;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Squad
{
    /// <summary>
    /// Suspensions by each competition's real rules (Carlos, Oct 9; data/rules/discipline.json): yellow-card bans count in
    /// their own competition; red-card bans in the competition, every competition of the country, or every continental cup
    /// of the confederation. Yellows are wiped as the rules say and at the end of each season; bans carry over.
    /// </summary>
    public static class Discipline
    {
        public const string Competition = "competition", Domestic = "domestic", Confederation = "confederation";

        /// <summary>The country and confederation a competition belongs to (null when it has none).</summary>
        public static (string country, string confed) Home(string competitionId, GameData d)
        {
            var league = d.Competitions.FirstOrDefault(c => c.Id == competitionId);
            if (league != null) return (league.CountryId, null);   // leagues are domestic, never part of a continental ban
            var cup = d.Cups.Cups.FirstOrDefault(c => c.Id == competitionId);
            if (cup == null) return (null, null);
            if (cup.CountryId != null) return (cup.CountryId, null);
            return (null, cup.Confederation);
        }

        public static bool Applies(Ban b, string competitionId, GameData d)
        {
            if (b.MatchesLeft <= 0) return false;
            if (b.Scope == Competition) return b.Key == competitionId;
            var (country, confed) = Home(competitionId, d);
            if (b.Scope == Domestic) return country != null && b.Key == country;
            if (b.Scope == Confederation) return confed != null && b.Key == confed;
            return false;
        }

        public static bool IsSuspended(GameWorld w, Player p, string competitionId, GameData d) => p.Bans.Any(b => Applies(b, competitionId, d));

        /// <summary>After a club's match: every player who was banned for it has served one match.</summary>
        public static void Serve(IEnumerable<Player> squad, string competitionId, GameData d)
        {
            foreach (var p in squad)
            {
                bool served = false;
                foreach (var b in p.Bans) if (Applies(b, competitionId, d)) { b.MatchesLeft--; served = true; }
                if (served) p.Bans.RemoveAll(b => b.MatchesLeft <= 0);
            }
        }

        /// <summary>A yellow card. Returns the ban it caused (null if none).</summary>
        public static Ban Yellow(Player p, string competitionId, int clubMatchesPlayed, GameData d)
        {
            p.Yellows.TryGetValue(competitionId, out int n);
            p.Yellows[competitionId] = ++n;
            var rule = d.Discipline.For(competitionId);
            if (rule == null) return null;
            int ban = 0;
            if (rule.Every > 0) { if (n % rule.Every == 0) ban = 1; }
            else foreach (var t in rule.Thresholds)
                    if (t.At == n && (t.ByRound == null || clubMatchesPlayed <= t.ByRound)) ban = t.Ban;
            if (ban == 0) return null;
            var b = new Ban { Scope = Competition, Key = competitionId, MatchesLeft = ban, Reason = n + " yellow cards" };
            p.Bans.Add(b);
            return b;
        }

        /// <summary>A red card (a second yellow is 1 match; a straight red 1-3 by type). Served where the competition's rules say.</summary>
        public static Ban Red(Player p, string competitionId, bool secondYellow, GameRandom rng, GameData d)
        {
            var reds = d.Discipline.RedCards;
            int matches = reds.SecondYellowBan; string reason = "sent off (two yellows)";
            if (!secondYellow && reds.Straight.Count > 0)
            {
                double x = rng.NextDouble() * reds.Straight.Sum(s => s.Share), acc = 0;
                var type = reds.Straight.Last();
                foreach (var s in reds.Straight) { acc += s.Share; if (x < acc) { type = s; break; } }
                matches = type.Ban; reason = "sent off (" + type.Name + ")";
            }
            var rule = d.Discipline.For(competitionId);
            string scope = rule?.RedScope ?? Competition;
            var (country, confed) = Home(competitionId, d);
            string key = scope == Domestic ? country : scope == Confederation ? confed : competitionId;
            if (key == null) { scope = Competition; key = competitionId; }
            var b = new Ban { Scope = scope, Key = key, MatchesLeft = matches, Reason = reason };
            p.Bans.Add(b);
            return b;
        }

        /// <summary>Before a round: wipe yellows when the competition's rules say (a new stage, or the playoffs in Argentina).</summary>
        public static void BeforeRound(GameWorld w, CompetitionRun run, Round round, GameData d)
        {
            var rule = d.Discipline.For(run.CompetitionId);
            if (rule == null || round == null) return;
            string previous = run.PlayedStages.Count == 0 ? null : run.PlayedStages[run.PlayedStages.Count - 1];
            if (previous == null || previous == round.Stage) return;
            // Wiped once, before the first of the listed stages that the competition reaches.
            bool wipe = rule.ResetOnStageChange || (rule.ResetBeforeStages.Contains(round.Stage) && !run.PlayedStages.Any(rule.ResetBeforeStages.Contains));
            if (!wipe) return;
            foreach (var p in w.Players) p.Yellows.Remove(run.CompetitionId);
        }

        /// <summary>End of season: every yellow is wiped; bans carry over.</summary>
        public static void SeasonEnd(GameWorld w)
        {
            foreach (var p in w.Players) p.Yellows.Clear();
        }
    }
}
