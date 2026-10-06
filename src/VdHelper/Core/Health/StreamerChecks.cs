﻿﻿﻿using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using VdHelper.Core.Checks;
using VdHelper.Core.Diagnosis;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// Checks that read what the Streamer itself left behind. On this machine every network check
/// passes while the Streamer has been unable to start since 2026-09-07 — the only evidence is
/// <c>C:\ProgramData\Virtual Desktop\ServiceLog.txt</c>. This class is that detector.
/// </summary>
public static class StreamerChecks
{
    public const string ProgramData = @"C:\ProgramData\Virtual Desktop";
    public const string ServiceLog = ProgramData + @"\ServiceLog.txt";
    public const string StreamerLog = ProgramData + @"\StreamerLog.txt";
    public const string StreamerExe = @"C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe";

    /// <summary>
    /// Where the Streamer actually is on this machine.
    /// <para>
    /// The constant is only the default install location. If VD was installed elsewhere, every
    /// "restart the Streamer" and every firewall rule built from this path points at a file that is
    /// not there, and streamer-proc would report "未安装" while the process is plainly running. So
    /// when it is running, ask the process where it lives.
    /// </para>
    /// <para>
    /// Reading MainModule of a process owned by another account or running elevated throws, hence
    /// the fallback: a hard failure here would be worse than a wrong-but-working default.
    /// </para>
    /// </summary>
    public static string ResolveStreamerExe()
    {
        Process[] running;
        try { running = Process.GetProcessesByName("VirtualDesktop.Streamer"); }
        catch { return StreamerExe; }
        try
        {
            foreach (var p in running)
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
                }
                catch { /* elevated or another account — try the next one, else fall back */ }
            }
        }
        finally { foreach (var p in running) p.Dispose(); }
        return StreamerExe;
    }

    /// <summary>PIDs of every running Streamer process; empty when it is not running.</summary>
    public static IReadOnlyList<int> StreamerProcessIds()
    {
        try
        {
            return Process.GetProcessesByName("VirtualDesktop.Streamer")
                .Select(p => p.Id)
                .ToList();
        }
        catch (InvalidOperationException)
        {
            return Array.Empty<int>();
        }
    }

    /// <summary>
    /// The three texts this machine has actually logged for a service-identity failure. Matching on
    /// them is what lets the Block branch name a cause it has evidence for instead of assuming one.
    /// </summary>
    private static bool LooksLikeIdentityFailure(string msg) =>
        msg.Contains("0x80070005", StringComparison.OrdinalIgnoreCase)
        || msg.Contains("-2147024891", StringComparison.OrdinalIgnoreCase)   // same value, decimal
        || msg.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
        || msg.Contains("UnauthorizedAccessException", StringComparison.OrdinalIgnoreCase)
        || msg.Contains("identity is incorrect", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Name and PID of whatever holds a UDP port, read from the connection table.
    /// <para>
    /// <c>IPGlobalProperties</c> lists listeners but cannot attribute them, and attribution is the
    /// whole point here: a port stolen by another process is invisible to every other check in this
    /// tool. Returns null when the owner cannot be resolved rather than guessing.
    /// </para>
    /// </summary>
    private static async Task<string?> WhoOwnsUdpPortAsync(int port, CancellationToken ct)
    {
        try
        {
            var rows = await PowerShellRunner
                .LinesAsync("Get-NetUDPEndpoint -LocalPort " + port + " -ErrorAction SilentlyContinue | "
                    + "ForEach-Object { \"$($_.OwningProcess)\" }", ct)
                .ConfigureAwait(false);
            var row = rows.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(row)) return null;
            if (!int.TryParse(row.Trim(), out var pid)) return null;
            using var proc = Process.GetProcessById(pid);
            return $"{proc.ProcessName} (PID {pid})";
        }
        catch (ArgumentException)
        {
            return null;   // the process exited between the two reads
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static readonly Regex ErrorLine =
        new(@"^(?<ts>\d{4}-\d{2}-\d{2}[^|]*)\|(?<level>ERROR|WARN)\|(?<comp>[^|]*)\|(?<msg>.*)$",
            RegexOptions.Compiled);

    public static ICheck StreamerProcessCheck() =>
        CheckFactory.Delegate(
            new("streamer-proc", "Streamer 进程", "串流进程真的在跑吗？", "进程"),
            ct =>
            {
                var running = Process.GetProcessesByName("VirtualDesktop.Streamer");
                var ev = new Dictionary<string, string>
                {
                    ["进程"] = running.Length > 0
                        ? string.Join(", ", running.Select(p => p.Id.ToString()))
                        : "未运行",
                    ["可执行文件"] = ResolveStreamerExe()
                        + (File.Exists(StreamerExe) ? "" : "（默认路径下没有；上面是正在运行的那个进程的实际位置）"),
                };
                foreach (var p in running) p.Dispose();

                if (running.Length == 0)
                    return Task.FromResult(new CheckResult("streamer-proc", CheckStatus.Block,
                        "Streamer 进程没有运行——PC 侧不会广播，也不会监听串流端口",
                        "服务「在运行」不等于进程能起来。本机 2026-09-07 起就持续记录启动失败，见 svc-log 项。",
                        ev, Fixes.RelaunchStreamer(),
                        "手动双击运行 Virtual Desktop Streamer.exe 看是否报错；报错内容决定下一步。"));

                return Task.FromResult(new CheckResult("streamer-proc", CheckStatus.Pass,
                    $"Streamer 进程运行中（{running.Length} 个）",
                    "PC 侧会周期广播 UDP 38860，并监听 38850/38810-40。", ev, Array.Empty<FixAction>()));
            });

    public static ICheck ServiceLogCheck() =>
        CheckFactory.Delegate(
            new("svc-log", "服务日志", "Streamer 启动时有没有报错？", "进程"),
            ct =>
            {
                var ev = new Dictionary<string, string> { ["日志路径"] = ServiceLog };
                if (!File.Exists(ServiceLog))
                    return Task.FromResult(new CheckResult("svc-log", CheckStatus.Pass,
                        "没有服务日志（首次运行前属正常）", "正常。", ev, Array.Empty<FixAction>()));

                var lines = ReadLines(ServiceLog);
                ev["日志行数"] = lines.Count.ToString();
                var errors = lines.Select(l => ErrorLine.Match(l)).Where(m => m.Success).ToList();
                var recent = errors.Where(m => m.Groups["level"].Value == "ERROR").ToList();
                ev["错误条数"] = recent.Count.ToString();
                if (recent.Count > 0)
                    ev["最近一条"] = recent[^1].Groups["ts"].Value + " — " + Truncate(recent[^1].Groups["msg"].Value, 160);

                if (recent.Count == 0)
                    return Task.FromResult(new CheckResult("svc-log", CheckStatus.Pass,
                        "服务日志里没有 ERROR", "正常。", ev, Array.Empty<FixAction>()));

                var last = recent[^1].Groups["ts"].Value;
                // A successful repair does not erase the log. When the Streamer is running right
                // now, those ERROR lines are history: blocking on them would make the tool cry
                // wolf on a machine that has already been fixed.
                var running = Process.GetProcessesByName("VirtualDesktop.Streamer").Length > 0;

                // How old is the newest ERROR? Reinstalling the service cannot retroactively change
                // log lines from weeks ago, so offering it for those is offering a destructive action
                // that provably does nothing about what was found.
                var newest = DateTime.MinValue;
                if (DateTime.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTs))
                    newest = parsedTs;
                var stale = newest != DateTime.MinValue && DateTime.Now - newest > TimeSpan.FromDays(2);

                if (stale && !running)
                    return Task.FromResult(new CheckResult("svc-log", CheckStatus.Warn,
                        $"服务日志有 {recent.Count} 条历史 ERROR，但最近一条是 {last}，已经不是当前状态",
                        $"这些 ERROR 距今约 {(DateTime.Now - newest).TotalDays:F0} 天。重装服务不会抹掉日志里的旧行，"
                        + "也不会让一个已经能正常启动的服务变好——所以这里不给你这个动作。",
                        ev, Array.Empty<FixAction>(),
                        "现在 Streamer 没在跑，按 streamer-proc 那一项启动它就够了。"
                        + "如果启动后仍然报错，那时的新 ERROR 才是当前问题，那时再看这一项。"));

                if (running)
                    return Task.FromResult(new CheckResult("svc-log", CheckStatus.Warn,
                        $"服务日志有 {recent.Count} 条历史 ERROR（最近一次 {last}），但 Streamer 正在运行",
                        "错误来自过去，重装服务也不会抹掉历史记录。当前进程能起来，说明服务身份绑定已恢复。",
                        ev, Array.Empty<FixAction>(),
                        "想从零验证：删掉 ServiceLog.txt，重启 Streamer，再看有没有新 ERROR。"));

                return Task.FromResult(new CheckResult("svc-log", CheckStatus.Block,
                    $"服务日志有 {recent.Count} 条 ERROR，最近一次 {last}",
                    // The judge is level == "ERROR" and nothing else. Any ERROR — network, config,
                    // the user cancelling — used to be translated into two specific assertions: 服务
                    // 尝试拉起 Streamer 时被系统拒绝, and 网络层再正常也不会广播. The message field went
                    // into the evidence truncated to 160 chars and was never judged. So let the log
                    // speak, and only name the identity-binding failure when the text actually says
                    // so — this machine has one logged, and that is not what this class of entry
                    // generally means.
                    (LooksLikeIdentityFailure(recent[^1].Groups["msg"].Value)
                        ? "**日志原文指向服务身份绑定问题**（0x80070005 / Access is denied / "
                          + "identity is incorrect）。这一类会让服务拉不起 Streamer，网络层再正常也不会广播。原文见证据。"
                        : "这条 ERROR 的原文见证据——本项只按级别判定、不解释内容，"
                          + "所以不能断定它是身份问题，也可能是网络、配置或用户取消。"),
                    ev, Fixes.RepairService(),
                    "若重装服务无效，检查服务登录账户密码是否与当前系统账户一致（重装需要管理员权限）。"));
            });

    /// <summary>
    /// The discovery socket's presence depends on whether a session is up: measured on this
    /// machine, the Streamer holds UDP 38850 while idle and releases it once a session is
    /// established on the TCP channels. Reporting "no discovery activity" as a warning during a
    /// live stream is exactly the kind of false alarm that sends people to reconfigure routers.
    /// </summary>
    public static ICheck UdpDiscoveryCheck() =>
        CheckFactory.Delegate(
            new("udp-discovery", "发现协议端口", "发现通道现在是什么状态？", "端口"),
            async ct =>
            {
                var ev = new Dictionary<string, string>();
                var listeners = new Dictionary<int, IPEndPoint>();
                try
                {
                    foreach (var ep in IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners()
                             .Where(e => e.Port is 38850 or 38860))
                    {
                        listeners[ep.Port] = ep;
                        ev["UDP " + ep.Port] = ep.Address + ":" + ep.Port;
                    }
                }
                catch (NetworkInformationException ex)
                {
                    return new CheckResult("udp-discovery", CheckStatus.Unknown,
                        "读不到 UDP 监听表", ex.Message, ev, Array.Empty<FixAction>(),
                        "需要管理员权限才能枚举全部监听套接字。");
                }

                ev["查询方式"] = "IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners()";
                ev["查询范围"] = "端口 38850（发现/配对收包）与 38860（PC 存在广播）";
                ev["枚举结果"] = listeners.Count == 0
                    ? "枚举成功；在这两个端口上找到 0 个监听套接字"
                    : "枚举成功；找到 " + listeners.Count + " 个：" + string.Join("、", listeners.Select(l => l.Key));
                // Who owns the port matters as much as whether it is bound. The Streamer opens
                // 38850 with `new UdpClient` and swallows the exception if the bind fails
                // (research/13-endpoints/02-discovery-protocol.md, VD-R/-.112.cs:281/330/:454-456),
                // so a port stolen by another process leaves no log line at all — the Streamer
                // just quietly never answers a discovery packet, and the headset reports
                // "no computer found" for a reason nothing on this machine would show.
                var streamerPid = StreamerProcessIds().FirstOrDefault();
                foreach (var port in listeners.Keys.ToList())
                {
                    var owner = port == 38850
                        ? await WhoOwnsUdpPortAsync(port, ct).ConfigureAwait(false)
                        : null;
                    if (owner is not null)
                        ev["UDP " + port + " 持有者"] = owner;
                }

                var bound38850 = listeners.TryGetValue(38850, out var l38850) ? l38850 : null;
                if (bound38850 is not null)
                {
                    var owner = await WhoOwnsUdpPortAsync(38850, ct).ConfigureAwait(false);
                    // Process.ProcessName has the .exe stripped, so comparing against the full
                    // file name flags every healthy machine. Verified: this exact mistake produced
                    // "UDP 38850 被别的进程占着：VirtualDesktop.Streamer" on the Streamer itself.
                    const string streamerName = "VirtualDesktop.Streamer";
                    if (owner is not null
                        && !owner.Contains(streamerName, StringComparison.OrdinalIgnoreCase))
                    {
                        return new CheckResult("udp-discovery", CheckStatus.Block,
                            $"UDP 38850 被别的进程占着：{owner}",
                            "**这会表现为「头显找不到电脑」，但网络配置全对。**"
                            + "Streamer 用 new UdpClient 独占绑定 38850，绑不上时异常被静默吞掉，"
                            + "日志里不会留任何一行——于是配对请求到了也没人回。",
                            ev, Array.Empty<FixAction>(),
                            $"先停掉上面那个进程再重测。常见占位者：其它串流/远控软件、VPN 客户端、旧版 Streamer 残留进程。"
                            + $"Streamer 自己的 PID 是 {(streamerPid > 0 ? streamerPid.ToString() : "（没找到）")}。");
                    }

                    var addr = bound38850.Address;
                    if (addr is not null
                        && !addr.Equals(IPAddress.Any) && !addr.Equals(IPAddress.IPv6Any))
                    {
                        return new CheckResult("udp-discovery", CheckStatus.Warn,
                            $"UDP 38850 只绑在 {bound38850.Address}，不是所有网卡",
                            "绑在单个地址上意味着从别的网卡进来的发现包收不到——" +
                            "有线连着 PC、头显走 Wi-Fi 的场景正好会中招。",
                            ev, Array.Empty<FixAction>(),
                            "重启 Streamer 通常会回到 0.0.0.0；若反复出现，看有没有虚拟网卡抢走了绑定。");
                    }
                }

                var (tcp, _) = await NetworkInventory
                    .ObserveVdPortsAsync(NetworkInventory.VdPorts, ct).ConfigureAwait(false);
                var established = tcp.Where(p => p.State == PortState.Established).ToList();
                var localNets = NetworkInventory.PrimaryCandidates()
                    .Select(a => a.PrimaryIPv4!).Where(ip => ip is not null).ToList();
                // Peer is "address:port", so it has to be split before IPAddress.TryParse — otherwise
                // every socket, the headset's included, parses as false and lands in the cloud bucket.
                //
                // Three buckets, not two. IPv6 peers used to fall into "not LAN" and were then
                // written to the report as 到公网的已建立连接 — but fe80:: is link-local, i.e. on this
                // very wire, and an IPv6 address does not parse out of `Split(':')[0]` anyway. So
                // "could not decide" was being reported as "decided: public internet", which is the
                // opposite failure: telling someone they have an outbound connection they may not
                // have, and hiding one they do.
                System.Net.IPAddress? PeerIp(PortView p)
                {
                    var s = p.Peer;
                    var cut = s.LastIndexOf(':');
                    if (cut > 0) s = s[..cut];
                    return System.Net.IPAddress.TryParse(s, out var ip) ? ip : null;
                }
                bool IsLan(PortView p) =>
                    PeerIp(p) is { AddressFamily: System.Net.Sockets.AddressFamily.InterNetwork } ip
                    && localNets.Any(l => NetworkInventory.IsLanPeer(ip, l));

                var live = established.Where(IsLan).ToList();
                var cloud = established.Where(p => PeerIp(p) is not null && !IsLan(p)).ToList();
                var unparsed = established.Where(p => PeerIp(p) is null).ToList();
                ev["是否有活动会话"] = live.Count > 0
                    ? "是（" + string.Join("、", live.Select(p => $"{p.Port}→{p.Peer}")) + "）"
                    : "否";
                if (cloud.Count > 0)
                    ev["非串流的已建立连接（对端不在本网段）"] = string.Join("、",
                        cloud.Select(p => $"{p.Port}→{p.Peer}"));
                if (unparsed.Count > 0)
                    ev["对端无法解析的已建立连接（IPv6 或非 IP，未判定是否串流）"] = string.Join("、",
                        unparsed.Select(p => $"{p.Port}→{p.Peer}"));

                if (ev.ContainsKey("UDP 38850"))
                    return new CheckResult("udp-discovery", CheckStatus.Pass,
                        "UDP 38850 正在监听（发现/配对协议就绪）",
                        "头显下一次搜索时能收到这台 PC 的应答。但要注意搜索窗口很窄："
                        + "头显每次刷新只广播 **一个** 包，回包窗口硬编码 3000 ms，没有重试"
                        + "（ComputerDiscoveryClient.cs:98-107）。"
                        + "**所以「先开头显、后开 Streamer」就会搜不到**——不是坏了，是那 3 秒已经过去了。\n\n"
                        + "**这一项通过，只代表 PC 这一半准备好了，不代表头显那边没问题。**"
                        + "把「头显到底有没有把发现包发出来」和「包到了但 PC 没回」分开，"
                        + "需要抓包（pktmon，要管理员权限）或从头显侧读日志；"
                        + "本工具两样都做不到，所以那一段是**测不到的盲区**，不是通过。",
                        ev, Array.Empty<FixAction>(),
                        "顺序永远是先开 PC 上的 Streamer，再在头显里点搜索。"
                        + "补丁基线的交接笔记里也记着同一条：先开 Quest VD 后开 PC Streamer 可能搜不到。\n\n"
                        + "要在本机分辨「包没来」还是「包来了没回」，以管理员身份跑：\n"
                        + "  直接跑 tools/capture-discovery.ps1（仓库里，管理员权限）：它会抓 60 秒、转成文本、筛出 38850 并区分是不是广播，省掉手工拼这一串。底层就是 pktmon start --capture --pkt-size 0 --comp nics 然后在 out.txt 里找 UDP 目的端口 38850。");

                if (live.Count > 0)
                    return new CheckResult("udp-discovery", CheckStatus.Pass,
                        "串流中：到头显的通道已建立，Streamer 已释放发现端口（正常）",
                        "实测：Streamer 空闲时绑 UDP 38850，进入会话后把它释放掉。"
                        + "此时再报「发现通道没有活动」就是假警报。", ev, Array.Empty<FixAction>());

                if (ev.ContainsKey("UDP 38860"))
                    return new CheckResult("udp-discovery", CheckStatus.Warn,
                        "只看到 UDP 38860 的广播脉冲，没看到 38850 在监听",
                        "广播在发但配对收包口没开：头显可能搜得到却配不上对。",
                        ev, Array.Empty<FixAction>(),
                        "先看 streamer-proc；Streamer 刚启动的一两分钟内 38850 还没绑上也属正常。");

                return new CheckResult("udp-discovery", CheckStatus.Warn,
                    "UDP 38850/38860 都没有套接字",
                    "发现通道完全没开。发现靠定向广播：PC 收 38850 的配对请求并单播回包，"
                    + "同时周期广播 38860。两者都没有，通常意味着 Streamer 进程没在跑。",
                    ev, Array.Empty<FixAction>(),
                    "先看 streamer-proc 与 svc-log 两项；进程起来后这里会变成通过。");
            });

    private static List<string> ReadLines(string path)
    {
        try { return File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToList(); }
        catch (IOException) { return new List<string>(); }
        catch (UnauthorizedAccessException) { return new List<string>(); }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}