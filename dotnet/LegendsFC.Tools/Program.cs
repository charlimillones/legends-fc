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
