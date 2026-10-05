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
    Func<CancellationToken, Task<FixResult>> Apply);

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

    public HealthVerdict Verdict =>
        Results.Any(r => r.Status == CheckStatus.Block) ? HealthVerdict.Blocked
        : Results.Any(r => r.Status == CheckStatus.Warn) ? HealthVerdict.AtRisk
        : Results.Count == 0 ? HealthVerdict.Unknown
        : HealthVerdict.Streamable;

    /// <summary>
    /// Established VD channels right now, from the port check. When this is non-empty the
    /// headline must not claim the stream "probably will not start" — the machine is streaming.
    /// Warnings stay listed and stay true; only the claim about the current session changes.
    /// </summary>
    public IReadOnlyList<int> LiveSessionPorts =>
        Results.FirstOrDefault(r => r.Id == "port-vd")?.Evidence.TryGetValue("_livePorts", out var raw) == true
            && !string.IsNullOrWhiteSpace(raw)
            ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList()
            : Array.Empty<int>();

    public string LiveSessionPeer =>
        Results.FirstOrDefault(r => r.Id == "port-vd")?.Evidence.GetValueOrDefault("_livePeer") ?? "";

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

            return Verdict switch
            {
                HealthVerdict.Blocked => "阻断：有检查项失败，串流很可能起不来",
                HealthVerdict.AtRisk => "有隐患：能串但可能不稳或掉帧",
                HealthVerdict.Streamable => "本机网络体检通过",
                _ => "尚未体检",
            };
        }
    }

    private int CountBy(CheckStatus status) => Results.Count(r => r.Status == status);
}