using System.Linq;
using LegendsFC.Core.Model;

namespace LegendsFC.Core.Rules
{
    public enum PositionFit { Natural, Comfortable, Cover, Related, Unrelated }

    public struct PositionFitResult
    {
        public PositionFit Fit;
        public bool WrongFoot;
        /// <summary>Total drop as a fraction (0.0-0.35). Applied to all attributes while he plays there (Oct 8).</summary>
        public double Drop;
    }

    /// <summary>
    /// Out-of-position rules (confirmed Oct 8): the archetype comes first (comfortable 0%, cover 5%),
    /// then the related map (10%), otherwise unrelated (30%). Wrong foot adds 5% unless the archetype is exempt.
    /// No position learning.
    /// </summary>
    public static class OutOfPosition
    {
        public static PositionFitResult Evaluate(Archetype archetype, Position main, Foot foot, Position target,
            PositionRules rules, OutOfPositionConfig c)
        {
            PositionFit fit;
            double drop;
            if (target == main) { fit = PositionFit.Natural; drop = 0; }
            else if (Matches(archetype.Comfortable, main, target)) { fit = PositionFit.Comfortable; drop = c.Comfortable; }
            else if (Matches(archetype.Cover, main, target)) { fit = PositionFit.Cover; drop = c.Cover; }
            else if (IsRelated(main, target, rules)) { fit = PositionFit.Related; drop = c.Related; }
            else { fit = PositionFit.Unrelated; drop = c.Unrelated; }

            bool wrongFoot = IsWrongFoot(foot, target) && !archetype.WrongFootExempt;
            if (wrongFoot) drop += c.WrongFoot;
            return new PositionFitResult { Fit = fit, WrongFoot = wrongFoot, Drop = drop };
        }

        public static bool IsWrongFoot(Foot foot, Position target)
        {
            var side = Positions.SideOf(target);
            if (side == Side.Central) return false;
            return (side == Side.Left) != (foot == Foot.Left);
        }

        public static bool IsRelated(Position main, Position target, PositionRules rules)
        {
            if (!rules.Related.TryGetValue(Positions.GroupOf(main).ToString(), out var tokens)) return false;
            return Matches(tokens, main, target);
        }

        private static bool Matches(System.Collections.Generic.IEnumerable<string> tokens, Position main, Position target)
            => tokens.Any(t => Positions.Resolve(t, main).Contains(target));
    }
}
