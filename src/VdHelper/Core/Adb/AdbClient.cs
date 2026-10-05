using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VdHelper.Core.Adb;

/// <summary>
/// Finds adb without assuming it is on PATH (it is not, on this machine). Candidate order and
/// the "only dl.google.com is an acceptable download source" rule come from the old VDH's
/// behaviour, which had 18 candidates; the ones that actually hit are kept first.
/// </summary>
public static class AdbLocator
{
    public const string DownloadUrl =
        "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";

    private static readonly string[] CandidateGlobs =
    [
        @"%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe",
        @"%ANDROID_HOME%\platform-tools\adb.exe",
        @"%ANDROID_SDK_ROOT%\platform-tools\adb.exe",
        @"D:\Software\Android\Sdk\platform-tools\adb.exe",
        @"D:\Software\VIVE Hub\VIVE Hub\CommonTools\ADB\adb.exe",
        @"%PROGRAMFILES%\VIVE Hub\Live\ADB\adb.exe",
        @"%LOCALAPPDATA%\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe",
    ];

    private static readonly string[] RepoGlobs =
    [
        @"D:\Project\VirtualDesktop\_upstream\quest_adb_tools\adb.exe",
        @"D:\Project\VirtualDesktopHelper\tools\platform-tools\adb.exe",
    ];

    public static string? RememberedPath
    {
        get => ConfigFile.Read<string>("adbPath");
        set => ConfigFile.Write("adbPath", value);
    }

    public static IReadOnlyList<(string Source, string Path)> Probe()
    {
        var hits = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Try(string source, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var full = Environment.ExpandEnvironmentVariables(path);
            if (!File.Exists(full) || !seen.Add(full)) return;
            hits.Add((source, full));
        }

        var remembered = RememberedPath;
        if (!string.IsNullOrWhiteSpace(remembered)) Try("上次选择", remembered);

        foreach (var glob in CandidateGlobs)
        {
            if (glob.Contains('*'))
            {
                var dir = Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(glob))!;
                var parent = Path.GetDirectoryName(dir);
                if (parent is null || !Directory.Exists(parent)) continue;
                foreach (var file in Directory.EnumerateFiles(parent, Path.GetFileName(dir)))
                    Try("Android SDK 目录", Path.Combine(file, "adb.exe"));
            }
            else
            {
                Try("Android SDK / VIVE Hub", glob);
            }
        }

        foreach (var glob in RepoGlobs) Try("仓库自带", glob);

        // PATH last: an adb on PATH may be a stale wrapper, so it never wins over a real SDK copy.
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir.Trim(), "adb.exe");
            if (File.Exists(candidate)) Try("PATH", candidate);
        }

        return hits;
    }

    public static string? Find() => Probe().FirstOrDefault().Path;
}

/// <summary>Small JSON config at %AppData%\VirtualDesktopHelper\config.json (legacy convention).</summary>
public static class ConfigFile
{
    private static string Path_ =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VirtualDesktopHelper", "config.json");

    public static T? Read<T>(string key)
    {
        try
        {
            if (!File.Exists(Path_)) return default;
            var node = JsonNode.Parse(File.ReadAllText(Path_))?.AsObject();
            if (node is null || !node.TryGetPropertyValue(key, out var value) || value is null) return default;
            return value.GetValue<T>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            return default;
        }
    }

    public static void Write<T>(string key, T value)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
            var node = JsonNode.Parse(File.Exists(Path_) ? File.ReadAllText(Path_) : "{}")?.AsObject() ?? new JsonObject();
            node[key] = JsonValue.Create(value);
            File.WriteAllText(Path_, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // remembering a path is a convenience; failing to remember must never break diagnosis
        }
    }
}

/// <summary>Runs adb with ArgumentList so nothing is shell-quoted into a command line.</summary>
public sealed class AdbClient(string adbPath)
{
    public string AdbPath { get; } = adbPath;

    public sealed record Result(int ExitCode, string StdOut, string StdErr, bool TimedOut)
    {
        public bool Ok => ExitCode == 0 && !TimedOut;
        public IReadOnlyList<string> Lines =>
            StdOut.Split('\r', '\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
    }

    public async Task<Result> RunAsync(IReadOnlyList<string> args, int timeoutMs = 8000, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = AdbPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        p.Start();
        var outTask = p.StandardOutput.ReadToEndAsync(ct);
        var errTask = p.StandardError.ReadToEndAsync(ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return new Result(-1, stdout.ToString(), stderr.ToString(), TimedOut: true);
        }

        await Task.WhenAll(outTask, errTask);
        return new Result(p.ExitCode, outTask.Result, errTask.Result, TimedOut: false);
    }
}