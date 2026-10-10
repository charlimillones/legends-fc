using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendsFC.Core.Saves;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

/// <summary>Offline saves (architecture rule 8; Oct 9 proposal in data/config/saves.json).</summary>
public class SaveTests : IDisposable
{
    private static LegendsFC.Core.Data.GameData D => TestData.Data;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lfc-saves-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _out;
    public SaveTests(ITestOutputHelper o) { _out = o; }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private SaveStore Store(int max = 5, int backups = 2) => new SaveStore(_root, new SaveConfig { MaxWorlds = max, Backups = backups });
    private static string Time(int i) => "2026-10-09T21:" + (i / 60).ToString("00") + ":" + (i % 60).ToString("00") + "Z";
    private static string Snapshot(GameSession s) => JsonConvert.SerializeObject(new { s.World, Rng = s.Rng.GetState() }, SaveStore.Json);

    [Fact]
    public void RandomState_Continues_TheSameSequence()
    {
        var a = new GameRandom(42);
        for (int i = 0; i < 10; i++) a.NextUInt64();
        var b = GameRandom.FromState(a.GetState());
        Assert.Equal(Enumerable.Range(0, 20).Select(_ => a.NextUInt64()), Enumerable.Range(0, 20).Select(_ => b.NextUInt64()));
    }

    [Fact]
    public void AWorldSavedMidSeason_LoadsBackExactly()
    {
        var store = Store();
        var s = GameSession.NewWorld(D, store, "Test", 7, "GBP", Time(0));
        s.Autosave = false;
        s.PickClub(s.World.Clubs.First(c => s.World.ClubLeague[c.Id] == "ENG-1").Id, Time(1));
        for (int i = 0; i < 30; i++) s.AdvanceWeek(Time(2));
        s.Save(Time(3));
        var back = GameSession.Load(D, store, s.SlotId);
        Assert.Equal(Snapshot(s), Snapshot(back));
        Assert.Equal(30, back.World.Calendar.Week);
        Assert.Equal("Test", back.Name);
        var info = store.List().Single();
        Assert.Equal(s.World.UserClubId, info.UserClubId);
        Assert.Equal(30, info.Week);
        Assert.Equal("GBP", info.CurrencyCode);
        _out.WriteLine($"Save file: {new FileInfo(store.PathOf(s.SlotId)).Length / 1e6:F2} MB");
    }

    [Fact]
    public void SavingAndReloading_NeverChangesWhatHappens()
    {
        // Same world and choices: one career plays straight on; the other is saved and reloaded mid-season,
        // just before the season ends, and early in the next season. They must end identical.
        GameSession Play(bool reload)
        {
            var store = new SaveStore(Path.Combine(_root, reload ? "b" : "a"), D.Saves);
            var s = GameSession.NewWorld(D, store, "Determinism", 99, "EUR", Time(0));
            s.Autosave = false;
            s.PickClub(s.World.Clubs.First(c => s.World.ClubLeague[c.Id] == "ESP-1").Id, Time(1));
            for (int week = 1; week <= 52 + 12; week++)
            {
                s.AdvanceWeek(Time(week));
                if (reload && (week == 7 || week == 28 || week == 51 || week == 52 || week == 55))
                {
                    s.Save(Time(week));
                    s = GameSession.Load(D, store, s.SlotId);
                    s.Autosave = false;
                }
            }
            return s;
        }
        var straight = Play(false); var reloaded = Play(true);
        Assert.Equal(2027, straight.World.SeasonStartYear);
        Assert.Equal(12, straight.World.Calendar.Week);
        Assert.Equal(Snapshot(straight), Snapshot(reloaded));
    }

    [Fact]
    public void Autosave_AfterEveryWeek_KeepsTwoBackups()
    {
        var store = Store();
        var s = GameSession.NewWorld(D, store, null, 3, "EUR", Time(0));
        Assert.True(s.Autosave);
        for (int i = 1; i <= 4; i++) s.AdvanceWeek(Time(i));
        string main = store.PathOf(s.SlotId);
        Assert.True(File.Exists(main) && File.Exists(main + ".bak1") && File.Exists(main + ".bak2"));
        Assert.False(File.Exists(main + ".bak3") || File.Exists(main + ".tmp"));
        Assert.Equal(4, SaveStore.ReadFile(main).World.Calendar.Week);
        Assert.Equal(3, SaveStore.ReadFile(main + ".bak1").World.Calendar.Week);
        Assert.Equal(2, SaveStore.ReadFile(main + ".bak2").World.Calendar.Week);
        Assert.Equal("World 01", s.Name);
    }

