// Builds read the game data from StreamingAssets/data (the editor reads the repo's data folder directly).
// Before each build this copies the repo's data folder there (git-ignored), plus files.txt: the list of copied files, which
// the web version downloads first because a browser can't list a folder (Oct 15).
using System.Collections.Generic;
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class LegendsDataForBuilds : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        string from = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "data"));
        string to = Path.Combine(Application.dataPath, "StreamingAssets", "data");
        if (Directory.Exists(to)) Directory.Delete(to, true);
        var list = new List<string>();
        foreach (var file in Directory.GetFiles(from, "*.json", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(from, file);
            string target = Path.Combine(to, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target, true);
            list.Add(rel.Replace('\\', '/'));
        }
        list.Sort(System.StringComparer.Ordinal);
        File.WriteAllText(Path.Combine(to, "files.txt"), string.Join("\n", list) + "\n");
        Debug.Log($"[LFC] game data copied for the build: {list.Count} files to {to}");
    }
}
