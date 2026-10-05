using System.IO;
using System.Text.Json;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// Keeps the last N runs so the tool can answer "it worked yesterday".
/// <para>
/// The symptom corpus found that "昨天还好好的" is the most common way people ask for help, and
/// that a static checklist fails there: every box is green while the fault is real
/// (research/09-failure-corpus/02-symptom-to-rootcause.md §1, conclusion 5). A diff against the
/// single previous run helps; a timeline is what actually answers it.
/// </para>
/// </summary>
public static class HealthHistory
{
    private const int MaxRuns = 40;

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VirtualDesktopHelper", "history");

    public sealed record Entry(DateTime At, string Verdict, Dictionary<string, string> Checks);

    public sealed record Change(string Id, string Before, string After, bool IsNew);

    public sealed record Snapshot(DateTime At, HealthVerdict Verdict, string Headline, int Logic);

    /// <summary>
    /// Which verdict rules produced a stored verdict. Bump this whenever the rules change.
    /// <para>
    /// The verdict logic changed twice in one session (all-Unknown and evidence-ratio both used to
    /// report Streamable). Comparing a run produced by the old rules with one produced by the new
    /// rules shows a transition that never happened — "Streamable → Unknown" across a rules change
    /// is noise, and noise in a trend line is how people stop reading it.
    /// </para>
    /// </summary>
    public const int LogicVersion = 2;

    /// <summary>Saves this run and returns what changed since the previous one.</summary>
    public static IReadOnlyList<Change> Save(HealthReport report)
    {
        var now = new Entry(DateTime.Now, report.Verdict.ToString(),
            report.Results.ToDictionary(r => r.Id, r => r.Summary));

        var previous = Load();
        var changes = Diff(previous, now);
        Append(now, report);
        return changes;
    }

    /// <summary>Oldest-to-newest timeline of past runs, most recent last.</summary>
    public static IReadOnlyList<Snapshot> Timeline()
    {
        var list = new List<Snapshot>();
        if (!Directory.Exists(Dir)) return list;
        foreach (var file in Directory.EnumerateFiles(Dir, "run-*.json").OrderBy(f => f))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                if (!root.TryGetProperty("at", out var at)) continue;
                var when = at.GetDateTime();
                var verdict = root.TryGetProperty("verdict", out var v)
                    ? Enum.TryParse<HealthVerdict>(v.GetString(), out var parsed) ? parsed : HealthVerdict.Unknown
                    : HealthVerdict.Unknown;
                var headline = root.TryGetProperty("headline", out var h) ? h.GetString() ?? "" : "";
                var logic = root.TryGetProperty("logic", out var lg) && lg.TryGetInt32(out var lv)
                    ? lv : 0;   // files written before this field existed
                list.Add(new Snapshot(when, verdict, headline, logic));
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // a truncated history file must never break the current run
            }
        }
        return list;
    }

    /// <summary>Checks whose summary differs between the latest run and the run before it.</summary>
    public static IReadOnlyList<Change> Diff(Entry? before, Entry after)
    {
        var changes = new List<Change>();
        foreach (var (id, summary) in after.Checks)
        {
            if (before is null) { changes.Add(new Change(id, "(首次记录)", summary, true)); continue; }
            if (!before.Checks.TryGetValue(id, out var old))
            {
                changes.Add(new Change(id, "(新增检测项)", summary, true));
                continue;
            }
            if (!old.Equals(summary, StringComparison.Ordinal))
                changes.Add(new Change(id, old, summary, false));
        }
        if (before is not null)
            foreach (var id in before.Checks.Keys)
                if (!after.Checks.ContainsKey(id))
                    changes.Add(new Change(id, before.Checks[id], "(检测项消失)", false));
        return changes;
    }

    private static void Append(Entry entry, HealthReport report)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var payload = JsonSerializer.Serialize(new
            {
                at = entry.At,
                verdict = report.Verdict.ToString(),
                headline = report.VerdictText,
                logic = LogicVersion,
                checks = entry.Checks,
            });
            File.WriteAllText(Path.Combine(Dir, $"run-{entry.At:yyyyMMdd-HHmmss}.json"), payload);

            foreach (var old in Directory.EnumerateFiles(Dir, "run-*.json")
                         .OrderByDescending(f => f).Skip(MaxRuns))
                File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // history is a convenience; never let it break a diagnosis run
        }
    }

    private static Entry? Load()
    {
        try
        {
            if (!Directory.Exists(Dir)) return null;
            var newest = Directory.EnumerateFiles(Dir, "run-*.json").OrderByDescending(f => f).FirstOrDefault();
            if (newest is null) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(newest));
            var root = doc.RootElement;
            var at = root.GetProperty("at").GetDateTime();
            var checks = new Dictionary<string, string>();
            foreach (var p in root.GetProperty("checks").EnumerateObject())
                checks[p.Name] = p.Value.GetString() ?? "";
            return new Entry(at, "", checks);
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException)
        {
            return null;
        }
    }
}