using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using VdHelper.Core.Checks;
using VdHelper.Core.Diagnosis;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// Three failure modes that survive every other check being green, because none of them is a
/// "value" — they are a link speed, a background process, and a session that owns the display.
/// Covers D4 (negotiated link rate), B4 (VPN client still resident) and E2 (RDP session holding
/// the monitor) of <c>research/09-failure-corpus/02-symptom-to-rootcause.md</c>.
/// </summary>
public static class LinkRateChecks
{
    public static IReadOnlyList<ICheck> Create() =>
    [
        LinkSpeedCheck(),
        VpnProcessCheck(),
        RdpSessionCheck(),
    ];

    // ================================================================ D4 协商速率

    /// <summary>1 Gbps — the official requirement for the wired computer link.</summary>
    private const long OneGbps = 1_000_000_000L;

    /// <summary>
    /// <c>query session</c> is the same binary as <c>qwinsta</c>, but the <c>query</c> wrapper exits
    /// non-zero even on success — verified on this machine (EXIT=1 for <c>query session</c>,
    /// EXIT=0 for <c>qwinsta</c>). Judging on the exit code would turn every RDP result into
    /// "检查未能完成", so the script normalises with an explicit <c>exit 0</c>.
    /// </summary>
    private const string PsQwinsta =
        "$ErrorActionPreference = 'SilentlyContinue'; qwinsta 2>&1 | Out-String -Width 200; exit 0";

    private static readonly Regex SessionRow =
        new(@"^\s*>?\s*(?<name>\S*)\s+(?<user>\S*)\s+(?<id>\d+)\s+(?<state>\S+)\s*(?<type>\S*)",
            RegexOptions.Compiled);


