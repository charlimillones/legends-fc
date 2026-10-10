using System;
using LegendsFC.Core.Data;
using LegendsFC.Core.Season;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Saves
{
    /// <summary>
    /// One career in play: the world, its random sequence and its save slot. The UI calls AdvanceWeek; the session
    /// plays the week, runs the end of the season when it comes, and autosaves (Oct 9 proposal: after every week).
    /// Same seed and same choices give the same career, saved and reloaded or not.
    /// </summary>
    public sealed class GameSession
    {
        private readonly GameData _d;
        private readonly SaveStore _store;
        private readonly SeasonCalendar _calendar;
        private readonly SeasonCycle _cycle;

        public GameWorld World { get; private set; }
        public GameRandom Rng { get; private set; }
        public string SlotId { get; private set; }
        public string Name { get; private set; }
        /// <summary>The report of the season that just ended (null until one ends in this session).</summary>
        public SeasonReport LastSeason { get; private set; }
        /// <summary>Autosave after every week (saves.json); tools and tests can turn it off for speed.</summary>
        public bool Autosave { get; set; }

        private GameSession(GameData d, SaveStore store)
        {
            _d = d; _store = store;
            _calendar = new SeasonCalendar(d); _cycle = new SeasonCycle(d);
            Autosave = d.Saves?.AutosaveEveryWeek ?? true;
        }

        /// <summary>A new world in the first free slot (null if all slots are used). The user's club is picked afterwards.</summary>
        public static GameSession NewWorld(GameData d, SaveStore store, string name, ulong seed, string currencyCode, string nowUtc)
        {
            string slot = store.FreeSlot();
            if (slot == null) return null;
            var s = new GameSession(d, store)
            {
                World = new WorldGenerator(d).Generate(seed, currencyCode),
                Rng = new GameRandom(seed ^ 0x5EA5_0DA7_C0FF_EE01UL),   // its own stream, independent of world generation
                SlotId = slot, Name = string.IsNullOrWhiteSpace(name) ? "World " + slot.Substring(1) : name,
            };
            s.Save(nowUtc);
            return s;
        }

        public static GameSession Load(GameData d, SaveStore store, string slot)
        {
            var file = store.Load(slot);
            return new GameSession(d, store)
            {
                World = file.World, Rng = GameRandom.FromState(file.Rng), SlotId = slot, Name = file.Summary?.Name ?? slot,
            };
        }

        public void PickClub(string clubId, string nowUtc)
        {
            if (World.Clubs.TrueForAll(c => c.Id != clubId)) throw new ArgumentException("Unknown club " + clubId, nameof(clubId));
            World.UserClubId = clubId;
            World.Market.SquadsChanged();
            Save(nowUtc);
        }

        /// <summary>Plays one week (the end of the season runs after week 52), then autosaves.</summary>
        public WeekReport AdvanceWeek(string nowUtc)
        {
            if (World.Calendar.Week == 0 || World.Calendar.Runs.Count == 0) _calendar.Start(World, Rng);
            var report = _calendar.PlayWeek(World, Rng);
            if (World.Calendar.SeasonOver) LastSeason = _cycle.EndSeason(World, Rng, _calendar.Outcomes(World));
            if (Autosave) Save(nowUtc);
            return report;
        }

        public void Save(string nowUtc) => _store?.Save(SlotId, World, Rng, Name, nowUtc);
    }
}
