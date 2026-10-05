using System.Globalization;
using VdHelper.Core.Checks;
using VdHelper.Core.Diagnosis;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>Escapes a value for embedding in a single-quoted PowerShell string literal.</summary>
public static class Ps
{
    public static string Literal(string s) => "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'";

    public static string Invariant(decimal d) => d.ToString(CultureInfo.InvariantCulture);
}

public static class HealthChecks
{
    private const string PsProfile =
        "Get-NetConnectionProfile | Select-Object InterfaceAlias,NetworkCategory | Format-Table -AutoSize | Out-String -Width 200";

    private const string PsVdRule =
        "Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like 'Virtual Desktop*' } | Select-Object DisplayName,Enabled,Direction,Action | Format-Table -AutoSize | Out-String -Width 200";

    private const string PsDefender =
        "Get-NetFirewallProfile | Select-Object Name,Enabled | Format-Table -AutoSize | Out-String -Width 120";

    private const string PsService =
        "Get-Service | Where-Object { $_.Name -like 'VirtualDesktop*' } | Select-Object Name,Status,StartType | Format-Table -AutoSize | Out-String -Width 160";

    public static IReadOnlyList<ICheck> Create() =>
    [
        PrimaryAdapterCheck(),
        StaleApipaAdapterCheck(),
        VirtualAdapterCheck(),
        NetworkProfileCheck(),
        VdFirewallRuleCheck(),
        DefenderFirewallCheck(),
        VdServiceCheck(),
        VdPortCheck(),
        StaleSessionCheck(),
        StreamerChecks.StreamerProcessCheck(),
        StreamerChecks.ServiceLogCheck(),
        StreamerChecks.UdpDiscoveryCheck(),
        WindowsStateChecks.StreamerSettingsCheck(),
        WindowsStateChecks.IcsCheck(),
        WindowsStateChecks.OutboundPolicyCheck(),
        WindowsStateChecks.ThirdPartyAvCheck(),
        WindowsStateChecks.RouteMetricCheck(),
        WindowsStateChecks.WirelessOnlyCheck(),
        ReachabilityCheck.Create(),
        .. LinkRateChecks.Create(),
        .. NatChecks.Create(),
        .. FirewallPairChecks.Create(),
        .. PerformanceChecks.Create(),
        .. MachineStateChecks.Create(),
        .. GpuRuntimeChecks.Create(),
        WifiQualityCheck.Create(),
        LossProbe.Create(),
    ];

    // ---------------------------------------------------------------- adapters

    /// <summary>Adapters a user can actually act on; Windows' filter miniports are excluded.</summary>
    public static IReadOnlyList<AdapterView> RealAdapters() =>
        NetworkInventory.ReadAdapters().Where(a => a.IsUserAdapter).ToList();

    private static ICheck PrimaryAdapterCheck() =>
        CheckFactory.Delegate(
            new("net-primary", "主网卡", "有没有一块拿得到局域网地址的网卡？", "网卡"),
            ct =>
            {
                var withGateway = RealAdapters()
                    .Where(a => a.IsUp && a.PrimaryIPv4 is not null && a.HasDefaultGateway).ToList();
                var ups = withGateway.Count > 0
                    ? withGateway
                    : NetworkInventory.PrimaryCandidates();
                var ev = new Dictionary<string, string>
                {
                    ["候选网卡"] = string.Join(" | ", ups.Select(a => $"{a.Name} {a.PrimaryIPv4} 网关={string.Join(',', a.Gateways.Select(g => g.ToString()))}")),
                    ["用户网卡"] = string.Join(" | ", RealAdapters().Select(a => $"{a.Name}[{a.Status}] {(a.IsVirtual ? "虚拟" : "")}")),
                    ["系统伪接口（不计入）"] = NetworkInventory.ReadAdapters().Count(a => !a.IsUserAdapter).ToString(),
                };
                if (ups.Count == 0)
                    return Task.FromResult(new CheckResult("net-primary", CheckStatus.Block,
                        "没有任何网卡持有可用的局域网地址",
                        "串流需要 PC 有一个和头显同网段的地址。所有网卡都没有非 169.254 地址时，先解决 DHCP 或网卡本身。",
                        ev, Fixes.DisableUnusableAdapters(),
                        "检查网线/无线是否连上、路由器是否分配地址，或手动指定静态 IP 与网关。"));

                return Task.FromResult(new CheckResult("net-primary", CheckStatus.Pass,
                    $"{ups.Count} 块网卡可用，主用 {ups[0].Name} ({ups[0].PrimaryIPv4})",
                    "PC 侧地址正常。", ev, Array.Empty<FixAction>()));
            });

