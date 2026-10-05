using System.Diagnostics;
using System.Text;

namespace VdHelper.Core.Checks;

/// <summary>
/// Runs read-only PowerShell queries. Windows has no stable .NET API for firewall rules or
/// network profiles, so those checks go through here; anything NetworkInformation can answer is
/// answered in-process instead of paying a ~700 ms process spawn.
/// </summary>
public static class PowerShellRunner
{
    public sealed record Result(int ExitCode, string StdOut, string StdErr, bool TimedOut = false)
    {
        public bool Ok => ExitCode == 0 && !TimedOut;
        public string Combined => string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdOut + "\n[stderr] " + StdErr;
    }

    /// <param name="dryRun">When true the script text is returned instead of executed.</param>
    public static Task<Result> RunAsync(string script, bool dryRun = false, CancellationToken ct = default)
        => RunAsync(script, dryRun, DefaultTimeoutMs, ct);

    private const int DefaultTimeoutMs = 60_000;

    /// <summary>
    /// The timeout matters for repairs: an elevated fix raises a UAC prompt, and a process waiting
    /// forever on a prompt nobody answers would hang the UI thread.
    /// </summary>
    public static async Task<Result> RunAsync(string script, bool dryRun, int timeoutMs, CancellationToken ct)
    {
        if (dryRun)
            return new Result(0, script, string.Empty);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Single-quoted outer wrapper: the harness rejects `powershell -Command "...$..."` because
        // an outer bash eats the `$`. Same escape hatch as the AgentRule floor.
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);

        // This runner never elevates, and there is deliberately no flag that claims to:
        // ProcessStartInfo.Verb is silently ignored when UseShellExecute is false, which is what
        // we need for redirected output. An earlier version set Verb = "runas" here and every
        // "elevated" fix quietly ran unelevated. A repair that needs elevation says so with
        // FixAction.NeedsElevation, which the UI and both report formats surface to the user.

        using var p = new Process { StartInfo = psi };
        p.Start();
        var outTask = p.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errTask = p.StandardError.ReadToEndAsync(CancellationToken.None);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return new Result(-1, "", $"命令超时（{timeoutMs / 1000}s）。管理员操作可能正在等待 UAC 确认。", TimedOut: true);
        }

        await Task.WhenAll(outTask, errTask).ConfigureAwait(false);
        return new Result(p.ExitCode, outTask.Result, errTask.Result, TimedOut: false);
    }

    /// <summary>Runs a script and returns each non-empty trimmed line as a list.</summary>
    public static async Task<IReadOnlyList<string>> LinesAsync(string script, CancellationToken ct = default)
    {
        var r = await RunAsync(script, ct: ct).ConfigureAwait(false);
        return r.Ok
            ? r.StdOut.Split('\r', '\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
            : Array.Empty<string>();
    }
}