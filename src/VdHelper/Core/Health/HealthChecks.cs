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
        StreamerChecks.StreamerProcessCheck(),
        StreamerChecks.ServiceLogCheck(),
        StreamerChecks.UdpDiscoveryCheck(),
        WindowsStateChecks.StreamerSettingsCheck(),
        WindowsStateChecks.IcsCheck(),
        WindowsStateChecks.OutboundPolicyCheck(),
        WindowsStateChecks.ThirdPartyAvCheck(),
        WindowsStateChecks.RouteMetricCheck(),
        WindowsStateChecks.WirelessOnlyCheck(),
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

                return Task.FromResult(new CheckResult("net-apipa", CheckStatus.Warn,
                    $"{stale.Count} 块离线网卡持有 APIPA 地址（{string.Join("、", stale.Select(a => a.Name))}）",
                    "这些地址不参与正常通信，但会让网卡枚举与广播绑定选错目标。禁用不用的网卡是最稳的处理。",
                    ev, Fixes.DisableAdapters(stale.Select(a => a.Name)),
                    "若这些网卡确实要用（备用网口、VPN 客户端），请勿禁用，改为排查它们为何停留在 APIPA。"));
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

    private static ICheck VdPortCheck() =>
        CheckFactory.Delegate(
            new("port-vd", "VD 端口", "38810/20/30/40 有没有被别的程序占着？", "端口"),
            async ct =>
            {
                var probed = NetworkInventory.VdPorts
                    .Select(p => (Port: p, Listening: NetworkInventory.IsListening(p)))
                    .ToList();
                var busy = probed.Where(p => p.Listening).ToList();
                var ev = probed.ToDictionary(p => p.Port.ToString(), p => p.Listening ? "被监听" : "空闲");
                if (busy.Count == 0)
                    return new CheckResult("port-vd", CheckStatus.Pass, "四个 VD 端口都空闲",
                        "没有冲突。", ev, Array.Empty<FixAction>());

                var owners = await NetworkInventory.BusyPortOwnersAsync(busy.Select(b => b.Port), ct)
                    .ConfigureAwait(false);
                foreach (var p in busy)
                    ev[p.Port.ToString()] = owners.TryGetValue(p.Port, out var owner)
                        ? $"被监听: {owner}"
                        : "被监听: 未能识别占用进程";

                var isVd = busy.Any(b => owners.GetValueOrDefault(b.Port, "")
                    .Contains("VirtualDesktop", StringComparison.OrdinalIgnoreCase));
                return new CheckResult("port-vd", CheckStatus.Pass,
                    $"{busy.Count} 个端口被监听：{string.Join("、", busy.Select(b => b.Port + "(" + owners.GetValueOrDefault(b.Port, "未知") + ")"))}",
                    isVd
                        ? "监听者是 Virtual Desktop 本身，说明串流服务正常在听。"
                        : "监听者不是 Virtual Desktop：可能是上次未退出的 Streamer，也可能是端口冲突。",
                    ev, Array.Empty<FixAction>());
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

    internal static async Task<Dictionary<string, string>> PsEvidenceAsync(string script, CancellationToken ct)
    {
        var r = await PowerShellRunner.RunAsync(script, ct: ct).ConfigureAwait(false);
        var lines = r.Ok
            ? r.StdOut.Split('\r', '\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList()
            : new List<string>();
        return new Dictionary<string, string>
        {
            ["_exit"] = r.ExitCode.ToString(),
            ["_lines"] = string.Join(" ;; ", lines),
            ["_first"] = lines.FirstOrDefault() ?? r.Combined,
            ["_script"] = script,
        };
    }
}