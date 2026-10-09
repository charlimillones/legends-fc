using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.World;

namespace LegendsFC.Core.Season
{
    /// <summary>data/world/cups.json (accepted Oct 9, competitions-draft.md).</summary>
    public sealed class CupData
    {
        public CoefficientConfig Coefficient = new CoefficientConfig();
        public List<CupDef> Cups = new List<CupDef>();
    }

    public sealed class CoefficientConfig
    {
        public int Seasons = 5;
        public double Win = 2, Draw = 1, WinnerBonus = 1;
        public Dictionary<string, double> StageBonus = new Dictionary<string, double>();
    }

    public enum CupKind { DomesticKnockout, LeaguePhase, Groups, Knockout, SuperCup, SuperCupFour, TwoLegSuperCup, Intercontinental, Ranking }

    /// <summary>How a level match or tie is decided.</summary>
    public enum DrawDecider { Penalties, ExtraTime }

    /// <summary>Home rule for single-match knockout rounds.</summary>
    public enum HomeRule { Draw, LowerDivision, Neutral }

    /// <summary>One way into a competition. Exactly one of League / Title / Import / Country is set.</summary>
    public sealed class EntrySlot
    {
        /// <summary>The next clubs not yet placed, from last season's table of this league (or cup-only ranking).</summary>
        public string League;
        public int Count = 1;
        /// <summary>"COMPETITION:Title" from last season, e.g. "ENG-FA:Winner". If missing or already placed: next club from Fallback.</summary>
        public string Title;
        /// <summary>A league / ranking id, or "confed:UEFA" (best-reputation club of that confederation).</summary>
        public string Fallback;
        /// <summary>"COMPETITION:Export", filled during the season (e.g. qualifying losers).</summary>
        public string Import;
        /// <summary>Every league club of a country (domestic cups).</summary>
        public string Country;
    }

    public sealed class LateEntry
    {
        public List<string> From = new List<string>();
        public int JoinAtSize = 32;
    }

    public sealed class CupPrizes
    {
        /// <summary>Paid once per club for each stage it plays in (e.g. "League phase", "Quarter-final").</summary>
        public Dictionary<string, double> Stages = new Dictionary<string, double>();
        public List<string> WinDrawStages = new List<string>();
        public double Win, Draw, Winner;
    }

    public sealed class CupDef
    {
        public string Id, Name, CountryId, Confederation;
        public CupKind Kind;
        /// <summary>Clubs placed in one competition of a scope can't enter another of the same scope (UEFA, CONMEBOL ...).</summary>
        public string Scope;
        public List<EntrySlot> Entry = new List<EntrySlot>();
        public List<EntrySlot> Qualifying = new List<EntrySlot>();
        /// <summary>Imports used later in the format (not entrants at the start), e.g. "CONMEBOL-LIB:GroupThirds": 4.</summary>
        public Dictionary<string, int> Imports = new Dictionary<string, int>();
        public LateEntry Late;
        public int Pots = 3;
        public bool GroupThirdsDropDown;
        public HomeRule Home = HomeRule.Draw;
        public DrawDecider SingleDraw = DrawDecider.ExtraTime, TieDraw = DrawDecider.ExtraTime, FinalDraw = DrawDecider.ExtraTime;
        /// <summary>Knockout rounds with this many clubs or fewer are two-legged (0 = never).</summary>
        public int TwoLegsFromSize;
        public bool TwoLegSemis, TwoLegFinal, SemisNeutral, FinalNeutral = true;
        public List<int> Weeks = new List<int>();
        public List<int> SpreadWeeks = new List<int>();
        public CupPrizes Prizes = new CupPrizes();

        public bool IsContinental => Kind == CupKind.LeaguePhase || Kind == CupKind.Groups || Kind == CupKind.Knockout;

        /// <summary>How many clubs an import key brings (entry slots and later imports).</summary>
        public int ImportCount(string key)
            => Entry.Concat(Qualifying).Where(s => s.Import == key).Sum(s => s.Count) + (Imports.TryGetValue(key, out var n) ? n : 0);
    }

    /// <summary>A title won: competition, title name ("Winner", "Champion", "Apertura" ...), club and season.</summary>
    public sealed class TitleRecord
    {
        public string CompetitionId, Title, ClubId;
        public int SeasonStartYear;
    }

    /// <summary>What the formats know about a club, frozen at the start of the season (saved, so a replay draws the same).</summary>
    public sealed class ClubSeed
    {
        public string CountryId;
        public int Division;
        /// <summary>Coefficient (last 5 seasons), then reputation / 1000 as a tiebreak.</summary>
        public double Score;
    }

    /// <summary>
    /// Who enters each competition this season (accepted Oct 9). Continental places come from last season's tables and
    /// titles; a cup winner already placed passes the spot to the next club in that league's table (the real rule).
    /// The first season has no history: tables are by reputation and missing titles fall back the same way.
    /// </summary>
    public static class Qualification
    {
        public sealed class Entrants
        {
            public List<string> Direct = new List<string>();
            public List<string> Qualifying = new List<string>();
            public List<string> Late = new List<string>();
            public List<string> All => Direct.Concat(Qualifying).Concat(Late).ToList();
        }

