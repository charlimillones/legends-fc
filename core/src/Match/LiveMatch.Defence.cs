using System;

namespace LegendsFC.Core.Match
{
    /// <summary>
    /// The defending team's shape (Carlos Oct 15 playtest: "The defense is too open when the other player is attacking").
    /// Same code for both teams; the user's controlled player is never moved by it.
    /// </summary>
    public sealed partial class LiveMatch
    {
        private readonly int[] _markOf = new int[N];
        private readonly int[] _markTick = { -1, -1 };
        private readonly int[] _danger = new int[11];
        private readonly float[] _dangerScore = new float[11];

        /// <summary>How fully team t's block is formed (0 in the opponent's half, 1 deep in its own), from where its AI reads
        /// the ball.</summary>
        private float CompactWeight(Vec2 ballFrame)
        {
            var d = _c.Defence;
            return MathUtil.Clamp01((d.CompactFromX - ballFrame.X) / Math.Max(0.1f, d.BlendMetres));
        }

        /// <summary>The back line's x in team t's frame: one line behind the ball, never deeper than DeepestLine.</summary>
        private float BackLineX(Vec2 ballFrame) => Math.Max(_c.Defence.DeepestLine, Math.Min(ballFrame.X - _c.Defence.LineBehindBall, _c.Ai.BackLineMax));

        /// <summary>
        /// The block's zonal spot for player i in his team's frame: the back line on one line, the midfield line LineGap in
        /// front of it, both narrowed and shifted to the ball's side. Forwards keep their usual spot.
        /// </summary>
        private Vec2 CompactSpot(int i, Vec2 shape, Vec2 ballFrame)
        {
            var p = _p[i];
            var d = _c.Defence;
            if (p.Line == 3) return shape;
            float line = BackLineX(ballFrame);
            float x = p.Line == 1 ? line : line + d.LineGap;
            float y = p.Base.Y * d.CompactWidth + ballFrame.Y * d.BallSideShift;
            return new Vec2(x, y);
        }

        /// <summary>
        /// Team t is defending: who man-marks the opponents' most dangerous runners (in the box, at or behind the back line,
        /// or running at goal from deep). Greedy: the most dangerous runner first, the nearest free defender or midfielder;
        /// defenders count extra distance for a runner still well in front of the line, so midfielders track runners from
        /// deep. The presser, the cover, forwards, the keeper and the user's player are never assigned. Once per tick.
        /// </summary>
        private void AssignMarks(int t, int presser, int cover)
        {
            var d = _c.Defence;
            _markTick[t] = _tick;
            for (int s = 0; s < 11; s++) _markOf[t * 11 + s] = -1;
            var bf = ToFrame(t, PerceivedBall(t) + _readNoise[t]);
            if (CompactWeight(bf) <= 0) return;
            float line = BackLineX(bf);
            int own = -Dir(t);
            var goal = new Vec2(own * HalfX, 0);
            // the dangerous runners, most dangerous first
            int nd = 0;
            for (int s = 0; s < 11; s++)
            {
                int j = (1 - t) * 11 + s;
                var o = _p[j];
                if (o.Keeper || j == _owner) continue;
                var of = ToFrame(t, o.Pos);
                float run = -Vec2.Dot(ToFrame(t, o.Vel), new Vec2(1, 0));   // speed towards our goal
                bool box = InOwnBox(t, o.Pos, 0f);
                bool behind = of.X <= line + d.BehindLineMargin;
                bool runner = run > d.RunnerSpeed && of.X <= line + d.RunnerZone;
                if (!box && !behind && !runner) continue;
                float score = -Vec2.Distance(o.Pos, goal) + (box ? d.BoxBonus : 0f) + (runner ? d.RunnerBonus : 0f);
                int at = nd++;
                while (at > 0 && _dangerScore[at - 1] < score) { _danger[at] = _danger[at - 1]; _dangerScore[at] = _dangerScore[at - 1]; at--; }
                _danger[at] = j; _dangerScore[at] = score;
            }
            if (nd > d.MarkRunners) nd = d.MarkRunners;
            for (int k = 0; k < nd; k++)
            {
                int j = _danger[k];
                var o = _p[j];
                bool deep = ToFrame(t, o.Pos).X > line + d.DeepRunnerGap;
                int best = -1; float bc = d.PickupRadius;
                for (int s = 0; s < 11; s++)
                {
                    int i = t * 11 + s;
                    var p = _p[i];
                    if (p.Keeper || p.Line == 3 || i == presser || i == cover || i == _controlled || _markOf[i] >= 0) continue;
                    float c = Vec2.Distance(p.Pos, o.Pos) + (deep && p.Line == 1 ? d.DeepRunnerBias : 0f);
                    if (c < bc) { bc = c; best = i; }
                }
                if (best >= 0) _markOf[best] = j;
            }
        }

        /// <summary>Player i's man-marking target this tick (or -1): goal-side of the runner, a little ahead of his run.</summary>
        private bool ManMark(int i, out Vec2 spot)
        {
            spot = Vec2.Zero;
            var p = _p[i];
            int t = p.Team;
            if (_markTick[t] != _tick || _markOf[i] < 0) return false;
            var o = _p[_markOf[i]];
            var at = o.Pos + o.Vel * _c.Defence.MarkLeadSeconds;
            var goal = new Vec2(-Dir(t) * HalfX, 0);
            spot = at + (goal - at).Normalized() * _c.Ai.MarkGoalSide;
            return true;
        }

        /// <summary>
        /// The size of team t's defensive block now: depth (along the pitch) and width (across) of its defenders and
        /// midfielders, or of all ten outfield players with includeForwards. Read-only, for tests, tuning and a debug overlay.
        /// </summary>
        public void BlockSize(int team, bool includeForwards, out float depth, out float width)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int s = 0; s < 11; s++)
            {
                var p = _p[team * 11 + s];
                if (p.Keeper || (!includeForwards && p.Line == 3)) continue;
                if (p.Pos.X < minX) minX = p.Pos.X;
                if (p.Pos.X > maxX) maxX = p.Pos.X;
                if (p.Pos.Y < minY) minY = p.Pos.Y;
                if (p.Pos.Y > maxY) maxY = p.Pos.Y;
            }
            depth = maxX > minX ? maxX - minX : 0;
            width = maxY > minY ? maxY - minY : 0;
        }
    }
}
