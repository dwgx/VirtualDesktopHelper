using System.Diagnostics;
using System.Net.NetworkInformation;
using VdHelper.Core.Adb;
using VdHelper.Core.Checks;
using VdHelper.Core.Diagnosis;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// D5 — random packet loss somewhere on the path. The corpus thread behind this one had ping at
/// 9 ms and full bars, and the stream still stuttered; the culprit was loss between the PC and the
/// router, which a single ping never reveals.
/// <para>
/// Deliberately NOT part of the normal pass: 20 pings take ~20 s, and a health check that makes the
/// tool wait 20 s is a health check people stop running. Run it on demand.
/// </para>
/// </summary>
public static class LossProbe
{
    /// <summary>Samples for an on-demand run. The in-pass check uses fewer — see <see cref="InPassSamples"/>.</summary>
    public const int DefaultSamples = 20;

    /// <summary>
    /// The health pass runs checks in parallel, but its wall-clock time is set by its slowest one.
    /// A 20-sample probe would stretch a ~5 s pass to ~20 s, so the in-pass check takes a quick
    /// sample and the full run is an explicit action (CLI <c>--deep</c>, button in the UI).
    /// </summary>
    public const int InPassSamples = 8;

    public sealed record Sample(string Target, int Sent, int Received, double LossPercent,
        double MinMs, double AvgMs, double MaxMs, double JitterMs, bool Completed);

    /// <summary>Pings a host and reports loss plus jitter, which is what video actually cares about.</summary>
    public static async Task<Sample> MeasureAsync(string target, int count = DefaultSamples, int timeoutMs = 1000,
        CancellationToken ct = default)
    {
        using var ping = new Ping();
        var rtts = new List<double>();
        var sent = 0;
        var replies = 0;
        var completed = 0;

        for (var i = 0; i < count && !ct.IsCancellationRequested; i++)
        {
            sent++;
            try
            {
                var reply = await ping.SendPingAsync(target, timeoutMs);
                if (reply.Status == IPStatus.Success)
                {
                    replies++;
                    rtts.Add(reply.RoundtripTime);
                }
            }
            catch (Exception ex) when (ex is PingException or System.Net.Sockets.SocketException)
            {
                // a lost packet is the measurement, not an error
            }
            await Task.Delay(100, ct).ConfigureAwait(false);
        }

        if (ct.IsCancellationRequested)
            return new Sample(target, sent, replies, sent == 0 ? 0 : (sent - replies) * 100.0 / sent,
                0, 0, 0, 0, false);

        // Jitter: mean absolute difference between consecutive round trips.
        var jitter = 0.0;
        for (var i = 1; i < rtts.Count; i++) jitter += Math.Abs(rtts[i] - rtts[i - 1]);
        if (rtts.Count > 1) jitter /= rtts.Count - 1;

        return new Sample(
            target, sent, replies,
            sent == 0 ? 0 : (sent - replies) * 100.0 / sent,
            rtts.Count == 0 ? 0 : rtts.Min(),
            rtts.Count == 0 ? 0 : rtts.Average(),
            rtts.Count == 0 ? 0 : rtts.Max(),
            jitter,
            completed >= count);
    }

