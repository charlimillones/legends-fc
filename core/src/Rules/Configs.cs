using System.Linq;
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

        [Newtonsoft.Json.JsonIgnore] private Dictionary<string, (int attr, double weight)[]> _compiled;
        /// <summary>The weights as (attribute index, weight) arrays, built once (rating is computed millions of times a season).</summary>
        public (int attr, double weight)[] Compiled(string key)
        {
            var c = _compiled;
            if (c == null)
            {
                c = new Dictionary<string, (int, double)[]>();
                foreach (var kv in Weights)
                    c[kv.Key] = kv.Value.Select(x => ((int)(Model.Attr)System.Enum.Parse(typeof(Model.Attr), x.Key), x.Value)).ToArray();
                _compiled = c;   // built whole, then published: safe if two threads race
            }
            return c.TryGetValue(key, out var w) ? w : null;
        }
    }

    /// <summary>data/rules/positions.json: related positions per group, as position tokens.</summary>
    public sealed class PositionRules
    {
        public Dictionary<string, List<string>> Related = new Dictionary<string, List<string>>();
    }
}
