using System;
using LegendsFC.Core.Util;

namespace LegendsFC.Core.Season
{
    /// <summary>data/config/match-sim.json.</summary>
    public sealed class MatchSimConfig
    {
        public double BaseGoals = 1.35, StrengthFactor = 0.05, HomeAdvantage = 0.0, ExtraTimeShare = 1.0 / 3;
        public int PointsWin = 3, PointsDraw = 1, StartingElevenSize = 11;
    }

    public struct MatchResult
    {
        public string Home, Away;
        public int HomeGoals, AwayGoals;
        /// <summary>Knockout matches level after 90 minutes: who won the shootout (null otherwise).</summary>
        public string PenaltyWinner;
        /// <summary>The match went to extra time (goals include it).</summary>
        public bool AfterExtraTime;
        /// <summary>Goals, cards, injuries and substitutions (sim mode, Oct 9).</summary>
        public System.Collections.Generic.List<Squad.MatchEvent> Events;
        public string PlayerOfTheMatch;
        /// <summary>Each player's match (kept in his stats; not saved with the result).</summary>
        [Newtonsoft.Json.JsonIgnore] public System.Collections.Generic.List<Squad.PlayerMatch> Players;
    }

    /// <summary>Sim-mode results (approved Oct 8). Same model for every club; the user's club gets no help.</summary>
    public static class MatchSim
    {
        public static MatchResult Play(string home, string away, double homeStrength, double awayStrength, MatchSimConfig c, GameRandom rng)
        {
            double d = homeStrength - awayStrength + c.HomeAdvantage;
            return new MatchResult
            {
                Home = home, Away = away,
                HomeGoals = Poisson(c.BaseGoals * Math.Exp(c.StrengthFactor * d), rng),
                AwayGoals = Poisson(c.BaseGoals * Math.Exp(-c.StrengthFactor * d), rng),
            };
        }

        /// <summary>Extra time (Oct 9): 30 minutes = a third of the 90-minute scoring rate.</summary>
        public static MatchResult ExtraTime(string home, string away, double homeStrength, double awayStrength, MatchSimConfig c, GameRandom rng)
        {
            double d = homeStrength - awayStrength + c.HomeAdvantage;
            return new MatchResult
            {
                Home = home, Away = away,
                HomeGoals = Poisson(c.BaseGoals * c.ExtraTimeShare * Math.Exp(c.StrengthFactor * d), rng),
                AwayGoals = Poisson(c.BaseGoals * c.ExtraTimeShare * Math.Exp(-c.StrengthFactor * d), rng),
            };
        }

        private static int Poisson(double lambda, GameRandom rng)
        {
            double l = Math.Exp(-lambda), p = 1.0;
            int k = 0;
            while (true) { p *= rng.NextDouble(); if (p < l) return k; k++; }
        }
    }
}
