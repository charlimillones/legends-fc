// Legends FC dev bridge (editor only). Lets the developer drive the editor through small files in the project's Logs folder,
// so code can be compiled, played and checked without clicking around:
//   Logs/lfc-refresh.flag  -> reimport changed files and recompile
//   Logs/lfc-play.flag     -> enter Play mode          Logs/lfc-stop.flag -> leave Play mode
//   Logs/lfc-shot.flag     -> capture the Game view to Logs/lfc-shot.png (Play mode)
//   Logs/lfc-tap.flag      -> press the button whose label is the file's text (Play mode)
//   Logs/lfc-type.flag     -> put the file's text into the first input field on screen (Play mode)
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

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < _next) return;
        _next = EditorApplication.timeSinceStartup + 1.0;
        if (Take("lfc-refresh.flag", out _)) { Append("lfc-compile.txt", $"{Now()} refresh requested\n"); AssetDatabase.Refresh(); }
        if (Take("lfc-play.flag", out _)) EditorApplication.isPlaying = true;
        if (Take("lfc-stop.flag", out _)) EditorApplication.isPlaying = false;
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