        public static string Placeholder(string key, int i) => "?" + key + "#" + i;
        public static bool IsPlaceholder(string id) => id != null && id.StartsWith("?", StringComparison.Ordinal);

        public static Dictionary<string, Entrants> Resolve(GameWorld w, GameData d)
        {
            var result = new Dictionary<string, Entrants>();
            var scopes = new Dictionary<string, HashSet<string>>();
            var last = (w.LastOutcomes ?? new List<CompetitionOutcome>()).ToDictionary(o => o.CompetitionId);

            foreach (var def in d.Cups.Cups)
            {
                var placed = def.Scope != null
                    ? (scopes.TryGetValue(def.Scope, out var s) ? s : scopes[def.Scope] = new HashSet<string>())
                    : new HashSet<string>();
                var e = new Entrants();
                if (def.Kind == CupKind.Ranking)
                    e.Direct = ClubsOfCountry(w, def.CountryId, leagueOnly: false);
                else
                {
                    e.Direct = Fill(def.Entry, w, d, last, placed);
                    e.Qualifying = Fill(def.Qualifying, w, d, last, placed);
                }
                if (def.Late != null)
                {
                    var late = new HashSet<string>(def.Late.From.Where(result.ContainsKey).SelectMany(id => result[id].Direct.Concat(result[id].Qualifying)));
                    e.Late = e.Direct.Where(late.Contains).ToList();
                    e.Direct = e.Direct.Where(c => !late.Contains(c)).ToList();
                }
                result[def.Id] = e;
            }
            return result;
        }

        private static List<string> Fill(List<EntrySlot> slots, GameWorld w, GameData d, Dictionary<string, CompetitionOutcome> last, HashSet<string> placed)
        {
            var list = new List<string>();
            foreach (var slot in slots)
            {
                if (slot.Country != null) { list.AddRange(ClubsOfCountry(w, slot.Country, leagueOnly: true)); continue; }
                if (slot.Import != null) { for (int i = 0; i < slot.Count; i++) list.Add(Placeholder(slot.Import, i)); continue; }
                if (slot.League != null)
                {
                    for (int i = 0; i < slot.Count; i++)
                    {
                        var next = Table(slot.League, w, d, last).FirstOrDefault(c => !placed.Contains(c));
                        if (next == null) break;
                        placed.Add(next); list.Add(next);
                    }
                    continue;
                }
                if (slot.Title != null)
                {
                    var parts = slot.Title.Split(':');
                    string holder = last.TryGetValue(parts[0], out var o) && o.Titles.TryGetValue(parts[1], out var t) ? t : null;
                    if (holder != null && w.Clubs.Any(c => c.Id == holder) && !placed.Contains(holder)) { placed.Add(holder); list.Add(holder); continue; }
                    var fb = Fallback(slot.Fallback, w, d, last).FirstOrDefault(c => !placed.Contains(c));
                    if (fb != null) { placed.Add(fb); list.Add(fb); }
                }
            }
            return list;
        }

        private static IEnumerable<string> Fallback(string fallback, GameWorld w, GameData d, Dictionary<string, CompetitionOutcome> last)
        {
            if (fallback == null) return Enumerable.Empty<string>();
            if (fallback.StartsWith("confed:", StringComparison.Ordinal))
            {
                string confed = fallback.Substring(7);
                var countries = new HashSet<string>(w.Countries.Where(c => c.Confederation == confed).Select(c => c.Id));
                return w.Clubs.Where(c => countries.Contains(c.CountryId) && (c.Division <= 1))
                    .OrderByDescending(c => c.Reputation).ThenBy(c => c.Id, StringComparer.Ordinal).Select(c => c.Id);
            }
            return Table(fallback, w, d, last);
        }

        /// <summary>Last season's final order of a league or cup-only ranking (ARG-1: the annual table). By reputation if there's none yet.</summary>
        public static List<string> Table(string id, GameWorld w, GameData d, Dictionary<string, CompetitionOutcome> last)
        {
            if (last.TryGetValue(id, out var o))
            {
                var t = o.Tables.TryGetValue("League", out var l) ? l : o.Tables.TryGetValue("Annual", out var a) ? a : null;
                if (t != null) return t.Select(r => r.ClubId).Where(c => w.Clubs.Any(x => x.Id == c)).ToList();
            }
            var comp = w.Competitions.FirstOrDefault(c => c.Id == id);
            var cup = d.Cups.Cups.FirstOrDefault(c => c.Id == id);
            IEnumerable<Model.Club> clubs = comp != null ? w.Clubs.Where(c => w.ClubLeague.TryGetValue(c.Id, out var lg) && lg == id)
                                          : cup != null ? w.Clubs.Where(c => c.CountryId == cup.CountryId) : Enumerable.Empty<Model.Club>();
            return clubs.OrderByDescending(c => c.Reputation).ThenBy(c => c.Id, StringComparer.Ordinal).Select(c => c.Id).ToList();
        }

