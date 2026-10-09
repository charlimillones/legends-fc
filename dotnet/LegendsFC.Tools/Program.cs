using System;
using System.IO;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using LegendsFC.Core.World;

// Usage: dotnet run --project dotnet/LegendsFC.Tools -- world-report [seed]
var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "data", "rules"))) dir = dir.Parent;
var data = GameData.LoadFromDirectory(Path.Combine(dir.FullName, "data"));
ulong seed = args.Length > 1 ? ulong.Parse(args[1]) : 2026;
if (args.Length > 0 && args[0] == "protege-report")
{
    var g = new WorldGenerator(data);
    var wr = g.Generate(seed);
    Console.WriteLine("Academy level | avg potential | avg price (EUR) | price share");
    foreach (var lvl in new[] { 1, 3, 5, 8, 10 })
    {
        var club = wr.Clubs.First();
        club.Facilities[LegendsFC.Core.Model.Facility.Academy].Level = lvl;
        var rr = new LegendsFC.Core.Util.GameRandom((ulong)lvl);
        var rows = Enumerable.Range(0, 200).Select(_ => g.CreateYearlyProtege(wr, club, rr, LegendsFC.Core.Model.Position.CM, null)).ToList();
        double share = Math.Min(data.Protege.PriceShareMax, data.Protege.PriceShareBase + data.Protege.PriceSharePerLevel * lvl);
        Console.WriteLine($"{lvl,13} | {rows.Average(r => r.player.Potential),13:F1} | {rows.Average(r => (double)r.priceEur),15:N0} | {share:P0}");
    }
    return;
}

if (args.Length > 0 && args[0] == "finance-dump")
{
    // One line per club at world creation: key, rank, teams, reputation, capacity, wage bill, home games, fan mood
    var dw = new WorldGenerator(data).Generate(seed);
    foreach (var g in dw.Clubs.GroupBy(c => dw.ClubLeague[c.Id] ?? c.CountryId))
    {
        var ranked = g.OrderByDescending(c => c.Reputation).ToList();
        int home = LegendsFC.Core.Season.SeasonSimulator.HomeLeagueMatches(g.Key, ranked.Count, data);
        for (int i = 0; i < ranked.Count; i++)
        {
            var c = ranked[i];
            Console.WriteLine($"{g.Key},{i + 1},{ranked.Count},{c.Reputation},{c.StadiumCapacity},{dw.Players.Where(p => p.ClubId == c.Id).Sum(p => (double)p.Wage):F0},{home},{c.FanMood:F1},{string.Join(";", dw.Players.Where(p => p.ClubId == c.Id).Select(p => (p.Wage / data.Finance.ExpectedWageShareOfValue).ToString("F0")))}");
        }
    }
    return;
}
if (args.Length > 0 && args[0] == "finance-report")
{
    // Money over 10 seasons, per league (EUR). Usage: finance-report [seed]
    var fw = new WorldGenerator(data).Generate(seed);
    var cycle = new LegendsFC.Core.Season.SeasonCycle(data);
    var frng = new LegendsFC.Core.Util.GameRandom(seed + 1);
    string Key(Club c) => fw.ClubLeague[c.Id] ?? c.CountryId;
    var start = fw.Clubs.ToDictionary(c => c.Id, c => (double)c.Balance);
    var startKey = fw.Clubs.ToDictionary(c => c.Id, Key);
    string M(double v) => (v / 1e6).ToString("0.0") + "M";
    var startBill = fw.Clubs.ToDictionary(c => c.Id, c => fw.Players.Where(p => p.ClubId == c.Id).Sum(p => (double)p.Wage));
    LegendsFC.Core.Season.SeasonReport first = null;
    var history = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, double>>();
    for (int s = 0; s < 10; s++)
    {
        var rep = cycle.Advance(fw, frng);
        first ??= rep;
        history.Add(fw.Clubs.ToDictionary(c => c.Id, c => (double)c.Balance));
        if (s == 9)
        {
            Console.WriteLine($"Season 10 ({rep.SeasonStartYear}/{rep.SeasonStartYear + 1 - 2000}): {rep.Renewed} renewed, {rep.LeftAtContractEnd} left at contract end, {rep.FreeAgentSignings} free-agent signings");
        }
    }
    Console.WriteLine();
    Console.WriteLine("Season 1, by starting league (averages per club):");
    Console.WriteLine("League | start balance | income Y1 | wages Y1 | wages/income | start balance / wages | balance Y1 | Y5 | Y10 | in debt Y10");
    foreach (var g in fw.Clubs.GroupBy(c => startKey[c.Id]).OrderBy(g => g.Key))
    {
        var ids = g.Select(c => c.Id).ToList();
        double inc = ids.Average(id => first.Income[id].Total), wages = ids.Average(id => first.WageBill[id]);
        Console.WriteLine($"{g.Key,-6} | {M(ids.Average(id => start[id])),13} | {M(inc),9} | {M(wages),8} | {wages / inc,12:P0} | {ids.Average(id => start[id]) / ids.Average(id => startBill[id]),21:F2} | {M(ids.Average(id => history[0][id])),10} | {M(ids.Average(id => history[4][id])),6} | {M(ids.Average(id => history[9][id])),6} | {ids.Count(id => history[9][id] < 0),3}/{ids.Count}");
    }
    Console.WriteLine();
    Console.WriteLine("Wages / income, best vs worst club in each league (season 1):");
    foreach (var g in fw.Clubs.GroupBy(c => startKey[c.Id]).OrderBy(g => g.Key))
    {
        var r = g.Select(c => first.WageBill[c.Id] / first.Income[c.Id].Total).ToList();
        Console.WriteLine($"{g.Key,-6} min {r.Min():P0}  median {r.OrderBy(x => x).ElementAt(r.Count / 2):P0}  max {r.Max():P0}");
    }
    return;
}

