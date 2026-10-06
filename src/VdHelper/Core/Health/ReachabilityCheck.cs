﻿﻿﻿﻿﻿﻿﻿using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using VdHelper.Core.Adb;
using VdHelper.Core.Checks;
using VdHelper.Core.Diagnosis;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// PC → headset reachability. Community triage starts with "can you ping the headset" and that
/// single question separates "PC is invisible" from "PC is visible but unreachable"
/// (research/09-failure-corpus/01-symptom-corpus.md R10/R02). The tool runs it so the user does
/// not have to open a console.
/// </summary>
public static class ReachabilityCheck
{
    public const string IpKey = "headsetIp";

    /// <summary>Ports to probe on the headset. Discovery is broadcast-based, so these are the
    /// Streamer's session ports; a closed port on a reachable host still proves reachability.</summary>
    private static readonly int[] ProbePorts = [38810, 38820];

    public static ICheck Create() =>
        CheckFactory.Delegate(
            new("lan-reach", "头显可达性", "PC 能不能在网络上够到头显？", "连通性"),
            async ct =>
            {
                var ip = ConfigFile.Read<string>(IpKey);
                var local = NetworkInventory.PrimaryCandidates().FirstOrDefault();
                var ev = new Dictionary<string, string>
                {
                    ["头显 IP"] = string.IsNullOrWhiteSpace(ip) ? "(未填写)" : ip,
                    ["本机地址"] = local is null ? "-" : $"{local.Name} {local.PrimaryIPv4}",
                };

                if (string.IsNullOrWhiteSpace(ip))
                    return new CheckResult("lan-reach", CheckStatus.Unknown,
                        "还没填头显 IP，无法测可达性",
                        "头显 IP 在「设置 → Wi-Fi → 连接当前网络 → IP 地址」能看到。填上就能自动测。",
                        ev, Array.Empty<FixAction>(),
                        "填了 IP 之后，这一项会直接告诉你「网络层到底通不通」，把「看不见」和「连不上」分开。");


                if (!IPAddress.TryParse(ip, out var address))
                    return new CheckResult("lan-reach", CheckStatus.Unknown,
                        "填的头显 IP 不是地址", ip, ev, Array.Empty<FixAction>(),
                        "例如 192.168.11.23。");

                // If the address we hold does not answer, do not stop there. A changed DHCP lease
                // is one of the most common causes this project documents, so sweep the local
                // subnet once and name whatever answers. Reported, never acted on: only the user
                // knows which device is the headset, and silently retargeting would be worse than
                // not knowing.
                if (!await PingOnceAsync(address, ct).ConfigureAwait(false))
                {
                    var candidates = await DiscoverHostsAsync(local, ct).ConfigureAwait(false);
                    ev["扫到的候选主机"] = candidates.Count > 0
                        ? string.Join(" ;; ", candidates)
                        : "(同网段内除网关与本机外，没有其它主机应答)";
                    ev["说明"] = candidates.Count > 0
                        ? "上面那个 IP 不应答；以下是同网段内应答的主机。挑出头显那台，改掉配置里的"
                          + " headsetIp 再跑一次。工具不会替你猜哪台是头显。"
                        : "网络层这一段是空的：头显可能没连这个 Wi-Fi、连了访客网络，或者地址变了。"
                          + "先在头显里确认它连的是哪一个网络。";
                }

                // Windows' own neighbour cache is better evidence than our guess: if the ARP entry
                // is absent the device is not on this link at all (as opposed to "firewalled").
                var neighbor = await PowerShellRunner.LinesAsync(
                    "Get-NetNeighbor -IPAddress " + (address.ToString()) + " -ErrorAction SilentlyContinue | "
                    + "ForEach-Object { \"$($_.State)|$($_.LinkLayerAddress)\" }", ct).ConfigureAwait(false);
                // Only Reachable means the device answered recently. Stale is a leftover entry
                // whose lifetime has expired — treating it as "on the link" would hide a departed
                // device behind reassuring wording.
                var reachable = neighbor.Count > 0
                    && neighbor.Any(n => n.StartsWith("Reachable", StringComparison.OrdinalIgnoreCase));
                // The state exactly as Windows reports it. Reachable, Stale, Probe and Incomplete
                // mean different things, and only Reachable proves the device is on this link.
                // Checked rather than assumed: a local address that stops answering goes to Probe,
                // not Stale, and this headset's entry was seen in both within minutes — so wording
                // that said "已过期" for everything non-Reachable would have named a state the
                // tool never saw.
                var stateName = neighbor.Count == 0 ? "" : neighbor[0].Split('|')[0].Trim();
                ev["邻居表"] = neighbor.Count == 0
                    ? "(ARP 缓存里没有这台设备 → 它当前不在这个链路上)"
                    : string.Join(" ;; ", neighbor)
                      + (reachable ? "（状态可达）" : "（状态陈旧：这条记录已经过期，不能当作它还在）");

                var sameSubnet = local?.PrimaryIPv4 is not null
                    && SameSubnet(local.PrimaryIPv4, address);
                ev["同网段"] = sameSubnet ? "是" : "否";
                if (!sameSubnet)
                    ev["提示"] = "两端不在同一网段：头显可能在访客网络，或路由器开了 AP 隔离";

                // Sample three times. This used to take exactly one ping and a single timeout was a
                // Block. Caught on this machine rather than reasoned about: the check's own ping timed
                // out and reported 阻断：串流很可能起不来, and six pings taken seconds later were all
                // answered — the neighbour state was oscillating Reachable / Probe the whole time,
                // which is what a power-saving client looks like.
                //
                // Any reply means the link is up, because the verdict is about reachability and not
                // about loss: loss is net-loss's job and it samples twenty times.
                // Sampling only matters while it is failing. Once it answers the question is settled,
                // and whether the link is lossy is net-loss's job with its twenty samples.
                //
                // All three shapes of this loop have since been observed on this machine without being
                // provoked: first-reply early exit (采样: 1/1 次应答 / 首次即应答 — the one this loop was
                // added for), partial (3 次采样应答 2 次 — the case a single sample used to Block on),
                // and all-three-failed. It was left marked unverified when only two of the three existed.
                var attempts = new List<(bool Success, long Ms, string Detail)>();
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    if (attempt > 0) await Task.Delay(300, ct).ConfigureAwait(false);
                    var a = await PingAsync(address, 1200, ct).ConfigureAwait(false);
                    attempts.Add(a);
                    if (a.Success) break;
                }
                var ping = attempts.FirstOrDefault(a => a.Success);
                var replies = attempts.Count(a => a.Success);
                // one phrase, used by both the evidence and the summary, so they cannot disagree
                var probeNote = replies == 1 && attempts.Count == 1
                    ? "，首次即应答"
                    : $"，{attempts.Count} 次采样应答 {replies} 次";
                ev["ping"] = replies > 0
                    ? $"{ping.Ms} ms（{probeNote.TrimStart('，')}）"
                    : $"不通（{attempts.Count} 次采样全部未应答）";
                ev["ping 明细"] = string.Join(" ;; ", attempts.Select((a, i) => $"#{i + 1} {a.Detail}"));
                ev["采样"] = $"{replies}/{attempts.Count} 次应答";

                var openPorts = new List<string>();
                foreach (var port in ProbePorts)
                    if (await IsPortOpenAsync(address, port, 700, ct))
                        openPorts.Add(port.ToString());
                ev["端口探测"] = openPorts.Count > 0
                    ? string.Join(",", openPorts) + " 有响应"
                    : "38810/38820 未响应（串流未开始时本就不开，不能据此判故障）";

                if (replies > 0)
                    return new CheckResult("lan-reach", CheckStatus.Pass,
                        $"头显 {ip} 可达（ping {ping.Ms} ms{probeNote}）",
                        "网络层通。如果头显里还是「连不上」，问题在 VD 应用侧或账号侧，不在网络。",
                        ev, Array.Empty<FixAction>());

                var absent = !reachable;
                // Two causes here have nothing to do with the network, and both are easy to
                // misread as a network fault. They belong on BOTH branches, not whichever one
                // happens to fire today.
                const string notNetwork =
                    "\n\n**动手改网络之前，先排除两个跟网络无关的原因**："
                    + "\n① 头显里那个应用是不是直接退出了。客户端拿不到账号身份时会自己杀掉进程"
                    + "（NetworkManager.cs:184-186 与 :212-214 两条 Kill 路径），"
                    + "表现是打开就闪退，不是「找不到电脑」。"
                    + "\n② 搜索窗口只有 3 秒且不重试。头显每次刷新只广播一个包，"
                    + "回包窗口硬编码 3000 ms（ComputerDiscoveryClient.cs:98-107）。"
                    + "先开头显、后开 PC 上的 Streamer，就一定搜不到——这不是坏了，是那 3 秒已经过去了。";
                return new CheckResult("lan-reach", CheckStatus.Block,
                    $"头显 {ip} ping 不通"
                    + (absent
                        ? "（ARP 缓存里也没有它）"
                        : reachable
                            ? "（邻居表状态 Reachable：它在这条链路上）"
                            : $"（邻居表里有记录，但状态是 {stateName}，不是 Reachable）"),
                    (absent
                        ? "两件事同时成立：它不在这个链路上，而且它上次的地址也不再通。"
                        + "最常见的是头显改了 IP（DHCP 续租后跳号）、连到了访客网络、或者根本没连 Wi-Fi。"
                        : reachable
                            ? "邻居表状态是 Reachable：它最近还在这条链路上回应过这台 PC，所以只是 ping 被挡或它不响应 ICMP —— 这种情况更像 AP 隔离或来宾网络。"
                            : stateName.Equals("Stale", StringComparison.OrdinalIgnoreCase)
                                ? "邻居表里有它的记录，但状态是 Stale：这只说明它**曾经在**这条链路上，记录已经过期，不能证明它现在还在。更可能是它改了 IP 或者连去了别的网段。"
                                : $"邻居表里有它的记录，但状态是 {stateName}，不是 Reachable——此刻它在链路那一头没有回应。更像是它关了 Wi-Fi、掉电，或者连到了别的网段，而不是被 AP 隔离挡住。")
                    + notNetwork,
                    ev, Array.Empty<FixAction>(),
                    absent
                        ? "先在头显「设置 → Wi-Fi」里看一眼当前 IP，填回上面那个框；"
                        + "IP 变了是「昨天还好好的」类故障里最常见的一种，填对了这一项立刻变绿。"
                        : "同网段还不通：查 AP 隔离 / 访客网络 / 无线与有线隔离 / 头显连的是 5GHz 还是 2.4GHz。");
            });

    public static bool SameSubnet(IPAddress a, IPAddress b)
    {
        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        if (x.Length != 4 || y.Length != 4) return false;
        // /24 comparison: routers in the home segment are effectively /24 and this avoids
        // guessing a prefix length we cannot verify.
        return x[0] == y[0] && x[1] == y[1] && x[2] == y[2];
    }

    private static async Task<(bool Success, long RoundtripMs, string Detail)> PingAsync(
        IPAddress address, int timeoutMs, CancellationToken ct)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(address, timeoutMs);
            var detail = $"{reply.Status}" +
                (reply.Address is not null ? $" -> {reply.Address}" : "") +
                (reply.RoundtripTime >= 0 ? $" {reply.RoundtripTime}ms" : "");
            return (reply.Status == IPStatus.Success, Math.Max(0, reply.RoundtripTime), detail);
        }
        catch (Exception ex) when (ex is PingException or SocketException)
        {
            return (false, 0, ex.Message);
        }
    }

    private static async Task<bool> IsPortOpenAsync(IPAddress address, int port, int timeoutMs, CancellationToken ct)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(address, port, ct)
                .AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct);
            return client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or TimeoutException)
        {
            return false;
        }
    }

    /// <summary>One ping, short timeout. Used as a liveness probe, never as a latency measure.</summary>
    private static async Task<bool> PingOnceAsync(IPAddress target, CancellationToken ct)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(target, 800).WaitAsync(ct).ConfigureAwait(false);
            return reply.Status == IPStatus.Success;
        }
        catch (Exception ex) when (ex is PingException or OperationCanceledException) { return false; }
    }

    /// <summary>
    /// Hosts on the same /24 that answer a ping, excluding this machine and the gateway. Bounded:
    /// at most ~250 pings fired in parallel with a single deadline, so it cannot hang a pass.
    /// </summary>
    private static async Task<IReadOnlyList<string>> DiscoverHostsAsync(
        AdapterView? local, CancellationToken ct)
    {
        if (local?.PrimaryIPv4 is null) return Array.Empty<string>();
        var prefix = local.PrimaryIPv4!.GetAddressBytes().Take(3).ToArray();
        if (prefix.Length != 3) return Array.Empty<string>();
        var self = local.PrimaryIPv4!.ToString();
        var gateway = local.Gateways.FirstOrDefault()?.ToString();
        var gate = new SemaphoreSlim(64);
        var found = new List<string>();

        var probes = Enumerable.Range(1, 254).Select(async i =>
        {
            if (!await gate.WaitAsync(0, ct).ConfigureAwait(false)) return;
            try
            {
                var ip = new IPAddress(prefix.Concat(new byte[] { (byte)i }).ToArray());
                if (ip.ToString() == self || ip.ToString() == gateway) return;
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 500).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success) lock (found) found.Add(ip + "  (" + reply.RoundtripTime + " ms)");
            }
            catch (PingException) { /* not there, or blocked */ }
            finally { gate.Release(); }
        });

        await Task.WhenAll(probes).WaitAsync(TimeSpan.FromSeconds(6), ct).ConfigureAwait(false);
        return found;
    }

}
