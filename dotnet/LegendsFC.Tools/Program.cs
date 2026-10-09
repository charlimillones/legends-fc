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
