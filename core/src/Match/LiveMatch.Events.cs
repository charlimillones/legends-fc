using System;

namespace LegendsFC.Core.Match
{
    /// <summary>The match-event feed for on-screen banners (Oct 15). A ring of the last 32 events; no allocations.
    /// The client remembers the last EventCount it saw and reads the new ones each frame.</summary>
    public sealed partial class LiveMatch
    {
        private const int EventRing = 32;
        private readonly MatchEventInfo[] _events = new MatchEventInfo[EventRing];
        private int _eventCount;
        private int _woodworkTick = -1;

        /// <summary>Total events so far in this match (never resets).</summary>
        public int EventCount => _eventCount;

        /// <summary>One of the last 32 events: index in [max(0, EventCount - 32), EventCount - 1].</summary>
        public MatchEventInfo GetEvent(int index)
        {
            if (index < 0 || index >= _eventCount || index < _eventCount - EventRing)
                throw new ArgumentOutOfRangeException(nameof(index), "Only the last " + EventRing + " events are kept.");
            return _events[index % EventRing];
        }

        /// <summary>team: 0, 1 or 255 (none); player: index or -1.</summary>
        private void Emit(MatchEventKind kind, int team, int player)
        {
            _events[_eventCount % EventRing] = new MatchEventInfo
            {
                Kind = kind, Team = (byte)(team < 0 ? 255 : team), Minute = _minute, Player = (sbyte)(player < 0 ? -1 : player)
            };
            _eventCount++;
        }

        /// <summary>The post or bar was hit: one event per tick (post and bar in the same tick are one hit).</summary>
        private void EmitWoodwork()
        {
            if (_woodworkTick == _tick) return;
            _woodworkTick = _tick;
            int shooter = _shooter >= 0 && _shotAge < 4f && (_lastToucher == _shooter || _lastTouchDeflect) ? _shooter : _lastToucher;
            Emit(MatchEventKind.Woodwork, shooter >= 0 ? _p[shooter].Team : LastTouchTeam, shooter);
        }
    }
}