    public static ICheck Create() =>
        CheckFactory.Delegate(
            new("net-loss", "丢包与抖动", "到路由器/头显的路上有没有丢包？", "连通性"),
            async ct =>
            {
                var ev = new Dictionary<string, string>();
                var targets = new List<(string Label, string Host)>();

                targets.AddRange(Targets());

                if (targets.Count == 0)
                    return new CheckResult("net-loss", CheckStatus.Unknown, "没有可测的目标",
                        "既没有默认网关，也没有填头显 IP。", ev, Array.Empty<FixAction>(),
                        "在第三屏填一个头显 IP，这一项才有东西可测。");

                var worst = 0.0;
                foreach (var (label, host) in targets)
                {
                    var s = await MeasureAsync(host, InPassSamples, 500, ct).ConfigureAwait(false);
                    ev[label + " " + host] =
                        $"{s.Received}/{s.Sent} 收到 · 丢包 {s.LossPercent:F0}% · "
                        + $"延迟 {s.MinMs:F0}/{s.AvgMs:F0}/{s.MaxMs:F0} ms · 抖动 {s.JitterMs:F1} ms";
                    worst = Math.Max(worst, s.LossPercent);
                }

                if (worst >= 5)
                    return new CheckResult("net-loss", CheckStatus.Warn,
                        $"测到 {worst:F0}% 丢包（快速采样 {InPassSamples} 次）",
                        "视频流对丢包极其敏感：1% 的丢包就能让画面明显卡顿。"
                        + "注意单次 ping 永远测不出来——丢包往往是随机的、或者只在某个方向上。",
                        ev, Array.Empty<FixAction>(),
                        "这一步只定位「大方向上有丢包」。要区分是网线/交换机/路由器还是 Wi-Fi 干扰，"
                        + "得分别测网关和头显：网关也丢 → 问题在 PC 到路由器这一段。");

                return new CheckResult("net-loss", CheckStatus.Pass,
                    $"没有测到丢包（快速采样 {InPassSamples} 次）",
                    "注意这是单次快照，而且只有 8 次采样；随机丢包很容易刚好没赶上。"
                    + "画面卡的时候用「深度探测」跑 20 次，或命令行 --deep。",
                    ev, Array.Empty<FixAction>());
            });

    /// <summary>Runs the probe against the CLI so it can be timed separately from the main pass.</summary>
    /// <summary>
    /// Hosts worth probing, labelled: the default gateway, then the headset if one is configured.
    /// Both callers share this so the CLI and the UI can never probe different things.
    /// </summary>
    public static IReadOnlyList<(string Label, string Host)> Targets()
    {
        var targets = new List<(string, string)>();
        var gateway = NetworkInventory.ReadAdapters()
            .Where(a => a.IsUp && a.PrimaryIPv4 is not null && a.HasDefaultGateway)
            .Select(a => a.Gateways.FirstOrDefault()).FirstOrDefault();
        if (gateway is not null) targets.Add(("默认网关", gateway.ToString()));
        var headset = ConfigFile.Read<string>(ReachabilityCheck.IpKey);
        if (!string.IsNullOrWhiteSpace(headset)) targets.Add(("头显", headset));
        return targets;
    }

    public static async Task<int> RunDeepAsync(string[] args)
    {
        var count = DefaultSamples;
        var i = Array.IndexOf(args, "--samples");
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var n)) count = Math.Clamp(n, 4, 100);

        var targets = Targets();
        if (targets.Count == 0)
        {
            Console.WriteLine("没有可测的目标：既没有默认网关，也没有填头显 IP。");
            return 2;
        }

        Console.WriteLine($"丢包探测：每目标 {count} 次采样");
        var sw = Stopwatch.StartNew();
        var worst = 0.0;
        foreach (var (label, host) in targets)
        {
            var s = await MeasureAsync(host, count, 800).ConfigureAwait(false);
            worst = Math.Max(worst, s.LossPercent);
            Console.WriteLine($"  {label} {host,-15} {s.Received}/{s.Sent} 收到 · 丢包 {s.LossPercent,5:F0}% · "
                + $"延迟 min/avg/max {s.MinMs:F0}/{s.AvgMs:F0}/{s.MaxMs:F0} ms · 抖动 {s.JitterMs:F1} ms");
        }
        sw.Stop();
        Console.WriteLine($"耗时 {sw.ElapsedMilliseconds} ms，最差丢包 {worst:F0}%");
        Console.WriteLine(worst >= 5
            ? "结论：测到丢包。先分清是 PC→路由器这一段还是 Wi-Fi 那一段。"
            : "结论：这一次没测到丢包。画面卡的时候再跑一次——随机丢包不会每次都赶上。");
        return 0;
    }
}