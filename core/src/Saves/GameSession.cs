using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>Manager messages delivered with the last week (at most 2).</summary>
        public List<Inbox.InboxMessage> NewMessages { get; private set; } = new List<Inbox.InboxMessage>();

        /// <summary>Plays one week (the end of the season runs after week 52), delivers the inbox, then autosaves.</summary>
        public WeekReport AdvanceWeek(string nowUtc)
        {
            var before = Inbox.InboxEngine.Snapshot(World);
            if (World.Calendar.Week == 0 || World.Calendar.Runs.Count == 0) _calendar.Start(World, Rng);
            var report = _calendar.PlayWeek(World, Rng);
            var played = World.Calendar;   // the season-end reset replaces it
            if (World.Calendar.SeasonOver) LastSeason = _cycle.EndSeason(World, Rng, _calendar.Outcomes(World));
            NewMessages = Inbox.InboxEngine.AfterWeek(World, before, report, played, _d);
            if (Autosave) Save(nowUtc);
            return report;
        }

        // ---- the user's actions between weeks (same rules as the AI)

        public Model.Club UserClub => World.Clubs.FirstOrDefault(c => c.Id == World.UserClubId);

        /// <summary>Instant upgrade at the fixed price (Oct 9). The manager thanks you in next week's messages.</summary>
        public Facilities.UpgradeResult UpgradeFacility(Model.Facility f)
        {
            var club = UserClub;
            var r = Facilities.FacilityRules.Upgrade(club, f, _d.FacilityRules);
            if (r == Facilities.UpgradeResult.Done) Inbox.InboxEngine.QueueUpgrade(World, club, f, _d);
            return r;
        }

        public bool RepairFacility(Model.Facility f)
        {
            var club = UserClub;
            bool needed = club.Facilities[f].Condition < 100;
            bool ok = Facilities.FacilityRules.Repair(club, f, _d.FacilityRules);
            if (ok && needed) Inbox.InboxEngine.Queue(World, "MSG-REPAIRED", f, _d, ("facility", _d.FacilityName(f)));
            return ok;
        }

        /// <summary>Repair all: pays every repair at once (or nothing). One manager thanks you for the biggest job.</summary>
        public bool RepairAll()
        {
            var club = UserClub;
            var worst = club.Facilities.OrderBy(kv => kv.Value.Condition).ThenBy(kv => kv.Key).First();
            bool needed = worst.Value.Condition < 100;
            bool ok = Facilities.FacilityRules.RepairAll(club, _d.FacilityRules);
            if (ok && needed) Inbox.InboxEngine.Queue(World, "MSG-REPAIRED", worst.Key, _d, ("facility", _d.FacilityName(worst.Key)));
            return ok;
        }

        // ---- squad and tactics (agreed Oct 9)

        private List<Model.Player> MySquad => Transfers.Market.Squad(World, World.UserClubId);
        private Model.Player Mine(string playerId) => MySquad.FirstOrDefault(p => p.Id == playerId) ?? throw new ArgumentException("Not in your squad: " + playerId);

        public void SetFormation(string formation)
        {
            if (!_d.Squad.Formations.ContainsKey(formation)) throw new ArgumentException("Unknown formation " + formation);
            var t = UserClub.Tactics;
            if (t.Formation != formation) { t.Formation = formation; t.Lineup.Clear(); }   // slots changed: pick again (auto-pick fills it)
        }

        /// <summary>-2 very defensive ... +2 very attacking.</summary>
        public void SetMentality(int mentality) => UserClub.Tactics.Mentality = Math.Max(-2, Math.Min(2, mentality));

        /// <summary>11 player ids in the formation's slot order, and up to 9 on the bench. Unavailable players are replaced at kick-off.</summary>
        public void SetLineup(IList<string> starters, IList<string> bench)
        {
            int slots = _d.Squad.Formations[UserClub.Tactics.Formation].Count;
            if (starters.Count != slots) throw new ArgumentException($"The formation has {slots} slots.");
            if (bench.Count > _d.Squad.Bench) throw new ArgumentException($"At most {_d.Squad.Bench} on the bench.");
            var all = starters.Concat(bench).ToList();
            if (all.Distinct().Count() != all.Count) throw new ArgumentException("A player can only be picked once.");
            foreach (var id in all) Mine(id);
            UserClub.Tactics.Lineup = starters.ToList();
            UserClub.Tactics.Bench = bench.ToList();
        }

        /// <summary>The auto-pick button: the AI's choice for the user's formation (or the best formation if keepFormation is false).</summary>
        public LegendsFC.Core.Squad.TeamSheet AutoPick(string competitionId, bool keepFormation = true)
        {
            var club = UserClub; var t = club.Tactics;
            var r = new LegendsFC.Core.Squad.RatingTable(_d);
            if (!keepFormation)
                t.Formation = LegendsFC.Core.Squad.Lineups.BestFormation(MySquad.Where(p => LegendsFC.Core.Squad.Lineups.Available(World, p, competitionId, _d)).ToList(), r, _d);
            t.Lineup.Clear(); t.Bench.Clear();
            var sheet = LegendsFC.Core.Squad.Lineups.Pick(World, club, competitionId, r, _d);
            t.Lineup = sheet.Starters.Select(p => p?.Id).ToList();
            t.Bench = sheet.Bench.Select(p => p.Id).ToList();
            return sheet;
        }

        public void SetCaptainAndTakers(string captain, string penalties, string freeKicks, string corners)
        {
            foreach (var id in new[] { captain, penalties, freeKicks, corners }.Where(x => x != null)) Mine(id);
            var t = UserClub.Tactics;
            t.Captain = captain; t.PenaltyTaker = penalties; t.FreeKickTaker = freeKicks; t.CornerTaker = corners;
        }

        /// <summary>Saves the current formation, mentality and team under a name (up to 5; the same name replaces).</summary>
        public bool SaveLineup(string name)
        {
            var t = UserClub.Tactics;
            t.Saved.RemoveAll(x => x.Name == name);
            if (t.Saved.Count >= _d.Squad.SavedLineups) return false;
            t.Saved.Add(new Model.SavedLineup { Name = name, Formation = t.Formation, Mentality = t.Mentality, Lineup = t.Lineup.ToList(), Bench = t.Bench.ToList() });
            return true;
        }

        public bool LoadLineup(string name)
        {
            var t = UserClub.Tactics; var s = t.Saved.FirstOrDefault(x => x.Name == name);
            if (s == null) return false;
            t.Formation = s.Formation; t.Mentality = s.Mentality; t.Lineup = s.Lineup.ToList(); t.Bench = s.Bench.ToList();
            return true;
        }

        public void DeleteLineup(string name) => UserClub.Tactics.Saved.RemoveAll(x => x.Name == name);

        /// <summary>Training regime per player (light rests fully; heavier grows faster but tires).</summary>
        public void SetRegime(string playerId, string regime)
        {
            if (!_d.Development.Regimes.ContainsKey(regime)) throw new ArgumentException("Unknown regime " + regime);
            Mine(playerId).Regime = regime;
        }

        // ---- coaches (Oct 9 night; rules decided Oct 7)

        public List<Model.Coach> MyCoaches => LegendsFC.Core.Squad.Coaching.OfClub(World, World.UserClubId);
        public List<Model.Coach> FreeCoaches => World.Coaches.Where(c => c.ClubId == null).OrderByDescending(c => c.Rating).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        public int CoachLimit => LegendsFC.Core.Squad.Coaching.Limit(UserClub, _d);
        public long CoachPrice(string coachId, int seasons) => LegendsFC.Core.Squad.Coaching.Price(Coach(coachId), UserClub, seasons, _d);
        private Model.Coach Coach(string id) => World.Coaches.FirstOrDefault(c => c.Id == id) ?? throw new ArgumentException("Unknown coach " + id);

        public LegendsFC.Core.Squad.HireResult HireCoach(string coachId, int seasons)
        {
            var coach = Coach(coachId);
            var r = LegendsFC.Core.Squad.Coaching.Hire(World, UserClub, coach, seasons, _d);
            if (r == LegendsFC.Core.Squad.HireResult.Done)
                Inbox.InboxEngine.Queue(World, "MSG-COACH-SIGNED", Model.Facility.TrainingGround, _d, ("coach", coach.Name), ("position", LegendsFC.Core.Squad.Coaching.GroupName(coach.Group)));
            return r;
        }

        public bool RenewCoach(string coachId, int seasons) => LegendsFC.Core.Squad.Coaching.Renew(World, UserClub, Coach(coachId), seasons, _d);

        public void ReleaseCoach(string coachId)
        {
            var coach = Coach(coachId);
            if (coach.ClubId != World.UserClubId) throw new ArgumentException("Not your coach.");
            LegendsFC.Core.Squad.Coaching.Release(World, coach);
        }

        /// <summary>Who trains a player (null coach = he develops naturally). A coach trains only his group.</summary>
        public bool AssignCoach(string playerId, string coachId) => LegendsFC.Core.Squad.Coaching.Assign(World, Mine(playerId), coachId == null ? null : Coach(coachId));

        public void AutoAssignCoaches() => LegendsFC.Core.Squad.Coaching.AutoAssign(World, UserClub);

        public void Save(string nowUtc) => _store?.Save(SlotId, World, Rng, Name, nowUtc);
    }
}
