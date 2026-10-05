using System.IO;
using System.Text.Json;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// Keeps the previous run so the tool can answer "it worked yesterday". The symptom corpus
/// (research/09-failure-corpus/02-symptom-to-rootcause.md) found that "昨天还好好的" is the most
/// common way people ask for help, and that a static checklist fails there: every box is green
/// while the fault is real. A diff against the last run is what turns that into a lead.
/// </summary>
public static class HealthHistory
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VirtualDesktopHelper", "history");

    private static string CurrentPath => Path.Combine(Dir, "last.json");
    private static string PreviousPath => Path.Combine(Dir, "previous.json");

    private sealed record Entry(DateTime At, string Verdict, Dictionary<string, string> Checks);

    public sealed record Change(string Id, string Before, string After, bool IsNew);

    /// <summary>Saves this run and returns what changed since the previous one.</summary>
    public static IReadOnlyList<Change> Save(HealthReport report)
    {
        var now = Create(report);
        var previous = Read(CurrentPath);
        var changes = Diff(previous, now);
        Save(now);
        return changes;
    }

    private static IReadOnlyList<Change> Diff(Entry? before, Entry after)
    {
        var changes = new List<Change>();
        foreach (var (id, summary) in after.Checks)
        {
            if (before is null)
            {
                changes.Add(new Change(id, "(首次记录)", summary, IsNew: true));
                continue;
            }
            if (!before.Checks.TryGetValue(id, out var old))
            {
                changes.Add(new Change(id, "(新增检测项)", summary, IsNew: true));
                continue;
            }
            if (!old.Equals(summary, StringComparison.Ordinal))
                changes.Add(new Change(id, old, summary, IsNew: false));
        }
        if (before is not null)
            foreach (var id in before.Checks.Keys)
                if (!after.Checks.ContainsKey(id))
                    changes.Add(new Change(id, before.Checks[id], "(检测项消失)", IsNew: false));
        return changes;
    }

    public static DateTime? LastRunTime => Read(CurrentPath)?.At;

    private static Entry Create(HealthReport report) => new(
        DateTime.Now,
        report.Verdict.ToString(),
        report.Results.ToDictionary(r => r.Id, r => r.Summary));

    private static void Save(Entry entry)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (File.Exists(CurrentPath)) File.Copy(CurrentPath, PreviousPath, overwrite: true);
            File.WriteAllText(CurrentPath, JsonSerializer.Serialize(entry));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // history is a convenience: never let it break a diagnosis run
        }
    }

    private static Entry? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Entry>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }
}