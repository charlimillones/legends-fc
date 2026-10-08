using System.Collections.Generic;

namespace LegendsFC.Core.Rules
{
    /// <summary>data/config/out-of-position.json (confirmed Oct 8).</summary>
    public sealed class OutOfPositionConfig
    {
        public double Comfortable = 0.0;
        public double Cover = 0.05;
        public double Related = 0.10;
        public double Unrelated = 0.30;
        public double WrongFoot = 0.05;
    }

    /// <summary>data/config/probability.json: every shown probability is clamped and rounded (design principle).</summary>
    public sealed class ProbabilityConfig
    {
        public double Min = 0.05;
        public double Max = 0.95;
        public double DisplayStep = 0.05;
    }

    /// <summary>data/config/lucky-charm.json: luck = first / (1 - decay) * (1 - decay^n) (confirmed Oct 7).</summary>
    public sealed class LuckyCharmConfig
    {
        public double First = 0.10;
        public double Decay = 0.8;
    }

    /// <summary>data/config/position-ratings.json: weights per position (PROPOSAL v0, DaiVinci).</summary>
    public sealed class PositionRatingConfig
    {
        public Dictionary<string, Dictionary<string, double>> Weights = new Dictionary<string, Dictionary<string, double>>();
    }

    /// <summary>data/rules/positions.json: related positions per group, as position tokens.</summary>
    public sealed class PositionRules
    {
        public Dictionary<string, List<string>> Related = new Dictionary<string, List<string>>();
    }
}
