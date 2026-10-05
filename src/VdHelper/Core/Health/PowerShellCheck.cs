using VdHelper.Core.Checks;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// A check whose evidence comes from a read-only PowerShell query. The script runs first, its
/// output becomes the evidence map, then the judge turns that into a verdict.
/// </summary>
public sealed class PowerShellCheck(
    CheckDefinition definition,
    string script,
    CheckStatus onFailure,
    Func<IReadOnlyDictionary<string, string>, bool> judge,
    Func<IReadOnlyDictionary<string, string>, string> summary,
    Func<IReadOnlyDictionary<string, string>, string> detail,
    IReadOnlyList<FixAction>? fixes,
    string? guidance) : ICheck
{
    public CheckDefinition Definition { get; } = definition;

    public async Task<CheckResult> RunAsync(CancellationToken ct = default)
    {
        Dictionary<string, string> evidence;
        try
        {
            evidence = await HealthChecks.PsEvidenceAsync(script, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new CheckResult(Definition.Id, CheckStatus.Unknown, "检查未能完成", ex.Message,
                new Dictionary<string, string> { ["_script"] = script },
                Array.Empty<FixAction>(), "无法读取系统状态，请以管理员身份重试。");
        }

        if (evidence["_exit"] != "0")
            return new CheckResult(Definition.Id, CheckStatus.Unknown, "检查未能完成",
                "查询返回非零退出码：" + evidence["_first"], evidence,
                Array.Empty<FixAction>(), "请以管理员身份运行 VDHelper 后重试。");

        // A cmdlet that does not exist on this machine writes to stderr, prints nothing to stdout
        // and still exits 0. Handing that to the judge made every "nothing matched" predicate read
        // as a pass — the tool reported a clean bill of health for a machine it could not read.
        var errLines = evidence.GetValueOrDefault("_errLines") ?? "";
        if (errLines.Length > 0 && (evidence.GetValueOrDefault("_lines") ?? "").Length == 0)
            return new CheckResult(Definition.Id, CheckStatus.Unknown, "检查未能完成（查询有错误且无输出）",
                "命令在 stderr 上报了错但没有产生任何输出，通常是**这条机器上没有该 cmdlet**或"
                + "权限不足。这一项既不算通过也不算失败——它根本没测成。",
                evidence, Array.Empty<FixAction>(),
                "以管理员身份重试；若仍然如此，多半是系统版本不提供这条查询。");

        var ok = judge(evidence);
        return new CheckResult(
            Definition.Id,
            ok ? CheckStatus.Pass : onFailure,
            summary(evidence),
            ok ? "正常。" : detail(evidence),
            evidence,
            ok ? Array.Empty<FixAction>() : fixes ?? Array.Empty<FixAction>(),
            ok ? null : guidance);
    }
}

public static class PsCheck
{
    public static ICheck Create(
        string id, string title, string question, string category, string script,
        CheckStatus onFailure,
        Func<IReadOnlyDictionary<string, string>, bool> judge,
        Func<IReadOnlyDictionary<string, string>, string> summary,
        Func<IReadOnlyDictionary<string, string>, string> detail,
        IReadOnlyList<FixAction>? fixes = null,
        string? guidance = null)
        => new PowerShellCheck(new(id, title, question, category), script, onFailure, judge, summary, detail, fixes, guidance);
}