var sw = System.Diagnostics.Stopwatch.StartNew();
var w = new WorldGenerator(data).Generate(seed);
sw.Stop();
double R(Player p) => PositionRating.Base(p.Attributes, p.MainPosition, data.PositionRatings);
var byClub = w.Players.GroupBy(p => p.ClubId).ToDictionary(g => g.Key, g => g.ToList());

Console.WriteLine($"Seed {seed}: {w.Clubs.Count} clubs, {w.Players.Count} players, generated in {sw.ElapsedMilliseconds} ms");
Console.WriteLine($"Personalities: {w.Players.Count(p => p.PersonalityId != null) * 100.0 / w.Players.Count:F1}% | A Keepers: {w.Players.Count(p => p.ArchetypeId == "ARC-RARE-A-KEEPER")}");
Console.WriteLine();
Console.WriteLine("League    Clubs  Best club (avg XI)                  Worst club (avg XI)");
foreach (var key in w.ClubLeague.Values.Where(v => v != null).Distinct().OrderBy(v => v))
{
    var clubs = w.Clubs.Where(c => w.ClubLeague[c.Id] == key)
        .Select(c => (c, xi: byClub[c.Id].Select(R).OrderByDescending(x => x).Take(11).Average())).OrderByDescending(t => t.xi).ToList();
    Console.WriteLine($"{key,-9} {clubs.Count,5}  {clubs[0].c.Name,-28} {clubs[0].xi,5:F1}   {clubs[^1].c.Name,-28} {clubs[^1].xi,5:F1}");
}
var best = w.Players.OrderByDescending(R).Take(5);
Console.WriteLine();
Console.WriteLine("Top 5 players:");
foreach (var p in best)
    Console.WriteLine($"  {p.Name,-24} {p.MainPosition,-3} {R(p),5:F1} pot {p.Potential} age {w.SeasonStartYear - p.BirthYear} ({w.Clubs.First(c => c.Id == p.ClubId).Name})");
var kids = w.Players.Where(p => w.SeasonStartYear - p.BirthYear <= 18).OrderByDescending(p => p.Potential).Take(5);
Console.WriteLine("Top 5 academy prospects:");
foreach (var p in kids)
    Console.WriteLine($"  {p.Name,-24} {p.MainPosition,-3} {R(p),5:F1} pot {p.Potential} age {w.SeasonStartYear - p.BirthYear} ({w.Clubs.First(c => c.Id == p.ClubId).Name})");

// Season summary (sim mode, leagues only)
var sim = new LegendsFC.Core.Season.SeasonSimulator(data);
var tables = sim.PlayLeagues(w, new LegendsFC.Core.Util.GameRandom(seed + 1));
Console.WriteLine();
Console.WriteLine("Sim season 2026/27 (leagues only): champion, points, draws");
foreach (var kv in tables.OrderBy(k => k.Key))
{
    var t = kv.Value;
    double draws = t.Sum(r => r.Drawn) / (double)t.Sum(r => r.Played) * 100;
    Console.WriteLine($"  {kv.Key,-6} {w.Clubs.First(c => c.Id == t[0].ClubId).Name,-30} {t[0].Points,3} pts ({t[0].Played} games)  last: {t[^1].Points,3}  draws {draws:F0}%");
}
