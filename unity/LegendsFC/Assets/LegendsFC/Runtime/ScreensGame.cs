// In-game screens: home, squad, player, tactics, club, inbox, league. Placeholder look (Oct 10); all rules live in the core.
using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Career;
using LegendsFC.Core.Model;
using LegendsFC.Core.Squad;
using LegendsFC.Core.Transfers;
using UnityEngine;
using UnityEngine.UI;

namespace LegendsFC.App
{
    public static class Ui2
    {
        /// <summary>A whole row that reacts to taps.</summary>
        public static RectTransform ClickRow(Transform parent, float height, Action onClick, Color? bg = null)
        {
            var r = UIKit.Row(parent, height, 12, bg ?? UIKit.Panel, 20);
            var b = r.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => onClick());
            return r;
        }

        public static string LeagueOf(string clubId)
        {
            var w = App.I.Session.World;
            return clubId != null && w.ClubLeague.TryGetValue(clubId, out var l) ? l : null;
        }

        public static Color RatingColor(double drop) => drop <= 0.001 ? UIKit.Accent : drop <= 0.06 ? UIKit.Warn : UIKit.Bad;
    }

    public sealed class HomeScreen : UIScreen
    {
        public override void Build(RectTransform body)
        {
            var w = S.World; var club = S.UserClub; var k = S.Career;
            if (club == null) { A.Show(new JobsScreen()); return; }
            var list = UIKit.Scroll(body, 8);
            string league = Ui2.LeagueOf(club.Id);

            // Board objective
            UIKit.Title(list, "Board objective");
            bool canChoose = w.Calendar.Week < D.Board.ChooseObjectiveBeforeWeek;
            UIKit.Note(list, $"{Fmt.Cap(k.ObjectiveKind)}: finish {Fmt.Ordinal(k.ObjectiveTarget)} or better in the {Fmt.Competition(k.ObjectiveLeague)} (the board expects {Fmt.Ordinal(k.ExpectedPlace)}).", UIKit.Ink, 26);
            if (canChoose)
            {
                UIKit.Note(list, $"You can change it until week {D.Board.ChooseObjectiveBeforeWeek}. Higher stakes: a bigger wage bar and bonus if met, bigger loss of confidence if missed.");
                var row = UIKit.Row(list, 64, 10);
                foreach (var kv in S.ObjectiveOptions())
                {
                    string kind = kv.Key;
                    UIKit.Button(row, $"{Fmt.Cap(kind)}: {Fmt.Ordinal(kv.Value)} or better", () => { S.ChooseObjective(kind); A.Refresh(); }, kind == k.ObjectiveKind ? UIKit.AccentDark : UIKit.Panel2, 24, 420, 64);
                }
            }
            UIKit.Note(list, $"Board confidence {k.Confidence:0}/100   |   Manager reputation {k.Reputation:0}/100", k.Confidence < 30 ? UIKit.Bad : UIKit.Muted, 24);

            // League position
            if (league != null)
            {
                var table = Board.TableSoFar(w, league, D);
                int pos = table.FindIndex(r => r.ClubId == club.Id) + 1;
                var me = pos > 0 ? table[pos - 1] : null;
                UIKit.Gap(list, 6);
                UIKit.Title(list, Fmt.Competition(league));
                UIKit.Note(list, me == null || me.Played == 0 ? "The league hasn't started yet (league matches run from week 9 to week 50)."
                    : $"{Fmt.Ordinal(pos)} of {table.Count}, {me.Points} points after {me.Played} games (W{me.Won} D{me.Drawn} L{me.Lost}).", UIKit.Ink, 26);
            }

            // Money
            UIKit.Gap(list, 6);
            UIKit.Title(list, "Money");
            double bill = Market.WageBill(w, club.Id), room = Market.WageRoom(w, club, D);
            UIKit.Note(list, $"Balance {Fmt.Money(club.Balance)}   |   Wages {Fmt.Money(bill)} a season   |   Wage room {Fmt.Money(room)}", UIKit.Ink, 24);

            // Recent results
            UIKit.Gap(list, 6);
            UIKit.Title(list, "Recent results");
            var recent = Recent(w, club.Id, 6);
            if (recent.Count == 0) UIKit.Note(list, "No matches played yet this season.");
            foreach (var (week, comp, m) in recent)
            {
                bool home = m.Home == club.Id;
                int us = home ? m.HomeGoals : m.AwayGoals, them = home ? m.AwayGoals : m.HomeGoals;
                string res = us > them ? "W" : us < them ? "L" : m.PenaltyWinner == club.Id ? "W (pens)" : m.PenaltyWinner != null ? "L (pens)" : "D";
                var row = UIKit.Row(list, 52, 12, UIKit.Panel, 20);
                UIKit.Cell(row, $"Week {week}", 130, 22, UIKit.Muted);
                UIKit.Cell(row, Fmt.Competition(comp), 330, 22, UIKit.Muted);
                UIKit.Cell(row, $"{Fmt.Club(m.Home)} {m.HomeGoals}-{m.AwayGoals} {Fmt.Club(m.Away)}{(m.AfterExtraTime ? " (aet)" : "")}", 0, 24);
                UIKit.Cell(row, res, 120, 24, res.StartsWith("W") ? UIKit.Accent : res.StartsWith("L") ? UIKit.Bad : UIKit.Warn, TextAnchor.MiddleCenter, FontStyle.Bold);
            }

            UIKit.Gap(list, 6);
            if (k.Offers.Count > 0) UIKit.Button(list, $"{k.Offers.Count} job offer{(k.Offers.Count > 1 ? "s" : "")} - have a look", () => A.Show(new JobsScreen()), UIKit.Info, 24, -1, 60);
            if (!k.FirstProtegeDone) UIKit.Button(list, "Create your first protégé", () => A.Show(new ProtegeScreen()), UIKit.Panel2, 24, -1, 60);
            int unread = w.Inbox.Messages.Count(m => !m.Read);
            if (unread > 0) UIKit.Button(list, $"{unread} unread message{(unread > 1 ? "s" : "")} - open the inbox", () => A.Show(new InboxScreen()), UIKit.Panel2, 24, -1, 60);
            if (k.Seasons.Count > 0) UIKit.Button(list, "Last season's summary", () => A.Show(new SeasonSummaryScreen()), UIKit.Panel2, 24, -1, 60);
            UIKit.Note(list, $"Press Continue to play week {w.Calendar.Week + 1}.");
        }

        public static List<(int week, string comp, Core.Season.MatchResult m)> Recent(Core.World.GameWorld w, string clubId, int n)
        {
            var list = new List<(int, string, Core.Season.MatchResult)>();
            foreach (var run in w.Calendar.Runs)
            {
                if (!w.Calendar.RoundWeeks.TryGetValue(run.CompetitionId, out var weeks)) continue;
                for (int i = 0; i < run.PlayedRounds.Count; i++)
                    foreach (var m in run.PlayedRounds[i].Where(m => m.Home == clubId || m.Away == clubId))
                        list.Add((i < weeks.Count ? weeks[i] : 0, run.CompetitionId, m));
            }
            return list.OrderByDescending(x => x.Item1).Take(n).ToList();
        }
    }

    public sealed class SquadScreen : UIScreen
    {
        private static readonly string[] Regimes = { "light", "moderate", "heavy" };

        public override void Build(RectTransform body)
        {
            var w = S.World; var club = S.UserClub;
            var squad = Market.Squad(w, club.Id).OrderBy(p => p.MainPosition).ThenByDescending(p => Pricing.Rating(p, D)).ToList();
            var list = UIKit.Scroll(body, 4);
            UIKit.Title(list, $"Squad ({squad.Count} players)");
            UIKit.Note(list, "Tap a player for details. The regime sets training: light rests fully, heavy grows faster but tires.");
            var head = UIKit.Row(list, 36, 12, null, 20);
            foreach (var (t, wd) in new[] { ("Pos", 80f), ("Name", 0f), ("Age", 70f), ("Rating", 100f), ("Energy", 100f), ("Status", 170f), ("Contract", 120f), ("Wage", 130f), ("Regime", 170f) })
                UIKit.Cell(head, t, wd, 20, UIKit.Muted);
            foreach (var p in squad)
            {
                string id = p.Id;
                var row = Ui2.ClickRow(list, 58, () => A.Show(new PlayerScreen(id)));
                UIKit.Cell(row, p.MainPosition.ToString(), 80, 22, UIKit.Muted);
                UIKit.Cell(row, p.Name + (p.LoanFromClubId != null ? " (loan)" : ""), 0, 24);
                UIKit.Cell(row, (w.SeasonStartYear - p.BirthYear).ToString(), 70, 22);
                UIKit.Cell(row, Pricing.Rating(p, D).ToString("0"), 100, 24, UIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.Cell(row, p.Energy.ToString("0"), 100, 22, p.Energy < 60 ? UIKit.Bad : p.Energy < 80 ? UIKit.Warn : UIKit.Ink);
                string status = p.Injured ? $"Injured {Math.Ceiling(p.Injury.WeeksLeft)}w" : p.Bans.Count > 0 ? "Suspended" : "";
                UIKit.Cell(row, status, 170, 20, UIKit.Bad);
                UIKit.Cell(row, p.ContractEndYear.ToString(), 120, 22, p.ContractEndYear <= w.SeasonStartYear ? UIKit.Warn : UIKit.Ink);
                UIKit.Cell(row, Fmt.Money(p.Wage), 130, 22);
                string next = Regimes[(Array.IndexOf(Regimes, p.Regime) + 1) % Regimes.Length];
                UIKit.Button(row, Fmt.Cap(p.Regime) + (p.RestedFrom != null ? " (rest)" : ""), () => { S.SetRegime(id, next); A.Refresh(); }, UIKit.Panel2, 22, 170, 48);
            }
        }
    }

    public sealed class PlayerScreen : UIScreen
    {
        private readonly string _id;
        public PlayerScreen(string id) { _id = id; }

        public override void Build(RectTransform body)
        {
            var w = S.World;
            var p = w.Players.FirstOrDefault(x => x.Id == _id);
            var v = p == null ? null : S.ViewPlayer(_id);
            var list = UIKit.Scroll(body, 6);
            if (p == null || v == null) { UIKit.Note(list, "Your scouts don't cover this player."); return; }
            bool mine = p.ClubId == w.UserClubId;
            UIKit.Title(list, $"{v.Name}  ({v.MainPosition}{(v.OtherPositions.Count > 0 ? ", " + string.Join(", ", v.OtherPositions) : "")})");
            string rating = v.RatingLow == v.RatingHigh ? v.RatingLow.ToString() : $"{v.RatingLow}-{v.RatingHigh}";
            // A range never shows potential below what he already is.
            string potential = v.PotentialLow.HasValue ? $"{Math.Max(v.PotentialLow.Value, v.RatingLow)}-{Math.Max(v.PotentialHigh.Value, v.RatingHigh)}" : "?";
            string personality = !v.PersonalityKnown ? "?" : v.PersonalityId == null ? "None" : D.Personality(v.PersonalityId).Name;
            UIKit.Note(list, $"{Fmt.Club(v.ClubId)}   |   Age {v.Age}   |   {v.NationalityId}   |   {v.Foot} foot", UIKit.Ink, 26);
            UIKit.Note(list, $"Rating {rating}   |   Potential {potential}   |   Personality {personality}   |   Form {(v.Form > 0 ? v.Form.ToString("0.0") : "-")}", UIKit.Ink, 26);
            double value = FreeAgents.MarketValueEur(p, w.SeasonStartYear, Math.Max(0, p.ContractEndYear - w.SeasonStartYear), D);
            UIKit.Note(list, $"Value about {Fmt.Money(value)}   |   Wage {Fmt.Money(v.Wage)} a season   |   Contract to {v.ContractEndYear}{(mine ? $"   |   Energy {v.Energy:0}" : "")}{(v.Injured ? "   |   Injured" : "")}{(v.Suspended ? "   |   Suspended" : "")}", UIKit.Muted, 24);
            if (!v.Exact) UIKit.Note(list, "Ranges: your Scouting Centre level decides how exactly you see other clubs' players.", UIKit.Muted, 22);

            UIKit.Gap(list, 6);
            UIKit.Title(list, "Attributes");
            var attrs = Enum.GetValues(typeof(Attr)).Cast<Attr>().ToList();
            var show = v.MainPosition == Position.GK ? attrs.Skip(11).ToList() : attrs.Take(11).ToList();
            var grid = UIKit.Node("Attributes", list);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(420, 52); g.spacing = new Vector2(10, 6);
            UIKit.Size(grid, -1, ((show.Count + 2) / 3) * 58);
            foreach (var a in show)
            {
                int i = (int)a, lo = v.AttributeLow[i], hi = v.AttributeHigh[i];
                var cell = UIKit.Row(grid, 52, 8, UIKit.Panel, 16);
                UIKit.Cell(cell, a.ToString(), 0, 22, UIKit.Muted);
                UIKit.Cell(cell, lo == hi ? lo.ToString() : $"{lo}-{hi}", 110, 24, hi >= 75 ? UIKit.Accent : lo < 50 ? UIKit.Bad : UIKit.Ink, TextAnchor.MiddleRight, FontStyle.Bold);
            }

            UIKit.Gap(list, 6);
            var actions = UIKit.Row(list, 64, 10);
            if (mine)
            {
                foreach (var r in new[] { "light", "moderate", "heavy" })
                {
                    string reg = r;
                    UIKit.Button(actions, "Regime: " + Fmt.Cap(r), () => { S.SetRegime(_id, reg); A.Refresh(); }, p.Regime == r ? UIKit.AccentDark : UIKit.Panel2, 22, 250, 60);
                }
                UIKit.Button(actions, "Renew contract", () =>
                {
                    var t = Market.OpenRenewal(w, S.UserClub, p, D);
                    if (t == null) A.Toast("He won't talk about a new contract right now."); else A.Show(new OfferScreen(t.Id));
                }, UIKit.Panel2, 22, 240, 60);
                bool listed = w.Market.IsListed(p.Id);
                UIKit.Button(actions, listed ? "Take off the list" : "List for sale", () =>
                {
                    if (listed) Market.Unlist(w, p); else Market.List(w, p, null, S.Rng, D);
                    A.Toast(listed ? $"{p.Name} is off the transfer list." : $"{p.Name} is on the transfer list: bids arrive during the window.");
                    A.Refresh();
                }, UIKit.Panel2, 22, 240, 60);
            }
            else
            {
                UIKit.Button(actions, p.ClubId == null ? "Offer a contract" : "Make an offer", () =>
                {
                    var t = Market.OpenSigning(w, S.UserClub, p, D, out var blocked);
                    if (t == null) A.Toast(Why(blocked)); else A.Show(new OfferScreen(t.Id));
                }, UIKit.Accent, 24, 300, 60);
            }
            UIKit.Button(actions, "Back", () => A.Show(mine ? (UIScreen)new SquadScreen() : new TransfersScreen()), UIKit.Panel2, 22, 160, 60);
        }

        public static string Why(Blocked b) => b switch
        {
            Blocked.WindowClosed => "The transfer window is closed (free agents can be signed any time).",
            Blocked.Cooldown => "They walked away recently: try again next week.",
            Blocked.NotEnoughMoney => "Not enough money for that fee.",
            Blocked.OverWageBar => "That wage doesn't fit under your wage bar.",
            Blocked.SquadFull => "Your squad is full (32 players).",
            Blocked.SellerTooFewPlayers => "His club can't sell: it would have too few players.",
            Blocked.NotAvailable => "His club won't sell a key player.",
            Blocked.InvalidTerms => "Those terms aren't valid.",
            Blocked.TalksOver => "These talks are over.",
            Blocked.NotOnTheMarket => "He isn't on the market.",
            _ => "Not possible right now.",
        };
    }

    public sealed class TacticsScreen : UIScreen
    {
        private static int _selected = -1;

        public override void Build(RectTransform body)
        {
            var w = S.World; var club = S.UserClub; var t = club.Tactics;
            string league = Ui2.LeagueOf(club.Id) ?? "";
            var r = new RatingTable(D);
            var sheet = Lineups.Pick(w, club, league, r, D);
            var list = UIKit.Scroll(body, 6);
            UIKit.Title(list, "Tactics");
            var forms = UIKit.Row(list, 60, 8);
            foreach (var f in D.Squad.Formations.Keys)
            {
                string form = f;
                UIKit.Button(forms, f, () => { S.SetFormation(form); S.AutoPick(league); _selected = -1; A.Refresh(); }, f == t.Formation ? UIKit.AccentDark : UIKit.Panel2, 22, 150, 60);
            }
            var ment = UIKit.Row(list, 60, 8);
            UIKit.Button(ment, "Assistant decides", () => { S.SetAssistant(true, t.AssistantRest); A.Refresh(); }, t.AssistantMentality ? UIKit.AccentDark : UIKit.Panel2, 22, 260, 60);
            string[] names = { "Very defensive", "Defensive", "Balanced", "Attacking", "Very attacking" };
            for (int m = -2; m <= 2; m++)
            {
                int mm = m;
                UIKit.Button(ment, names[m + 2], () => { S.SetMentality(mm); A.Refresh(); }, !t.AssistantMentality && t.Mentality == m ? UIKit.AccentDark : UIKit.Panel2, 22, 220, 60);
            }
            var rest = UIKit.Row(list, 56, 8);
            UIKit.Button(rest, t.AssistantRest ? "Assistant rests tired players: on" : "Assistant rests tired players: off", () => { S.SetAssistant(t.AssistantMentality, !t.AssistantRest); A.Refresh(); }, t.AssistantRest ? UIKit.AccentDark : UIKit.Panel2, 22, 520, 56);
            UIKit.Cell(rest, t.AssistantMentality ? "   The assistant sets the mentality for each match (defensive against stronger teams)." : "", 0, 20, UIKit.Muted);
            var tools = UIKit.Row(list, 60, 8);
            UIKit.Button(tools, "Auto pick", () => { S.AutoPick(league); _selected = -1; A.Refresh(); }, UIKit.Accent, 24, 220, 60);
            UIKit.Button(tools, "Best formation", () => { S.AutoPick(league, keepFormation: false); _selected = -1; A.Refresh(); }, UIKit.Panel2, 22, 260, 60);
            var strength = Lineups.Strength(sheet, r, D.Squad);
            UIKit.Cell(tools, $"   Team strength: attack {strength.attack:0}, defence {strength.defence:0}", 0, 24, UIKit.Muted);

            UIKit.Note(list, _selected < 0 ? "Tap a starter, then tap the player to bring in (bench or reserves)." : $"Pick who goes into {sheet.Slots[_selected]}:", _selected < 0 ? UIKit.Muted : UIKit.Warn, 24);
            UIKit.Title(list, "Starting XI");
            for (int i = 0; i < sheet.Slots.Count; i++)
            {
                var p = sheet.Starters[i]; int idx = i;
                var row = Ui2.ClickRow(list, 54, () => { _selected = idx; A.Refresh(); }, idx == _selected ? UIKit.AccentDark : UIKit.Panel);
                UIKit.Cell(row, sheet.Slots[i], 90, 22, UIKit.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.Cell(row, p?.Name ?? "(nobody)", 0, 24);
                if (p != null) RatingCells(row, p, sheet.SlotPositions[i], r);
            }
            UIKit.Title(list, "Bench");
            foreach (var p in sheet.Bench) PlayerRow(list, p, r, sheet);
            UIKit.Title(list, "Reserves");
            var picked = new HashSet<string>(sheet.Starters.Where(x => x != null).Select(x => x.Id).Concat(sheet.Bench.Select(x => x.Id)));
            foreach (var p in Market.Squad(w, club.Id).Where(p => !picked.Contains(p.Id)).OrderBy(p => p.MainPosition)) PlayerRow(list, p, r, sheet);
        }

        private void RatingCells(RectTransform row, Player p, Position pos, RatingTable r)
        {
            double main = r.Main(p), at = r.At(p, pos);
            double drop = main <= 0 ? 0 : 1 - at / main;
            UIKit.Cell(row, p.MainPosition.ToString(), 80, 22, UIKit.Muted);
            UIKit.Cell(row, at.ToString("0"), 80, 24, Ui2.RatingColor(drop), TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Cell(row, $"Energy {p.Energy:0}", 150, 22, p.Energy < 60 ? UIKit.Bad : UIKit.Muted);
            UIKit.Cell(row, p.Injured ? "Injured" : p.Bans.Count > 0 ? "Suspended" : "", 130, 22, UIKit.Bad);
        }

        private void PlayerRow(RectTransform list, Player p, RatingTable r, TeamSheet sheet)
        {
            string id = p.Id;
            var row = Ui2.ClickRow(list, 50, () => Swap(id, sheet));
            UIKit.Cell(row, "", 90, 22);
            UIKit.Cell(row, p.Name, 0, 24);
            RatingCells(row, p, _selected >= 0 ? sheet.SlotPositions[_selected] : p.MainPosition, r);
        }

        private void Swap(string inId, TeamSheet sheet)
        {
            if (_selected < 0) { A.Toast("Tap a starter first."); return; }
            var starters = sheet.Starters.Select(x => x?.Id).ToList();
            var bench = sheet.Bench.Select(x => x.Id).ToList();
            string outId = starters[_selected];
            int benchIdx = bench.IndexOf(inId);
            starters[_selected] = inId;
            if (benchIdx >= 0) { if (outId != null) bench[benchIdx] = outId; else bench.RemoveAt(benchIdx); }
            if (starters.Any(x => x == null)) { A.Toast("Fill every slot first (use Auto pick)."); return; }
            try { S.SetLineup(starters, bench); }
            catch (Exception e) { A.Toast(e.Message); }
            _selected = -1;
            A.Refresh();
        }
    }

    public sealed class ClubScreen : UIScreen
    {
        public override void Build(RectTransform body)
        {
            var w = S.World; var club = S.UserClub; var fc = D.FacilityRules;
            var list = UIKit.Scroll(body, 6);
            UIKit.Title(list, "Facilities");
            UIKit.Note(list, $"Total condition {Core.Facilities.FacilityRules.TotalCondition(club):0}%. Upgrades are instant; repairs restore the condition.");
            foreach (Facility f in Enum.GetValues(typeof(Facility)))
            {
                var st = club.Facilities[f]; var fac = f;
                var row = UIKit.Row(list, 64, 12, UIKit.Panel, 20);
                UIKit.Cell(row, D.FacilityName(f), 260, 24);
                UIKit.Cell(row, $"Level {st.Level}", 120, 24, UIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.Cell(row, $"{st.Condition:0}%", 90, 24, st.Condition < 50 ? UIKit.Bad : st.Condition < 75 ? UIKit.Warn : UIKit.Accent);
                UIKit.Cell(row, st.Manager?.Name ?? "", 0, 22, UIKit.Muted);
                if (st.Level < Core.Facilities.FacilityRules.MaxLevel)
                    UIKit.Button(row, $"Upgrade {Fmt.Money(Core.Facilities.FacilityRules.UpgradePrice(st.Level + 1, fc))}", () => { var res = S.UpgradeFacility(fac); A.Toast(res.ToString()); A.Refresh(); }, UIKit.Panel2, 22, 260, 52);
                long cost = Core.Facilities.FacilityRules.RepairCost(st, fc);
                var rb = UIKit.Button(row, cost > 0 ? $"Repair {Fmt.Money(cost)}" : "No repair needed", () => { A.Toast(S.RepairFacility(fac) ? "Repaired." : "Not enough money."); A.Refresh(); }, UIKit.Panel2, 22, 260, 52);
                rb.interactable = cost > 0;
            }
            long all = Core.Facilities.FacilityRules.RepairAllCost(club, fc);
            if (all > 0) UIKit.Button(list, $"Repair all ({Fmt.Money(all)})", () => { A.Toast(S.RepairAll() ? "All repaired." : "Not enough money."); A.Refresh(); }, UIKit.Accent, 24, -1, 60);

            UIKit.Gap(list);
            var mine = S.MyCoaches;
            UIKit.Title(list, $"Coaches ({mine.Count} of {S.CoachLimit})");
            UIKit.Note(list, "Coaches speed up training for the players they're assigned. The Training Grounds level sets how many you can have.");
            foreach (var c in mine)
            {
                var row = UIKit.Row(list, 56, 12, UIKit.Panel, 20);
                int pupils = w.Players.Count(p => p.CoachId == c.Id);
                UIKit.Cell(row, c.Name, 0, 24);
                UIKit.Cell(row, Coaching.GroupName(c.Group), 200, 22, UIKit.Muted);
                UIKit.Cell(row, $"Rating {c.Rating}", 150, 22);
                UIKit.Cell(row, $"{pupils} players", 140, 22, UIKit.Muted);
                UIKit.Cell(row, $"Contract to {c.ContractEndYear}", 220, 22, UIKit.Muted);
                string id = c.Id;
                UIKit.Button(row, "Release", () => { S.ReleaseCoach(id); A.Refresh(); }, UIKit.Panel2, 20, 140, 48);
            }
            UIKit.Button(list, "Assign players to coaches automatically", () => { S.AutoAssignCoaches(); A.Toast("Players assigned."); A.Refresh(); }, UIKit.Panel2, 22, -1, 56);
            UIKit.Note(list, "Free coaches (price for 2 seasons, paid once):");
            foreach (var c in S.FreeCoaches.Take(8))
            {
                var row = UIKit.Row(list, 52, 12, UIKit.Panel, 20);
                UIKit.Cell(row, c.Name, 0, 22);
                UIKit.Cell(row, Coaching.GroupName(c.Group), 200, 22, UIKit.Muted);
                UIKit.Cell(row, $"Rating {c.Rating}", 150, 22);
                string id = c.Id;
                UIKit.Button(row, $"Hire {Fmt.Money(S.CoachPrice(id, 2))}", () => { A.Toast(S.HireCoach(id, 2).ToString()); A.Refresh(); }, UIKit.Panel2, 20, 240, 46);
            }

            UIKit.Gap(list);
            UIKit.Title(list, "Youth Academy");
            UIKit.Note(list, "One protégé a season: choose his position (everything else is random). His price is a share of his value set by the academy level.");
            if (S.Career.LastProtegeSeason == w.SeasonStartYear) UIKit.Note(list, "This season's protégé has already joined.", UIKit.Muted);
            else
            {
                var pr = UIKit.Row(list, 60, 8);
                foreach (var pos in new[] { Position.GK, Position.CB, Position.LB, Position.CM, Position.AM, Position.LW, Position.ST })
                {
                    var pp = pos;
                    UIKit.Button(pr, pos.ToString(), () =>
                    {
                        try
                        {
                            var (pl, price) = S.BuyProtege(pp, null);
                            if (pl == null) A.Toast($"Not enough money: he costs {Fmt.Money(price)}."); else { A.Toast($"{pl.Name} joined for {Fmt.Money(price)}."); A.Show(new PlayerScreen(pl.Id)); return; }
                        }
                        catch (Exception e) { A.Toast(e.Message); }
                        A.Refresh();
                    }, UIKit.Panel2, 22, 150, 56);
                }
            }

            UIKit.Gap(list);
            UIKit.Title(list, $"Scouts ({S.MyScouts.Count})");
            foreach (var sc in S.MyScouts)
            {
                var row = UIKit.Row(list, 52, 12, UIKit.Panel, 20);
                UIKit.Cell(row, sc.Name, 0, 22);
                UIKit.Cell(row, $"Rating {sc.Rating}", 150, 22);
                UIKit.Cell(row, $"Contract to {sc.ContractEndYear}", 220, 22, UIKit.Muted);
            }
            UIKit.Note(list, "Free scouts (price for 2 seasons):");
            foreach (var sc in S.FreeScouts.Take(5))
            {
                var row = UIKit.Row(list, 52, 12, UIKit.Panel, 20);
                UIKit.Cell(row, sc.Name, 0, 22);
                UIKit.Cell(row, $"Rating {sc.Rating}", 150, 22);
                string id = sc.Id;
                UIKit.Button(row, $"Hire {Fmt.Money(S.ScoutPrice(id, 2))}", () => { A.Toast(S.HireScout(id, 2).ToString()); A.Refresh(); }, UIKit.Panel2, 20, 240, 46);
            }
        }
    }

    public sealed class InboxScreen : UIScreen
    {
        public override void Build(RectTransform body)
        {
            var w = S.World;
            var list = UIKit.Scroll(body, 6);
            var top = UIKit.Row(list, 56, 10);
            UIKit.Cell(top, $"Inbox ({w.Inbox.Messages.Count(m => !m.Read)} unread)", 0, 32, UIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Button(top, "Mark all read", () => { Core.Inbox.InboxEngine.MarkAllRead(w); A.Refresh(); }, UIKit.Panel2, 22, 240, 52);
            var open = new HashSet<int>(S.Career.Decisions.Select(d => d.Id));
            foreach (var m in Core.Inbox.InboxEngine.List(w))
            {
                var card = UIKit.Column(list, 6, 16);
                var bg = card.gameObject.AddComponent<Image>(); bg.color = m.Read ? UIKit.Panel : UIKit.Panel2;
                UIKit.Note(card, $"{m.From}   |   {Fmt.Season(m.SeasonStartYear)}, week {m.Week}{(m.Read ? "" : "   |   NEW")}", m.Read ? UIKit.Muted : UIKit.Accent, 20);
                UIKit.Note(card, m.Text, UIKit.Ink, 24);
                if (m.DecisionId != 0 && open.Contains(m.DecisionId) && m.Options != null)
                {
                    var row = UIKit.Row(card, 56, 10);
                    for (int i = 0; i < m.Options.Count; i++)
                    {
                        int opt = i, dec = m.DecisionId;
                        UIKit.Button(row, m.Options[i], () => { A.Toast(S.Decide(dec, opt) ? "Done." : "Too late: the decision was made for you."); m.Read = true; A.Refresh(); }, UIKit.Accent, 22, 360, 52);
                    }
                }
                else if (!m.Read)
                {
                    int id = m.Id;
                    UIKit.Button(card, "Mark read", () => { Core.Inbox.InboxEngine.MarkRead(w, id); A.Refresh(); }, UIKit.Panel2, 20, -1, 44);
                }
            }
            if (w.Inbox.Messages.Count == 0) UIKit.Note(list, "No messages.");
        }
    }

    public sealed class LeagueScreen : UIScreen
    {
        private static string _league;

        public override void Build(RectTransform body)
        {
            var w = S.World;
            _league ??= Ui2.LeagueOf(w.UserClubId) ?? "ENG-1";
            if (!w.Calendar.RoundWeeks.ContainsKey(_league)) _league = Ui2.LeagueOf(w.UserClubId) ?? "ENG-1";
            var tabs = UIKit.Row(body, 60, 8);
            var tr = (RectTransform)tabs; tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(0.5f, 1); tr.sizeDelta = new Vector2(0, 60); tr.anchoredPosition = Vector2.zero;
            foreach (var c in w.Competitions.Where(c => c.Type == CompetitionType.League).OrderBy(c => c.CountryId).ThenBy(c => c.Level))
            {
                string id = c.Id;
                UIKit.Button(tabs, c.Name, () => { _league = id; A.Refresh(); }, id == _league ? UIKit.AccentDark : UIKit.Panel2, 20, -1, 60);
            }
            var area = UIKit.Node("Table", body); UIKit.Fill(area, 0, 76, 0, 0);
            var list = UIKit.Scroll(area, 3);
            var table = Board.TableSoFar(w, _league, D);
            if (table.Count == 0 || table.All(r => r.Played == 0))
            {
                UIKit.Note(list, "The league hasn't started yet. Clubs:");
                foreach (var c in w.Clubs.Where(c => w.ClubLeague.TryGetValue(c.Id, out var l) && l == _league).OrderByDescending(c => c.Reputation)) UIKit.Note(list, c.Name, UIKit.Ink);
                return;
            }
            var head = UIKit.Row(list, 36, 8, null, 20);
            foreach (var (t, wd) in new[] { ("#", 60f), ("Club", 0f), ("P", 70f), ("W", 70f), ("D", 70f), ("L", 70f), ("GF", 80f), ("GA", 80f), ("GD", 80f), ("Pts", 90f) })
                UIKit.Cell(head, t, wd, 20, UIKit.Muted);
            for (int i = 0; i < table.Count; i++)
            {
                var r = table[i];
                bool me = r.ClubId == w.UserClubId;
                var row = UIKit.Row(list, 46, 8, me ? UIKit.AccentDark : (i % 2 == 0 ? UIKit.Panel : UIKit.Bg), 20);
                UIKit.Cell(row, (i + 1).ToString(), 60, 22, UIKit.Muted);
                UIKit.Cell(row, Fmt.Club(r.ClubId), 0, 22);
                foreach (var v in new[] { r.Played, r.Won, r.Drawn, r.Lost }) UIKit.Cell(row, v.ToString(), 70, 22);
                foreach (var v in new[] { r.GoalsFor, r.GoalsAgainst, r.GoalDifference }) UIKit.Cell(row, v.ToString(), 80, 22);
                UIKit.Cell(row, r.Points.ToString(), 90, 22, UIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            }
        }
    }
}
