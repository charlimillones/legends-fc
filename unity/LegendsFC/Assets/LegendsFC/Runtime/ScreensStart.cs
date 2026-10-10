// Start of the game: worlds list, new world, club pick, jobs, season summary. Placeholder look (Oct 10).
using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Model;
using LegendsFC.Core.Saves;
using UnityEngine;
using UnityEngine.UI;

namespace LegendsFC.App
{
    /// <summary>Formatting shared by the screens.</summary>
    public static class Fmt
    {
        public static string Money(double eur)
        {
            var s = App.I.Session;
            string code = s?.World.CurrencyCode ?? "EUR";
            return App.I.Data.Currencies.Format(eur, code);
        }

        public static string Season(int year) => $"{year}/{(year + 1) % 100:00}";
        public static string Ordinal(int n) => n + (n % 100 >= 11 && n % 100 <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
        public static string Club(string id) => id == null ? "-" : App.I.Session.World.Clubs.FirstOrDefault(c => c.Id == id)?.Name ?? id;
        public static string Player(string id) => id == null ? "-" : App.I.Session.World.PlayerName(id) ?? id;
        public static string Competition(string id)
        {
            var w = App.I.Session.World;
            return w.Competitions.FirstOrDefault(c => c.Id == id)?.Name ?? App.I.Data.Cups.Cups.FirstOrDefault(c => c.Id == id)?.Name ?? id;
        }
        public static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
    }

    public sealed class TitleScreen : UIScreen
    {
        public override bool InGame => false;

        public override void Build(RectTransform body)
        {
            var list = UIKit.Scroll(body, 10);
            UIKit.Title(list, "Your worlds");
            UIKit.Note(list, "Up to 5 worlds on this device. The game saves after every week.");
            var worlds = A.Store.List();
            foreach (var w in worlds)
            {
                var row = UIKit.Row(list, 96, 16, UIKit.Panel, 20);
                UIKit.Cell(row, $"{w.Name}\n{w.ClubName ?? "No club yet"}", 0, 26);
                UIKit.Cell(row, $"Season {Fmt.Season(w.SeasonStartYear)}, week {w.Week}", 380, 24, UIKit.Muted);
                string slot = w.SlotId;
                UIKit.Button(row, "Continue", () => Open(slot), UIKit.Accent, 26, 200, 70);
                UIKit.Button(row, "Delete", () => { A.Store.Delete(slot); A.Toast($"{w.Name} deleted"); A.Refresh(); }, UIKit.Panel2, 22, 140, 70);
            }
            if (worlds.Count == 0) UIKit.Note(list, "No worlds yet.");
            UIKit.Gap(list, 10);
            var b = UIKit.Button(list, "New world", () => A.Show(new NewWorldScreen()), UIKit.Accent, 30, -1, 84);
            b.interactable = A.Store.FreeSlot() != null;
            if (!b.interactable) UIKit.Note(list, "All 5 slots are used: delete a world to start a new one.", UIKit.Warn);
        }

        private void Open(string slot)
        {
            A.Run("Loading the world...", () => GameSession.Load(A.Data, A.Store, slot), s =>
            {
                A.Session = s;
                if (s.World.UserClubId != null) A.Show(new HomeScreen());
                else if (s.Career.Tenures.Count > 0) A.Show(new JobsScreen());
                else A.Show(new PickClubScreen());
            });
        }
    }

    public sealed class NewWorldScreen : UIScreen
    {
        public override bool InGame => false;
        private string _currency = "EUR";
        private string _name = "My world";

        public override void Build(RectTransform body)
        {
            var col = UIKit.Scroll(body, 12);
            UIKit.Title(col, "New world");
            UIKit.Note(col, "World name");
            var input = UIKit.Input(col, _name, "My world");
            input.onValueChanged.AddListener(v => _name = v);
            UIKit.Note(col, "Currency (money is shown in it for the whole game)");
            var grid = UIKit.Node("Currencies", col);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(190, 64); g.spacing = new Vector2(8, 8);
            int count = D.Currencies.Currencies.Count;
            UIKit.Size(grid, -1, ((count + 7) / 8) * 72);
            foreach (var c in D.Currencies.Currencies)
            {
                string code = c.Code;
                UIKit.Button(grid, $"{c.Symbol} {c.Code}", () => { _currency = code; A.Refresh(); }, code == _currency ? UIKit.AccentDark : UIKit.Panel2, 24);
            }
            UIKit.Gap(col, 8);
            UIKit.Button(col, "Create world", Create, UIKit.Accent, 30, -1, 84);
            UIKit.Button(col, "Back", () => A.Show(new TitleScreen()), UIKit.Panel2, 26, -1, 64);
        }

