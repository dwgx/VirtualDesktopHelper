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
    public sealed record Result(int ExitCode, string StdOut, string StdErr)
    {
        public bool Ok => ExitCode == 0;
        public string Combined => string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdOut + "\n[stderr] " + StdErr;
    }

    /// <param name="dryRun">When true the script text is returned instead of executed.</param>
    public static async Task<Result> RunAsync(string script, bool asAdministrator = false, bool dryRun = false, CancellationToken ct = default)
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

        if (asAdministrator)
        {
            psi.Verb = "runas";
            psi.UseShellExecute = false;
        }

        using var p = new Process { StartInfo = psi };
        p.Start();
        var outTask = p.StandardOutput.ReadToEndAsync(ct);
        var errTask = p.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(outTask, errTask).ConfigureAwait(false);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        return new Result(p.ExitCode, outTask.Result, errTask.Result);
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