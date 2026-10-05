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

                var seen = new List<(string Label, string Host, int Received, int Sent, double Loss)>();
                foreach (var (label, host) in targets)
                {
                    var s = await MeasureAsync(host, InPassSamples, 500, ct).ConfigureAwait(false);
                    ev[label + " " + host] =
                        $"{s.Received}/{s.Sent} 收到 · 丢包 {s.LossPercent:F0}% · "
                        + $"延迟 {s.MinMs:F0}/{s.AvgMs:F0}/{s.MaxMs:F0} ms · 抖动 {s.JitterMs:F1} ms";
                    seen.Add((label, host, s.Received, s.Sent, s.LossPercent));
                }

                // Same distinction as the CLI verdict: "nobody answered" is not packet loss. The
                // screen used to report "测到 100% 丢包" whenever the headset was simply asleep,
                // and then told the reader to suspect the router — which the gateway line right
                // above had already ruled out.
                // Three-way, and in the same order as Verdict() below — the CLI and the screen were
                // answering differently about the same measurement.
                //
                // The gap that let this come back: branch 1 required clean.Count > 0, so a gateway
                // that answered with 30% loss plus a headset that answered nothing skipped it, and
                // fell through to `worst >= 5` — where worst is the max over all targets, and a dead
                // target contributes 100. The screen printed 测到 100% 丢包 for an asleep headset and
                // pointed at the router, while --deep on the same data printed the headset as
                // partially lossy and said 这不是丢包. "Nobody answered" is never packet loss, whatever
                // the other target happened to be doing.
                var silent = seen.Where(r => r.Received == 0).ToList();
                var partial = seen.Where(r => r.Received > 0 && r.Loss >= 5).ToList();
                var clean = seen.Where(r => r.Received > 0 && r.Loss < 5).ToList();
                var worstPartial = partial.Count == 0 ? 0 : partial.Max(r => r.Loss);

                if (silent.Count > 0)
                    return new CheckResult("net-loss", CheckStatus.Warn,
                        string.Join("、", silent.Select(r => $"{r.Label} {r.Host}")) + $" 完全不应答（0 收到）",
                        "这一项测的不是丢包，而是「有没有人应答」。0 收到不等于丢包——睡着的头显和关着屏幕的笔记本都是 0 收到。",
                        ev, Array.Empty<FixAction>(),
                        "本机链路本身" + (clean.Count > 0
                            ? "没问题：" + string.Join("、", clean.Select(r => $"{r.Label} {r.Host}")) + " 通畅。 "
                            : "无法从这次采样判断。 ")
                        + "先确认头显醒着、Wi-Fi 连着、地址没变；把这一项当成丢包去查路由器会白查。");

                if (worstPartial >= 5)
                    return new CheckResult("net-loss", CheckStatus.Warn,
                        string.Join("、", partial.Select(r => $"{r.Label} {r.Host} {r.Loss:F0}%"))
                        + $" 部分丢包（快速采样 {InPassSamples} 次，最高 {worstPartial:F0}%）",
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
        var results = new List<(string Label, string Host, int Received, int Sent, double Loss)>();
        foreach (var (label, host) in targets)
        {
            var s = await MeasureAsync(host, count, 800).ConfigureAwait(false);
            results.Add((label, host, s.Received, s.Sent, s.LossPercent));
            Console.WriteLine($"  {label} {host,-15} {s.Received}/{s.Sent} 收到 · 丢包 {s.LossPercent,5:F0}% · "
                + $"延迟 min/avg/max {s.MinMs:F0}/{s.AvgMs:F0}/{s.MaxMs:F0} ms · 抖动 {s.JitterMs:F1} ms");
        }
        sw.Stop();

        var worst = results.Max(r => r.Loss);
        Console.WriteLine($"耗时 {sw.ElapsedMilliseconds} ms，最差丢包 {worst:F0}%");
        foreach (var line in Verdict(results)) Console.WriteLine(line);
        return 0;
    }

    /// <summary>
    /// Turns the per-target numbers into wording that follows from them.
    /// <para>
    /// The first version took only the worst loss across all targets and printed one fixed sentence
    /// telling the user to work out whether the loss was before or after the router. On this machine
    /// that said "分不清是 PC→路由器还是 Wi-Fi" while the gateway line directly above it read 0% —
    /// the measurement had already answered the question and the verdict threw the answer away.
    /// </para>
    /// <para>
    /// The distinction that matters: one target answering nothing while another is clean is not
    /// packet loss, it is that host not being there. A headset that is asleep produces 100% and so
    /// does a laptop with its screen closed; calling both "丢包" sends people to the router first.
    /// </para>
    /// </summary>
    private static IEnumerable<string> Verdict(List<(string Label, string Host, int Received, int Sent, double Loss)> results)
    {
        var dead = results.Where(r => r.Received == 0).ToList();
        var partial = results.Where(r => r.Received > 0 && r.Loss >= 5).ToList();
        var clean = results.Where(r => r.Received > 0 && r.Loss < 5).ToList();

        if (results.All(r => r.Loss < 5))
        {
            yield return "结论：这次每个目标都通，没有测到丢包。画面卡的时候再跑一次——随机丢包不会每次都赶上。";
            yield break;
        }

        if (dead.Count == results.Count)
        {
            yield return "结论：所有目标一个都没应答（0 收到）。这不是丢包，是这些地址此刻都没在应答——";
            yield return "头显可能睡着/换了 IP/换了网段，或者它所在的那一段被挡住了。先确认它开着、Wi-Fi 连着。";
            yield break;
        }

        if (partial.Count > 0)
        {
            foreach (var r in partial)
            {
                yield return $"{r.Label} {r.Host} 部分丢包 {r.Loss:F0}%（{r.Received}/{r.Sent} 收到）——这一段是通的，但不稳。";
            }
            if (clean.Count > 0)
            {
                yield return string.Join("、", clean.Select(r => $"{r.Label} {r.Host}")) + " 是干净的，";
                yield return "所以问题落在上面这几条链路上，不是这台 PC 的整体网络。";
            }
        }

        if (dead.Count > 0)
        {
            foreach (var r in dead)
            {
                yield return $"{r.Label} {r.Host} 完全不应答（0/{r.Sent}）——这不是丢包，是它此刻不在应答。";
            }
            if (clean.Count > 0)
            {
                yield return "同一次里 " + string.Join("、", clean.Select(r => $"{r.Label} {r.Host}"))
                         + " 通畅，所以 PC 的网卡和路由器这一段是好的；";
                yield return "要处理的是上面这个目标本身（睡着 / 改了 IP / 被 AP 隔离），不是路由器。";
            }
        }
    }
}
