// Builds read the game data from StreamingAssets/data (the editor reads the repo's data folder directly).
// Before each build this copies the repo's data folder there (git-ignored).
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
        foreach (var file in Directory.GetFiles(from, "*.json", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target, true);
        }
        Debug.Log("[LFC] game data copied for the build: " + to);
    }
}