        private void Create()
        {
            if (A.Store.FreeSlot() == null) { A.Toast("All 5 slots are used."); return; }
            string name = string.IsNullOrWhiteSpace(_name) ? "My world" : _name.Trim();
            ulong seed = (ulong)DateTime.UtcNow.Ticks;
            string currency = _currency, now = App.NowUtc();
            A.Run("Creating the world (clubs, players, competitions)...", () => GameSession.NewWorld(A.Data, A.Store, name, seed, currency, now), s =>
            {
                if (s == null) { A.Toast("All 5 slots are used."); return; }
                A.Session = s;
                A.Show(new PickClubScreen());
            });
        }
    }

    public sealed class PickClubScreen : UIScreen
    {
        public override bool InGame => false;
        private static string _league = "ENG-1";

        public override void Build(RectTransform body)
        {
            var w = S.World;
            var tabs = UIKit.Row(body, 64, 8);
            ((RectTransform)tabs).anchorMin = new Vector2(0, 1); ((RectTransform)tabs).anchorMax = new Vector2(1, 1); ((RectTransform)tabs).pivot = new Vector2(0.5f, 1);
            ((RectTransform)tabs).sizeDelta = new Vector2(0, 64); ((RectTransform)tabs).anchoredPosition = Vector2.zero;
            foreach (var c in w.Competitions.Where(c => c.Type == CompetitionType.League).OrderBy(c => c.CountryId).ThenBy(c => c.Level))
            {
                string id = c.Id;
                UIKit.Button(tabs, c.Name, () => { _league = id; A.Refresh(); }, id == _league ? UIKit.AccentDark : UIKit.Panel2, 22, -1, 64);
            }
            var area = UIKit.Node("List", body); UIKit.Fill(area, 0, 80, 0, 0);
            var list = UIKit.Scroll(area, 6);
            UIKit.Title(list, "Pick your club");
            UIKit.Note(list, "Any club in any league. The board sets your objectives from the squad you inherit.");
            var head = UIKit.Row(list, 40, 12, null, 20);
            UIKit.Cell(head, "Club", 0, 22, UIKit.Muted); UIKit.Cell(head, "Reputation", 160, 22, UIKit.Muted); UIKit.Cell(head, "Money", 180, 22, UIKit.Muted); UIKit.Cell(head, "Stadium", 160, 22, UIKit.Muted); UIKit.Cell(head, "", 180, 22);
            foreach (var club in w.Clubs.Where(c => w.ClubLeague.TryGetValue(c.Id, out var l) && l == _league).OrderByDescending(c => c.Reputation))
            {
                var row = UIKit.Row(list, 72, 12, UIKit.Panel, 20);
                UIKit.Cell(row, club.Name, 0, 26);
                UIKit.Cell(row, club.Reputation.ToString(), 160, 24);
                UIKit.Cell(row, Fmt.Money(club.Balance), 180, 24);
                UIKit.Cell(row, club.StadiumCapacity.ToString("N0"), 160, 24);
                string id = club.Id;
                UIKit.Button(row, "Manage", () => Pick(id), UIKit.Accent, 24, 180, 56);
            }
        }

        private void Pick(string clubId)
        {
            string now = App.NowUtc();
            var s = S;
            A.Run("Meeting the board...", () => { s.PickClub(clubId, now); return true; }, _ => A.Show(s.Career.FirstProtegeDone ? (UIScreen)new HomeScreen() : new ProtegeScreen()));
        }
    }