    public static ICheck LinkSpeedCheck() =>
        CheckFactory.Delegate(
            new("link-rate", "协商速率", "这条网线到底协商到多少？", "物理链路"),
            ct =>
            {
                var real = HealthChecks.RealAdapters();
                var up = real.Where(a => a.IsUp && a.PrimaryIPv4 is not null).ToList();
                var gateways = up.Where(a => a.HasDefaultGateway).ToList();

                // Speed 不在 AdapterView 里（NetworkInventory 不读它），所以按接口名回查一次。
                var speeds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    long s;
                    try { s = ni.Speed; }
                    catch (NetworkInformationException) { continue; }
                    speeds.TryAdd(ni.Name, s);
                }

                string Show(AdapterView a) =>
                    speeds.TryGetValue(a.Name, out var s) ? FormatSpeed(s) : "(读不到协商速率)";

                var ev = new Dictionary<string, string>
                {
                    ["承载默认网关的网卡"] = gateways.Count == 0
                        ? "(无)"
                        : string.Join(" | ", gateways.Select(a => $"{a.Name} {a.PrimaryIPv4} 网关={string.Join(',', a.Gateways.Select(g => g.ToString()))} 速率={Show(a)}")),
                    ["全部用户网卡"] = real.Count == 0
                        ? "(读不到网卡)"
                        : string.Join(" | ", real.Select(a => $"{a.Name}[{a.Status}] {Show(a)}{(a.HasDefaultGateway ? " 有网关" : " 无网关")}")),
                    ["系统伪接口（不计入）"] = NetworkInventory.ReadAdapters().Count(a => !a.IsUserAdapter).ToString(),
                    ["官方要求"] = "Wired computer to 5 GHz AC or AX Wi-Fi router",
                    ["阈值"] = "≥ 1 Gbps 视为合格；< 1 Gbps 报隐患",
                };

                if (real.Count == 0)
                    return Task.FromResult(new CheckResult("link-rate", CheckStatus.Unknown,
                        "读不到网卡列表", "NetworkInterface 没有返回任何用户网卡。", ev,
                        Array.Empty<FixAction>(), "以管理员身份重试；若仍为空，检查网卡驱动是否正常加载。"));

                if (gateways.Count == 0)
                    return Task.FromResult(new CheckResult("link-rate", CheckStatus.Unknown,
                        up.Count == 0 ? "没有已连接的用户网卡" : "没有网卡拿到默认网关",
                        up.Count == 0
                            ? "所有用户网卡都不在 Up 状态，速率无从谈起（未接线时属正常，不要当成故障）。"
                            : "已连接但拿不到默认网关，说明还没真正接入局域网。",
                        ev, Array.Empty<FixAction>(),
                        up.Count == 0
                            ? "先把网线/无线连上再测；本项只对已连通的链路判分，不会把「没插网线」报成失败。"
                            : "确认 DHCP 已分配到 192.168.x.x 一类的局域网地址（169.254.x.x 表示 DHCP 失败）。"));

                var main = gateways[0];

                // Speed == -1 是「未知」，不是「慢」。未连上的无线网卡常年返回 -1，把它判成失败
                // 只会制造假警报，所以这里只跳过、不惩罚。
                if (!speeds.TryGetValue(main.Name, out var mainSpeed) || mainSpeed <= 0)
                    return Task.FromResult(new CheckResult("link-rate", CheckStatus.Unknown,
                        $"{main.Name} 的协商速率读不到（系统返回 -1 = 未知）",
                        "这块网卡已经连上并拿到了局域网地址，但 Windows 没有报出协商速率。"
                        + "未知不等于慢，本项不据此判失败。", ev, Array.Empty<FixAction>(),
                        "用管理员身份的 PowerShell 执行 Get-NetAdapter，看 LinkSpeed 列是否有值；"
                        + "若也是 0 bps，多半是驱动未上报，换驱动版本比改路由器更可能有用。"));

                ev["主网卡原始 Speed"] = mainSpeed.ToString();

                if (mainSpeed >= OneGbps)
                    return Task.FromResult(new CheckResult("link-rate", CheckStatus.Pass,
                        $"{main.Name} 协商速率 {FormatSpeed(mainSpeed)}",
                        "已达到官方对电脑端有线链路的要求，码率上限不是这条链路在限制。",
                        ev, Array.Empty<FixAction>()));

                return Task.FromResult(new CheckResult("link-rate", CheckStatus.Warn,
                    $"{main.Name} 只协商到 {FormatSpeed(mainSpeed)}（不到 1 Gbps）——这是社区里常见的隐蔽坑",
                    "串流带宽先被这条链路卡住：用户看到的是「画质差」「码率上不去」「卡在测速」，"
                    + "但 ping 正常、测速数字看着也不差，所以很难自己想到。"
                    + "官方 Computer Requirements 要求电脑走网线接 5GHz AC/AX 路由器，"
                    + "语料 R46 的用户就是靠逐段排查才发现路由器 LAN 口只支持 10/100M。",
                    ev, Array.Empty<FixAction>(),
                    "按这个顺序逐段查（从最常出问题的一段开始）："
                    + "① 网线：Cat5e 及以上才支持千兆，中途有一段是 Cat5e/Cat5 就降速；水晶头压线不良也常见。"
                    + "② 交换机端口：百兆口插千兆线必然降速，端口旁的速率指示灯会亮 100M。"
                    + "③ 路由器 LAN 口：老路由器的 LAN 口可能只有 10/100M（正是 R46 的最终根因）。"
                    + "④ 网卡属性：设备管理器 → 网卡 → 高级 → 「速度和双工」被手动设成了 100 Mbps 半双工。"
                    + "工具不改网卡设置：改「速度和双工」或重装驱动会影响这台机器的全部网络连接，由用户自己决定。"));
            });

    /// <summary>bits/s → 可读形式；保留一位小数，够定位问题又不假装精确。</summary>
    private static string FormatSpeed(long bitsPerSecond) =>
        bitsPerSecond < 0 ? "未知 (-1)"
        : bitsPerSecond >= OneGbps ? $"{bitsPerSecond / 1_000_000_000d:0.##} Gbps"
        : bitsPerSecond >= 1_000_000 ? $"{bitsPerSecond / 1_000_000d:0.##} Mbps"
        : bitsPerSecond >= 1_000 ? $"{bitsPerSecond / 1_000d:0.##} Kbps"
        : $"{bitsPerSecond} bps";

    // ================================================================ B4 VPN 进程存活

    /// <summary>
    /// 常见商业 VPN 与代理客户端的进程名（不含 .exe）。这里查的是「进程在不在」，不是「有没有连上」
    /// ——官方 FAQ 只说 "Make sure your PC isn't running VPN software"，这句话让用户以为退出界面就等于没在跑，
    /// 而 R19 原话是「我以为从托盘退出 NordVPN 并且没连接就够了，但它后台肯定还在跑某些东西」。
    /// </summary>
    private static readonly string[] VpnProcessNames =
    [
        "openvpn", "openvpn-service", "wireguard", "nordvpn", "nordvpn-service",
        "anyconnect", "vpnagent", "forticlient", "fortisvc", "globalprotect", "paned",
        "tailscaled", "zerotier-one", "mullvad-daemon", "protonvpn", "surfshark",
        "expressvpn", "cyberghost", "psiphon", "shadowsocks", "v2ray", "sing-box",
        "nekoray", "clash", "mihomo", "tor", "proxifier",
    ];

    /// <summary>
    /// 进程名整体锚定，避免误伤：裸正则会把 AggregatorHost（内含 "tor"）、WUDFCompanionHost
    /// （内含 "pan"）之类无关进程算成 VPN——两个都在本机实测复现过。
    /// </summary>
    private static readonly string VpnNameRegex =
        "^(?:" + string.Join("|", VpnProcessNames.Select(Regex.Escape)) + ")\\.exe$";

    private static string VpnCimScript =>
        "Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | "
        + "Where-Object { $_.Name -match " + Ps.Literal(VpnNameRegex) + " } | "
        + "ForEach-Object { \"$($_.Name)|$($_.ProcessId)|$($_.ExecutablePath)\" }";

    public static ICheck VpnProcessCheck() =>
        CheckFactory.Delegate(
            new("vpn-proc", "VPN 客户端", "有没有 VPN/代理客户端在后台跑着？", "进程"),
            async ct =>
            {
                // ① .NET 精确匹配（不带 .exe）
                var byNet = new List<(string Name, int Pid)>();
                foreach (var name in VpnProcessNames)
                {
                    Process[] procs;
                    try { procs = Process.GetProcessesByName(name); }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or SystemException)
                    {
                        continue; // 个别进程名查询会抛异常，跳过即可，不该让整项检测失败
                    }
                    foreach (var p in procs)
                    {
                        byNet.Add((p.ProcessName, p.Id));
                        p.Dispose();
                    }
                }

                // ② CIM：进程名 + PID + 可执行路径
                var r = await PowerShellRunner.RunAsync(VpnCimScript, ct: ct).ConfigureAwait(false);
                var byCim = new List<(string Name, int Pid, string Path)>();
                if (r.StdOut is { Length: > 0 })
                    foreach (var line in r.StdOut.Split('\r', '\n').Select(l => l.Trim()).Where(l => l.Length > 0))
                    {
                        var parts = line.Split('|');
                        if (parts.Length < 2 || !int.TryParse(parts[1].Trim(), out var pid)) continue;
                        byCim.Add((parts[0].Trim(), pid, parts.Length > 2 ? parts[2].Trim() : ""));
                    }

                var ev = new Dictionary<string, string>
                {
                    [".NET 精确匹配命中"] = byNet.Count == 0
                        ? "无"
                        : string.Join(" | ", byNet.Select(p => $"{p.Name}.exe pid={p.Pid}")),
                    ["CIM 命中（名字 PID 路径）"] = byCim.Count == 0
                        ? "无"
                        : string.Join(" | ", byCim.Select(p => $"{p.Name} pid={p.Pid} path={(p.Path.Length == 0 ? "(读不到路径)" : p.Path)}")),
                    ["监听名单"] = string.Join(", ", VpnProcessNames),
                    ["官方口径"] = "Make sure your PC isn't running VPN software (FAQ)",
                };

                if (!r.Ok)
                {
                    ev["PowerShell"] = "查询失败：" + r.Combined;
                    return new CheckResult("vpn-proc", CheckStatus.Unknown, "进程枚举没能完成",
                        "WMI 查询返回非零退出码，无法判断本机是否跑着 VPN 客户端。", ev,
                        Array.Empty<FixAction>(), "以管理员身份重试。");
                }

                if (byCim.Count == 0 && byNet.Count == 0)
                    return new CheckResult("vpn-proc", CheckStatus.Pass,
                        "没有发现 VPN/代理客户端进程",
                        "本项查的是进程存活。两路枚举都为空，说明这类客户端没有常驻。",
                        ev, Array.Empty<FixAction>());

                // 两路不一致要讲清楚 —— 这正是官方措辞的漏洞所在：托盘里「已退出」的客户端，
                // 服务进程往往还在，只是不在界面上。
                var onlyNet = byNet.Where(n => byCim.All(c => c.Pid != n.Pid)).ToList();
                var onlyCim = byCim.Where(c => byNet.All(n => n.Pid != c.Pid)).ToList();
                var mismatch = new List<string>();
                if (onlyNet.Count > 0)
                    mismatch.Add("只在 .NET 枚举里出现：" + string.Join("、", onlyNet.Select(p => $"{p.Name}.exe(pid={p.Pid})")));
                if (onlyCim.Count > 0)
                    mismatch.Add("只在 CIM 枚举里出现：" + string.Join("、", onlyCim.Select(p => $"{p.Name}(pid={p.Pid})")));
                if (mismatch.Count > 0)
                    ev["两路差异"] = string.Join(" ;; ", mismatch);

                var names = byCim.Select(p => p.Name)
                    .Concat(byNet.Select(p => p.Name + ".exe"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // 「连上了」和「只是进程在跑」是两件事：前者多一张虚拟网卡并接管默认路由，
                // 后者同样会装 WFP 过滤驱动吞掉 UDP 广播。这就是官方那句话不够用的地方。
                var tunnelUp = NetworkInventory.ReadAdapters()
                    .Where(a => a.IsUp && a.PrimaryIPv4 is not null && (a.IsVirtual || IsTunnelName(a)))
                    .Select(a => a.Name).ToList();

                var detail =
                    "命中的 VPN/代理相关进程：" + string.Join("、", names) + "。"
                    + "要点分清两种情况："
                    + "① 「已连上」——界面上有隧道，本机多一张虚拟网卡且默认路由被它接管；"
                    + "② 「只是进程在跑、没连」——界面关了、托盘退了，但服务进程或过滤驱动还在。"
                    + "官方 FAQ 只说 PC 上不该跑 VPN，这两种情况在用户眼里长得一模一样，都要排查。"
                    + "更要命的是：VD 的局域网发现走 UDP 广播（UDP 38850 / 周期广播 38860），"
                    + "而 VPN 客户端常装 WFP 过滤驱动把广播吞掉或改写，"
                    + "于是头显收到零个应答 → 「No computer found」，而 ping 电脑 IP 是通的。"
                    + (mismatch.Count > 0 ? " 两路枚举结果不一致，说明有进程没在界面上露头（详见证据栏「两路差异」）。" : "")
                    + (tunnelUp.Count > 0 ? " 本机当前有已连接的虚拟/隧道网卡：" + string.Join("、", tunnelUp) + "，偏向情况 ①。" : " 本机没有已连接的隧道网卡，更像是情况 ②（进程在跑、没连）。");

                return new CheckResult("vpn-proc", CheckStatus.Warn,
                    "本机跑着 VPN/代理相关进程：" + string.Join("、", names),
                    detail, ev, Array.Empty<FixAction>(),
                    "先完全退出 VPN 再重测一次头显能不能发现电脑，顺序是："
                    + "① 托盘图标右键退出（不是只关窗口）；"
                    + "② 任务管理器里确认上述进程真的消失了；"
                    + "③ 关完再测，仍不行就临时禁用该客户端的过滤驱动 / 局域网阻断开关（如 NordVPN 的「Invisibility on LAN」、「CyberSec」）。"
                    + "工具不会替你关 VPN：关掉别人的 VPN 是破坏性操作，可能正在承载他的出口或远程接入，"
                    + "必须由用户自己判断并执行。另提醒：R03 的实证案例根因在头显侧而非 PC 侧，"
                    + "PC 全绿时要去头显设置里把 VPN 关掉。");
            });

    private static bool IsTunnelName(AdapterView a) =>
        a.Name.Contains("vpn", StringComparison.OrdinalIgnoreCase)
        || a.Name.Contains("tap", StringComparison.OrdinalIgnoreCase)
        || a.Name.Contains("tunnel", StringComparison.OrdinalIgnoreCase)
        || a.Name.Contains("wintun", StringComparison.OrdinalIgnoreCase)
        || a.Description.Contains("vpn", StringComparison.OrdinalIgnoreCase)
        || a.Description.Contains("tap", StringComparison.OrdinalIgnoreCase);

    // ================================================================ E2 RDP 会话独占显示器

    public static ICheck RdpSessionCheck() =>
        CheckFactory.Delegate(
            new("rdp-session", "活动 RDP 会话", "有没有远程桌面会话正占着显示器？", "显示"),
            async ct =>
            {
                var r = await PowerShellRunner.RunAsync(PsQwinsta, ct: ct).ConfigureAwait(false);
                var raw = (r.StdOut ?? string.Empty).TrimEnd();
                var ev = new Dictionary<string, string>
                {
                    ["query session 原文"] = raw.Length == 0 ? "(空)" : raw,
                    ["命令"] = "query session   （等价于 qwinsta；注意 query.exe 会返回退出码 1，脚本里已归一）",
                    ["退出码"] = r.ExitCode.ToString(),
                };
                if (r.StdErr is { Length: > 0 } && !string.IsNullOrWhiteSpace(r.StdErr))
                    ev["stderr"] = r.StdErr.Trim();

                var rows = new List<(string Name, string User, int Id, string State)>();
                foreach (var line in raw.Split('\r', '\n'))
                {
                    if (line.Contains("SESSIONNAME", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("会话名", StringComparison.Ordinal))
                        continue;
                    var m = SessionRow.Match(line);
                    if (!m.Success) continue;
                    if (!int.TryParse(m.Groups["id"].Value.Trim(), out var id)) continue;
                    rows.Add((m.Groups["name"].Value.Trim(), m.Groups["user"].Value.Trim(), id, m.Groups["state"].Value.Trim()));
                }

                if (rows.Count == 0)
                    return new CheckResult("rdp-session", CheckStatus.Unknown,
                        raw.Length == 0 ? "读不到会话列表" : "会话列表解析不出行",
                        raw.Length == 0
                            ? "query session 没有输出，可能是系统不支持或权限不足。"
                            : "命令有输出但格式与预期不符：" + raw,
                        ev, Array.Empty<FixAction>(), "以管理员身份重试；也可手动执行 query session 核对。");

                // 只有「真的登录过、持有显示输出」的远程会话才算数。qwinsta 里还有一批保留槽位：
                // `listen`（RDP 监听器预留的会话槽，官方示例里就有一行 `rdp-tcp ... 2 listen`）、
                // `idle`（会话池里未分配的槽位）、以及只有 ID 没有 SESSIONNAME 的行 —— 它们不占显示器，
                // 若计进去会让整份报告凭空变成 Blocked。状态名大小写在不同版本/语言下不一致，故忽略大小写。
                var notOwning = new[] { "listen", "idle", "init", "down" };
                var remote = rows.Where(r => r.Name.Length > 0
                                             && !r.Name.Equals("console", StringComparison.OrdinalIgnoreCase)
                                             && !r.Name.Equals("services", StringComparison.OrdinalIgnoreCase)
                                             && !notOwning.Contains(r.State, StringComparer.OrdinalIgnoreCase)).ToList();
                var console = rows.Where(r => r.Name.Equals("console", StringComparison.OrdinalIgnoreCase)).ToList();

                ev["会话数"] = rows.Count.ToString();
                ev["远程会话"] = remote.Count == 0
                    ? "无"
                    : string.Join(" | ", remote.Select(r => $"{r.Name}(id={r.Id}, {r.User}, {r.State})"));
                var skipped = rows.Where(r => r.Name.Length > 0
                                               && !r.Name.Equals("console", StringComparison.OrdinalIgnoreCase)
                                               && !r.Name.Equals("services", StringComparison.OrdinalIgnoreCase)
                                               && !remote.Contains(r)).ToList();
                if (skipped.Count > 0)
                    ev["不计为占屏的保留槽位"] = string.Join(" | ", skipped.Select(r => $"{r.Name}(id={r.Id}, {r.State})"));

                if (remote.Count == 0)
                    return new CheckResult("rdp-session", CheckStatus.Pass,
                        console.Count > 0
                            ? $"只有本机 console 会话（{console.Count} 条），没有远程桌面在跑"
                            : "没有远程桌面会话在跑",
                        "显示器输出没有被任何 RDP 会话接管，VD 拿得到画面。",
                        ev, Array.Empty<FixAction>());

                return new CheckResult("rdp-session", CheckStatus.Block,
                    $"{remote.Count} 个远程桌面会话占着显示输出："
                        + string.Join("、", remote.Select(r => $"{r.Name}（id={r.Id}，用户 {r.User}，{r.State}）")),
                    "Windows 把显示输出交给了那个 RDP 会话——哪怕它已经断开但没注销（Disconnected）。"
                    + "这时串流的表现是：连上了、能听见声音、画面全黑或干脆没有画面，"
                    + "而 PC 上的 ping、带宽、Streamer 一切正常，PC 侧其他检测会全绿放行。"
                    + "语料 R50 的原话就是「如果另一台机器通过 Microsoft Remote Desktop 连着你的 PC，"
                    + "先断开它，否则那个显示器会显示成黑的」——这与 VD 第二显示器逻辑完全吻合，"
                    + "也是「第二次连接就黑屏」的经典真因。",
                    ev, Array.Empty<FixAction>(),
                    $"注销那个会话，命令行可复制：logoff {remote[0].Id}"
                    + (remote.Count > 1 ? $"（本机有 {remote.Count} 个远程会话，先跑 query session 看清哪个是你要的）" : "")
                    + "。等价的图形界面做法：Win+R 输入 logoff 或 tsdiscon 选该会话。"
                    + "工具不提供自动注销：注销别人（含你自己在另一台机器上的）活动会话是破坏性操作，"
                    + "会直接掐掉对方正在做的工作，必须由你确认后自己执行。"
                    + "注意「断开」和「注销」不是一回事——断开（tsdiscon）只把会话挂起，输出仍被占着。");
            });
}