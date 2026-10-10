using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Career
{
    /// <summary>data/rules/events.json (decided Oct 7; numbers PROPOSAL).</summary>
    public sealed class EventData
    {
        public double ChancePerMatchWeek = 0.07, GoodChanceBase = 0.5, GoodChancePerImbalance = 0.15, GoodChanceMin = 0.2, GoodChanceMax = 0.8, DisasterChancePerSeason = 0.02;
        public int MaxPerSeason = 4, NoRepeatSeasons = 3, DecisionWeeks = 1;
        public List<EventDef> Events = new List<EventDef>();
    }

    public sealed class EventDef
    {
        public string Id, Kind, Name, Text, Effect, Facility, ReducedBy;
        public int PlayersMin, PlayersMax, WeeksMin, WeeksMax, PotentialBonus;
        public double Amount, AmountMin, AmountMax, ShareMin, ShareMax, Share, Reputation, FanMood, Energy, DonationShare, AiAcceptChance = 0.5;
        public bool NeedsGoodForm;
        public List<string> Options = new List<string>();
    }

    /// <summary>An event that happened to a club (for balance and no-repeats).</summary>
    public sealed class EventRecord { public int Season; public string Id, Kind; }

    /// <summary>
    /// Random events (decided Oct 7): rare (2-4 a season), balanced good and bad over time, about a third ask for a choice,
    /// no repeats too soon, softened by facilities; AI clubs get the same events (their choices are made for them).
    /// Effects use existing systems: energy, injuries, money, facilities, reputation, fan mood.
    /// </summary>
    public static class Events
    {
        public static void Week(GameWorld w, GameRandom rng, GameData d)
        {
            var c = d.Events; var cal = d.Calendar;
            int week = w.Calendar.Week;
            ResolveExpiredDecisions(w, rng, d);
            if (week < cal.FirstMatchWeek || week > cal.LastMatchWeek) return;
            foreach (var club in w.Clubs.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                var log = w.ClubEvents.TryGetValue(club.Id, out var l) ? l : w.ClubEvents[club.Id] = new List<EventRecord>();
                if (week == 30 && rng.Chance(c.DisasterChancePerSeason)) { Fire(w, club, c.Events.First(e => e.Kind == "disaster"), rng, d); continue; }
                if (log.Count(e => e.Season == w.SeasonStartYear) >= c.MaxPerSeason || !rng.Chance(c.ChancePerMatchWeek)) continue;
                // About a third are choices; otherwise good or bad, balanced over the last 2 seasons.
                string kind;
                if (rng.Chance(1 / 3.0)) kind = "choice";
                else
                {
                    var recent = log.Where(e => w.SeasonStartYear - e.Season <= 1).ToList();
                    int imbalance = recent.Count(e => e.Kind == "bad") - recent.Count(e => e.Kind == "good");
                    double good = Board.Clamp(c.GoodChanceBase + c.GoodChancePerImbalance * imbalance, c.GoodChanceMin, c.GoodChanceMax);
                    kind = rng.Chance(good) ? "good" : "bad";
                }
                var options = c.Events.Where(e => e.Kind == kind && !log.Any(r => r.Id == e.Id && w.SeasonStartYear - r.Season < c.NoRepeatSeasons)).ToList();
                if (options.Count == 0) continue;
                var ev = options[rng.NextInt(0, options.Count - 1)];
                Fire(w, club, ev, rng, d);
            }
        }

        /// <summary>Applies an event to a club. Returns false if it couldn't happen (then nothing is recorded).</summary>
        public static bool Fire(GameWorld w, Club club, EventDef ev, GameRandom rng, GameData d)
        {
            bool user = club.Id == w.UserClubId;
            var squad = Transfers.Market.Squad(w, club.Id).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
            var values = new Dictionary<string, string>();
            double income = Money.Finance.SeasonIncome(club, w.MoneyKey(club), 1, 1, 0, d.Finance, d.FacilityRules).Total;
            string Fmt(double eur) => d.Currencies.Format(Math.Abs(eur), w.CurrencyCode);
            Facility? damaged = null;
            switch (ev.Effect)
            {
                case "energy":
                {
                    var fit = squad.Where(p => !p.Injured).ToList();
                    int n = rng.NextInt(ev.PlayersMin, ev.PlayersMax);
                    if (ev.ReducedBy == "MedicalCentre")
                        n = Math.Max(1, (int)Math.Round(n * (1 - 0.06 * Facilities.FacilityRules.WorkingLevel(club.Facilities[Facility.MedicalCentre], d.FacilityRules))));
                    n = Math.Min(n, fit.Count);
                    if (n == 0) return false;
                    foreach (var p in fit.OrderBy(_ => rng.NextDouble()).Take(n)) p.Energy = Math.Max(0, p.Energy - ev.Amount);
                    values["n"] = n.ToString();
                    if (user && ev.Id == "EVT-FLU") Inbox.InboxEngine.Queue(w, "MSG-ILLNESS", Facility.MedicalCentre, d, ("n", n.ToString()));
                    break;
                }
                case "injury":
                {
                    var fit = squad.Where(p => !p.Injured).ToList();
                    if (fit.Count == 0) return false;
                    var p = fit[rng.NextInt(0, fit.Count - 1)];
                    int weeks = rng.NextInt(ev.WeeksMin, ev.WeeksMax);
                    p.Injury = new Injury { TypeId = ev.Id, Name = "accident", WeeksLeft = weeks, TotalWeeks = weeks };
                    values["player"] = p.Name; values["n"] = weeks.ToString();
                    break;
                }
                case "facility":
                {
                    var f = (Facility)Enum.Parse(typeof(Facility), ev.Facility);
                    var s = club.Facilities[f];
                    s.Condition = Math.Max(0, s.Condition - rng.Uniform(ev.AmountMin, ev.AmountMax));
                    damaged = f;
                    break;
                }
                case "money":
                {
                    if (ev.NeedsGoodForm && Wins(w, club.Id, 5) < 3) return false;
                    if (ev.ReducedBy == "Stadium" && rng.Chance(club.Facilities[Facility.Stadium].Condition / 200.0)) return false;
                    double amount = Math.Round(income * rng.Uniform(ev.ShareMin, ev.ShareMax));
                    if (amount < 0) { long pay = (long)Math.Min(club.Balance, -amount); club.Balance -= pay; club.OtherMoney -= pay; }
                    else { club.Balance += (long)amount; club.OtherMoney += (long)amount; }
                    values["amount"] = Fmt(amount);
                    break;
                }
                case "upgrade":
                {
                    var open = club.Facilities.Where(kv => kv.Value.Level < Facilities.FacilityRules.MaxLevel).OrderBy(kv => kv.Value.Level).ThenBy(kv => kv.Key).ToList();
                    if (open.Count == 0) return false;
                    open[0].Value.Level++;
                    values["facility"] = d.FacilityName(open[0].Key);
                    if (user) Inbox.InboxEngine.QueueUpgrade(w, club, open[0].Key, d);
                    break;
                }
                case "repair":
                {
                    var worst = club.Facilities.OrderBy(kv => kv.Value.Condition).ThenBy(kv => kv.Key).First();
                    if (worst.Value.Condition >= 95) return false;
                    worst.Value.Condition = 100;
                    values["facility"] = d.FacilityName(worst.Key);
                    break;
                }
                case "reputation":
                    club.Reputation = Math.Min(99, club.Reputation + (int)ev.Reputation);
                    club.FanMood = Board.Clamp(club.FanMood + ev.FanMood, 0, 100);
                    break;
                case "prospect":
                {
                    if (squad.Count >= d.Development.MaxSquadSize) return false;
                    var before = new HashSet<string>(w.Players.Select(p => p.Id));
                    new WorldGenerator(d).AddAcademyIntake(w, club, rng, 1);
                    var kid = w.Players.First(p => !before.Contains(p.Id));
                    kid.Potential = Math.Min(99, kid.Potential + ev.PotentialBonus);
                    w.Market.SquadsChanged();
                    values["player"] = kid.Name;
                    break;
                }
                case "store":
                {
                    double store = Money.Finance.SeasonIncome(club, w.MoneyKey(club), 1, 1, 0, d.Finance, d.FacilityRules).Store * ev.Share;
                    club.Balance += (long)Math.Round(store); club.OtherMoney += (long)Math.Round(store);
                    values["amount"] = Fmt(store);
                    break;
                }
                case "disaster":
                {
                    var f = club.Facilities.Keys.OrderBy(k => k).ToList()[rng.NextInt(0, club.Facilities.Count - 1)];
                    club.Facilities[f].Condition = Math.Max(0, club.Facilities[f].Condition - ev.Amount);
                    double gift = Math.Round(income * ev.DonationShare);
                    club.Balance += (long)gift; club.OtherMoney += (long)gift;
                    values["facility"] = d.FacilityName(f); values["amount"] = Fmt(gift);
                    damaged = f;
                    break;
                }
                case "bid":
                {
                    var star = squad.Where(p => p.LoanFromClubId == null).OrderByDescending(p => Transfers.Pricing.Rating(p, d)).FirstOrDefault();
                    var buyer = w.Clubs.Where(x => x.Id != club.Id && x.Reputation > club.Reputation && Transfers.Market.Squad(w, x.Id).Count < d.Development.MaxSquadSize)
                        .OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
                    if (star == null || buyer.Count == 0 || squad.Count <= d.Development.MinSquadSize) return false;
                    var b = buyer[rng.NextInt(0, buyer.Count - 1)];
                    long fee = (long)Math.Round(1.1 * Transfers.Pricing.Value(w, star, d));
                    if (b.Balance < fee) return false;
                    values["club"] = b.Name; values["player"] = star.Name; values["amount"] = Fmt(fee);
                    return Choice(w, club, ev, values, new Dictionary<string, string> { ["player"] = star.Id, ["buyer"] = b.Id, ["fee"] = fee.ToString() }, rng, d);
                }
                case "naming":
                    values["amount"] = Fmt(income * ev.Share);
                    return Choice(w, club, ev, values, new Dictionary<string, string>(), rng, d);
                case "charity":
                case "interview":
                    return Choice(w, club, ev, values, new Dictionary<string, string>(), rng, d);
                default:
                    return false;
            }
            Record(w, club, ev);
            if (user)
            {
                Inbox.InboxEngine.Post(w, Fill(ev.Text, values), "Club news");
                if (damaged != null)
                {
                    var v = new List<(string, string)> { ("facility", d.FacilityName(damaged.Value)), ("event", ev.Id == "EVT-STORM" ? "A storm" : ev.Id == "EVT-FLOOD" ? "A flood" : "A disaster"),
                                                         ("n", Math.Floor(club.Facilities[damaged.Value].Condition).ToString("0")) };
                    if (ev.Id == "EVT-STORM") v.Add(("storm", "yes"));
                    Inbox.InboxEngine.Queue(w, "MSG-EVENT-DAMAGE", damaged.Value, d, v.ToArray());
                }
            }
            return true;
        }

        private static void Record(GameWorld w, Club club, EventDef ev)
        {
            if (!w.ClubEvents.TryGetValue(club.Id, out var log)) log = w.ClubEvents[club.Id] = new List<EventRecord>();
            log.Add(new EventRecord { Season = w.SeasonStartYear, Id = ev.Id, Kind = ev.Kind });
        }

        /// <summary>A choice event: the user decides (inbox); an AI club's choice is made for it.</summary>
        private static bool Choice(GameWorld w, Club club, EventDef ev, Dictionary<string, string> values, Dictionary<string, string> data, GameRandom rng, GameData d)
        {
            Record(w, club, ev);
            if (club.Id != w.UserClubId) { Resolve(w, club, ev, data, rng.Chance(ev.AiAcceptChance) ? 0 : ev.Options.Count - 1, rng, d); return true; }
            var k = w.Career;
            var decision = new Decision { Id = ++k.DecisionSeq, Season = w.SeasonStartYear, Week = w.Calendar.Week, EventId = ev.Id, ClubId = club.Id, Text = Fill(ev.Text, values), Options = ev.Options.ToList(), Data = data };
            k.Decisions.Add(decision);
            Inbox.InboxEngine.Post(w, decision.Text, "Decision needed", decision.Id, decision.Options);
            return true;
        }

        /// <summary>The user's answer (option index). Unanswered decisions take the last option after a week.</summary>
        public static bool Decide(GameWorld w, int decisionId, int option, GameRandom rng, GameData d)
        {
            var k = w.Career;
            var dec = k.Decisions.FirstOrDefault(x => x.Id == decisionId);
            if (dec == null || option < 0 || option >= dec.Options.Count) return false;
            k.Decisions.Remove(dec);
            var club = w.Clubs.FirstOrDefault(x => x.Id == dec.ClubId);
            if (club == null) return false;
            Resolve(w, club, d.Events.Events.First(e => e.Id == dec.EventId), dec.Data, option, rng, d);
            return true;
        }

        private static void ResolveExpiredDecisions(GameWorld w, GameRandom rng, GameData d)
        {
            foreach (var dec in w.Career.Decisions.Where(x => x.Season != w.SeasonStartYear || w.Calendar.Week - x.Week > d.Events.DecisionWeeks).ToList())
                Decide(w, dec.Id, dec.Options.Count - 1, rng, d);
        }

        private static void Resolve(GameWorld w, Club club, EventDef ev, Dictionary<string, string> data, int option, GameRandom rng, GameData d)
        {
            bool yes = option == 0;
            if (!yes) return;
            double income = Money.Finance.SeasonIncome(club, w.MoneyKey(club), 1, 1, 0, d.Finance, d.FacilityRules).Total;
            switch (ev.Effect)
            {
                case "bid":
                {
                    var p = w.Players.FirstOrDefault(x => x.Id == data["player"]);
                    var buyer = w.Clubs.FirstOrDefault(x => x.Id == data["buyer"]);
                    long fee = long.Parse(data["fee"]);
                    if (p == null || buyer == null || p.ClubId != club.Id || buyer.Balance < fee) return;
                    if (Transfers.Market.Squad(w, club.Id).Count <= d.Development.MinSquadSize || Transfers.Market.Squad(w, buyer.Id).Count >= d.Development.MaxSquadSize) return;
                    buyer.Balance -= fee; club.Balance += fee;
                    if (!p.FormerClubIds.Contains(club.Id)) p.FormerClubIds.Add(club.Id);
                    p.ClubId = buyer.Id; p.CoachId = null;
                    w.Market.History.Add(new Transfers.TransferRecord { Day = w.Market.Day, Season = w.SeasonStartYear, PlayerId = p.Id, FromClubId = club.Id, ToClubId = buyer.Id, Fee = fee, ValueAtDeal = fee / 1.1 });
                    w.Market.SquadsChanged();
                    break;
                }
                case "charity":
                    club.Reputation = Math.Min(99, club.Reputation + (int)ev.Reputation);
                    foreach (var p in Transfers.Market.Squad(w, club.Id)) p.Energy = Math.Max(0, p.Energy - ev.Energy);
                    break;
                case "interview":
                {
                    double sign = rng.Chance(0.5) ? 1 : -1;
                    club.FanMood = Board.Clamp(club.FanMood + sign * ev.FanMood, 0, 100);
                    if (club.Id == w.UserClubId) w.Career.Reputation = Board.Clamp(w.Career.Reputation + sign, 1, 100);
                    break;
                }
                case "naming":
                    club.Balance += (long)Math.Round(income * ev.Share); club.OtherMoney += (long)Math.Round(income * ev.Share);
                    club.FanMood = Board.Clamp(club.FanMood + ev.FanMood, 0, 100);
                    break;
            }
        }

        private static int Wins(GameWorld w, string clubId, int n)
        {
            var list = new List<(int week, bool won)>();
            foreach (var run in w.Calendar.Runs)
                for (int i = 0; i < run.PlayedRounds.Count; i++)
                    foreach (var m in run.PlayedRounds[i].Where(m => m.Home == clubId || m.Away == clubId))
                        list.Add((w.Calendar.RoundWeeks[run.CompetitionId][i], m.Home == clubId ? m.HomeGoals > m.AwayGoals : m.AwayGoals > m.HomeGoals));
            return list.OrderByDescending(x => x.week).Take(n).Count(x => x.won);
        }

        private static string Fill(string text, Dictionary<string, string> values)
            => System.Text.RegularExpressions.Regex.Replace(text, @"\[([a-z]+)\]", m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }
}
