using System;
using System.Collections.Generic;

namespace LegendsFC.Core.Model
{
    /// <summary>The 12 player positions (confirmed Oct 7). Wing-backs are a formation slot only.</summary>
    public enum Position { GK, CB, LB, RB, DM, CM, AM, LM, RM, LW, RW, ST }

    public enum Side { Central, Left, Right }

    public enum Foot { Left, Right }

    /// <summary>Position groups used by archetypes and the related-position map.</summary>
    public enum PositionGroup { GK, CB, FB, DM, CM, AM, WM, W, ST }

    public static class Positions
    {
        public static readonly Position[] All = (Position[])Enum.GetValues(typeof(Position));

        public static Side SideOf(Position p)
        {
            switch (p)
            {
                case Position.LB: case Position.LM: case Position.LW: return Side.Left;
                case Position.RB: case Position.RM: case Position.RW: return Side.Right;
                default: return Side.Central;
            }
        }

        public static bool IsOutfield(Position p) => p != Position.GK;

        public static PositionGroup GroupOf(Position p)
        {
            switch (p)
            {
                case Position.GK: return PositionGroup.GK;
                case Position.CB: return PositionGroup.CB;
                case Position.LB: case Position.RB: return PositionGroup.FB;
                case Position.DM: return PositionGroup.DM;
                case Position.CM: return PositionGroup.CM;
                case Position.AM: return PositionGroup.AM;
                case Position.LM: case Position.RM: return PositionGroup.WM;
                case Position.LW: case Position.RW: return PositionGroup.W;
                default: return PositionGroup.ST;
            }
        }

        /// <summary>
        /// Resolves a position token from the data files into concrete positions, relative to a player's main position.
        /// Plain positions ("CB", "GK") resolve to themselves.
        /// Group tokens "FB", "WM", "W" mean the same side when the main position is on a side, and both sides when it is central.
        /// "OPP_FB", "OPP_WM", "OPP_W" mean the opposite side.
        /// </summary>
        public static IReadOnlyList<Position> Resolve(string token, Position main)
        {
            var side = SideOf(main);
            switch (token)
            {
                case "FB": return Sided(Position.LB, Position.RB, side, false);
                case "WM": return Sided(Position.LM, Position.RM, side, false);
                case "W": return Sided(Position.LW, Position.RW, side, false);
                case "OPP_FB": return Sided(Position.LB, Position.RB, side, true);
                case "OPP_WM": return Sided(Position.LM, Position.RM, side, true);
                case "OPP_W": return Sided(Position.LW, Position.RW, side, true);
            }
            if (Enum.TryParse(token, out Position p) && Enum.IsDefined(typeof(Position), p)) return new[] { p };
            throw new ArgumentException("Unknown position token: " + token);
        }

        public static bool IsValidToken(string token)
        {
            try { Resolve(token, Position.LB); return true; } catch (ArgumentException) { return false; }
        }

        private static Position[] Sided(Position left, Position right, Side side, bool opposite)
        {
            if (side == Side.Central) return opposite ? new Position[0] : new[] { left, right };
            bool wantLeft = (side == Side.Left) != opposite;
            return new[] { wantLeft ? left : right };
        }
    }
}