    private static ICheck StaleApipaAdapterCheck() =>
        CheckFactory.Delegate(
            new("net-apipa", "APIPA 残留网卡", "有没有离线的网卡占着 169.254 自我地址？", "网卡"),
            ct =>
            {
                var adapters = RealAdapters();
                var stale = adapters.Where(a => !a.IsUp && a.ApipaAddresses.Count > 0).ToList();
                var ev = new Dictionary<string, string>
                {
                    ["APIPA 网卡数"] = stale.Count.ToString(),
                    ["明细"] = string.Join(" | ", stale.Select(a => $"{a.Name}={string.Join(',', a.ApipaAddresses.Select(x => x.ToString()))} [{a.Status}]")),
                    ["说明"] = "169.254.0.0/16 是 DHCP 失败时的自我分配地址，离线网卡仍持有它会干扰广播发现与路由选路。",
                };
                if (stale.Count == 0)
                    return Task.FromResult(new CheckResult("net-apipa", CheckStatus.Pass,
                        "没有离线网卡持有 APIPA 地址", "正常。", ev, Array.Empty<FixAction>()));

                // No repair here, and the absence is deliberate. These adapters are already down;
                // disabling one changes nothing observable today and makes the state permanent, so
                // clicking it costs the user the adapter later (Wi-Fi and Bluetooth Network
                // Connection are in this list on a normal laptop) in exchange for nothing. The next
                // -actions panel used to rank it as '再修', above eleven findings worth knowing.
                // Disabling is also the wrong remedy even in the general case: an adapter stuck on
                // APIPA is failing to get a DHCP lease, and the address that needs fixing is the
                // lease, not the adapter.
                return Task.FromResult(new CheckResult("net-apipa", CheckStatus.Warn,
                    $"{stale.Count} 块离线网卡持有 APIPA 地址（{string.Join("、", stale.Select(a => a.Name))}）",
                    "这些网卡本来就是 Down 状态，**禁用它们不会改善任何当下的行为**，"
                    + "只会把这个状态变成永久的——之后想用 Wi-Fi 或蓝牙网络连接的人会发现它没了。"
                    + "这里只报告、不提供一键禁用。",
                    ev, Array.Empty<FixAction>(),
                    "真正该做的是让它们不再停留在 APIPA：检查这些网卡为何拿不到 DHCP 地址"
                    + "（网线/USB 转接口没插好、DHCP 池耗尽、交换机没开）。"
                    + "如果确认某块网口永远不用，可以在「网络连接」里手动禁用它——那是你的决定，不是工具替你按。"));
            });

    private static ICheck VirtualAdapterCheck() =>
        CheckFactory.Delegate(
            new("net-virtual", "虚拟网卡", "Hyper-V / WSL / VPN 虚拟网卡是否介入？", "网卡"),
            ct =>
            {
                var virt = RealAdapters().Where(a => a.IsVirtual && a.IsUp).ToList();
                var ev = new Dictionary<string, string>
                {
                    ["启用的虚拟网卡"] = string.Join(" | ", virt.Select(a => $"{a.Name} {string.Join(',', a.Addresses.Select(x => x.ToString()))}")),
                    ["已排除的系统伪接口数"] = NetworkInventory.ReadAdapters().Count(a => !a.IsUserAdapter).ToString(),
                    ["说明"] = "虚拟交换机会改变广播/组播转发路径，WSL2 与 VPN 客户端尤其常见。",
                };
                if (virt.Count == 0)
                    return Task.FromResult(new CheckResult("net-virtual", CheckStatus.Pass,
                        "没有启用中的虚拟网卡", "正常。", ev, Array.Empty<FixAction>()));

                return Task.FromResult(new CheckResult("net-virtual", CheckStatus.Warn,
                    $"{virt.Count} 块虚拟网卡启用中：{string.Join("、", virt.Select(a => a.Name))}",
                    "不一定致障，但在「找不到头显」时应当先把它们排除掉。", ev,
                    Array.Empty<FixAction>(),
                    "排查顺序建议：先临时停用 VPN/WSL/虚拟网卡，再测是否恢复；确认无关后再长期停用。"));
            });

    /// <summary>A socket older than this is treated as a leftover rather than a live stream.</summary>
    private static readonly TimeSpan FreshSession = TimeSpan.FromMinutes(2);

    private static TimeSpan Age(DateTime since) => DateTime.Now - since;

