using System.Collections.ObjectModel;

namespace VdHelper.Core.Model;

/// <summary>Single outcome of one diagnostic check. </summary>
public enum CheckStatus
{
    Unknown,
    Pass,
    Warn,
    Block,
}

/// <summary>Overall verdict of the PC-side health pass. </summary>
public enum HealthVerdict
{
    Unknown,
    Streamable,
    AtRisk,
    Blocked,
}

public enum FixRisk
{
    None,
    Low,
    Medium,
    High,
}

public sealed record FixResult(bool Success, string Message, string? RollbackHint = null);

/// <summary>
/// A repair the user can apply. Every fix must be reversible: <see cref="Backup"/> says what was
/// saved, <see cref="Rollback"/> says how to undo it (ADR-003). Fixes the tool cannot undo
/// (router settings, third-party AV) are not fixes — they are guidance, exposed via
/// <see cref="Guidance"/> on <see cref="CheckResult"/> instead.
/// </summary>
public sealed record FixAction(
    string Id,
    string Title,
    string What,
    string Backup,
    string Rollback,
    FixRisk Risk,
    Func<CancellationToken, Task<FixResult>> Apply,
    /// <summary>Set when applying raises a UAC prompt. Measured: this can take 90 s waiting for a
    /// human, so the UI has to say so instead of looking hung.</summary>
    bool NeedsElevation = false);

public sealed record CheckResult(
    string Id,
    CheckStatus Status,
    string Summary,
    string Detail,
    IReadOnlyDictionary<string, string> Evidence,
    IReadOnlyList<FixAction> Fixes,
    string? Guidance = null)
{
    public bool Actionable => Fixes.Count > 0 || !string.IsNullOrWhiteSpace(Guidance);
}

public sealed record CheckDefinition(string Id, string Title, string Question, string Category);

public sealed class HealthReport
{
    private readonly List<CheckResult> _results = new();

    public HealthReport(IReadOnlyList<CheckDefinition> definitions)
    {
        foreach (var d in definitions)
            Definitions.Add(d);
    }

    public Collection<CheckDefinition> Definitions { get; } = new();
    public Collection<CheckResult> Results { get; } = new();

    /// <summary>
    /// A Block or a Warn is real evidence, so those decide the verdict on their own.
    /// <para>
    /// When nothing failed, the verdict must rest on how much was actually measured. Reporting
    /// Streamable while most checks returned Unknown is the same false all-clear as before, just
    /// diluted: thirty unreadable checks and three passes is not a healthy machine, it is a machine
    /// the tool could not read. Streamable therefore requires the measured results to outnumber the
    /// unmeasured ones.
    /// </para>
    /// </summary>
    public HealthVerdict Verdict
    {
        get
        {
            if (Results.Count == 0) return HealthVerdict.Unknown;
            if (Results.Any(r => r.Status == CheckStatus.Block)) return HealthVerdict.Blocked;
            if (Results.Any(r => r.Status == CheckStatus.Warn)) return HealthVerdict.AtRisk;

            var passed = CountBy(CheckStatus.Pass);
            var unknown = CountBy(CheckStatus.Unknown);
            return unknown >= passed ? HealthVerdict.Unknown : HealthVerdict.Streamable;
        }
    }

    /// <summary>
    /// Established VD channels right now, from the port check. When this is non-empty the
    /// headline must not claim the stream "probably will not start" — the machine is streaming.
    /// Warnings stay listed and stay true; only the claim about the current session changes.
    /// </summary>
    public IReadOnlyList<int> LiveSessionPorts =>
        Results.FirstOrDefault(r => r.Id == "session-stale")?.Evidence.TryGetValue("_livePorts", out var raw) == true
            && !string.IsNullOrWhiteSpace(raw)
            ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList()
            : Array.Empty<int>();

    public string LiveSessionPeer =>
        Results.FirstOrDefault(r => r.Id == "session-stale")?.Evidence.GetValueOrDefault("_livePeer") ?? "";

