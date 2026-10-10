using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Season;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Career
{
    /// <summary>data/config/board.json (rules decided Oct 7-8; numbers PROPOSAL).</summary>
    public sealed class BoardConfig
    {
        public Dictionary<string, ObjectiveStakes> Objectives = new Dictionary<string, ObjectiveStakes>();
        public int ChooseObjectiveBeforeWeek = 9;
        public double StartConfidence = 60, ConfidenceWin = 0.6, ConfidenceLoss = 0.6, ConfidenceOnTarget = 1, ConfidencePerPlaceBelow = 0.5, ConfidenceMaxBelow = 3;
        public double ConfidenceFanMood = 0.3, ConfidenceBroke = 1;
        public double SackBelowMidSeason = 15, SackBelowSeasonEnd = 25;
        public int SackFromWeek = 20;
        public double ReputationStartBase = 20, ReputationStartShare = 0.5, ReputationLeagueTitle = 6, ReputationCup = 3, ReputationContinental = 8;
        public double ReputationPromotion = 4, ReputationRelegation = -6, ReputationSacked = -8;
        public double OfferRangeBelow = 10, OfferRangeAbove = 10, OfferChance = 0.35, ApplyBase = 0.5, ApplyPerPoint = 0.025;
        public int MaxOffers = 3;
        public double WageAskBase = 0.7, WageAskPerShare = 2.0, WageAskConfidence = 0.3;
        public double AwardMinShare = 0.6, YoungAwardMinShare = 0.4;
        public int YoungAwardMaxAge = 21, GoldenBallMinApps = 30;
    }

    public sealed class ObjectiveStakes
    {
        public double BonusShareOfIncome, ConfidenceMet, ConfidenceMissed, ReputationMet, ReputationMissed;
        /// <summary>Confidence lost per place missed, up to ConfidenceMissed (0 = always the full amount). Confirmed Oct 10.</summary>
        public double ConfidencePerPlaceMissed;
    }

    public sealed class JobOffer { public string ClubId; public int Season; public bool AfterSacking; }

    public sealed class CupRun { public string CompetitionId, Stage; public bool Won; }

    /// <summary>The end-of-season screen (confirmed Oct 8): league finish, cups, top scorer, biggest win and defeat, full record. Feeds club history.</summary>
    public sealed class SeasonSummary
    {
        public int Season;
        public string ClubId, LeagueId;
        public int Position, Teams, Won, Drawn, Lost, GoalsFor, GoalsAgainst;
        public List<CupRun> Cups = new List<CupRun>();
        public string TopScorerId;
        public int TopScorerGoals;
        public string BiggestWin, BiggestDefeat;
        public string ObjectiveKind;
        public int ObjectiveTarget;
        public bool ObjectiveMet;
        public List<string> Titles = new List<string>();
    }

    public sealed class AwardRecord
    {
        public int Season;
        public string Award, Scope, PlayerId, ClubId;
        public double Value;
    }

    /// <summary>A choice a random event asks the user to make (resolved with GameSession.Decide; unanswered = the last option).</summary>
    public sealed class Decision
    {
        public int Id, Season, Week;
        public string EventId, ClubId, Text;
        public List<string> Options = new List<string>();
        public Dictionary<string, string> Data = new Dictionary<string, string>();
    }

    public sealed class Tenure { public string ClubId; public int FromSeason, ToSeason; public bool Sacked; }

    /// <summary>The user's career (saved with the world).</summary>
    public sealed class CareerState
    {
        public string ManagerName = "Manager";
        public double Reputation = 50, Confidence = 60;
        public string ObjectiveKind = "standard", ObjectiveLeague;
        public int ObjectiveTarget, ExpectedPlace, ObjectiveSeason;
        public bool ObjectiveChosen, AskedWageThisSeason, Unemployed;
        public double WageBarBonus;
        public string FormerClubId;
        public List<JobOffer> Offers = new List<JobOffer>();
        public List<SeasonSummary> Seasons = new List<SeasonSummary>();
        public List<Tenure> Tenures = new List<Tenure>();
        public List<Decision> Decisions = new List<Decision>();
        public int DecisionSeq;
        public bool FirstProtegeDone;
        public int LastProtegeSeason;
    }

    /// <summary>
    /// The board, jobs and the manager's reputation (decided Oct 7-8; numbers PROPOSAL in board.json). Negotiable objectives with
    /// stakes; a confidence meter; sacking leads to offers from lower clubs, never game over; offers scale with reputation.
    /// </summary>
    public static class Board
    {
        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>A new career at a club: reputation from the club, confidence from the board's start value.</summary>
        public static void Start(GameWorld w, Club club, GameData d)
        {
            var c = d.Board; var k = w.Career;
            k.Reputation = Clamp(c.ReputationStartBase + c.ReputationStartShare * club.Reputation, 1, 100);
            k.Confidence = c.StartConfidence;
            k.Unemployed = false;
            k.Tenures.Add(new Tenure { ClubId = club.Id, FromSeason = w.SeasonStartYear });
            SetObjective(w, d, "standard", chosen: false);
        }

        /// <summary>The club's expected league place: its rank by squad strength (best 11 by main-position rating).</summary>
        public static (string league, int expected, int teams) Expected(GameWorld w, Club club, GameData d)
        {
            if (!w.ClubLeague.TryGetValue(club.Id, out var league) || league == null) return (null, 1, 1);
            var r = new Squad.RatingTable(d);
            double Strength(Club x) => Transfers.Market.Squad(w, x.Id).Select(r.Main).OrderByDescending(v => v).Take(11).DefaultIfEmpty(30).Average();
            var clubs = w.Clubs.Where(x => w.ClubLeague.TryGetValue(x.Id, out var l) && l == league).OrderByDescending(Strength).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            return (league, clubs.IndexOf(club) + 1, clubs.Count);
        }

        /// <summary>The three objectives on offer: finish no lower than this place.</summary>
        public static Dictionary<string, int> Options(GameWorld w, Club club, GameData d)
        {
            var (_, expected, teams) = Expected(w, club, d);
            int margin = Math.Max(2, teams / 6);
            return new Dictionary<string, int>
            {
                ["safe"] = Math.Min(teams, expected + margin),
                ["standard"] = expected,
                ["ambitious"] = Math.Max(1, expected - margin),
            };
        }

        public static bool SetObjective(GameWorld w, GameData d, string kind, bool chosen = true)
        {
            var k = w.Career;
            var club = w.Clubs.FirstOrDefault(x => x.Id == w.UserClubId);
            if (club == null || !d.Board.Objectives.ContainsKey(kind)) return false;
            if (chosen && w.Calendar.Week >= d.Board.ChooseObjectiveBeforeWeek && k.ObjectiveSeason == w.SeasonStartYear) return false;   // the league has started
            var (league, expected, _) = Expected(w, club, d);
            k.ObjectiveKind = kind; k.ObjectiveLeague = league; k.ExpectedPlace = expected;
            k.ObjectiveTarget = Options(w, club, d)[kind];
            k.ObjectiveSeason = w.SeasonStartYear; k.ObjectiveChosen = chosen;
            return true;
        }

        /// <summary>The league table so far (all league matches played, for Argentina the annual table).</summary>
        public static List<TableRow> TableSoFar(GameWorld w, string league, GameData d)
        {
            var run = w.Calendar.Runs.FirstOrDefault(r => r.CompetitionId == league);
            if (run == null) return new List<TableRow>();
            var table = new LeagueTable(run.Clubs, d.MatchSim);
            foreach (var round in run.PlayedRounds) foreach (var m in round) if (run.Clubs.Contains(m.Home) && run.Clubs.Contains(m.Away) && m.PenaltyWinner == null) table.Add(m);
            return table.Standings();
        }

        /// <summary>Weekly board mood (after the week's matches). Returns true if the user was sacked.</summary>
        public static bool Week(GameWorld w, WeekReport report, GameData d)
        {
            var c = d.Board; var k = w.Career;
            var club = w.Clubs.FirstOrDefault(x => x.Id == w.UserClubId);
            if (club == null) return false;
            if (k.ObjectiveSeason != w.SeasonStartYear) SetObjective(w, d, "standard", chosen: false);
            var league = k.ObjectiveLeague;
            // League results this week.
            var run = league == null ? null : w.Calendar.Runs.FirstOrDefault(r => r.CompetitionId == league);
            if (run != null)
            {
                var weeks = w.Calendar.RoundWeeks[league];
                for (int i = 0; i < run.PlayedRounds.Count; i++)
                {
                    if (weeks[i] != w.Calendar.Week) continue;
                    foreach (var m in run.PlayedRounds[i].Where(m => m.Home == club.Id || m.Away == club.Id))
                    {
                        int us = m.Home == club.Id ? m.HomeGoals : m.AwayGoals, them = m.Home == club.Id ? m.AwayGoals : m.HomeGoals;
                        k.Confidence += us > them ? c.ConfidenceWin : us < them ? -c.ConfidenceLoss : 0;
                    }
                }
                if (w.Calendar.Week % 4 == 0 && run.PlayedRounds.Count > 0)
                {
                    int rank = TableSoFar(w, league, d).FindIndex(r => r.ClubId == club.Id) + 1;
                    if (rank > 0) k.Confidence += rank <= k.ObjectiveTarget ? c.ConfidenceOnTarget : -Math.Min(c.ConfidenceMaxBelow, c.ConfidencePerPlaceBelow * (rank - k.ObjectiveTarget));
                }
            }
            k.Confidence += (club.FanMood - 50) / 50.0 * c.ConfidenceFanMood;
            if (club.Balance <= 0) k.Confidence -= c.ConfidenceBroke;
            k.Confidence = Clamp(k.Confidence, 0, 100);
            if (w.Calendar.Week >= c.SackFromWeek && k.Confidence < c.SackBelowMidSeason) { Sack(w, d, new GameRandom(w.Seed ^ (ulong)w.SeasonStartYear * 7919UL ^ (ulong)w.Calendar.Week)); return true; }
            return false;
        }

        /// <summary>Asking for more wage room mid-season (once a season): the chance the board agrees.</summary>
        public static double WageAskChance(GameWorld w, double extraShare, GameData d)
        {
            var c = d.Board;
            return Clamp(c.WageAskBase - c.WageAskPerShare * extraShare + c.WageAskConfidence * (w.Career.Confidence / 100 - 0.5), 0.05, 0.95);
        }

        public static bool AskForWages(GameWorld w, double extraShare, GameRandom rng, GameData d)
        {
            var k = w.Career;
            if (k.AskedWageThisSeason || w.UserClubId == null) return false;
            k.AskedWageThisSeason = true;
            bool yes = rng.Chance(WageAskChance(w, extraShare, d));
            if (yes) k.WageBarBonus += extraShare;
            return yes;
        }

        /// <summary>Sacked: the club goes to an AI manager; 2-3 offers from lower clubs. Never game over.</summary>
        public static void Sack(GameWorld w, GameData d, GameRandom rng)
        {
            var k = w.Career; var c = d.Board;
            var club = w.Clubs.First(x => x.Id == w.UserClubId);
            k.Reputation = Clamp(k.Reputation + c.ReputationSacked, 1, 100);
            var tenure = k.Tenures.LastOrDefault(t => t.ClubId == club.Id && t.ToSeason == 0);
            if (tenure != null) { tenure.ToSeason = w.SeasonStartYear; tenure.Sacked = true; }
            k.FormerClubId = club.Id; k.Unemployed = true;
            w.UserClubId = null;
            w.Market.SquadsChanged();
            var lower = w.Clubs.Where(x => x.Reputation < club.Reputation && x.Id != club.Id && w.ClubLeague.TryGetValue(x.Id, out var l) && l != null)
                .OrderByDescending(x => x.Reputation).ThenBy(x => x.Id, StringComparer.Ordinal).Take(8).ToList();
            k.Offers = lower.OrderBy(_ => rng.NextDouble()).Take(rng.NextInt(2, 3)).Select(x => new JobOffer { ClubId = x.Id, Season = w.SeasonStartYear, AfterSacking = true }).ToList();
            Inbox.InboxEngine.Post(w, $"The board has decided to end your time at {club.Name}. {k.Offers.Count} clubs have been in touch about the job.", "The board");
        }

        /// <summary>Takes an offer: the new club keeps its squad; the old one goes to an AI manager.</summary>
        public static bool Accept(GameWorld w, string clubId, GameData d)
        {
            var k = w.Career;
            if (k.Offers.All(o => o.ClubId != clubId)) return false;
            Move(w, clubId, d);
            return true;
        }

        /// <summary>Applying to a club: chance = 0.5 + (your reputation - its reputation) / 40, 5%-95%.</summary>
        public static double ApplyChance(GameWorld w, Club club, GameData d)
            => Clamp(d.Board.ApplyBase + d.Board.ApplyPerPoint * (w.Career.Reputation - club.Reputation), 0.05, 0.95);

        public static bool Apply(GameWorld w, string clubId, GameRandom rng, GameData d)
        {
            var club = w.Clubs.FirstOrDefault(x => x.Id == clubId);
            if (club == null || club.Id == w.UserClubId) return false;
            if (!rng.Chance(ApplyChance(w, club, d))) return false;
            Move(w, clubId, d);
            return true;
        }

        private static void Move(GameWorld w, string clubId, GameData d)
        {
            var k = w.Career;
            if (w.UserClubId != null)
            {
                var t = k.Tenures.LastOrDefault(x => x.ClubId == w.UserClubId && x.ToSeason == 0);
                if (t != null) t.ToSeason = w.SeasonStartYear;
            }
            w.UserClubId = clubId;
            w.Market.SquadsChanged();
            k.Offers.Clear(); k.Unemployed = false; k.Confidence = d.Board.StartConfidence; k.WageBarBonus = 0;
            k.Tenures.Add(new Tenure { ClubId = clubId, FromSeason = w.SeasonStartYear });
            SetObjective(w, d, "standard", chosen: false);
        }

        /// <summary>Confidence change for a missed objective: per place missed, up to the full penalty.</summary>
        public static double MissedConfidence(ObjectiveStakes s, int placesMissed)
            => s.ConfidencePerPlaceMissed > 0 ? Math.Max(s.ConfidenceMissed, -s.ConfidencePerPlaceMissed * Math.Max(1, placesMissed)) : s.ConfidenceMissed;

        /// <summary>
        /// End of the season (before the new year starts): the objective's result (bonus, confidence, reputation), titles,
        /// the season summary, awards for every league, sacking or job offers.
        /// </summary>
        public static void SeasonEnd(GameWorld w, SeasonReport report, List<CompetitionOutcome> outcomes, GameRandom rng, GameData d)
        {
            Awards.Give(w, outcomes, d);
            var k = w.Career; var c = d.Board;
            var club = w.Clubs.FirstOrDefault(x => x.Id == w.UserClubId);
            if (club == null) { k.AskedWageThisSeason = false; return; }
            var summary = Summary(w, club, outcomes, d);
            if (k.ObjectiveSeason != w.SeasonStartYear) SetObjective(w, d, "standard", chosen: false);
            var stakes = c.Objectives[k.ObjectiveKind];
            summary.ObjectiveKind = k.ObjectiveKind; summary.ObjectiveTarget = k.ObjectiveTarget;
            summary.ObjectiveMet = summary.LeagueId == null || summary.Position <= k.ObjectiveTarget;
            if (summary.ObjectiveMet)
            {
                double income = report.Income.TryGetValue(club.Id, out var inc) ? inc.Total : 0;
                long bonus = (long)Math.Round(income * stakes.BonusShareOfIncome);
                club.Balance += bonus; club.OtherMoney += bonus;
                k.Confidence += stakes.ConfidenceMet; k.Reputation += stakes.ReputationMet;
            }
            else
            {
                // Missing by one place costs less than missing by ten (confirmed Oct 10).
                k.Confidence += MissedConfidence(stakes, summary.Position - k.ObjectiveTarget);
                k.Reputation += stakes.ReputationMissed;
            }
            foreach (var o in outcomes.Where(o => o != null))
                foreach (var t in o.Titles.Where(t => t.Value == club.Id && t.Key != CupFormats.RunnerUp))
                {
                    var cup = d.Cups.Cups.FirstOrDefault(x => x.Id == o.CompetitionId);
                    if (cup?.Kind == CupKind.Ranking) continue;
                    k.Reputation += cup == null ? c.ReputationLeagueTitle : cup.IsContinental || cup.Kind == CupKind.Intercontinental ? c.ReputationContinental : c.ReputationCup;
                    summary.Titles.Add((cup?.Name ?? w.Competitions.FirstOrDefault(x => x.Id == o.CompetitionId)?.Name ?? o.CompetitionId) + (cup == null ? " (" + t.Key + ")" : ""));
                }
            if (outcomes.Any(o => o != null && o.Promoted.Contains(club.Id))) k.Reputation += c.ReputationPromotion;
            if (outcomes.Any(o => o != null && o.Relegated.Contains(club.Id))) k.Reputation += c.ReputationRelegation;
            k.Confidence = Clamp(k.Confidence, 0, 100); k.Reputation = Clamp(k.Reputation, 1, 100);
            k.Seasons.Add(summary);
            k.AskedWageThisSeason = false; k.WageBarBonus = 0;
            Inbox.InboxEngine.Post(w, $"Season {w.SeasonStartYear}/{(w.SeasonStartYear + 1) % 100:00} is over: " +
                (summary.LeagueId == null ? "" : $"{Ordinal(summary.Position)} of {summary.Teams}. ") +
                (summary.ObjectiveMet ? "The board's objective was met." : "The board's objective was missed."), "The board");

            if (k.Confidence < c.SackBelowSeasonEnd) { Sack(w, d, rng); return; }
            // Job offers: clubs around the manager's reputation (better managers get better clubs).
            k.Offers = w.Clubs.Where(x => x.Id != club.Id && w.ClubLeague.TryGetValue(x.Id, out var l) && l != null
                                          && x.Reputation >= k.Reputation - c.OfferRangeBelow && x.Reputation <= k.Reputation + c.OfferRangeAbove)
                .OrderBy(x => x.Id, StringComparer.Ordinal).Where(_ => rng.Chance(c.OfferChance)).OrderByDescending(x => x.Reputation)
                .Take(c.MaxOffers).Select(x => new JobOffer { ClubId = x.Id, Season = w.SeasonStartYear }).ToList();
            foreach (var o in k.Offers) Inbox.InboxEngine.Post(w, $"{w.Clubs.First(x => x.Id == o.ClubId).Name} would like you as their manager.", "Job offer");
        }

        public static string Ordinal(int n) => n + (n % 100 >= 11 && n % 100 <= 13 ? "th" : n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th");

        public static SeasonSummary Summary(GameWorld w, Club club, List<CompetitionOutcome> outcomes, GameData d)
        {
            var s = new SeasonSummary { Season = w.SeasonStartYear, ClubId = club.Id };
            // The league he played in this season (promotion and relegation may already have moved the club).
            foreach (var o in outcomes.Where(x => x != null && w.Competitions.Any(c => c.Id == x.CompetitionId && c.Type == CompetitionType.League)))
            {
                var table = o.Tables.TryGetValue("League", out var t) ? t : o.Tables.TryGetValue("Annual", out var a) ? a
                          : o.Tables.Values.FirstOrDefault(x => x.Any(r => r.ClubId == club.Id));
                int i = table?.FindIndex(r => r.ClubId == club.Id) ?? -1;
                if (i < 0) continue;
                var row = table[i];
                s.LeagueId = o.CompetitionId; s.Position = i + 1; s.Teams = table.Count;
                s.Won = row.Won; s.Drawn = row.Drawn; s.Lost = row.Lost; s.GoalsFor = row.GoalsFor; s.GoalsAgainst = row.GoalsAgainst;
                break;
            }
            foreach (var run in w.Calendar.Runs)
            {
                var cup = d.Cups.Cups.FirstOrDefault(x => x.Id == run.CompetitionId);
                if (cup == null || cup.Kind == CupKind.Ranking) continue;
                int last = -1;
                for (int i = 0; i < run.PlayedRounds.Count; i++) if (run.PlayedRounds[i].Any(m => m.Home == club.Id || m.Away == club.Id)) last = i;
                if (last < 0) continue;
                s.Cups.Add(new CupRun { CompetitionId = cup.Id, Stage = run.PlayedStages[last], Won = run.Outcome != null && run.Outcome.Titles.TryGetValue(CupFormats.Winner, out var wnr) && wnr == club.Id });
            }
            var scorers = w.Players.Select(p => (p, goals: p.Stats.Where(x => x.Season == w.SeasonStartYear && x.ClubId == club.Id).Sum(x => x.Goals))).Where(x => x.goals > 0)
                .OrderByDescending(x => x.goals).ThenBy(x => x.p.Id, StringComparer.Ordinal).FirstOrDefault();
            if (scorers.p != null) { s.TopScorerId = scorers.p.Id; s.TopScorerGoals = scorers.goals; }
            (int diff, string text) best = (int.MinValue, null), worst = (int.MaxValue, null);
            foreach (var run in w.Calendar.Runs)
                foreach (var round in run.PlayedRounds)
                    foreach (var m in round.Where(m => m.Home == club.Id || m.Away == club.Id))
                    {
                        bool home = m.Home == club.Id;
                        int us = home ? m.HomeGoals : m.AwayGoals, them = home ? m.AwayGoals : m.HomeGoals;
                        string opp = w.Clubs.First(x => x.Id == (home ? m.Away : m.Home)).Name;
                        string text = $"{us}-{them} v {opp}";
                        if (us - them > best.diff || (us - them == best.diff && best.text == null)) best = (us - them, text);
                        if (us - them < worst.diff) worst = (us - them, text);
                    }
            if (best.diff > 0) s.BiggestWin = best.text;
            if (worst.diff < 0) s.BiggestDefeat = worst.text;
            return s;
        }
    }

    /// <summary>WSC-style awards (decided Oct 7; categories PROPOSAL in board.json): every league, plus the world's best.</summary>
    public static class Awards
    {
        public static void Give(GameWorld w, List<CompetitionOutcome> outcomes, GameData d)
        {
            var c = d.Board; int season = w.SeasonStartYear;
            foreach (var league in w.Competitions.Where(x => x.Type == CompetitionType.League))
            {
                var o = outcomes.FirstOrDefault(x => x != null && x.CompetitionId == league.Id);
                if (o == null || o.Tables.Count == 0) continue;
                int matches = o.Tables.Values.SelectMany(t => t).Select(r => r.Played).DefaultIfEmpty(0).Max();   // zone leagues: the longest zone table
                if (matches == 0) continue;
                var lines = w.Players.Select(p => (p, s: p.Stats.FirstOrDefault(x => x.Season == season && x.CompetitionId == league.Id))).Where(x => x.s != null && x.s.Apps > 0).ToList();
                void Add(string award, (Player p, StatLine s) x, double value) { if (x.p != null) w.Awards.Add(new AwardRecord { Season = season, Award = award, Scope = league.Id, PlayerId = x.p.Id, ClubId = x.s.ClubId, Value = value }); }
                var pots = lines.Where(x => x.s.Apps >= c.AwardMinShare * matches && x.s.Rated > 0).OrderByDescending(x => x.s.AverageRating).ThenBy(x => x.p.Id, StringComparer.Ordinal).FirstOrDefault();
                Add("Player of the Season", pots, Math.Round(pots.s?.AverageRating ?? 0, 2));
                var boot = lines.OrderByDescending(x => x.s.Goals).ThenBy(x => x.s.Minutes).ThenBy(x => x.p.Id, StringComparer.Ordinal).FirstOrDefault();
                Add("Golden Boot", boot, boot.s?.Goals ?? 0);
                var assists = lines.OrderByDescending(x => x.s.Assists).ThenBy(x => x.s.Minutes).ThenBy(x => x.p.Id, StringComparer.Ordinal).FirstOrDefault();
                Add("Top Assists", assists, assists.s?.Assists ?? 0);
                var young = lines.Where(x => season - x.p.BirthYear <= c.YoungAwardMaxAge && x.s.Apps >= c.YoungAwardMinShare * matches && x.s.Rated > 0)
                    .OrderByDescending(x => x.s.AverageRating).ThenBy(x => x.p.Id, StringComparer.Ordinal).FirstOrDefault();
                Add("Young Player of the Season", young, Math.Round(young.s?.AverageRating ?? 0, 2));
                var gk = lines.Where(x => x.p.MainPosition == Position.GK).OrderByDescending(x => x.s.CleanSheets).ThenByDescending(x => x.s.AverageRating).ThenBy(x => x.p.Id, StringComparer.Ordinal).FirstOrDefault();
                Add("Goalkeeper of the Season", gk, gk.s?.CleanSheets ?? 0);
            }
            // World: the best players of the top divisions across every competition.
            var top = new HashSet<string>(w.Competitions.Where(x => x.Type == CompetitionType.League && x.Level == 1).Select(x => x.Id));
            var all = w.Players.Where(p => p.ClubId != null && w.ClubLeague.TryGetValue(p.ClubId, out var l) && l != null && top.Contains(l))
                .Select(p => (p, lines: p.Stats.Where(x => x.Season == season).ToList()))
                .Select(x => (x.p, apps: x.lines.Sum(l => l.Apps), goals: x.lines.Sum(l => l.Goals), rated: x.lines.Sum(l => l.Rated), sum: x.lines.Sum(l => l.RatingSum)))
                .Where(x => x.apps > 0).ToList();
            var ball = all.Where(x => x.apps >= c.GoldenBallMinApps && x.rated > 0).OrderByDescending(x => x.sum / x.rated).ThenBy(x => x.p.Id, StringComparer.Ordinal).FirstOrDefault();
            if (ball.p != null) w.Awards.Add(new AwardRecord { Season = season, Award = "Golden Ball", Scope = "WORLD", PlayerId = ball.p.Id, ClubId = ball.p.ClubId, Value = Math.Round(ball.sum / ball.rated, 2) });
            var wboot = all.OrderByDescending(x => x.goals).ThenBy(x => x.p.Id, StringComparer.Ordinal).FirstOrDefault();
            if (wboot.p != null) w.Awards.Add(new AwardRecord { Season = season, Award = "World Golden Boot", Scope = "WORLD", PlayerId = wboot.p.Id, ClubId = wboot.p.ClubId, Value = wboot.goals });
        }
    }
}
