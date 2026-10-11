using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Season;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Squad
{
    public enum EventType { Goal, Yellow, SecondYellow, Red, Injury, Substitution }

    public sealed class MatchEvent
    {
        public int Minute;
        public EventType Type;
        public string ClubId, PlayerId;
        /// <summary>Goal: the assist (null if none). Substitution: the player coming on.</summary>
        public string OtherPlayerId;
        /// <summary>Goal: an own goal (ClubId is the club it counts for; PlayerId is the player of the other club who scored it).</summary>
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
        public bool OwnGoal;
    }

    /// <summary>One player's match: minutes, contributions, cards and rating (0 = not rated).</summary>
    public sealed class PlayerMatch
    {
        public string PlayerId, ClubId;
        public Position Position;
        public bool Started;
        public int Minutes, Goals, Assists, Yellows, Reds;
        /// <summary>Distance and activity (energy, Carlos Oct 9). Estimated in sim mode; the 3D match measures them.</summary>
        public double Km;
        public int Passes, Shots;
        public bool CleanSheet, Injured;
        public double Rating;
        /// <summary>Distance, passes and shots were measured by the live match (not estimated).</summary>
        public bool Measured;
    }

    /// <summary>
    /// Sim-mode matches with real teams (agreed Oct 9): each club's lineup in its formation gives an attack and a defence
    /// rating; expected goals = base × e^(k × (our attack − their defence)), moved by mentality and by red cards. Injuries,
    /// cards and substitutions happen minute by minute; scorers and assists come from who is on the pitch; every player gets
    /// minutes, a 1-10 rating, energy cost, form, stats, and bans by the competition's rules. Same rules for every club.
    /// </summary>
    public static class MatchEngine
    {
        public sealed class Context
        {
            public GameWorld World;
            public GameData Data;
            public RatingTable Ratings;
            public GameRandom Rng;
            /// <summary>A match the user played live, and the fixture (with team sheets) it was played from. It replaces the sim
            /// for that one fixture, once; every other fixture is simulated.</summary>
            public PlayedMatch Played;
            public UserFixture PlayedFixture;
            public bool PlayedApplied;
        }

        private sealed class Side
        {
            public TeamSheet Sheet;
            public Club Club;
            public Player[] OnPitch;                       // by slot (null = empty)
            public List<Player> Bench;
            public Dictionary<string, PlayerMatch> Lines = new Dictionary<string, PlayerMatch>();
            public Dictionary<string, int> On = new Dictionary<string, int>();   // minute he came on
            public int SubsLeft;
            public double RedTime;                         // share of the match played a man down
            public double Attack, Defence, Overall;
            public double Luck;
            public HashSet<string> Booked = new HashSet<string>();
            public Dictionary<string, Player> Players = new Dictionary<string, Player>();
        }

        /// <summary>
        /// The two team sheets for a fixture, mentality included: AI clubs, and the user's club while the assistant picks the
        /// mentality (Oct 10), choose it by the AI rule. The sim and the pre-match peek (GameSession.NextUserFixture) both use
        /// this, so they always agree. commit = false changes nothing in the world.
        /// </summary>
        public static void TeamSheets(string competitionId, Fixture f, GameWorld w, RatingTable r, GameData d, bool commit, out TeamSheet home, out TeamSheet away)
        {
            var c = d.Squad;
            var homeClub = w.Clubs.First(k => k.Id == f.Home);
            var awayClub = w.Clubs.First(k => k.Id == f.Away);
            home = Lineups.Pick(w, homeClub, competitionId, r, d, commit);
            away = Lineups.Pick(w, awayClub, competitionId, r, d, commit);
            double homeOverall = Lineups.Strength(home, r, c).overall, awayOverall = Lineups.Strength(away, r, c).overall;
            if (homeClub.Id != w.UserClubId || homeClub.Tactics.AssistantMentality) home.Mentality = Lineups.AiMentality(homeOverall, awayOverall, c);
            if (awayClub.Id != w.UserClubId || awayClub.Tactics.AssistantMentality) away.Mentality = Lineups.AiMentality(awayOverall, homeOverall, c);
        }

        /// <summary>
        /// Plays a fixture. decide: knockout (a level result, or level aggregate with <paramref name="firstLeg"/>, goes to extra
        /// time if <paramref name="extraTime"/>, then penalties). If the context holds a match played live for this fixture, its
        /// result is used instead of the sim (once).
        /// </summary>
        public static MatchResult Play(string competitionId, Fixture f, bool decide, bool extraTime, MatchResult? firstLeg, Context x)
        {
            var pm = x.Played;
            if (pm != null && !x.PlayedApplied && pm.CompetitionId == competitionId && pm.HomeClubId == f.Home && pm.AwayClubId == f.Away)
            {
                x.PlayedApplied = true;
                return PlayLive(competitionId, f, decide, extraTime, firstLeg, x);
            }

            var w = x.World; var d = x.Data; var rng = x.Rng; var c = d.Squad; var e = d.MatchEvents;
            TeamSheets(competitionId, f, w, x.Ratings, d, true, out var homeSheet, out var awaySheet);
            var home = Prepare(w.Clubs.First(k => k.Id == f.Home), homeSheet, x);
            var away = Prepare(w.Clubs.First(k => k.Id == f.Away), awaySheet, x);
            StartLines(home); StartLines(away);

            var events = new List<MatchEvent>();
            // ---- minute-by-minute: injuries, cards, substitutions
            var plan = new List<(int minute, int kind, Side side)>();   // kind: 0 injury, 1 straight red, 2 yellow, 3 planned sub
            foreach (var s in new[] { home, away })
            {
                foreach (var p in s.OnPitch.Where(p => p != null))
                    if (rng.Chance(InjuryChance(p, 90, s, d))) plan.Add((rng.NextInt(1, 90), 0, s));
                int reds = Poisson(e.StraightRedsPerTeam * (1 - s.Luck), rng), yellows = Poisson(e.YellowsPerTeam * (1 - s.Luck), rng);
                for (int i = 0; i < reds; i++) plan.Add((rng.NextInt(1, 90), 1, s));
                for (int i = 0; i < yellows; i++) plan.Add((rng.NextInt(1, 90), 2, s));
                int subs = rng.NextInt(e.AiSubsMin, e.AiSubsMax);
                for (int i = 0; i < subs; i++) plan.Add((rng.NextInt(e.SubMinuteMin, e.SubMinuteMax), 3, s));
            }
            int seq = 0;
            foreach (var (minute, kind, s) in plan.Select(p => (p.minute, p.kind, p.side, n: seq++)).OrderBy(p => p.minute).ThenBy(p => p.kind).ThenBy(p => p.n).Select(p => (p.minute, p.kind, p.side)))
            {
                if (kind == 0) Injure(s, minute, competitionId, events, x);
                else if (kind == 1) Card(s, minute, true, competitionId, events, x);
                else if (kind == 2) Card(s, minute, false, competitionId, events, x);
                else PlannedSub(s, minute, events, x);
            }

            // ---- goals
            double lh = Lambda(home, away, home, x), la = Lambda(away, home, home, x);
            int hg = Goals(home, lh, 1, 90, events, x), ag = Goals(away, la, 1, 90, events, x);
            var r = new MatchResult { Home = f.Home, Away = f.Away, HomeGoals = hg, AwayGoals = ag };
            int end = 90;
            if (decide)
            {
                if (RoundPlayer.Level(r, firstLeg) && extraTime)
                {
                    r.HomeGoals += Goals(home, lh / 3, 91, 120, events, x);
                    r.AwayGoals += Goals(away, la / 3, 91, 120, events, x);
                    r.AfterExtraTime = true; end = 120;
                }
                if (RoundPlayer.Level(r, firstLeg))
                    r.PenaltyWinner = rng.Chance(d.LeagueFormats.ArgentinaFirst.PenaltyHomeWinChance) ? f.Home : f.Away;
            }
            return Finish(home, away, r, f, end, competitionId, events, x);
        }

        /// <summary>
        /// The fixture the user played live: score, scorers, assists and each player's minutes, distance, passes and shots
        /// come from the played match (no simulated goals, cards, injuries or substitutions). A level knockout that didn't play
        /// extra time live gets simulated extra time as usual, then penalties (the coin flip). Everything after the match
        /// (ratings, energy, form, clean sheets, stats, bans served) is Finish, the same as for sim results.
        /// </summary>
        private static MatchResult PlayLive(string competitionId, Fixture f, bool decide, bool extraTime, MatchResult? firstLeg, Context x)
        {
            var w = x.World; var d = x.Data; var rng = x.Rng; var pm = x.Played; var fx = x.PlayedFixture;
            var homeClub = w.Clubs.First(k => k.Id == f.Home); var awayClub = w.Clubs.First(k => k.Id == f.Away);
            // The match was played with the sheets the peek gave; an AI club keeps the formation it chose, as Pick does.
            foreach (var (club, sheet) in new[] { (homeClub, fx.HomeSheet), (awayClub, fx.AwaySheet) })
            {
                if (club.Id == w.UserClubId) continue;
                club.Tactics.Formation = sheet.Formation;
                if (!x.Ratings.Formations.ContainsKey(club.Id)) x.Ratings.Formations[club.Id] = sheet.Formation;
            }
            var home = Prepare(homeClub, fx.HomeSheet, x);
            var away = Prepare(awayClub, fx.AwaySheet, x);
            LiveLines(home, pm, x); LiveLines(away, pm, x);

            var events = new List<MatchEvent>();
            foreach (var g in pm.Goals.OrderBy(g => g.Minute))
            {
                if (!g.OwnGoal) (home.Lines.ContainsKey(g.ScorerId) ? home : away).Lines[g.ScorerId].Goals++;
                if (g.AssistId != null) (home.Lines.ContainsKey(g.AssistId) ? home : away).Lines[g.AssistId].Assists++;
                events.Add(new MatchEvent { Minute = g.Minute, Type = EventType.Goal, ClubId = g.ClubId, PlayerId = g.ScorerId, OtherPlayerId = g.AssistId, OwnGoal = g.OwnGoal });
            }
            var r = new MatchResult { Home = f.Home, Away = f.Away, HomeGoals = pm.HomeGoals, AwayGoals = pm.AwayGoals, AfterExtraTime = pm.ExtraTimePlayed };
            int end = pm.ExtraTimePlayed ? 120 : 90;
            if (decide)
            {
                if (RoundPlayer.Level(r, firstLeg) && extraTime && !pm.ExtraTimePlayed)
                {
                    // Extra time in sim mode, with the players on the pitch at the end of the live 90 minutes.
                    double lh = Lambda(home, away, home, x), la = Lambda(away, home, home, x);
                    foreach (var s in new[] { home, away })
                        foreach (var p in s.OnPitch.Where(p => p != null)) s.On[p.Id] = 90;
                    r.HomeGoals += Goals(home, lh / 3, 91, 120, events, x);
                    r.AwayGoals += Goals(away, la / 3, 91, 120, events, x);
                    foreach (var s in new[] { home, away })
                        foreach (var p in s.OnPitch.Where(p => p != null))
                        {
                            var line = s.Lines[p.Id];
                            var (km, passes, shots) = Fitness.Estimate(line, 30 / 90.0, s.Sheet.Mentality, x);
                            line.Km = Math.Round(line.Km + km, 1); line.Passes += passes; line.Shots = Math.Max(line.Goals, line.Shots + shots);
                        }
                    r.AfterExtraTime = true; end = 120;
                }
                if (RoundPlayer.Level(r, firstLeg))
                    r.PenaltyWinner = rng.Chance(d.LeagueFormats.ArgentinaFirst.PenaltyHomeWinChance) ? f.Home : f.Away;
            }
            return Finish(home, away, r, f, end, competitionId, events, x);
        }

        /// <summary>Expected goals over 90 minutes for one side (agreed Oct 9).</summary>
        private static double Lambda(Side us, Side them, Side home, Context x)
        {
            var d = x.Data; var c = d.Squad; var e = d.MatchEvents;
            double base90 = d.MatchSim.BaseGoals;
            return base90 * Math.Exp(d.MatchSim.StrengthFactor * (us.Attack - them.Defence + d.MatchSim.HomeAdvantage * (us == home ? 1 : 0)))
                   * (1 + c.MentalityScoringPerStep * us.Sheet.Mentality) * (1 + c.MentalityConcedingPerStep * them.Sheet.Mentality)
                   * Math.Max(0.2, 1 - e.RedCardStrengthLoss * us.RedTime) * (1 + e.RedCardStrengthLoss * them.RedTime);
        }

        /// <summary>After the match: minutes, ratings, energy, form, stats, bans served (the same for sim and live results).</summary>
        private static MatchResult Finish(Side home, Side away, MatchResult r, Fixture f, int end, string competitionId, List<MatchEvent> events, Context x)
        {
            var w = x.World; var d = x.Data; var rng = x.Rng; var c = d.Squad; var e = d.MatchEvents;
            var lines = new List<PlayerMatch>();
            foreach (var s in new[] { home, away })
            {
                bool won = s == home ? r.HomeGoals > r.AwayGoals || r.PenaltyWinner == f.Home : r.AwayGoals > r.HomeGoals || r.PenaltyWinner == f.Away;
                bool lost = s == home ? r.HomeGoals < r.AwayGoals || (r.PenaltyWinner != null && r.PenaltyWinner != f.Home)
                                      : r.AwayGoals < r.HomeGoals || (r.PenaltyWinner != null && r.PenaltyWinner != f.Away);
                int conceded = s == home ? r.AwayGoals : r.HomeGoals;
                foreach (var p in s.OnPitch.Where(p => p != null)) Close(s, p, end);
                double teamAvg = s.Sheet.Starters.Where(p => p != null).Select(p => x.Ratings.Main(p)).DefaultIfEmpty(50).Average();
                foreach (var line in s.Lines.Values)
                {
                    var p = s.Players[line.PlayerId];
                    line.CleanSheet = conceded == 0 && line.Minutes >= 60 && (line.Position == Position.GK || c.Lines[line.Position.ToString()] == "DEF");
                    if (line.Minutes >= e.MinMinutesForRating)
                    {
                        double rating = e.RatingBase + (won ? e.RatingWin : lost ? -e.RatingWin : 0) + e.RatingGoal * line.Goals + e.RatingAssist * line.Assists
                            + e.RatingPerPointAboveTeam * (x.Ratings.Main(p) - teamAvg) - e.RatingYellow * line.Yellows - e.RatingRed * line.Reds;
                        if (line.CleanSheet) rating += line.Position == Position.GK ? e.RatingCleanSheetGk : e.RatingCleanSheetDef;
                        if (line.Position == Position.GK || c.Lines[line.Position.ToString()] == "DEF") rating -= e.RatingPerConceded * conceded * line.Minutes / (double)end;
                        double noise = e.RatingNoiseSd * (p.PersonalityId != null && e.PersonalityNoise.TryGetValue(p.PersonalityId, out var pn) ? pn : 1);
                        line.Rating = Math.Round(Math.Max(1, Math.Min(10, rating + rng.Gaussian(0, noise))), 1);
                    }
                    Fitness.MatchEnergy(p, line, s.Sheet.Mentality, x);
                    lines.Add(line);
                }
                Discipline.Serve(Transfers.Market.Squad(w, s.Club.Id), competitionId, d);
            }
            var byId = home.Players.Concat(away.Players).ToDictionary(kv => kv.Key, kv => kv.Value);
            string motm = lines.Where(l => l.Rating > 0).OrderByDescending(l => l.Rating).ThenByDescending(l => l.Goals).ThenBy(l => l.PlayerId, StringComparer.Ordinal).FirstOrDefault()?.PlayerId;
            foreach (var line in lines) Stats.Record(byId[line.PlayerId], line, x.World.SeasonStartYear, competitionId, line.PlayerId == motm, d);
            r.Events = events;
            r.Players = lines;
            r.PlayerOfTheMatch = motm;
            return r;
        }

        private static Side Prepare(Club club, TeamSheet sheet, Context x)
        {
            var s = new Side { Club = club, Sheet = sheet, OnPitch = sheet.Starters.ToArray(), Bench = sheet.Bench.ToList(), SubsLeft = x.Data.Squad.Substitutions };
            foreach (var p in sheet.Starters.Concat(sheet.Bench).Where(p => p != null)) s.Players[p.Id] = p;
            (s.Attack, s.Defence, s.Overall) = Lineups.Strength(sheet, x.Ratings, x.Data.Squad);
            int charms = Transfers.Market.Squad(x.World, club.Id).Count(p => p.PersonalityId == "PER-LUCKY");
            s.Luck = Rules.LuckyCharm.TeamLuck(charms, x.Data.LuckyCharm);
            return s;
        }

        /// <summary>Sim mode: the starters are on the pitch from minute 0.</summary>
        private static void StartLines(Side s)
        {
            for (int i = 0; i < s.OnPitch.Length; i++)
                if (s.OnPitch[i] != null) { s.On[s.OnPitch[i].Id] = 0; s.Lines[s.OnPitch[i].Id] = new PlayerMatch { PlayerId = s.OnPitch[i].Id, ClubId = s.Club.Id, Position = s.Sheet.SlotPositions[i], Started = true }; }
        }

        /// <summary>
        /// A live match: one line per player who played, with what the match measured. Who was on the pitch at the end (used by
        /// simulated extra time): starters who played the club's longest time keep their slot; players who came on fill the
        /// other slots, best fit first (goalkeeper slot first), and are rated in that slot's position.
        /// </summary>
        private static void LiveLines(Side s, PlayedMatch pm, Context x)
        {
            var c = x.Data.Squad;
            var mine = pm.Players.Where(p => p.ClubId == s.Club.Id && p.Minutes > 0).ToList();
            int full = mine.Count == 0 ? 0 : mine.Max(p => p.Minutes);
            var slotOf = new Dictionary<string, int>();
            for (int i = 0; i < s.Sheet.Starters.Count; i++) if (s.Sheet.Starters[i] != null) slotOf[s.Sheet.Starters[i].Id] = i;
            var onPitch = new Player[s.Sheet.Slots.Count];
            foreach (var p in mine)
                if (slotOf.TryGetValue(p.PlayerId, out int i) && p.Minutes >= full) onPitch[i] = s.Players[p.PlayerId];
            var cameOn = mine.Where(p => !slotOf.ContainsKey(p.PlayerId)).Select(p => s.Players[p.PlayerId]).ToList();
            var position = new Dictionary<string, Position>();
            foreach (var i in Enumerable.Range(0, onPitch.Length).OrderBy(i => s.Sheet.SlotPositions[i] == Position.GK ? 0 : 1).ThenBy(i => i))
            {
                if (onPitch[i] != null || cameOn.Count == 0) continue;
                var pos = s.Sheet.SlotPositions[i];
                var best = cameOn.OrderByDescending(p => Lineups.Effective(p, pos, x.Ratings, c)).ThenBy(p => p.Id, StringComparer.Ordinal).First();
                cameOn.Remove(best);
                onPitch[i] = best; position[best.Id] = pos;
            }
            s.OnPitch = onPitch;
            s.Bench = new List<Player>(); s.SubsLeft = 0;
            foreach (var p in mine)
            {
                bool started = slotOf.TryGetValue(p.PlayerId, out int slot);
                var pos = started ? s.Sheet.SlotPositions[slot] : position.TryGetValue(p.PlayerId, out var q) ? q : s.Players[p.PlayerId].MainPosition;
                s.Lines[p.PlayerId] = new PlayerMatch
                {
                    PlayerId = p.PlayerId, ClubId = s.Club.Id, Position = pos, Started = started,
                    Minutes = p.Minutes, Km = p.Km, Passes = p.Passes, Shots = p.Shots, Measured = true,
                };
            }
        }

        private static void Close(Side s, Player p, int minute)
        {
            if (!s.On.TryGetValue(p.Id, out int from)) return;
            s.Lines[p.Id].Minutes += Math.Max(0, minute - from);
            s.On.Remove(p.Id);
        }

        private static double InjuryChance(Player p, int minutes, Side s, GameData d)
        {
            var e = d.MatchEvents;
            double risk = e.InjuryPer90 * minutes / 90.0 * (1 + e.InjuryFatiguePerPoint * Math.Max(0, e.InjuryFatigueBelow - p.Energy));
            if (p.PersonalityId != null && e.PersonalityInjury.TryGetValue(p.PersonalityId, out var m)) risk *= m;
            return risk * (1 - s.Luck);
        }

        private static void Injure(Side s, int minute, string competitionId, List<MatchEvent> events, Context x)
        {
            // The injury was drawn for a starter; if he has already gone off it doesn't happen.
            var candidates = Enumerable.Range(0, s.OnPitch.Length).Where(i => s.OnPitch[i] != null && s.Lines[s.OnPitch[i].Id].Started && !s.Lines[s.OnPitch[i].Id].Injured).ToList();
            if (candidates.Count == 0) return;
            int slot = candidates[x.Rng.NextInt(0, candidates.Count - 1)];
            var p = s.OnPitch[slot];
            s.Lines[p.Id].Injured = true;
            Fitness.Injure(p, s.Club, x);
            events.Add(new MatchEvent { Minute = minute, Type = EventType.Injury, ClubId = s.Club.Id, PlayerId = p.Id });
            Close(s, p, minute);
            s.OnPitch[slot] = null;
            Substitute(s, slot, minute, p.Id, events, x);
        }

        private static void Card(Side s, int minute, bool straightRed, string competitionId, List<MatchEvent> events, Context x)
        {
            var d = x.Data; var e = d.MatchEvents;
            var slots = Enumerable.Range(0, s.OnPitch.Length).Where(i => s.OnPitch[i] != null).ToList();
            if (slots.Count == 0) return;
            double Weight(int i)
            {
                var p = s.OnPitch[i];
                double wgt = e.CardWeights.TryGetValue(s.Sheet.SlotPositions[i].ToString(), out var cw) ? cw : 1;
                if (p.PersonalityId != null && e.PersonalityCards.TryGetValue(p.PersonalityId, out var pc)) wgt *= pc;
                return wgt;
            }
            int slot = Pick(slots, Weight, x.Rng);
            // A booked player usually stays careful: most second bookings go to someone else (PROPOSAL, match-events.json).
            if (!straightRed && s.Booked.Contains(s.OnPitch[slot].Id) && x.Rng.Chance(e.BookedCaution))
            {
                var others = slots.Where(i => !s.Booked.Contains(s.OnPitch[i].Id)).ToList();
                if (others.Count > 0) slot = Pick(others, Weight, x.Rng);
            }
            var player = s.OnPitch[slot];
            var line = s.Lines[player.Id];
            int played = ClubMatchesPlayed(x.World, s.Club.Id, competitionId) + 1;
            if (!straightRed && !s.Booked.Contains(player.Id))
            {
                s.Booked.Add(player.Id); line.Yellows++;
                events.Add(new MatchEvent { Minute = minute, Type = EventType.Yellow, ClubId = s.Club.Id, PlayerId = player.Id });
                Discipline.Yellow(player, competitionId, played, d);
                return;
            }
            bool second = !straightRed;
            if (second) line.Yellows++;
            line.Reds++;
            events.Add(new MatchEvent { Minute = minute, Type = second ? EventType.SecondYellow : EventType.Red, ClubId = s.Club.Id, PlayerId = player.Id });
            Discipline.Red(player, competitionId, second, x.Rng, d);
            Close(s, player, minute);
            s.OnPitch[slot] = null;
            s.RedTime += (90 - minute) / 90.0;
        }

        private static void PlannedSub(Side s, int minute, List<MatchEvent> events, Context x)
        {
            if (s.SubsLeft <= 0 || s.Bench.Count == 0) return;
            var c = x.Data.Squad;
            // The most tired outfield player (energy minus what this match has cost him so far).
            var slots = Enumerable.Range(0, s.OnPitch.Length).Where(i => s.OnPitch[i] != null && s.Sheet.SlotPositions[i] != Position.GK).ToList();
            if (slots.Count == 0) return;
            int slot = slots.OrderBy(i => s.OnPitch[i].Energy - 25.0 * (minute - s.On[s.OnPitch[i].Id]) / 90.0).ThenBy(i => i).First();
            var off = s.OnPitch[slot];
            Close(s, off, minute);
            s.OnPitch[slot] = null;
            if (!Substitute(s, slot, minute, off.Id, events, x)) { s.OnPitch[slot] = off; s.On[off.Id] = minute; }   // nobody suitable: he stays on
        }

        /// <summary>Brings on the best bench player for the slot. Returns false if there's no substitution left.</summary>
        private static bool Substitute(Side s, int slot, int minute, string offId, List<MatchEvent> events, Context x)
        {
            if (s.SubsLeft <= 0 || s.Bench.Count == 0) return false;
            var pos = s.Sheet.SlotPositions[slot];
            var bench = s.Bench.Where(p => pos == Position.GK || p.MainPosition != Position.GK || s.Bench.All(b => b.MainPosition == Position.GK)).ToList();
            if (bench.Count == 0) return false;
            var on = bench.OrderByDescending(p => Lineups.Effective(p, pos, x.Ratings, x.Data.Squad)).ThenBy(p => p.Id, StringComparer.Ordinal).First();
            s.Bench.Remove(on); s.SubsLeft--;
            s.OnPitch[slot] = on; s.On[on.Id] = minute;
            s.Lines[on.Id] = new PlayerMatch { PlayerId = on.Id, ClubId = s.Club.Id, Position = pos, Started = false };
            events.Add(new MatchEvent { Minute = minute, Type = EventType.Substitution, ClubId = s.Club.Id, PlayerId = offId, OtherPlayerId = on.Id });
            return true;
        }

        /// <summary>Goals between two minutes: scorer from who's on the pitch then (position × shooting), maybe an assist.</summary>
        private static int Goals(Side s, double lambda90, int from, int to, List<MatchEvent> events, Context x)
        {
            var d = x.Data; var e = d.MatchEvents;
            int n = Poisson(lambda90, x.Rng);
            for (int g = 0; g < n; g++)
            {
                int minute = x.Rng.NextInt(from, to);
                var slots = Enumerable.Range(0, s.OnPitch.Length).Where(i => s.OnPitch[i] != null).ToList();
                if (slots.Count == 0) { events.Add(new MatchEvent { Minute = minute, Type = EventType.Goal, ClubId = s.Club.Id }); continue; }
                int scorerSlot = Pick(slots, i => Weight(e.ScorerWeights, s, i) * Math.Pow(s.OnPitch[i].Attributes[Attr.Shooting] / 60.0, 2) + 1e-6, x.Rng);
                var scorer = s.OnPitch[scorerSlot];
                s.Lines[scorer.Id].Goals++;
                string assist = null;
                var mates = slots.Where(i => i != scorerSlot).ToList();
                if (mates.Count > 0 && x.Rng.Chance(e.AssistShare))
                {
                    var a = s.OnPitch[Pick(mates, i => Weight(e.AssistWeights, s, i) * Math.Pow((s.OnPitch[i].Attributes[Attr.Passing] + s.OnPitch[i].Attributes[Attr.Crossing]) / 120.0, 2) + 1e-6, x.Rng)];
                    s.Lines[a.Id].Assists++; assist = a.Id;
                }
                events.Add(new MatchEvent { Minute = minute, Type = EventType.Goal, ClubId = s.Club.Id, PlayerId = scorer.Id, OtherPlayerId = assist });
            }
            return n;
        }

        private static double Weight(Dictionary<string, double> table, Side s, int slot)
            => table.TryGetValue(s.Sheet.SlotPositions[slot].ToString(), out var v) ? v : 0.1;

        private static int Pick(List<int> items, Func<int, double> weight, GameRandom rng)
        {
            double total = items.Sum(weight), roll = rng.NextDouble() * total, acc = 0;
            foreach (var i in items) { acc += weight(i); if (roll < acc) return i; }
            return items[items.Count - 1];
        }

        private static int Poisson(double lambda, GameRandom rng)
        {
            double l = Math.Exp(-lambda), p = 1.0; int k = 0;
            while (true) { p *= rng.NextDouble(); if (p < l) return k; k++; }
        }

        /// <summary>Matches the club has played in this competition this season (for deadlines like "5 yellows by match 19").</summary>
        public static int ClubMatchesPlayed(GameWorld w, string clubId, string competitionId)
        {
            var run = w.Calendar.Runs.FirstOrDefault(r => r.CompetitionId == competitionId);
            return run == null ? 0 : run.PlayedRounds.Count(round => round.Any(m => m.Home == clubId || m.Away == clubId));
        }
    }
}