    public sealed class JobsScreen : UIScreen
    {
        public override void Build(RectTransform body)
        {
            var w = S.World; var k = S.Career;
            var list = UIKit.Scroll(body, 8);
            UIKit.Title(list, w.UserClubId == null ? "Out of work" : "Jobs");
            UIKit.Note(list, $"Manager reputation {k.Reputation:0}/100. The world plays on while you look for a club (press Continue to wait a week).");
            UIKit.Title(list, "Offers");
            if (k.Offers.Count == 0) UIKit.Note(list, "No offers right now.");
            foreach (var o in k.Offers)
            {
                var club = w.Clubs.First(c => c.Id == o.ClubId);
                var row = UIKit.Row(list, 72, 12, UIKit.Panel, 20);
                UIKit.Cell(row, club.Name, 0, 26);
                UIKit.Cell(row, Fmt.Competition(w.ClubLeague.TryGetValue(club.Id, out var l) ? l : null), 300, 22, UIKit.Muted);
                UIKit.Cell(row, $"Reputation {club.Reputation}", 200, 22, UIKit.Muted);
                string id = club.Id;
                UIKit.Button(row, "Accept", () => { if (S.AcceptJob(id)) { A.Toast($"Welcome to {club.Name}"); A.Show(new HomeScreen()); } }, UIKit.Accent, 24, 170, 56);
            }
            UIKit.Gap(list);
            UIKit.Title(list, "Apply to a club");
            UIKit.Note(list, "Acceptance isn't guaranteed: the chance depends on your reputation against the club's.");
            var near = w.Clubs.Where(c => c.Id != w.UserClubId && w.ClubLeague.TryGetValue(c.Id, out var l) && l != null)
                .OrderBy(c => Math.Abs(c.Reputation - k.Reputation)).Take(12).OrderByDescending(c => c.Reputation);
            foreach (var club in near)
            {
                var row = UIKit.Row(list, 64, 12, UIKit.Panel, 20);
                UIKit.Cell(row, club.Name, 0, 24);
                UIKit.Cell(row, Fmt.Competition(w.ClubLeague[club.Id]), 300, 22, UIKit.Muted);
                UIKit.Cell(row, $"Chance {S.ApplyChance(club.Id):P0}", 180, 22);
                string id = club.Id;
                UIKit.Button(row, "Apply", () =>
                {
                    if (S.ApplyForJob(id)) { A.Toast($"{club.Name} said yes."); A.Show(new HomeScreen()); }
                    else { A.Toast($"{club.Name} said no."); A.Refresh(); }
                }, UIKit.Panel2, 22, 150, 52);
            }
        }
    }

    public sealed class SeasonSummaryScreen : UIScreen
    {
        public override void Build(RectTransform body)
        {
            var w = S.World; var k = S.Career;
            var list = UIKit.Scroll(body, 8);
            var sum = k.Seasons.LastOrDefault();
            if (sum == null) { UIKit.Note(list, "No season finished yet."); return; }
            UIKit.Title(list, $"Season {Fmt.Season(sum.Season)} summary");
            if (sum.LeagueId != null)
                UIKit.Note(list, $"{Fmt.Competition(sum.LeagueId)}: {Fmt.Ordinal(sum.Position)} of {sum.Teams}   W{sum.Won} D{sum.Drawn} L{sum.Lost}, goals {sum.GoalsFor}-{sum.GoalsAgainst}", UIKit.Ink, 28);
            if (sum.Promoted) UIKit.Note(list, "Promoted!", UIKit.Accent, 30);
            if (sum.Relegated) UIKit.Note(list, "Relegated.", UIKit.Bad, 30);
            UIKit.Note(list, $"Objective ({sum.ObjectiveKind}): finish {Fmt.Ordinal(sum.ObjectiveTarget)} or better - {(sum.ObjectiveMet ? "met" : "missed")}", sum.ObjectiveMet ? UIKit.Accent : UIKit.Bad, 26);
            foreach (var c in sum.Cups) UIKit.Note(list, $"{Fmt.Competition(c.CompetitionId)}: {(c.Won ? "WON" : c.Stage)}", c.Won ? UIKit.Accent : UIKit.Ink);
            foreach (var t in sum.Titles) UIKit.Note(list, "Title: " + t, UIKit.Accent, 26);
            if (sum.TopScorerId != null) UIKit.Note(list, $"Top scorer: {Fmt.Player(sum.TopScorerId)} ({sum.TopScorerGoals} goals)");
            if (!string.IsNullOrEmpty(sum.BiggestWin)) UIKit.Note(list, "Biggest win: " + sum.BiggestWin);
            if (!string.IsNullOrEmpty(sum.BiggestDefeat)) UIKit.Note(list, "Biggest defeat: " + sum.BiggestDefeat);
            UIKit.Note(list, $"Board confidence {k.Confidence:0}/100, manager reputation {k.Reputation:0}/100");
            UIKit.Gap(list);
            UIKit.Title(list, "Awards");
            string league = sum.LeagueId;
            foreach (var a in S.AwardsOf(sum.Season).Where(a => a.Scope == "WORLD" || a.Scope == league))
                UIKit.Note(list, $"{(a.Scope == "WORLD" ? "World" : Fmt.Competition(a.Scope))} - {a.Award}: {Fmt.Player(a.PlayerId)} ({Fmt.Club(a.ClubId)}, {a.Value:0.##})", UIKit.Ink);
            UIKit.Gap(list);
            UIKit.Button(list, "Back to the club", () => A.Show(new HomeScreen()), UIKit.Accent, 28, -1, 72);
        }
    }
}

namespace LegendsFC.App
{
    /// <summary>The first protégé (confirmed Oct 7): free and fully custom, once per career. Abilities follow the Youth Academy, a little higher.</summary>
    public sealed class ProtegeScreen : UIScreen
    {
        private string _name = "", _nation, _personality;
        private LegendsFC.Core.Model.Position _pos = LegendsFC.Core.Model.Position.ST;
        private LegendsFC.Core.Model.Foot _foot = LegendsFC.Core.Model.Foot.Right;

