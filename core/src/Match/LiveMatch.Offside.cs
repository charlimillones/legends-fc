using System;

namespace LegendsFC.Core.Match
{
    /// <summary>
    /// Offside (Carlos Oct 15: standard rule, judged when the pass is played, indirect free kick restart; same rule for both
    /// teams, the user's player included). When a player plays the ball, his outfield teammates in an offside position are
    /// recorded. One of them becomes the next to touch the ball before an opponent deliberately plays it = offside.
    /// </summary>
    public sealed partial class LiveMatch
    {
        private readonly bool[] _offside = new bool[N];
        /// <summary>The team whose players are recorded, or -1 when there is no record.</summary>
        private int _offsideTeam = -1;

        private void ClearOffside()
        {
            _offsideTeam = -1;
            for (int i = 0; i < N; i++) _offside[i] = false;
        }

        /// <summary>The second-last opponent of team t, in t's frame (x towards the opponent's goal). Keepers count.</summary>
        private float SecondLastOpponent(int t)
        {
            float m1 = float.MinValue, m2 = float.MinValue;
            int d = Dir(t);
            for (int s = 0; s < 11; s++)
            {
                float x = _p[(1 - t) * 11 + s].Pos.X * d;
                if (x > m1) { m2 = m1; m1 = x; } else if (x > m2) m2 = x;
            }
            return m2;
        }

        /// <summary>Is player j in an offside position now: in the opponents' half and nearer their goal line than both the
        /// ball and the second-last opponent (level is onside)? ballX and secondLast are in j's team frame.</summary>
        private bool OffsidePosition(int j, float ballX, float secondLast)
        {
            var p = _p[j];
            if (p.Keeper) return false;
            float x = p.Pos.X * Dir(p.Team);
            float tol = _c.Offside.Tolerance;
            return x > 0f && x > ballX + tol && x > secondLast + tol;
        }

        /// <summary>Player i played the ball: a new record of his teammates in an offside position.</summary>
        private void RecordOffside(int i)
        {
            int t = _p[i].Team;
            _offsideTeam = t;
            float bx = _ballPos.X * Dir(t);
            float sl = SecondLastOpponent(t);
            for (int j = 0; j < N; j++) _offside[j] = j != i && _p[j].Team == t && OffsidePosition(j, bx, sl);
        }

        /// <summary>Player i touched the ball (after the offence check). A deliberate play (gaining possession or kicking it)
        /// always starts a new record; a touch by a teammate of the recorded team does too; a save, block or deflection off
        /// an opponent leaves the record as it is.</summary>
        private void OffsideTouch(int i, bool deliberate)
        {
            if (deliberate || _offsideTeam == _p[i].Team) RecordOffside(i);
        }

        /// <summary>Is player i's touch an offside offence? If so play stops for the free kick and this returns true.</summary>
        private bool OffsideCheck(int i)
        {
            if (_offsideTeam < 0 || _p[i].Team != _offsideTeam || !_offside[i]) return false;
            CallOffside(i);
            return true;
        }

        /// <summary>Indirect free kick to the defending team where the offending player touched the ball.</summary>
        private void CallOffside(int i)
        {
            var p = _p[i];
            int defending = 1 - p.Team;
            _counters.Offsides++;
            Emit(MatchEventKind.Offside, p.Team, i);
            var spot = ClampInside(p.Pos, 0.5f);
            ClearBallFlags();
            _counters.FreeKicks++;
            BeginRestart(Phase.FreeKick, defending, spot);
            Emit(MatchEventKind.FreeKick, defending, _taker);
        }

        /// <summary>The AI's view: would a pass from team t's carrier to teammate j now be to a player in an offside position?
        /// Not at throw-ins, goal kicks and corners.</summary>
        private bool AiOffsideTarget(int t, int j)
        {
            if (_phase == Phase.ThrowIn || _phase == Phase.GoalKick || _phase == Phase.Corner) return false;
            return OffsidePosition(j, _ballPos.X * Dir(t), SecondLastOpponent(t));
        }
    }
}