    private static string DescribeAge(DateTime since)
    {
        var age = Age(since);
        return age.TotalHours >= 1
            ? $"{(int)age.TotalHours} 小时 {age.Minutes} 分前"
            : age.TotalMinutes >= 1 ? $"{age.Minutes} 分钟前" : $"{Math.Max(0, age.Seconds)} 秒前";
    }

    private static ICheck VdPortCheck() =>
        CheckFactory.Delegate(
            new("port-vd", "VD 端口归属", "38810/20/30/40 被谁占着？", "端口"),
            async ct =>
            {
                // Windows is asked once, about every socket state. Binding a probe socket instead
                // fails with WSAEACCES whenever the port is held at all, which cannot tell a healthy
                // Streamer from a foreign squatter — and it reported all four ports free while a
                // live session was up (see notes/2026-10-05-port-state-finding.md).
                var (ports, raw) = await NetworkInventory.ObserveVdPortsAsync(NetworkInventory.VdPorts, ct)
                    .ConfigureAwait(false);

                var ev = new Dictionary<string, string>
                {
                    ["采样时间"] = DateTime.Now.ToString("HH:mm:ss"),
                    ["查询原文"] = string.IsNullOrWhiteSpace(raw) ? "(这四个端口上没有任何套接字)" : raw,
                };
                foreach (var v in ports)
                    ev[v.Port.ToString()] = v.State switch
                    {
                        PortState.Established => $"已建立 → {v.Peer}（{v.Owner}）",
                        PortState.Listen => $"在监听（{v.Owner}）",
                        PortState.Bound => $"已绑定未监听（{v.Owner}）",
                        _ => "空闲",
                    };

                static bool IsVd(string owner) =>
                    owner.Contains("VirtualDesktop", StringComparison.OrdinalIgnoreCase);

                var foreign = ports.Where(p => p.State != PortState.Free && !IsVd(p.Owner)).ToList();
                if (foreign.Count > 0)
                    return new CheckResult("port-vd", CheckStatus.Block,
                        $"{foreign.Count} 个端口被非 VD 程序占着：{string.Join("、", foreign.Select(p => p.Port + "(" + p.Owner + ")"))}",
                        "别的程序占住了 VD 的端口，Streamer 绑不上，表现为连上就断或根本连不上。",
                        ev, Array.Empty<FixAction>(),
                        "先确认那是什么程序（展开「查询原文」看进程名），关掉它再重测；不要直接杀进程。");

                var held = ports.Where(p => p.State != PortState.Free).ToList();
                if (held.Count > 0)
                    return new CheckResult("port-vd", CheckStatus.Pass,
                        $"{held.Count} 个端口由 Virtual Desktop 持有，没有被别的程序抢占",
                        "端口归属正常。是否有活动会话、会不会是残留，看 session-stale 那项。",
                        ev, Array.Empty<FixAction>());

                return new CheckResult("port-vd", CheckStatus.Pass, "四个 VD 端口当前没有任何套接字",
                    "端口空着通常意味着 Streamer 没在跑，或者它刚退出。看 streamer-proc 那项确认。",
                    ev, Array.Empty<FixAction>());
            });

    /// <summary>
    /// Windows leaves a socket in Established long after the peer has gone: measured here, four
    /// sockets stayed that way for 49 minutes while the headset had closed every one of its ports.
    /// To the user that looks like "connected but nothing happens", and it is its own failure
    /// mode rather than a port problem, so it gets its own check.
    /// </summary>
    /// <summary>Same /24 — enough to tell a LAN peer from a cloud relay, and no more than claimed.</summary>
    private static bool SameNet(System.Net.IPAddress a, System.Net.IPAddress b)
    {
        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        if (x.Length != 4 || y.Length != 4) return false;
        for (var i = 0; i < 3; i++) if (x[i] != y[i]) return false;
        return true;
    }

