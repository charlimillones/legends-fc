// Legends FC dev bridge (editor only). Lets the developer drive the editor through small files in the project's Logs folder,
// so code can be compiled, played and checked without clicking around:
//   Logs/lfc-refresh.flag  -> reimport changed files and recompile
//   Logs/lfc-play.flag     -> enter Play mode          Logs/lfc-stop.flag -> leave Play mode
//   Logs/lfc-shot.flag     -> capture the Game view to Logs/lfc-shot.png (Play mode)
//   Logs/lfc-tap.flag      -> press the button whose label is the file's text (Play mode)
//   Logs/lfc-type.flag     -> put the file's text into the first input field on screen (Play mode)
//   Logs/lfc-build.flag    -> build the Windows version to Builds/Windows/The FC.exe (not in Play mode)
//   Logs/lfc-webbuild.flag -> build the web version to Builds/Web (not in Play mode), then switch the editor back to Windows
// Compile results go to Logs/lfc-compile.txt; errors, exceptions and "[LFC]" logs to Logs/lfc-console.txt.
// This assembly doesn't depend on the game scripts, so it keeps working when they fail to compile.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class LegendsDevBridge
{
    private static double _next;
    private static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
    private static string P(string name) => Path.Combine(Dir, name);

    static LegendsDevBridge()
    {
        Directory.CreateDirectory(Dir);
        CompilationPipeline.assemblyCompilationFinished += (assembly, messages) =>
        {
            var errors = messages.Where(m => m.type == CompilerMessageType.Error).ToList();
            var lines = messages.Select(m => $"{m.type}: {m.message}");
            Append("lfc-compile.txt", $"{Now()} {Path.GetFileName(assembly)}: {errors.Count} errors\n" + string.Join("\n", lines) + "\n");
        };
        CompilationPipeline.compilationFinished += _ => Append("lfc-compile.txt", $"{Now()} compilation finished\n");
        Application.logMessageReceivedThreaded -= OnLog;
        Application.logMessageReceivedThreaded += OnLog;
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
        Append("lfc-compile.txt", $"{Now()} editor scripts loaded (playing: {EditorApplication.isPlayingOrWillChangePlaymode})\n");
    }

    private static string Now() => DateTime.Now.ToString("HH:mm:ss");

    private static void Append(string file, string text)
    {
        try { File.AppendAllText(P(file), text); } catch { }
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || message.StartsWith("[LFC]"))
            Append("lfc-console.txt", $"{Now()} {type}: {message}\n{(type == LogType.Exception || type == LogType.Error ? stack + "\n" : "")}");
    }

    private static bool Take(string flag, out string text)
    {
        text = null;
        string path = P(flag);
        if (!File.Exists(path)) return false;
        try { text = File.ReadAllText(path).Trim(); File.Delete(path); return true; } catch { return false; }
    }

    [MenuItem("Legends FC/Build Windows version")]
    public static void BuildWindows()
    {
        var options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray(),
            locationPathName = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Windows", "The FC.exe")),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        Append("lfc-compile.txt", $"{Now()} build started\n");
        var report = UnityEditor.BuildPipeline.BuildPlayer(options);
        Append("lfc-compile.txt", $"{Now()} build {report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalSize / 1e6:F0} MB, {report.summary.totalTime.TotalSeconds:F0} s -> {options.locationPathName}\n");
    }

    /// <summary>
    /// The web version (Oct 15: Carlos plays it in his phone's browser). Builds to Builds/Web with: gzip files that the page
    /// unpacks itself (decompression fallback, so any plain web host works), 64 MB of memory growing up to 1 GB, only
    /// thrown exceptions caught, unused engine code stripped, and the full-screen phone page in Assets/WebGLTemplates/TheFC.
    /// The build makes the web the editor's active platform; afterwards the editor goes back to Windows.
    /// </summary>
    [MenuItem("Legends FC/Build Web version")]
    public static void BuildWeb()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Web"));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Append("lfc-compile.txt", $"{Now()} web build started (active platform {EditorUserBuildSettings.activeBuildTarget})\n");
        try
        {
            bool toWeb = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            Append("lfc-compile.txt", $"{Now()} switched to Web: {toWeb}, {clock.Elapsed.TotalSeconds:F0} s\n");

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.initialMemorySize = 64;
            PlayerSettings.WebGL.maximumMemorySize = 1024;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.template = "PROJECT:TheFC";
            PlayerSettings.stripEngineCode = true;
            AssetDatabase.SaveAssets();

            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray(),
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };
            var report = UnityEditor.BuildPipeline.BuildPlayer(options);
            Append("lfc-compile.txt", $"{Now()} build {report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalSize / 1e6:F1} MB, {report.summary.totalTime.TotalSeconds:F0} s -> {outDir}\n");
            if (Directory.Exists(outDir))
                foreach (var f in Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).OrderBy(x => x))
                    Append("lfc-compile.txt", $"  {Path.GetRelativePath(outDir, f).Replace('\\', '/')}  {new FileInfo(f).Length} bytes\n");
        }
        catch (Exception e)
        {
            Append("lfc-compile.txt", $"{Now()} web build failed: {e}\n");
        }
        finally
        {
            var back = System.Diagnostics.Stopwatch.StartNew();
            bool toWindows = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            Append("lfc-compile.txt", $"{Now()} switched back to Windows: {toWindows}, {back.Elapsed.TotalSeconds:F0} s (whole web build {clock.Elapsed.TotalSeconds:F0} s)\n");
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < _next) return;
        _next = EditorApplication.timeSinceStartup + 1.0;
        if (Take("lfc-refresh.flag", out _)) { Append("lfc-compile.txt", $"{Now()} refresh requested\n"); AssetDatabase.Refresh(); }
        if (Take("lfc-play.flag", out _)) EditorApplication.isPlaying = true;
        if (Take("lfc-stop.flag", out _)) EditorApplication.isPlaying = false;
        if (!EditorApplication.isPlaying && Take("lfc-build.flag", out _)) BuildWindows();
        if (!EditorApplication.isPlaying && Take("lfc-webbuild.flag", out _)) BuildWeb();
        if (!EditorApplication.isPlaying) return;
        if (Take("lfc-shot.flag", out _))
        {
            string shot = P("lfc-shot.png");
            if (File.Exists(shot)) File.Delete(shot);
            ScreenCapture.CaptureScreenshot(shot);
            Append("lfc-console.txt", $"{Now()} Log: [LFC] screenshot requested\n");
        }
        if (Take("lfc-type.flag", out var typed))
        {
            var field = UnityEngine.Object.FindObjectsByType<InputField>(FindObjectsSortMode.None).FirstOrDefault(f => f.interactable);
            if (field != null) field.text = typed;
            Append("lfc-console.txt", $"{Now()} Log: [LFC] type '{typed}': {(field != null ? "done" : "no input field")}\n");
        }
        if (Take("lfc-tap.flag", out var label))
        {
            var button = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                .FirstOrDefault(b => b.interactable && b.GetComponentsInChildren<Text>().Any(t => t.text.Trim() == label));
            if (button != null) button.onClick.Invoke();
            Append("lfc-console.txt", $"{Now()} Log: [LFC] tap '{label}': {(button != null ? "pressed" : "not found")}\n");
        }
    }
}
