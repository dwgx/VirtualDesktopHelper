using VdHelper.Core.Checks;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

public static class HealthEngine
{
    public static IReadOnlyList<ICheck> CreateChecks() => HealthChecks.Create();

    /// <summary>Runs every check once, sequentially: PowerShell checks each spawn a process.</summary>
    public static async Task<HealthReport> RunAsync(CancellationToken ct = default)
    {
        var checks = CreateChecks();
        var report = new HealthReport(checks.Select(c => c.Definition).ToList());
        foreach (var check in checks)
        {
            ct.ThrowIfCancellationRequested();
            report.Results.Add(await check.RunAsync(ct).ConfigureAwait(false));
        }
        return report;
    }
}