    [Fact]
    public void ADamagedSave_FallsBackToTheNewestGoodCopy()
    {
        var store = Store();
        var s = GameSession.NewWorld(D, store, "Crash", 4, "EUR", Time(0));
        s.Autosave = false;
        s.AdvanceWeek(Time(1)); s.Save(Time(1));
        s.AdvanceWeek(Time(2)); s.Save(Time(2));
        string main = store.PathOf(s.SlotId);

        // A crash while writing: the file is cut short.
        var bytes = File.ReadAllBytes(main);
        File.WriteAllBytes(main, bytes.Take(bytes.Length / 3).ToArray());
        Assert.Equal(1, GameSession.Load(D, store, s.SlotId).World.Calendar.Week);       // backup 1

        // A crash between the swaps: the save is gone but the finished temp file is there.
        File.Delete(main);
        File.WriteAllBytes(main + ".tmp", bytes);
        Assert.Equal(2, GameSession.Load(D, store, s.SlotId).World.Calendar.Week);

        // Everything damaged: a clear error, never a half-loaded world.
        foreach (var f in new[] { main + ".tmp", main + ".bak1", main + ".bak2" }) if (File.Exists(f)) File.WriteAllText(f, "garbage");
        Assert.Throws<SaveNotFoundException>(() => GameSession.Load(D, store, s.SlotId));
    }

    [Fact]
    public void FiveWorlds_SortedByLastSave_DeletingFreesTheSlot()
    {
        var store = Store(max: 5);
        var made = Enumerable.Range(0, 5).Select(i => GameSession.NewWorld(D, store, "World " + i, (ulong)(100 + i), "EUR", Time(i))).ToList();
        Assert.All(made, Assert.NotNull);
        Assert.Equal(new[] { "W01", "W02", "W03", "W04", "W05" }, made.Select(m => m.SlotId));
        Assert.Null(store.FreeSlot());
        Assert.Null(GameSession.NewWorld(D, store, "Sixth", 200, "EUR", Time(9)));
        made[1].Save(Time(30));
        Assert.Equal("W02", store.List().First().SlotId);                     // most recent first
        store.Delete("W03");
        Assert.Equal(4, store.List().Count);
        Assert.Equal("W03", store.FreeSlot());
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "worlds"), "W03*"));
    }

    [Fact]
    public void OldSavesAreUpgraded_NewerSavesAreRefused()
    {
        var steps = new Dictionary<int, Action<JObject>>
        {
            [1] = o => o["Note"] = "v2",
            [2] = o => o["Note"] = o.Value<string>("Note") + "+v3",
        };
        var save = new JObject { ["SchemaVersion"] = 1 };
        Assert.Equal(1, Migrations.Upgrade(save, 3, steps));
        Assert.Equal(3, save.Value<int>("SchemaVersion"));
        Assert.Equal("v2+v3", save.Value<string>("Note"));
        Assert.Throws<SaveTooNewException>(() => Migrations.Upgrade(new JObject { ["SchemaVersion"] = 4 }, 3, steps));
        Assert.Throws<InvalidOperationException>(() => Migrations.Upgrade(new JObject { ["SchemaVersion"] = 1 }, 4, steps));

        // A real save from a future game version.
        var store = Store();
        var s = GameSession.NewWorld(D, store, "Future", 5, "EUR", Time(0));
        var file = SaveStore.ReadFile(store.PathOf(s.SlotId));
        file.SchemaVersion = LegendsFC.Core.GameInfo.SaveSchemaVersion + 1;
        using (var fs = File.Create(store.PathOf(s.SlotId)))
        using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest))
        using (var sw = new StreamWriter(gz)) JsonSerializer.Create(SaveStore.Json).Serialize(sw, file);
        Assert.Throws<SaveTooNewException>(() => GameSession.Load(D, store, s.SlotId));
    }
}