    private static ICheck StaleSessionCheck() =>
        CheckFactory.Delegate(
            new("session-stale", "会话新鲜度", "现在是真在串流，还是套接字没超时？", "串流"),
            async ct =>
            {
                var (ports, raw) = await NetworkInventory.ObserveVdPortsAsync(NetworkInventory.VdPorts, ct)
                    .ConfigureAwait(false);
                var established = ports.Where(p => p.State == PortState.Established).ToList();
                var ev = new Dictionary<string, string>
                {
                    ["采样时间"] = DateTime.Now.ToString("HH:mm:ss"),
                    ["查询原文"] = string.IsNullOrWhiteSpace(raw) ? "(这四个端口上没有任何套接字)" : raw,
                };

                var fresh = established.Where(p => p.Since is not null && Age(p.Since.Value) <= FreshSession).ToList();
                var stale = established.Except(fresh).ToList();

                // A freshly established VD socket is not automatically a headset session. The
                // Streamer also opens a channel to Virtual Desktop's cloud relay on startup — measured
                // here as 192.168.11.2:38810 -> 40.89.161.236:38812, where 40.89.161.236 is the
                // documented relay IP and 38812 is a remote relay port (38811-16), not a LAN one.
                // Calling that "串流中" told the user they were streaming when they were not, and it is
                // exactly the false all-clear this project keeps fixing. Only a same-subnet peer counts.
                var localNets = NetworkInventory.ReadAdapters()
                    .Where(a => a.IsUp && a.PrimaryIPv4 is not null)
                    .Select(a => a.PrimaryIPv4!).ToList();
                bool IsLan(PortView p) =>
                    !string.IsNullOrWhiteSpace(p.Peer)
                    && System.Net.IPAddress.TryParse(p.Peer.Split(':')[0], out var ip)
                    && System.Net.IPAddress.IsLoopback(ip)
                    || (System.Net.IPAddress.TryParse(p.Peer.Split(':')[0], out var peerIp)
                        && localNets.Any(l => SameNet(l, peerIp)));

                var lan = fresh.Where(IsLan).ToList();
                var relay = fresh.Except(lan).ToList();

                // Consumed by HealthReport: a headline that says "will not start" while channels are
                // up is the kind of contradiction that makes a tool untrustworthy.
                ev["_livePorts"] = string.Join(",", lan.Select(p => p.Port));
                ev["_livePeer"] = lan.Select(p => p.Peer).FirstOrDefault() ?? "";
                if (relay.Count > 0)
                    ev["云端中继连接"] = string.Join(" ;; ", relay.Select(p => $"{p.Port} -> {p.Peer}"))
                        + "（这不是头显串流：这是 Streamer 主动连到 Virtual Desktop 的云端中继）";

                if (relay.Count > 0 && lan.Count == 0)
                    return new CheckResult("session-stale", CheckStatus.Pass,
                        $"没有头显串流会话；Streamer 连着云端中继：{relay[0].Peer}",
                        "**这是「没有在串流」，不是「串流中」。**"
                        + "刚建立的 VD 通道指向的是 Virtual Desktop 的云端中继（对端不在本网段），"
                        + "不是头显。头显串流时对端应该是同网段的地址。",
                        ev, Array.Empty<FixAction>(),
                        "如果你的基线是去联网鉴权的补丁版，这条出网连接值得单独看一眼——"
                        + "它意味着 Streamer 在启动后仍会联系官方服务器。工具不阻断它，只如实报出。");

                if (fresh.Count > 0)
                    return new CheckResult("session-stale", CheckStatus.Pass,
                        $"{lan.Count} 个通道刚建立：{string.Join("、", fresh.Select(p => p.Port.ToString()))}",
                        $"建立于 {fresh.Min(p => DescribeAge(p.Since!.Value))}。PC 侧通道是通的。"
                        + (relay.Count > 0 ? "（同时还有云端中继连接，那不算串流。）" : ""),
                        ev, Array.Empty<FixAction>());

                // Only LAN sockets can be a leftover headset session. A relay socket that has not
                // aged out is a live cloud connection, not something to tell the user to clear.
                var staleLan = stale.Where(IsLan).ToList();
                if (staleLan.Count > 0)
                    return new CheckResult("session-stale", CheckStatus.Block,
                        $"{staleLan.Count} 个通道是残留套接字（最早建立于 {staleLan.Min(p => DescribeAge(p.Since!.Value))}）",
                        "Windows 只有在对端发 FIN 或超时之后才改状态。头显早就退出了、这边套接字还挂着时，"
                        + "表现就是「界面上像连着、实际什么都不发生」。重启 Streamer 能立刻清掉。",
                        ev, Fixes.RestartStreamer(),
                        "先确认头显此刻是不是真的在串流；如果早就退出了，重启 Streamer 即可，别去动路由器。");

                return new CheckResult("session-stale", CheckStatus.Pass,
                    "当前没有活动会话（也没有残留套接字）",
                    "Streamer 在正常待机，等头显来连。", ev, Array.Empty<FixAction>());
            });

    // ---------------------------------------------------------------- Windows state (PowerShell)

