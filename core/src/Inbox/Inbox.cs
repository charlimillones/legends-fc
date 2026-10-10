using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Season;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Inbox
{
    /// <summary>data/text/manager-messages.json: 45 triggers × 5 wordings (accepted Oct 9).</summary>
    public sealed class MessageBank
    {
        public Dictionary<string, string> Roles = new Dictionary<string, string>();
        public List<MessageTrigger> Triggers = new List<MessageTrigger>();
        public MessageTrigger Get(string id) => Triggers.First(t => t.Id == id);
    }

    public sealed class MessageTrigger
    {
        public string Id, Name, Sender, WaitsFor;
        public int Priority = 4;
        public bool Repeatable;
        public List<string> Wordings = new List<string>();
        public bool Live => WaitsFor == null;
    }

    /// <summary>data/config/inbox.json (PROPOSAL thresholds for the triggers).</summary>
    public sealed class InboxConfig
    {
        public int ManagerMessagesPerWeek = 2, MaxWaitWeeks = 2;
        public double AttendanceRandomMin = 0.85, AttendanceRandomMax = 1.15, LowAttendanceBelow = 0.6;
        public int StallTrainingWeeks = 12, StallMaxAge = 29;
        public int GoodWeekPlayers = 5, BigWinMargin = 3, BadRunMatches = 5;
        public int YoungsterMaxAge = 21, YoungsterReadyRank = 18, StarSigningTopRank = 3;
        public int ShirtsPerReputationMin = 20, ShirtsPerReputationMax = 60, WeeksPerMonth = 4;
        public int TooManyInjuries = 4, HeavyTooLongWeeks = 6, CoachContractReminderWeek = 40;
        /// <summary>The academy director warns this week when the squad has no room for a full intake (Carlos, Oct 10).</summary>
        public int AcademyRoomWarningWeek = 46;
        public double InjuryRiskBelowEnergy = 50;
        /// <summary>Messages kept: the newest 100; open decisions always stay (Carlos, Oct 10).</summary>
        public int MaxMessages = 100;
    }

    public enum MessageKind { Manager, Club, Decision }

    public sealed class InboxMessage
    {
        public int Id;
        public MessageKind Kind = MessageKind.Manager;
        public string TriggerId, Text;
        public Facility Sender;
        /// <summary>The sender's name and job, e.g. "Ana Ruiz, groundskeeper".</summary>
        public string From;
        public int Priority, SeasonStartYear, Week;
        public bool Read;
        /// <summary>Decision messages: the choices, answered with GameSession.Decide(DecisionId, option).</summary>
        public int DecisionId;
        public List<string> Options;
    }

    /// <summary>A message waiting for a free slot (at most 2 a week, by priority).</summary>
    public sealed class PendingMessage
    {
        public string TriggerId;
        public Facility Sender;
        public Dictionary<string, string> Values = new Dictionary<string, string>();
        public int Priority, Order, CreatedTick;
    }

    /// <summary>The user's inbox and what the triggers remember (saved with the world).</summary>
    public sealed class InboxState
    {
        public List<InboxMessage> Messages = new List<InboxMessage>();
        public List<PendingMessage> Pending = new List<PendingMessage>();
        public int Seq, Tick, Season;
        /// <summary>Wordings used this season per trigger (no repeats within a season).</summary>
        public Dictionary<string, List<int>> UsedWordings = new Dictionary<string, List<int>>();
        /// <summary>The last trigger each manager sent (never the same twice in a row).</summary>
        public Dictionary<Facility, string> LastTrigger = new Dictionary<Facility, string>();
        public Dictionary<string, int> LastWording = new Dictionary<string, int>();
        public int RecordAttendance, WinlessRun;
        public double LastMonthStore;
        public Dictionary<string, int> WeeksWithoutRise = new Dictionary<string, int>();
        public List<string> ReadyFlagged = new List<string>();
        public Dictionary<string, int> HeavyWeeks = new Dictionary<string, int>();
        public List<string> RiskFlagged = new List<string>();
        public bool TooManyFlagged;
        public List<string> BigGroupFlagged = new List<string>();
        public int UnreadCount => Messages.Count(m => !m.Read);
    }

    /// <summary>The user's club before a week is played: what the triggers compare against.</summary>
    public sealed class WeekSnapshot
    {
        public string ClubId;
        public int Season, Week, TalkSeq;
        public Dictionary<Facility, (int level, double condition, string manager)> Facilities = new Dictionary<Facility, (int, double, string)>();
        public Dictionary<string, int[]> Squad = new Dictionary<string, int[]>();
        public Dictionary<string, int> PlayedRounds = new Dictionary<string, int>();
        public HashSet<string> Finished = new HashSet<string>();
    }

    /// <summary>
    /// Facility manager messages (rules confirmed Oct 8, wordings accepted Oct 9): the triggers compare the user's club before
    /// and after each week; at most 2 messages a week by priority (health, then damage and maintenance, then contracts and
    /// rival interest, then the rest); lower ones wait up to 2 weeks or are dropped; no wording repeats within a season; a manager
    /// never sends the same trigger twice in a row. Messages never reveal archetypes, hidden personalities or potential.
    /// Wording choice uses its own random stream, so the inbox never changes what happens in the game.
    /// </summary>
    public static class InboxEngine
    {
        private static readonly Regex Slot = new Regex(@"\[([a-z/]+)\]");

        public static WeekSnapshot Snapshot(GameWorld w)
        {
            var s = new WeekSnapshot { ClubId = w.UserClubId, Season = w.SeasonStartYear, Week = w.Calendar.Week, TalkSeq = w.Market.Seq };
            if (w.UserClubId == null) return s;
            var club = w.Clubs.First(c => c.Id == w.UserClubId);
            foreach (var kv in club.Facilities) s.Facilities[kv.Key] = (kv.Value.Level, kv.Value.Condition, kv.Value.Manager?.Name);
            foreach (var p in w.Players.Where(p => p.ClubId == club.Id)) s.Squad[p.Id] = p.Attributes.Values;
            foreach (var r in w.Calendar.Runs) { s.PlayedRounds[r.CompetitionId] = r.PlayedRounds.Count; if (r.Finished) s.Finished.Add(r.CompetitionId); }
            return s;
        }

        /// <summary>After a week (and the season's end, when it came): find what happened, queue it, deliver up to 2 messages.</summary>
        public static List<InboxMessage> AfterWeek(GameWorld w, WeekSnapshot before, WeekReport report, CalendarState playedCalendar, GameData d)
        {
            if (before.ClubId == null || before.ClubId != w.UserClubId) return new List<InboxMessage>();
            var box = w.Inbox; var c = d.InboxRules;
            var club = w.Clubs.First(x => x.Id == before.ClubId);
            var rng = new GameRandom(w.Seed ^ ((ulong)before.Season * 1_000_003UL + (ulong)before.Week) * 0x9E3779B97F4A7C15UL);
            if (box.Season != before.Season) { box.Season = before.Season; box.UsedWordings.Clear(); }
            box.Tick++;
            var found = new List<PendingMessage>();
            void Add(string trigger, Facility sender, params (string key, string value)[] values)
            {
                var t = d.Messages.Get(trigger);
                if (!t.Live) return;
                var m = new PendingMessage { TriggerId = trigger, Sender = sender, Priority = t.Priority, CreatedTick = box.Tick };
                foreach (var (k, v) in values) m.Values[k] = v;
                found.Add(m);
            }
            double Rating(Player p) => Transfers.Pricing.Rating(p, d);
            int Age(Player p) => w.SeasonStartYear - p.BirthYear;
            string Fac(Facility f) => d.FacilityName(f);

            // ---- facilities
            foreach (var kv in club.Facilities)
            {
                if (!before.Facilities.TryGetValue(kv.Key, out var old)) continue;
                var now = kv.Value; var f = kv.Key;
                // Upgrades and repairs are the user's actions between weeks: GameSession queues those messages.
                if (now.Level < old.level) Add("MSG-LEVEL-DROP", f, ("facility", Fac(f)), ("level", now.Level.ToString()));
                if (now.Condition < 30 && old.condition >= 30) Add("MSG-COND-30", f, ("facility", Fac(f)), ("n", Math.Floor(now.Condition).ToString("0")));
                else if (now.Condition < 60 && old.condition >= 60) Add("MSG-COND-60", f, ("facility", Fac(f)), ("n", Math.Floor(now.Condition).ToString("0")));
                if (now.Manager != null && old.manager != null && now.Manager.Name != old.manager)
                    Add("MSG-NEW-MANAGER", f, ("name", now.Manager.Name), ("role", d.Messages.Roles.TryGetValue(f.ToString(), out var role) ? role : "manager"), ("club", club.Name), ("facility", Fac(f)));
            }
            if (w.Calendar.Week == 1)   // first week of a season: managers who leave at its end say so
                foreach (var kv in club.Facilities)
                    if (kv.Value.Manager != null && w.SeasonStartYear + 1 - kv.Value.Manager.BirthYear >= kv.Value.Manager.RetireAge)
                        Add("MSG-MANAGER-RETIRING", kv.Key, ("facility", Fac(kv.Key)));

            // ---- matches this week (from the calendar that was played, before any season reset)
            foreach (var run in playedCalendar.Runs)
            {
                before.PlayedRounds.TryGetValue(run.CompetitionId, out int from);
                bool league = w.Competitions.Any(x => x.Id == run.CompetitionId) || d.Cups.Cups.Any(x => x.Id == run.CompetitionId && x.Kind == CupKind.Ranking);
                for (int i = from; i < run.PlayedRounds.Count; i++)
                    foreach (var m in run.PlayedRounds[i].Where(m => m.Home == club.Id || m.Away == club.Id))
                    {
                        bool home = m.Home == club.Id;
                        int us = home ? m.HomeGoals : m.AwayGoals, them = home ? m.AwayGoals : m.HomeGoals;
                        string opponent = w.Clubs.First(x => x.Id == (home ? m.Away : m.Home)).Name;
                        bool won = us > them || (us == them && m.PenaltyWinner == club.Id);
                        box.WinlessRun = won ? 0 : box.WinlessRun + 1;
                        if (us - them >= c.BigWinMargin) Add("MSG-BIG-WIN", Facility.ClubStore, ("score", us + "-" + them), ("opponent", opponent));
                        if (box.WinlessRun == c.BadRunMatches) Add("MSG-BAD-RUN", Facility.ClubStore);
                        if (home && league) Attendance(w, club, box, c, d, rng, Add);
                    }
                if (run.Finished && !before.Finished.Contains(run.CompetitionId) && run.Outcome != null)
                    foreach (var t in run.Outcome.Titles.Where(t => t.Value == club.Id && t.Key != CupFormats.RunnerUp))
                        Add("MSG-TROPHY", Facility.ClubStore, ("trophy", TitleName(run.CompetitionId, t.Key, d, w)));
            }

            // ---- medical: injuries in this week's matches, recoveries, too many injured, players at risk (Oct 9)
            if (report != null)
            {
                foreach (var m in report.UserMatches)
                    foreach (var e in (m.Events ?? new List<Squad.MatchEvent>()).Where(e => e.Type == Squad.EventType.Injury && e.ClubId == club.Id))
                    {
                        var p = w.Players.FirstOrDefault(x => x.Id == e.PlayerId);
                        if (p?.Injury == null) continue;
                        if (p.Injury.Serious) Add("MSG-SERIOUS-INJURY", Facility.MedicalCentre, ("player", p.Name), ("injury", p.Injury.Name), ("n", Math.Max(1, (int)Math.Round(p.Injury.TotalWeeks / 4.3)).ToString()));
                        else Add("MSG-INJURED", Facility.MedicalCentre, ("player", p.Name), ("injury", p.Injury.Name), ("n", p.Injury.TotalWeeks.ToString()));
                    }
                foreach (var id in report.AheadOfSchedule) { var p = w.Players.FirstOrDefault(x => x.Id == id); if (p != null) Add("MSG-RECOVERY-AHEAD", Facility.MedicalCentre, ("player", p.Name), ("n", "1")); }
                foreach (var id in report.Recovered) { var p = w.Players.FirstOrDefault(x => x.Id == id); if (p != null && p.ClubId == club.Id) Add("MSG-BACK-IN-TRAINING", Facility.MedicalCentre, ("player", p.Name)); }
                // Debuts: an academy graduate whose only matches for the club are this week's.
                var playedThisWeek = report.UserMatches.Where(um => um.Players != null)
                    .SelectMany(um => um.Players.Where(x => x.ClubId == club.Id && x.Minutes > 0)).GroupBy(x => x.PlayerId);
                foreach (var g in playedThisWeek)
                {
                    var p = w.Players.FirstOrDefault(x => x.Id == g.Key);
                    if (p == null || p.AcademyClubId != club.Id) continue;
                    if (p.Stats.Where(st => st.ClubId == club.Id).Sum(st => st.Apps) == g.Count())
                        Add("MSG-DEBUT", Facility.Academy, ("player", p.Name), ("club", club.Name));
                }
            }
            {
                var squadNow = w.Players.Where(p => p.ClubId == club.Id).ToList();
                int injured = squadNow.Count(p => p.Injured);
                if (injured >= c.TooManyInjuries && !box.TooManyFlagged) { box.TooManyFlagged = true; Add("MSG-TOO-MANY-INJURIES", Facility.MedicalCentre, ("n", injured.ToString())); }
                if (injured < c.TooManyInjuries - 1) box.TooManyFlagged = false;
                foreach (var p in squadNow.Where(p => !p.Injured))
                {
                    if (p.Energy < c.InjuryRiskBelowEnergy && !box.RiskFlagged.Contains(p.Id)) { box.RiskFlagged.Add(p.Id); Add("MSG-INJURY-RISK", Facility.MedicalCentre, ("player", p.Name)); }
                    else if (p.Energy >= 70) box.RiskFlagged.Remove(p.Id);
                }
                // Heavy training too long: 6 weeks in a row (rebuilt in squad order, so a reloaded inbox stays identical).
                var heavy = new Dictionary<string, int>();
                foreach (var p in squadNow.Where(p => p.Regime == "heavy" && !p.Injured))
                {
                    box.HeavyWeeks.TryGetValue(p.Id, out int n);
                    heavy[p.Id] = ++n;
                    if (n == c.HeavyTooLongWeeks) Add("MSG-HEAVY-TOO-LONG", Facility.TrainingGround, ("player", p.Name));
                }
                box.HeavyWeeks = heavy;
            }

            // ---- coaches: contracts ending this season (week 40), groups that are too big
            if (w.Calendar.Week == c.CoachContractReminderWeek)
                foreach (var coach in w.Coaches.Where(x => x.ClubId == club.Id && x.ContractEndYear == w.SeasonStartYear + 1).OrderBy(x => x.Id, StringComparer.Ordinal))
                    Add("MSG-COACH-CONTRACT", Facility.TrainingGround, ("coach", coach.Name));
            {
                var load = w.Players.Where(p => p.ClubId == club.Id && p.CoachId != null).GroupBy(p => p.CoachId).ToDictionary(g => g.Key, g => g.Count());
                var flagged = new List<string>();
                foreach (var coach in w.Coaches.Where(x => x.ClubId == club.Id).OrderBy(x => x.Id, StringComparer.Ordinal))
                {
                    int n = load.TryGetValue(coach.Id, out var v) ? v : 0;
                    if (n < d.Coaches.GroupTooBig) continue;
                    flagged.Add(coach.Id);
                    if (!box.BigGroupFlagged.Contains(coach.Id))
                        Add("MSG-COACH-GROUP-BIG", Facility.TrainingGround, ("coach", coach.Name), ("n", n.ToString()), ("position", Squad.Coaching.GroupName(coach.Group)));
                }
                box.BigGroupFlagged = flagged;
            }

            // ---- scouting: reports, wonderkids, contracts ending, next opponent
            if (report != null && report.ScoutReports.Count > 0)
            {
                foreach (var g in report.ScoutReports.GroupBy(r => r.ScoutId).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    var scout = w.Scouts.FirstOrDefault(x => x.Id == g.Key);
                    if (scout == null) continue;
                    string region = scout.Task.CountryId != null ? (w.Countries.FirstOrDefault(x => x.Id == scout.Task.CountryId)?.Name ?? scout.Task.CountryId)
                                  : scout.Task.Confederation ?? "abroad";
                    string pos = scout.Task.Position == null ? "general" : PositionName(scout.Task.Position.Value);
                    Add("MSG-SCOUT-REPORT", Facility.ScoutingCentre, ("scout", scout.Name), ("region", region), ("position", pos),
                        ("region/position", scout.Task.Position != null ? pos : region), ("n", g.Count().ToString()));
                }
                foreach (var r in report.ScoutReports.Where(r => r.Wonderkid))
                {
                    var p = w.Players.FirstOrDefault(x => x.Id == r.PlayerId);
                    var scout = w.Scouts.FirstOrDefault(x => x.Id == r.ScoutId);
                    if (p == null) continue;
                    var club2 = p.ClubId == null ? null : w.Clubs.FirstOrDefault(x => x.Id == p.ClubId);
                    string country = w.Countries.FirstOrDefault(x => x.Id == (club2?.CountryId ?? p.NationalityId))?.Name ?? p.NationalityId;
                    Add("MSG-WONDERKID", Facility.ScoutingCentre, ("player", p.Name), ("age", (w.SeasonStartYear - p.BirthYear).ToString()), ("country", country), ("scout", scout?.Name ?? "Our scout"));
                }
            }
            if (w.Calendar.Week == c.CoachContractReminderWeek)
                foreach (var scout in w.Scouts.Where(x => x.ClubId == club.Id && x.ContractEndYear == w.SeasonStartYear + 1).OrderBy(x => x.Id, StringComparer.Ordinal))
                    Add("MSG-SCOUT-CONTRACT", Facility.ScoutingCentre, ("scout", scout.Name));
            if (!w.Calendar.SeasonOver && w.Calendar.Week > 0)
            {
                var nextOpponent = NextOpponent(w, club, d);
                if (nextOpponent != null)
                {
                    var info = Scouting.Scouts.Opponent(w, club, nextOpponent, d);
                    if (info != null)
                    {
                        int wins = LastResults(w, nextOpponent.Id, 5);
                        Add("MSG-OPPONENT-REPORT", Facility.ScoutingCentre, ("opponent", nextOpponent.Name), ("day", "the weekend"), ("player", info.Value.best.Name),
                            ("formation", info.Value.formation), ("n", wins.ToString()));
                    }
                }
            }

            // ---- next week's big home match (cup semi-finals and finals, continental knockouts)
            if (!w.Calendar.SeasonOver && w.Calendar.Week > 0)
                foreach (var run in w.Calendar.Runs.Where(r => !r.Finished))
                {
                    var weeks = w.Calendar.RoundWeeks[run.CompetitionId];
                    if (run.PlayedRounds.Count >= weeks.Count || weeks[run.PlayedRounds.Count] != w.Calendar.Week + 1) continue;
                    var def = d.Cups.Cups.FirstOrDefault(x => x.Id == run.CompetitionId);
                    if (def == null || def.Kind == CupKind.Ranking) continue;
                    Round next;
                    try { run.Info = w.Calendar.Seeds; next = run.Next(d); } catch { continue; }
                    if (next == null || next.Neutral) continue;
                    bool big = next.Stage == "Final" || next.Stage == "Semi-final" || (def.IsContinental && (next.Stage == "Quarter-final" || next.Stage == "Knockout play-off"));
                    var fx = next.Fixtures.FirstOrDefault(x => x.Home == club.Id);
                    if (big && fx.Home != null)
                        Add("MSG-BIG-MATCH", Facility.Stadium, ("opponent", w.Clubs.First(x => x.Id == fx.Away).Name), ("day", "Wednesday"), ("competition", def.Name));
                }

            // ---- squad: training, youngsters, signings
            var squad = w.Players.Where(p => p.ClubId == club.Id).ToList();
            // ---- academy: no room for the coming intake (graduates who don't fit leave as free agents at the season end)
            if (w.Calendar.Week == c.AcademyRoomWarningWeek)
            {
                int room = d.Development.MaxSquadSize - squad.Count;
                if (room < d.Development.AcademyIntakeMax) Add("MSG-ACADEMY-ROOM", Facility.Academy, ("n", Math.Max(0, room).ToString()));
            }
            var ranked = squad.OrderByDescending(Rating).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
            var improved = new List<(Player p, int attr, int gain)>();
            foreach (var p in squad)
            {
                if (!before.Squad.TryGetValue(p.Id, out var old)) continue;
                var now = p.Attributes.Values;
                int best = -1, gain = 0;
                for (int i = 0; i < now.Length; i++) if (now[i] - old[i] > gain) { gain = now[i] - old[i]; best = i; }
                if (gain > 0) { improved.Add((p, best, gain)); box.WeeksWithoutRise[p.Id] = 0; }
                else if (report != null && report.Trained)
                {
                    box.WeeksWithoutRise.TryGetValue(p.Id, out int n);
                    box.WeeksWithoutRise[p.Id] = ++n;
                    if (n == c.StallTrainingWeeks && Age(p) <= c.StallMaxAge) Add("MSG-STALLED", Facility.TrainingGround, ("player", p.Name));
                }
            }
            if (improved.Count >= c.GoodWeekPlayers) Add("MSG-GOOD-WEEK", Facility.TrainingGround, ("n", improved.Count.ToString()));
            else if (improved.Count > 0)
            {
                var top = improved.OrderByDescending(x => x.gain).ThenBy(x => x.p.Id, StringComparer.Ordinal).First();
                Add("MSG-ATTRIBUTE-UP", Facility.TrainingGround, ("player", top.p.Name), ("attribute", AttributeName((Attr)top.attr)));
            }
            // Rebuilt in squad order (not edited in place), so a saved and reloaded inbox stays identical.
            box.WeeksWithoutRise = squad.Where(p => box.WeeksWithoutRise.ContainsKey(p.Id)).ToDictionary(p => p.Id, p => box.WeeksWithoutRise[p.Id]);

            var arrivals = squad.Where(p => !before.Squad.ContainsKey(p.Id)).ToList();
            var intake = arrivals.Where(p => p.AcademyClubId == club.Id && Age(p) <= c.YoungsterMaxAge && p.FormerClubIds.Count == 0).ToList();
            if (intake.Count > 0)
            {
                Add("MSG-INTAKE", Facility.Academy, ("n", intake.Count.ToString()));
                Add("MSG-TOP-PROSPECT", Facility.Academy, ("player", intake.OrderByDescending(Rating).ThenBy(p => p.Id, StringComparer.Ordinal).First().Name));
            }
            foreach (var p in arrivals.Except(intake))
            {
                int rank = ranked.IndexOf(p) + 1;
                if (rank >= 1 && rank <= c.StarSigningTopRank)
                    Add("MSG-STAR-SIGNING", Facility.ClubStore, ("player", p.Name), ("n", (club.Reputation * rng.NextInt(c.ShirtsPerReputationMin, c.ShirtsPerReputationMax)).ToString("N0")));
            }
            if (ranked.Count >= c.YoungsterReadyRank)
            {
                double bar = Rating(ranked[c.YoungsterReadyRank - 1]);
                foreach (var p in squad.Where(p => p.AcademyClubId == club.Id && Age(p) <= c.YoungsterMaxAge && !box.ReadyFlagged.Contains(p.Id) && !intake.Contains(p)))
                    if (Rating(p) >= bar) { box.ReadyFlagged.Add(p.Id); Add("MSG-YOUNGSTER-READY", Facility.Academy, ("player", p.Name)); }
            }
            foreach (var id in before.Squad.Keys)
            {
                var p = w.Players.FirstOrDefault(x => x.Id == id);
                if (p != null && p.ClubId == null && !p.Retired && p.AcademyClubId == club.Id && Age(p) <= c.YoungsterMaxAge + 1)
                    Add("MSG-YOUNGSTER-RELEASED", Facility.Academy, ("player", p.Name));
            }

            // ---- rival interest: bids that arrived this week for players the user hasn't listed
            foreach (var t in w.Market.Talks.Where(t => t.Id > before.TalkSeq && t.Kind == Transfers.TalkKind.Bid && t.SellerClubId == club.Id))
                if (!w.Market.Listings.Any(l => l.PlayerId == t.PlayerId))
                {
                    var p = w.Players.FirstOrDefault(x => x.Id == t.PlayerId);
                    var buyer = w.Clubs.FirstOrDefault(x => x.Id == t.BuyerClubId);
                    if (p != null && buyer != null) Add("MSG-RIVAL-INTEREST", Facility.ScoutingCentre, ("club", buyer.Name), ("player", p.Name));
                }

            // ---- the Club Store's monthly report
            if (before.Week > 0 && before.Week + 1 <= 52 && (before.Week + 1) % c.WeeksPerMonth == 0)
            {
                double month = Money.Finance.SeasonIncome(club, w.MoneyKey(club), 1, 1, 0, d.Finance, d.FacilityRules).Store * c.WeeksPerMonth / 52.0;
                var values = new List<(string, string)> { ("amount", d.Currencies.Format(month, w.CurrencyCode)) };
                if (box.LastMonthStore > 0) values.Add(("up/down", month >= box.LastMonthStore ? "up" : "down"));
                box.LastMonthStore = month;
                Add("MSG-MONTHLY-REPORT", Facility.ClubStore, values.ToArray());
            }

            return Deliver(w, found, before, rng, d);
        }

        /// <summary>The user's first opponent next week (peeks at the next round of each competition due then).</summary>
        private static Club NextOpponent(GameWorld w, Club club, GameData d)
        {
            foreach (var run in w.Calendar.Runs.Where(r => !r.Finished))
            {
                var weeks = w.Calendar.RoundWeeks[run.CompetitionId];
                if (run.PlayedRounds.Count >= weeks.Count || weeks[run.PlayedRounds.Count] != w.Calendar.Week + 1) continue;
                Round next;
                try { run.Info = w.Calendar.Seeds; next = run.Next(d); } catch { continue; }
                if (next == null) continue;
                foreach (var f in next.Fixtures)
                    if (f.Home == club.Id || f.Away == club.Id) return w.Clubs.FirstOrDefault(x => x.Id == (f.Home == club.Id ? f.Away : f.Home));
            }
            return null;
        }

        /// <summary>Wins in a club's last N matches this season.</summary>
        private static int LastResults(GameWorld w, string clubId, int n)
        {
            var list = new List<(int week, bool won)>();
            foreach (var run in w.Calendar.Runs)
                for (int i = 0; i < run.PlayedRounds.Count; i++)
                    foreach (var m in run.PlayedRounds[i].Where(m => m.Home == clubId || m.Away == clubId))
                    {
                        bool home = m.Home == clubId;
                        bool won = home ? m.HomeGoals > m.AwayGoals || m.PenaltyWinner == clubId : m.AwayGoals > m.HomeGoals || m.PenaltyWinner == clubId;
                        list.Add((w.Calendar.RoundWeeks[run.CompetitionId][i], won));
                    }
            return list.OrderByDescending(x => x.week).Take(n).Count(x => x.won);
        }

        /// <summary>Club news, board and job messages, and event decisions: delivered at once (the 2-a-week limit is for managers only).</summary>
        public static InboxMessage Post(GameWorld w, string text, string from, int decisionId = 0, List<string> options = null)
        {
            if (w.UserClubId == null && decisionId == 0 && from != "The board" && from != "Job offer") return null;
            var m = new InboxMessage
            {
                Id = ++w.Inbox.Seq, Kind = decisionId > 0 ? MessageKind.Decision : MessageKind.Club, Text = text, From = from, Priority = 0,
                SeasonStartYear = w.SeasonStartYear, Week = Math.Max(1, w.Calendar.Week), DecisionId = decisionId, Options = options?.ToList(),
            };
            w.Inbox.Messages.Add(m);
            return m;
        }

        /// <summary>Messages for an upgrade the user just paid for (delivered with the next week's messages).</summary>
        public static void QueueUpgrade(GameWorld w, Club club, Facility f, GameData d)
        {
            int level = club.Facilities[f].Level;
            if (f == Facility.Stadium) Queue(w, "MSG-STADIUM-UPGRADED", f, d, ("level", level.ToString()), ("n", Facilities.FacilityRules.StadiumCapacity(club, d.FacilityRules).ToString("N0")));
            else if (f == Facility.ScoutingCentre) Queue(w, "MSG-SCOUTING-UPGRADED", f, d, ("level", level.ToString()));
            else Queue(w, "MSG-UPGRADED", f, d, ("facility", d.FacilityName(f)), ("level", level.ToString()));
        }

        /// <summary>A message the game posts directly (e.g. the protégé arriving). Waits for a slot like the others.</summary>
        public static void Queue(GameWorld w, string trigger, Facility sender, GameData d, params (string key, string value)[] values)
        {
            var t = d.Messages.Get(trigger);
            if (!t.Live || w.UserClubId == null) return;
            var m = new PendingMessage { TriggerId = trigger, Sender = sender, Priority = t.Priority, CreatedTick = w.Inbox.Tick + 1, Order = -1 };
            foreach (var (k, v) in values) m.Values[k] = v;
            w.Inbox.Pending.Add(m);
        }

        /// <summary>Attendance for a home league match (PROPOSAL): the season fill rate × a random 0.85-1.15, up to the capacity.</summary>
        private static void Attendance(GameWorld w, Club club, InboxState box, InboxConfig c, GameData d, GameRandom rng,
                                       Action<string, Facility, (string, string)[]> add)
        {
            var f = d.Finance;
            int seats = Facilities.FacilityRules.StadiumCapacity(club, d.FacilityRules);
            double fill = Math.Min(1.0, f.FillBase + f.FillMood * club.FanMood / 100.0) * rng.Uniform(c.AttendanceRandomMin, c.AttendanceRandomMax);
            int crowd = (int)Math.Round(Math.Min(1.0, fill) * seats);
            if (crowd >= seats) add("MSG-SELL-OUT", Facility.Stadium, new[] { ("n", crowd.ToString("N0")) });
            else if (crowd < c.LowAttendanceBelow * seats) add("MSG-LOW-ATTENDANCE", Facility.Stadium, new[] { ("n", crowd.ToString("N0")) });
            if (box.RecordAttendance > 0 && crowd > box.RecordAttendance) add("MSG-RECORD-ATTENDANCE", Facility.Stadium, new[] { ("n", crowd.ToString("N0")) });
            box.RecordAttendance = Math.Max(box.RecordAttendance, crowd);
        }

        private static List<InboxMessage> Deliver(GameWorld w, List<PendingMessage> found, WeekSnapshot before, GameRandom rng, GameData d)
        {
            var box = w.Inbox; var c = d.InboxRules;
            int order = 0;
            foreach (var m in found) { m.Order = order++; box.Pending.Add(m); }
            box.Pending.RemoveAll(m => box.Tick - m.CreatedTick > c.MaxWaitWeeks);
            var delivered = new List<InboxMessage>();
            foreach (var m in box.Pending.OrderBy(m => m.Priority).ThenBy(m => m.CreatedTick).ThenBy(m => m.Order).ToList())
            {
                if (delivered.Count >= c.ManagerMessagesPerWeek) break;
                var t = d.Messages.Get(m.TriggerId);
                // Never the same trigger twice in a row from the same manager (the monthly report is exempt).
                if (!t.Repeatable && box.LastTrigger.TryGetValue(m.Sender, out var last) && last == t.Id) { box.Pending.Remove(m); continue; }
                if (!box.UsedWordings.TryGetValue(t.Id, out var used)) used = box.UsedWordings[t.Id] = new List<int>();
                box.LastWording.TryGetValue(t.Id, out int lastWording);
                var options = Enumerable.Range(0, t.Wordings.Count)
                    .Where(i => t.Repeatable ? (t.Wordings.Count == 1 || i != lastWording || !used.Contains(i)) : !used.Contains(i))
                    .Where(i => CanFill(t.Wordings[i], m.Values)).ToList();
                box.Pending.Remove(m);
                if (options.Count == 0) continue;   // every wording used this season: dropped
                int pick = options[rng.NextInt(0, options.Count - 1)];
                used.Add(pick); box.LastWording[t.Id] = pick;
                box.LastTrigger[m.Sender] = t.Id;
                var club = w.Clubs.First(x => x.Id == before.ClubId);
                var manager = club.Facilities[m.Sender].Manager;
                string role = d.Messages.Roles.TryGetValue(m.Sender.ToString(), out var r) ? r : "manager";
                var msg = new InboxMessage
                {
                    Id = ++box.Seq, TriggerId = t.Id, Sender = m.Sender, Priority = t.Priority,
                    Text = Fill(t.Wordings[pick], m.Values),
                    From = manager == null ? role : manager.Name + ", " + role,
                    SeasonStartYear = before.Season, Week = Math.Max(1, before.Week + 1),
                };
                box.Messages.Add(msg);
                delivered.Add(msg);
            }
            Trim(w, d);
            return delivered;
        }

        private static bool CanFill(string wording, Dictionary<string, string> values)
        {
            if (wording.IndexOf("derby", StringComparison.OrdinalIgnoreCase) >= 0 && !values.ContainsKey("derby")) return false;
            if (wording.IndexOf("storm", StringComparison.OrdinalIgnoreCase) >= 0 && !values.ContainsKey("storm")) return false;
            return Slot.Matches(wording).Cast<Match>().All(x => values.ContainsKey(x.Groups[1].Value));
        }

        private static string Fill(string wording, Dictionary<string, string> values) => Slot.Replace(wording, x => values[x.Groups[1].Value]);

        public static string PositionName(Position p)
        {
            switch (p)
            {
                case Position.GK: return "goalkeeper";
                case Position.CB: return "centre-back";
                case Position.LB: return "left-back";
                case Position.RB: return "right-back";
                case Position.DM: return "defensive midfielder";
                case Position.CM: return "midfielder";
                case Position.AM: return "attacking midfielder";
                case Position.LM: return "left midfielder";
                case Position.RM: return "right midfielder";
                case Position.LW: return "left winger";
                case Position.RW: return "right winger";
                default: return "striker";
            }
        }

        public static string AttributeName(Attr a)
        {
            switch (a)
            {
                case Attr.RushingOut: return "rushing out";
                default: return a.ToString().ToLowerInvariant();
            }
        }

        private static string TitleName(string competitionId, string title, GameData d, GameWorld w)
        {
            var cup = d.Cups.Cups.FirstOrDefault(x => x.Id == competitionId);
            if (cup != null) return cup.Name;
            var league = w.Competitions.FirstOrDefault(x => x.Id == competitionId);
            string name = league?.Name ?? competitionId;
            return title == "Champion" || title == "League" ? name + " title" : title + " title";
        }

        // ---- inbox actions for the UI

        /// <summary>
        /// Carlos, Oct 10: the inbox keeps the newest 100 messages (inbox.json maxMessages); older ones are deleted.
        /// Open decisions are never deleted. Runs after each week's delivery.
        /// </summary>
        public static void Trim(GameWorld w, GameData d)
        {
            int max = d.InboxRules.MaxMessages;
            var box = w.Inbox.Messages;
            if (max <= 0 || box.Count <= max) return;
            var open = new HashSet<int>(w.Career.Decisions.Select(x => x.Id));
            int drop = box.Count - max;
            var gone = new HashSet<InboxMessage>(box.OrderBy(m => m.Id).Where(m => m.DecisionId == 0 || !open.Contains(m.DecisionId)).Take(drop));
            box.RemoveAll(gone.Contains);
        }

        public static void MarkRead(GameWorld w, int id) { var m = w.Inbox.Messages.FirstOrDefault(x => x.Id == id); if (m != null) m.Read = true; }
        public static void MarkAllRead(GameWorld w) { foreach (var m in w.Inbox.Messages) m.Read = true; }
        public static void Delete(GameWorld w, int id) => w.Inbox.Messages.RemoveAll(x => x.Id == id);
        /// <summary>Newest first.</summary>
        public static List<InboxMessage> List(GameWorld w) => w.Inbox.Messages.OrderByDescending(m => m.Id).ToList();
    }
}