    public string VerdictText
    {
        get
        {
            if (Results.Count == 0) return "尚未体检";
            var live = LiveSessionPorts;
            if (live.Count > 0)
            {
                var scope = string.IsNullOrWhiteSpace(LiveSessionPeer) ? "" : $"（对端 {LiveSessionPeer}）";
                return Verdict == HealthVerdict.Streamable
                    ? $"串流中：{live.Count} 个通道已建立会话{scope}，体检全过"
                    : $"串流中：{live.Count} 个通道已建立会话{scope}；下面 {CountBy(CheckStatus.Warn) + CountBy(CheckStatus.Block)} 项隐患不影响当前这一局，但下次连接前值得看一眼";
            }

            var verdict = Verdict switch
            {
                HealthVerdict.Blocked => "阻断：有检查项失败，串流很可能起不来",
                HealthVerdict.AtRisk => "有隐患：能串但可能不稳或掉帧",
                HealthVerdict.Streamable => "本机网络体检通过",
                _ => (string?)null,
            };

            // "Unknown" means two very different things and collapsing them is how a tool loses
            // trust: nothing was run yet, versus everything was attempted and most of it returned
            // no result. Neither may read as good news.
            if (verdict is not null) return verdict;

            if (Results.Count == 0) return "尚未体检";
            return Results.All(r => r.Status == CheckStatus.Unknown)
                ? $"无法判定：{Results.Count} 项检查全部没有取到结果"
                  + "（PowerShell 不可用 / 权限不足 / 目标不存在都会这样）。"
                  + "**这不是通过**——这一轮什么都没测出来。"
                : $"无法判定：{CountBy(CheckStatus.Pass)} 项通过，"
                  + $"但 {CountBy(CheckStatus.Unknown)} 项没测出结果，测到的比没测到的还少。"
                  + "**这不是通过**——多数检查这一轮没有给出任何信息。";
        }
    }

    /// <summary>
    /// The three things worth doing next, in the order a person should do them.
    /// <para>
    /// A verdict alone is not actionable: "阻断" tells you that something is wrong but not which
    /// of 34 rows to touch first, and the first thing most people do with a 34-row list is close
    /// the window. Ordering is deliberate:
    /// </para>
    /// <list type="number">
    /// <item>Failures, because those are what stop a session from starting.</item>
    /// <item>Warnings that carry a repair, because those are actionable.</item>
    /// <item>Warnings that only explain something — still worth knowing, listed last, never
    /// dressed up as urgent.</item>
    /// </list>
    /// <para>
    /// A finding that a live session is currently up is deliberately excluded: telling someone
    /// their stream is broken while they are streaming it is the fastest way to lose them.
    /// </para>
    /// </summary>
    public IReadOnlyList<NextAction> NextActions
    {
        get
        {
            var live = LiveSessionPorts.Count > 0;
            var failures = Results.Where(r => r.Status == CheckStatus.Block).ToList();
            var fixable = Results.Where(r => r.Status == CheckStatus.Warn && r.Fixes.Count > 0).ToList();
            var advisory = Results.Where(r => r.Status == CheckStatus.Warn && r.Fixes.Count == 0).ToList();

            var actions = new List<NextAction>();
            foreach (var r in failures)
            {
                var fix = r.Fixes.FirstOrDefault();
                actions.Add(new NextAction(
                    r.Id,
                    DefinitionOf(r.Id)?.Title ?? r.Id,
                    fix?.Title ?? r.Summary,
                    NextActionKind.FixThisFirst,
                    live));
            }
            foreach (var r in fixable)
            {
                var fix = r.Fixes[0];
                actions.Add(new NextAction(r.Id, DefinitionOf(r.Id)?.Title ?? r.Id,
                    fix.Title, NextActionKind.ThenThis, live));
            }
            foreach (var r in advisory)
            {
                actions.Add(new NextAction(r.Id, DefinitionOf(r.Id)?.Title ?? r.Id,
                    r.Summary, NextActionKind.WorthKnowing, live));
            }
            // No cap, and that is a deliberate reversal. An earlier version showed the first six,
            // which on this machine cut the disabled VD display driver — a Warn with no repair, so
            // it sorted behind six rows of advisory noise and never appeared at all. A cap that
            // hides findings is worse than a longer list; every Warn is a real observation the
            // user paid for by running the tool.
            return actions;
        }
    }

    private CheckDefinition? DefinitionOf(string id) =>
        Definitions.FirstOrDefault(d => d.Id == id);

    private int CountBy(CheckStatus status) => Results.Count(r => r.Status == status);
}
/// <summary>One concrete next step, with enough context to act on it without hunting.</summary>
public sealed record NextAction(
    string CheckId,
    string Title,
    string What,
    NextActionKind Kind,
    bool SessionIsLive)
{
    /// <summary>Shown when a session is up right now, so urgency is not overstated.</summary>
    public string Caveat => SessionIsLive ? "当前正在串流，这一条不影响这一局" : "";
}

public enum NextActionKind
{
    /// <summary>Stops the stream from starting at all.</summary>
    FixThisFirst,

    /// <summary>Actionable, but the session can still start.</summary>
    ThenThis,

    /// <summary>Explains something. Not a repair.</summary>
    WorthKnowing,

}
