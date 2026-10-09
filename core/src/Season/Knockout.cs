using LegendsFC.Core.Util;

namespace LegendsFC.Core.Season
{
    public enum DrawRule { Penalties, BetterSeedAdvances }

    /// <summary>One knockout tie played as a single match. "Seed" ranks the clubs: lower = better.</summary>
    public static class Knockout
    {
        public static string Play(string a, int seedA, string b, int seedB, DrawRule rule, double penaltyHomeWinChance,
            System.Func<string, double> strength, MatchSimConfig c, GameRandom rng, out MatchResult result)
        {
            // Better seed plays at home (with no home advantage configured this only matters for the record).
            bool aHome = seedA <= seedB;
            string home = aHome ? a : b, away = aHome ? b : a;
            result = MatchSim.Play(home, away, strength(home), strength(away), c, rng);
            if (result.HomeGoals > result.AwayGoals) return home;
            if (result.AwayGoals > result.HomeGoals) return away;
            if (rule == DrawRule.BetterSeedAdvances) return seedA <= seedB ? a : b;
            return rng.Chance(penaltyHomeWinChance) ? home : away; // penalties (coin flip until a shootout model exists)
        }
    }
}
