using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LegendsFC.Core.Saves
{
    /// <summary>data/config/saves.json.</summary>
    public sealed class SaveConfig
    {
        public int MaxWorlds = 5;
        public int Backups = 2;
        public bool AutosaveEveryWeek = true;
        /// <summary>Transfer records kept (seasons back, 0 = all).</summary>
        public int KeepTransferSeasons = 5;
        /// <summary>Inbox messages kept (newest first; open decisions always stay; 0 = all).</summary>
        public int KeepInboxMessages = 300;
    }

    /// <summary>What the world list shows without opening the save.</summary>
    public sealed class WorldSummary
    {
        public string SlotId, Name, UserClubId, ClubName, CurrencyCode, SavedAtUtc, GameVersion;
        public int SeasonStartYear, Week;
        public long BalanceEur;
    }

    /// <summary>The save file: header, the whole world and the random state (so a reloaded game plays out exactly the same).</summary>
    public sealed class SaveFile
    {
        public int SchemaVersion = GameInfo.SaveSchemaVersion;
        public WorldSummary Summary = new WorldSummary();
        public GameWorld World;
        public ulong[] Rng;
    }

    public sealed class SaveTooNewException : Exception
    {
        public SaveTooNewException(int version) : base($"This world was saved by a newer version of the game (save format {version}, this game reads up to {GameInfo.SaveSchemaVersion}). Update the game to open it.") { }
    }

    public sealed class SaveNotFoundException : Exception
    {
        public SaveNotFoundException(string slot) : base($"No readable save for {slot}.") { }
    }

    /// <summary>
    /// Save format upgrades: each step turns a save of version N into version N+1 (on the raw JSON),
    /// so old worlds keep loading after updates. Version 1 is the first format; no steps yet.
    /// </summary>
    public static class Migrations
    {
        public static readonly Dictionary<int, Action<JObject>> Steps = new Dictionary<int, Action<JObject>>();

        /// <summary>Upgrades a save to the current version. Returns the version it started at.</summary>
        public static int Upgrade(JObject save, int current = GameInfo.SaveSchemaVersion, IDictionary<int, Action<JObject>> steps = null)
        {
            steps = steps ?? Steps;
            int start = save.Value<int?>(nameof(SaveFile.SchemaVersion)) ?? 1;
            if (start > current) throw new SaveTooNewException(start);
            for (int v = start; v < current; v++)
            {
                if (!steps.TryGetValue(v, out var step)) throw new InvalidOperationException($"No upgrade from save format {v} to {v + 1}.");
                step(save);
                save[nameof(SaveFile.SchemaVersion)] = v + 1;
            }
            return start;
        }
    }

    /// <summary>
    /// Offline saves (architecture rule 8): one file per world, compressed JSON with a schema version.
    /// Writing never touches the current save until the new one is complete on disk (temp file, then swap),
    /// and the last saves are kept as backups. Loading falls back to the newest readable copy, so a crash never loses a world.
    /// File times come from the caller (no clock inside the core).
    /// </summary>
    public sealed class SaveStore
    {
        public const string Extension = ".lfc";
        private readonly string _dir;
        private readonly SaveConfig _c;

        public static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,   // never append to lists a constructor already filled
            Formatting = Formatting.None,
        };

        public SaveStore(string rootDirectory, SaveConfig config)
        {
            _dir = Path.Combine(rootDirectory, "worlds");
            _c = config ?? new SaveConfig();
            Directory.CreateDirectory(_dir);
        }

        public int MaxWorlds => _c.MaxWorlds;
        public string PathOf(string slot) => Path.Combine(_dir, slot + Extension);
        private string SummaryPath(string slot) => Path.Combine(_dir, slot + ".summary.json");
        private string Backup(string slot, int i) => PathOf(slot) + ".bak" + i;
        private string Temp(string slot) => PathOf(slot) + ".tmp";

        /// <summary>The worlds on this device, most recently saved first.</summary>
        public List<WorldSummary> List()
        {
            var slots = Directory.GetFiles(_dir).Select(Path.GetFileName)
                .Where(f => f.StartsWith("W", StringComparison.Ordinal))
                .Select(f => f.Substring(0, 3)).Distinct(StringComparer.Ordinal);
            var list = new List<WorldSummary>();
            foreach (var slot in slots)
            {
                WorldSummary s = null;
                try { s = JsonConvert.DeserializeObject<WorldSummary>(File.ReadAllText(SummaryPath(slot)), Json); } catch { }
                if (s == null) try { s = ReadFile(Candidates(slot).First(File.Exists)).Summary; } catch { }
                if (s != null) { s.SlotId = slot; list.Add(s); }
            }
            return list.OrderByDescending(s => s.SavedAtUtc, StringComparer.Ordinal).ThenBy(s => s.SlotId, StringComparer.Ordinal).ToList();
        }

        /// <summary>The first free slot (W01 ... W05), or null when every slot is used.</summary>
        public string FreeSlot()
        {
            var used = new HashSet<string>(List().Select(s => s.SlotId));
            for (int i = 1; i <= _c.MaxWorlds; i++) { var id = "W" + i.ToString("00"); if (!used.Contains(id)) return id; }
            return null;
        }

        public void Save(string slot, GameWorld world, GameRandom rng, string name, string savedAtUtc)
        {
            var club = world.UserClubId == null ? null : world.Clubs.FirstOrDefault(c => c.Id == world.UserClubId);
            var file = new SaveFile
            {
                World = world, Rng = rng.GetState(),
                Summary = new WorldSummary
                {
                    SlotId = slot, Name = name, UserClubId = world.UserClubId, ClubName = club?.Name, CurrencyCode = world.CurrencyCode,
                    SeasonStartYear = world.SeasonStartYear, Week = world.Calendar.Week, BalanceEur = club?.Balance ?? 0,
                    SavedAtUtc = savedAtUtc, GameVersion = GameInfo.Version,
                },
            };

            // 1) The new save goes to a temp file, flushed to disk.
            string tmp = Temp(slot);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var gz = new GZipStream(fs, CompressionLevel.Fastest, leaveOpen: true))
                using (var sw = new StreamWriter(gz, new UTF8Encoding(false)))
                    JsonSerializer.Create(Json).Serialize(sw, file);
                fs.Flush(true);
            }
            // 2) Backups move down a step (the oldest is dropped).
            for (int i = _c.Backups; i >= 1; i--)
            {
                string from = i == 1 ? PathOf(slot) : Backup(slot, i - 1), to = Backup(slot, i);
                if (!File.Exists(from)) continue;
                if (File.Exists(to)) File.Delete(to);
                File.Move(from, to);
            }
            if (_c.Backups == 0 && File.Exists(PathOf(slot))) File.Delete(PathOf(slot));
            // 3) The temp file becomes the save. Until this line, the previous save is still there as backup 1.
            File.Move(tmp, PathOf(slot));
            File.WriteAllText(SummaryPath(slot), JsonConvert.SerializeObject(file.Summary, Json));
        }

        /// <summary>Loads a world: the save, else a finished temp file, else the newest backup. Old formats are upgraded.</summary>
        public SaveFile Load(string slot)
        {
            Exception last = null;
            foreach (var path in Candidates(slot).Where(File.Exists))
            {
                try { return ReadFile(path); }
                catch (SaveTooNewException) { throw; }
                catch (Exception e) { last = e; }   // damaged copy: try the next one
            }
            throw last == null ? new SaveNotFoundException(slot) : new SaveNotFoundException(slot + " (" + last.Message + ")");
        }

        public void Delete(string slot)
        {
            foreach (var p in Candidates(slot).Concat(new[] { SummaryPath(slot) }))
                if (File.Exists(p)) File.Delete(p);
        }

        private IEnumerable<string> Candidates(string slot)
        {
            yield return PathOf(slot);
            yield return Temp(slot);
            for (int i = 1; i <= Math.Max(_c.Backups, 2); i++) yield return Backup(slot, i);
        }

        /// <summary>The save format version, read from the start of the file (null when the file doesn't start with it).</summary>
        private static int? VersionOf(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var sr = new StreamReader(gz, Encoding.UTF8))
            using (var jr = new JsonTextReader(sr))
            {
                if (!jr.Read() || jr.TokenType != JsonToken.StartObject) return null;
                if (!jr.Read() || jr.TokenType != JsonToken.PropertyName || (string)jr.Value != nameof(SaveFile.SchemaVersion)) return null;
                return jr.ReadAsInt32();
            }
        }

        public static SaveFile ReadFile(string path)
        {
            // A save in the current format is read straight into the world (fast); only older formats go through the
            // JSON tree so the upgrade steps can rewrite them.
            SaveFile file;
            if (VersionOf(path) == GameInfo.SaveSchemaVersion)
            {
                using (var fs = File.OpenRead(path))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                using (var sr = new StreamReader(gz, Encoding.UTF8))
                using (var jr = new JsonTextReader(sr))
                    file = JsonSerializer.Create(Json).Deserialize<SaveFile>(jr);
            }
            else
            {
                JObject root;
                using (var fs = File.OpenRead(path))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                using (var sr = new StreamReader(gz, Encoding.UTF8))
                using (var jr = new JsonTextReader(sr))
                    root = JObject.Load(jr);
                Migrations.Upgrade(root);
                file = root.ToObject<SaveFile>(JsonSerializer.Create(Json));
            }
            if (file?.World == null || file.Rng == null || file.Rng.Length != 4) throw new InvalidDataException("The save is incomplete.");
            return file;
        }
    }
}
