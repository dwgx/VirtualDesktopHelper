using VdHelper.Core.Checks;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

public static class HealthEngine
{
    /// <summary>
    /// PowerShell-backed checks each cost a process spawn, so running them in series made a pass
    /// take 16 s — long enough that the UI looks hung. They are independent, so they run
    /// concurrently with a small cap. Results are re-ordered back into definition order so the
    /// list, the history file and the generated docs stay byte-stable across runs.
    /// </summary>
    private const int MaxConcurrency = 8;

    public static IReadOnlyList<ICheck> CreateChecks() => HealthChecks.Create();

    public static async Task<HealthReport> RunAsync(CancellationToken ct = default)
    {
        var checks = CreateChecks();
        var report = new HealthReport(checks.Select(c => c.Definition).ToList());

        using var gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var tasks = checks.Select(async check =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await check.RunAsync(ct).ConfigureAwait(false);
                sw.Stop();
                return Timed(result, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new CheckResult(check.Definition.Id, CheckStatus.Unknown,
                    "检查未能完成", ex.Message,
                    new Dictionary<string, string>(), Array.Empty<FixAction>(),
                    "这一项没跑起来，不影响其它项；点「重新检测」再试一次。");
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (var r in results.OrderBy(r => IndexOf(checks, r.Id)))
            report.Results.Add(r);
        return report;
    }

    /// <summary>
    /// Runs the pass and reports each result the moment it lands, so the UI can fill in instead of
    /// sitting blank for fifteen seconds. Ordering of the streamed results is completion order.
    /// </summary>
    public static async Task<HealthReport> RunStreamingAsync(
        Action<CheckResult> onResult, CancellationToken ct = default)
    {
        var checks = CreateChecks();
        var report = new HealthReport(checks.Select(c => c.Definition).ToList());

        using var gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var collected = new System.Collections.Concurrent.ConcurrentQueue<CheckResult>();
        var pending = new List<Task>();
        foreach (var check in checks)
        {
            pending.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var result = await check.RunAsync(ct).ConfigureAwait(false);
                    sw.Stop();
                    collected.Enqueue(Timed(result, sw.ElapsedMilliseconds));
                    onResult(Timed(result, sw.ElapsedMilliseconds));
                }
                finally { gate.Release(); }
            }, ct));
        }
        await Task.WhenAll(pending).ConfigureAwait(false);

        foreach (var r in collected.OrderBy(r => IndexOf(checks, r.Id)))
            report.Results.Add(r);
        return report;
    }

    /// <summary>
    /// Records how long a check took. Users ask "why is it slow" and the honest answer has to be
    /// measured, not guessed from a total.
    /// </summary>
    private static CheckResult Timed(CheckResult result, long elapsedMs)
    {
        var evidence = new Dictionary<string, string>(result.Evidence) { ["_耗时"] = elapsedMs + " ms" };
        return result with
        {
            Evidence = evidence,
            Detail = result.Detail + $"（本项耗时 {elapsedMs} ms）",
        };
    }

    private static int IndexOf(IReadOnlyList<ICheck> checks, string id)
    {
        for (var i = 0; i < checks.Count; i++)
            if (checks[i].Definition.Id == id) return i;
        return int.MaxValue;
    }
}