    private static ICheck NetworkProfileCheck() =>
        PsCheck.Create("net-profile", "网络配置文件", "主网卡的网络类别是专用还是公用？", "防火墙", PsProfile,
            CheckStatus.Warn,
            e => Lines(e).Any(l => l.Contains("Private")) && !Lines(e).Any(l => l.Contains("Public")),
            e => "主网卡网络类别：" + DescribeProfile(e),
            _ => "公用网络（Public）会让很多入站规则失效，串流服务可能只对专用网络放行。",
            Array.Empty<FixAction>(),
            "在「设置 → 网络和 Internet → 属性」里把该网络设为「专用网络」。");

    private static ICheck VdFirewallRuleCheck() =>
        PsCheck.Create("fw-vd", "VD 防火墙规则", "官方入站放行规则在不在？", "防火墙", PsVdRule,
            CheckStatus.Block,
            e => Lines(e).Any(l => l.Contains("Virtual Desktop") && l.Contains("Allow")),
            e => Lines(e).Any(l => l.Contains("Virtual Desktop"))
                ? $"找到入站放行规则（{Lines(e).FirstOrDefault(l => l.Contains("Virtual Desktop"))?.Trim() ?? "Virtual Desktop Streamer"}）"
                : "未找到 Virtual Desktop 入站规则",
            _ => "官方安装器会建一条入站放行规则；被删、被第三方防火墙接管或只装了 Android 端时会缺失。",
            Fixes.RestoreVdRule(),
            "若装过第三方杀软，确认它没有接管防火墙且允许 Virtual Desktop 进程。");

    private static ICheck DefenderFirewallCheck() =>
        PsCheck.Create("fw-defender", "防火墙总开关", "三个 profile 是否都被接管或关闭？", "防火墙", PsDefender,
            CheckStatus.Warn,
            e => !Lines(e).Any(l => l.Contains("False")),
            e => "Defender 防火墙：" + string.Join(" / ", Lines(e).Where(l => l.Contains("True") || l.Contains("False"))),
            _ => "任一 profile 被关闭或接管，VD 的入站与广播都可能被拦。",
            Array.Empty<FixAction>(),
            "被第三方杀软接管是常见情况：需要在该杀软里放行 Virtual Desktop Streamer 与其服务。");
    private static ICheck VdServiceCheck() =>
        PsCheck.Create("svc-vd", "VD 服务", "VirtualDesktop 服务在跑吗？", "服务", PsService,
            CheckStatus.Warn,
            e => Lines(e).Any(l => l.Contains("Running")),
            e => Lines(e).FirstOrDefault(l => l.Contains("VirtualDesktop")) ?? "服务未安装",
            _ => "服务不在运行，PC 侧无法建立串流连接。",
            Fixes.StartVdService(),
            "若服务被优化软件禁用，需要在杀软/系统优化的排除列表里放行。");

    // ---------------------------------------------------------------- helpers

    internal static IReadOnlyList<string> Lines(IReadOnlyDictionary<string, string> e) =>
        e.TryGetValue("_lines", out var raw) && raw.Length > 0
            ? raw.Split(" ;; ", StringSplitOptions.None)
            : Array.Empty<string>();

    private static string DescribeProfile(IReadOnlyDictionary<string, string> e)
    {
        var line = Lines(e).FirstOrDefault(l => l.Contains("Ethernet"));
        if (line is null) return "未取到";
        return line.Contains("Private") ? "专用网络 (Private)"
            : line.Contains("Public") ? "公用网络 (Public)"
            : line;
    }

    /// <summary>
    /// Runs a read-only PowerShell query and turns its output into the evidence map.
    /// <para>
    /// Stderr is captured deliberately. PowerShell exits 0 even when a cmdlet is missing — a script
    /// calling an unavailable cmdlet writes to stderr, prints nothing to stdout, and still reports
    /// success. With stderr discarded, <c>!Lines.Any(...)</c> judged "nothing wrong" and the check
    /// came back Pass, which is a false all-clear from a machine the tool simply could not read.
    /// The caller turns a non-empty stderr with empty stdout into Unknown instead.
    /// </para>
    /// </summary>
    internal static async Task<Dictionary<string, string>> PsEvidenceAsync(string script, CancellationToken ct)
    {
        var r = await PowerShellRunner.RunAsync(script, ct: ct).ConfigureAwait(false);
        var lines = r.Ok
            ? r.StdOut.Split('\r', '\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList()
            : new List<string>();
        var err = (r.StdErr ?? "").Split('\r', '\n')
            .Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
        return new Dictionary<string, string>
        {
            ["_exit"] = r.ExitCode.ToString(),
            ["_lines"] = string.Join(" ;; ", lines),
            ["_errLines"] = string.Join(" ;; ", err),
            ["_first"] = lines.FirstOrDefault() ?? r.Combined,
            ["_script"] = script,
        };
    }
}