        public override void Build(RectTransform body)
        {
            _nation ??= S.UserClub.CountryId;
            var list = UIKit.Scroll(body, 10);
            UIKit.Title(list, "Your first protégé");
            UIKit.Note(list, "A youngster you create from scratch. He joins your academy for free; how good he becomes depends on how you develop him.");
            var input = UIKit.Input(list, _name, "His name");
            input.onValueChanged.AddListener(v => _name = v);
            Grid(list, "Nationality", D.Countries.Select(c => (c.Id, c.Name)).ToList(), _nation, v => _nation = v, 6);
            Grid(list, "Position", System.Enum.GetValues(typeof(LegendsFC.Core.Model.Position)).Cast<LegendsFC.Core.Model.Position>().Select(p => (p.ToString(), p.ToString())).ToList(), _pos.ToString(), v => _pos = (LegendsFC.Core.Model.Position)System.Enum.Parse(typeof(LegendsFC.Core.Model.Position), v), 12);
            Grid(list, "Foot", new List<(string, string)> { ("Left", "Left"), ("Right", "Right") }, _foot.ToString(), v => _foot = (LegendsFC.Core.Model.Foot)System.Enum.Parse(typeof(LegendsFC.Core.Model.Foot), v), 6);
            var pers = new List<(string, string)> { (null, "None") };
            pers.AddRange(D.Personalities.Where(p => !p.ArchetypeOnly).Select(p => (p.Id, p.Name)));
            Grid(list, "Personality", pers, _personality, v => _personality = v, 5);
            if (_personality != null) UIKit.Note(list, D.Personality(_personality).Effect, UIKit.Muted);
            var row = UIKit.Row(list, 72, 10);
            UIKit.Button(row, "Create him", Create, UIKit.Accent, 28, 320, 72);
            UIKit.Button(row, "Later", () => A.Show(new HomeScreen()), UIKit.Panel2, 24, 200, 72);
        }

        private void Grid(RectTransform list, string title, List<(string id, string label)> items, string selected, System.Action<string> pick, int perRow)
        {
            UIKit.Note(list, title, UIKit.Ink, 26);
            var grid = UIKit.Node(title, list);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(1500f / perRow - 8, 56); g.spacing = new Vector2(8, 8);
            UIKit.Size(grid, -1, ((items.Count + perRow - 1) / perRow) * 64);
            foreach (var (id, label) in items)
            {
                string v = id;
                UIKit.Button(grid, label, () => { pick(v); A.Refresh(); }, v == selected ? UIKit.AccentDark : UIKit.Panel2, 20);
            }
        }

        private void Create()
        {
            if (string.IsNullOrWhiteSpace(_name)) { A.Toast("Give him a name first."); return; }
            try
            {
                var p = S.CreateFirstProtege(_name.Trim(), _nation, _pos, _foot, _personality);
                A.Toast($"{p.Name} has joined your academy.");
                A.Show(new PlayerScreen(p.Id));
            }
            catch (System.Exception e) { A.Toast(e.Message); }
        }
    }
}