        private static List<string> ClubsOfCountry(GameWorld w, string countryId, bool leagueOnly)
            => w.Clubs.Where(c => c.CountryId == countryId && (!leagueOnly || (w.ClubLeague.TryGetValue(c.Id, out var l) && l != null)))
                      .Select(c => c.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();

        /// <summary>Seeds for every club, frozen at the start of the season.</summary>
        public static Dictionary<string, ClubSeed> Seeds(GameWorld w)
            => w.Clubs.ToDictionary(c => c.Id, c => new ClubSeed
            {
                CountryId = c.CountryId, Division = c.Division,
                Score = (w.Coefficients.TryGetValue(c.Id, out var pts) ? pts.Sum() : 0) + c.Reputation / 1000.0,
            });
    }
}

namespace LegendsFC.Core.Season
{
    /// <summary>Cup prize money and club coefficients from a finished season (accepted Oct 9, PROPOSAL amounts in cups.json).</summary>
    public static class CupRewards
    {
        /// <summary>
        /// Prize money per club (EUR): each stage a club plays in is paid once; wins and draws in the league phase / group stage;
        /// a bonus for the winner.
        /// </summary>
        public static Dictionary<string, double> PrizeMoney(IEnumerable<CompetitionRun> runs, GameData d)
        {
            var money = new Dictionary<string, double>();
            void Add(string club, double v) { if (club == null || v == 0) return; money.TryGetValue(club, out var m); money[club] = m + v; }
            foreach (var run in runs)
            {
                var def = d.Cups.Cups.FirstOrDefault(c => c.Id == run.CompetitionId);
                if (def?.Prizes == null) continue;
                var p = def.Prizes;
                var paid = new HashSet<string>();
                for (int i = 0; i < run.PlayedRounds.Count; i++)
                {
                    string stage = run.PlayedStages[i];
                    p.Stages.TryGetValue(stage, out var stagePrize);
                    bool winDraw = p.WinDrawStages.Contains(stage);
                    foreach (var r in run.PlayedRounds[i])
                    {
                        foreach (var club in new[] { r.Home, r.Away })
                            if (paid.Add(stage + "|" + club)) Add(club, stagePrize);
                        if (!winDraw) continue;
                        if (r.HomeGoals > r.AwayGoals) Add(r.Home, p.Win);
                        else if (r.AwayGoals > r.HomeGoals) Add(r.Away, p.Win);
                        else { Add(r.Home, p.Draw); Add(r.Away, p.Draw); }
                    }
                }
                if (run.Outcome != null && run.Outcome.Titles.TryGetValue(CupFormats.Winner, out var winner)) Add(winner, p.Winner);
            }
            return money;
        }

        /// <summary>
        /// This season's coefficient points from continental competitions: 2 a win, 1 a draw (a shootout counts as a draw),
        /// 1 for each of the quarter-final, semi-final and final reached, 1 for winning.
        /// </summary>
        public static Dictionary<string, double> SeasonPoints(IEnumerable<CompetitionRun> runs, GameData d)
        {
            var c = d.Cups.Coefficient;
            var pts = new Dictionary<string, double>();
            void Add(string club, double v) { pts.TryGetValue(club, out var m); pts[club] = m + v; }
            foreach (var run in runs)
            {
                var def = d.Cups.Cups.FirstOrDefault(x => x.Id == run.CompetitionId);
                if (def == null || !def.IsContinental) continue;
                var reached = new HashSet<string>();
                for (int i = 0; i < run.PlayedRounds.Count; i++)
                {
                    c.StageBonus.TryGetValue(run.PlayedStages[i], out var bonus);
                    foreach (var r in run.PlayedRounds[i])
                    {
                        Add(r.Home, 0); Add(r.Away, 0);
                        if (r.HomeGoals > r.AwayGoals) Add(r.Home, c.Win);
                        else if (r.AwayGoals > r.HomeGoals) Add(r.Away, c.Win);
                        else { Add(r.Home, c.Draw); Add(r.Away, c.Draw); }
                        foreach (var club in new[] { r.Home, r.Away })
                            if (bonus > 0 && reached.Add(run.PlayedStages[i] + "|" + club)) Add(club, bonus);
                    }
                }
                if (run.Outcome != null && run.Outcome.Titles.TryGetValue(CupFormats.Winner, out var winner)) Add(winner, c.WinnerBonus);
            }
            return pts;
        }

        /// <summary>Adds this season to every club's coefficient (0 if it didn't play), keeping the last N seasons.</summary>
        public static void UpdateCoefficients(World.GameWorld w, Dictionary<string, double> season, GameData d)
        {
            int keep = d.Cups.Coefficient.Seasons;
            foreach (var club in w.Clubs)
            {
                season.TryGetValue(club.Id, out var p);
                if (!w.Coefficients.TryGetValue(club.Id, out var list))
                {
                    if (p == 0) continue;
                    list = w.Coefficients[club.Id] = new List<double>();
                }
                list.Add(p);
                while (list.Count > keep) list.RemoveAt(0);
            }
        }
    }
}
