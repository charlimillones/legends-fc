// Copy into unity/LegendsFC/Assets/Editor/ after the Unity project is created.
// Menu: Legends FC → Smoke Test. Proves the core package compiles inside Unity and the data loads.
using System.IO;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Season;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using UnityEditor;
using UnityEngine;

public static class LegendsSmokeTest
{
    [MenuItem("Legends FC/Smoke Test")]
    public static void Run()
    {
        // unity/LegendsFC → repo root → data/
        string dataRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "data"));
        var data = GameData.LoadFromDirectory(dataRoot);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var world = new WorldGenerator(data).Generate(2026);
        var report = new SeasonCycle(data).Advance(world, new GameRandom(1));
        var champ = report.Outcomes.Single(o => o.CompetitionId == "ENG-1").Titles["Champion"];
        Debug.Log($"Legends FC smoke test OK: {world.Clubs.Count} clubs, {world.Players.Count} players, " +
                  $"ENG-1 champion {world.Clubs.First(c => c.Id == champ).Name}, {sw.ElapsedMilliseconds} ms");
    }
}
