using System.IO;
using LegendsFC.Core.Data;

/// <summary>Loads the real data/ folder from the repo for tests.</summary>
public static class TestData
{
    private static GameData _data;
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "data", "rules"))) dir = dir.Parent;
            return Path.Combine(dir.FullName, "data");
        }
    }
    public static GameData Data => _data ??= GameData.LoadFromDirectory(Root);
}
