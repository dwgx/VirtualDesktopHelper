﻿using System.Text.RegularExpressions;
using System.IO;
using System.Text.Json;
using VdHelper.Core.Checks;
using VdHelper.Core.Config;
using VdHelper.Core.Diagnosis;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// Checks whose evidence comes from the Streamer's own settings file or from Windows state that
/// changes the routing picture. Covers items 4, 9, 10, 17, 18, 23 of
/// <c>research/02-network-diagnosis/02-pc-checklist.md</c>.
/// </summary>
public static class WindowsStateChecks
{
    private const string PsIcs =
        "Get-Service SharedAccess -ErrorAction SilentlyContinue | Select-Object Name,Status,StartType | Format-Table -AutoSize | Out-String -Width 160";

    private const string PsAv =
        "try { Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction Stop | ForEach-Object { \"$($_.displayName) :: $($_.productState)\" } } catch { Write-Error (\"SecurityCenter2 读不到：\" + $_.Exception.Message) }";

    private const string PsMetric =
        "Get-NetIPInterface -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.ConnectionState -eq 'Connected' } "
        + "| Select-Object InterfaceAlias,InterfaceMetric,NlMtu | Sort-Object InterfaceMetric | Format-Table -AutoSize | Out-String -Width 160";

    private const string PsOutbound =
        "Get-NetFirewallProfile | Select-Object Name,DefaultOutboundAction | Format-Table -AutoSize | Out-String -Width 160";

    // ---------------------------------------------------------------- Streamer config

    /// <summary>
    /// Two failure modes hide in this file: pairing requests switched off (a new headset is
    /// silently ignored) and VD's own NetworkProfile warning muted. Both look like "voodoo"
    /// failures from the network side.
    /// </summary>
    public static ICheck StreamerSettingsCheck() =>
        CheckFactory.Delegate(
            new("cfg-streamer", "Streamer 配置", "配置里有没有把自己屏蔽掉的告警？", "配置"),
            ct =>
            {
                var s = StreamerSettings.Load();
                var ev = new Dictionary<string, string>
                {
                    ["路径"] = s.Path,
                    ["顶层键"] = s.Exists ? string.Join(", ", s.TopLevelKeys) : "(文件不存在)",
                    ["最后修改"] = s.LastWriteTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
                };
                if (!s.Exists)
                    return Task.FromResult(new CheckResult("cfg-streamer", CheckStatus.Unknown,
                        "读不到 Streamer 配置", s.ParseError ?? "文件不存在", ev,
                        Array.Empty<FixAction>(), "官方安装会写这个文件；缺失说明 Streamer 从未正常运行过。"));

                var last = StreamerSettings.ParseTimestamp(s.GetString("LastConnectDate"));
                ev["LastConnectDate"] = s.GetString("LastConnectDate") ?? "(空 = 从未成功连过)";
                ev["ShowPairingRequests"] = s.GetBool("ShowPairingRequests")?.ToString() ?? "(未设置)";
                ev["DontWarnApps"] = s.GetString("DontWarnApps") ?? string.Join(",", s.GetStringArray("DontWarnApps"));
                ev["DeviceName"] = s.GetString("DeviceName") ?? "-";
                ev["CodecName"] = s.GetString("CodecName") ?? "-";
                ev["PreferredCodec"] = s.GetRaw("PreferredCodec") ?? "-";
                // The audit caught this: these keys were listed as "present" but their values were
                // never read, so the user could not see that auto-bitrate is OFF on this machine.
                ev["AutoAdjustBitrate"] = s.GetBool("AutoAdjustBitrate")?.ToString() ?? "(未设置 → 默认 true)";
                ev["OpenXRRuntime"] = s.GetRaw("OpenXRRuntime") ?? "-";
                ev["MonitorCount"] = s.GetRaw("MonitorCount") ?? "-";
                ev["ShownH264PlusWarning"] = s.GetBool("ShownH264PlusWarning")?.ToString() ?? "-";
                var autoBitrate = s.GetBool("AutoAdjustBitrate");
                var muted = s.GetString("DontWarnApps") ?? string.Join(",", s.GetStringArray("DontWarnApps"));
                var pairingOff = s.GetBool("ShowPairingRequests") == false;
                var neverConnected = last is null;

                var problems = new List<string>();
                if (pairingOff)
                    problems.Add("ShowPairingRequests=false：靠弹窗配对新头显会被静默忽略（靠名字在客户端选则不受影响）");
                if (muted.Contains("NetworkProfile", StringComparison.OrdinalIgnoreCase))
                    problems.Add("DontWarnApps 含 NetworkProfile：官方自己的网络告警被屏蔽了");
                if (neverConnected) problems.Add("LastConnectDate 为空：这台 PC 从未成功连过");
                if (autoBitrate == false)
                    problems.Add("AutoAdjustBitrate=false：自动调码率已关，卡在「measuring bandwidth」时社区的首选解法就是把它打开");

                if (problems.Count == 0)
                    return Task.FromResult(new CheckResult("cfg-streamer", CheckStatus.Pass,
                        last is null ? "配置可读，没有被屏蔽的告警" : $"配置正常，最近一次成功连接 {last:yyyy-MM-dd}",
                        "没有发现用户自己屏蔽掉的告警。", ev, Array.Empty<FixAction>()));

                // Only "never connected" is fatal on its own. The other two are traps for users
                // who expect VD to prompt them; someone who pairs by picking the computer's name
                // in the client never sees them fire.
                var status = neverConnected ? CheckStatus.Block : CheckStatus.Warn;

                // ShowPairingRequests is the one problem here the tool can actually repair, and
                // the reason it matters is mechanical rather than advisory: when a headset's token
                // is unknown, the PC raises a pairing event and then falls off the end of the
                // loop without sending a single byte back
                // (localization/desktop/decompiled_streamer/VirtualDesktop.Streamer/-/-.112.cs:432-442,
                // three `continue` paths, none of which reach the reply at :451). With the dialog
                // suppressed, the headset is told "no computer found" for a reason no log records.
                var fixes = new List<FixAction>();
                if (pairingOff)
                    fixes.Add(new FixAction(
                        "enable-pairing-requests",
                        "打开 ShowPairingRequests",
                        "把 StreamerSettings.json 的 ShowPairingRequests 写成 true",
                        "StreamerSettings.json.vdhelper.bak",
                        "把备份文件复制回 StreamerSettings.json",
                        FixRisk.Low,
                        async ct =>
                        {
                            var p = StreamerSettings.DefaultPath;
                            if (!File.Exists(p))
                                return new FixResult(false, "找不到 StreamerSettings.json");
                            if (System.Diagnostics.Process.GetProcessesByName("VirtualDesktop.Streamer").Length > 0)
                                return new FixResult(false, "Streamer 正在运行：它有 2 秒防抖保存，会覆盖写入。先退出 Streamer 再执行。");
                            try
                            {
                                using var doc = JsonDocument.Parse("true");
                                var r = StreamerConfigWriter.Write(p, "ShowPairingRequests", doc.RootElement.Clone());
                                if (!r.Success) return new FixResult(false, r.Message);

                                // Post-condition, not just "the writer returned". Two repairs shipped
                                // false successes earlier because they reported a command's exit code
                                // without checking the thing they were supposed to change.
                                using var after = JsonDocument.Parse(await File.ReadAllTextAsync(p, ct));
                                var nowOn = after.RootElement.TryGetProperty("ShowPairingRequests", out var v)
                                            && v.ValueKind == JsonValueKind.True;
                                return nowOn
                                    ? new FixResult(true, "已确认磁盘上 ShowPairingRequests=true，备份在 " + r.BackupPath)
                                    : new FixResult(false, "写入返回成功，但重新读取文件确认不到该值 —— 已保留备份，请用备份还原");
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                                              or System.Text.Json.JsonException)
                            {
                                return new FixResult(false, ex.Message);
                            }
                        }));

                return Task.FromResult(new CheckResult("cfg-streamer", status,
                    string.Join("；", problems),
                    "这几项都会让「连不上」看起来像玄学：配对请求被静默忽略、网络告警被屏蔽、从没成功过。",
                    ev, fixes,
                    "网络告警屏蔽只能在 Streamer 界面里改；配对开关下面这一条工具可以直接修（会先备份）。"));
            });

    // ---------------------------------------------------------------- Windows state

    public static ICheck IcsCheck() =>
        PsCheck.Create("ics", "网络共享 (ICS)", "ICS 有没有在跑？", "路由", PsIcs,
            CheckStatus.Warn,
            e => !HealthChecks.Lines(e).Any(l => l.Contains("Running")),
            e => "SharedAccess(ICS)："
                + (HealthChecks.Lines(e).Any(l => l.Contains("Running")) ? "Running" : "Stopped"),
            _ => "ICS 会把共享网卡 NAT 化，广播可能被过滤或改写。常见于手机热点/共享场景。",
            Array.Empty<FixAction>(),
            "只报告不修改：ICS 往往在承载别人的网络，停它会断掉热点。确认不再共享后可在「网络共享」里关掉。");

    public static ICheck OutboundPolicyCheck() =>
        PsCheck.Create("fw-outbound", "出站策略", "出站默认是放行还是拦截？", "防火墙", PsOutbound,
            CheckStatus.Warn,
            e => !HealthChecks.Lines(e).Any(l => l.Contains("Block")),
            e => "出站策略：" + string.Join(" / ",
                HealthChecks.Lines(e).Where(l => l.Contains("Allow") || l.Contains("Block") || l.Contains("NotConfigured"))),
            _ => "出站被默认 Block 或企业策略拦截时，PC 侧应答发不出去。",
            Array.Empty<FixAction>(),
            "企业策略只报告不修改：请让 IT 把 Virtual Desktop 加入出站例外。");

    public static ICheck ThirdPartyAvCheck() =>
        PsCheck.Create("av", "第三方安全软件", "有没有别的杀软接管防火墙？", "防火墙", PsAv,
            CheckStatus.Warn,
            e => HealthChecks.Lines(e).Count == 0
                 || HealthChecks.Lines(e).All(l => l.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase)),
            e =>
            {
                var lines = HealthChecks.Lines(e);
                // Reaching here with no lines means the query succeeded and nothing is registered.
                // The failure case (no SecurityCenter2 namespace) now writes to stderr and is turned
                // into Unknown before this judge runs, so zero lines no longer has to mean both.
                if (lines.Count == 0) return "已注册杀软：只有 Windows Defender（SecurityCenter2 读到了，只是没有别的）";
                var names = lines.Select(l => l.Split(" :: ")[0].Trim()).Where(s => s.Length > 0);
                return "已注册杀软：" + string.Join(" ;; ", names)
                     + "（productState 原值见下方原始输出，未解码：那是各版本含义不一的位掩码）";
            },
            _ => "官方 FAQ 点名：McAfee/Norton 需禁用或加例外；Avast/AVG 需把网络配置文件设为 Private。",
            Array.Empty<FixAction>(),
            "自动禁用杀软是绝对禁止的：工具只报名称，指引用户在该软件里放行 Virtual Desktop Streamer 与其服务。");

    public static ICheck RouteMetricCheck() =>
        CheckFactory.Delegate(
            new("route-metric", "出口网卡优先级", "物理网卡的路由优先级是不是最高的？", "路由"),
            async ct =>
            {
                var lines = await PowerShellRunner.LinesAsync(PsMetric, ct).ConfigureAwait(false);
                var rows = new List<(string Alias, int Metric)>();
                foreach (var line in lines)
                {
                    var m = Regex.Match(line, @"^(?<alias>\S.*?)\s{2,}(?<metric>\d+)\s");
                    if (m.Success && int.TryParse(m.Groups["metric"].Value, out var metric))
                        rows.Add((m.Groups["alias"].Value.Trim(), metric));
                }

                var ev = new Dictionary<string, string>
                {
                    ["排序（越小越优先）"] = rows.Count == 0
                        ? "-"
                        : string.Join(" | ", rows.Select(r => $"{r.Alias}={r.Metric}")),
                    ["原始输出"] = string.Join(" ;; ", lines),
                };
                if (rows.Count == 0)
                    return new CheckResult("route-metric", CheckStatus.Unknown, "读不到网卡优先级",
                        "Get-NetIPInterface 没有返回已连接的接口。", ev, Array.Empty<FixAction>(),
                        "以管理员身份重试。");

                var wired = rows.Where(r => !IsWirelessName(r.Alias) && !IsVirtualName(r.Alias)).ToList();
                if (wired.Count == 0)
                    return new CheckResult("route-metric", CheckStatus.Warn,
                        "已连接的接口里没有可识别的有线网卡", "路由选路不可判定。", ev,
                        Array.Empty<FixAction>(), "确认网线已插好并拿到地址。");

                var bestWired = wired.Min(r => r.Metric);
                var ahead = rows.Where(r => IsVirtualName(r.Alias) && r.Metric < bestWired).ToList();
                if (ahead.Count == 0)
                    return new CheckResult("route-metric", CheckStatus.Pass,
                        $"有线网卡优先级 {bestWired}，没有虚拟网卡排在它前面",
                        "定向广播会从物理网卡发出。", ev, Array.Empty<FixAction>());

                return new CheckResult("route-metric", CheckStatus.Warn,
                    $"{ahead.Count} 个虚拟网卡排在有线网卡（{bestWired}）前面：{string.Join("、", ahead.Select(v => v.Alias + "=" + v.Metric))}",
                    "广播从虚拟出口发出时，头显收不到。", ev, Array.Empty<FixAction>(),
                    "改 InterfaceMetric 会影响全局路由与 VPN，属高风险：工具只显示排序，由用户自己决定是否调整。");
            });

    public static ICheck WirelessOnlyCheck() =>
        CheckFactory.Delegate(
            new("link-type", "PC 接入方式", "PC 是走网线还是无线？", "物理链路"),
            ct =>
            {
                var real = HealthChecks.RealAdapters().Where(a => a.IsUp && a.PrimaryIPv4 is not null).ToList();
                var wired = real.Where(a => !a.IsVirtual && !IsWireless(a)).ToList();
                var wireless = real.Where(IsWireless).ToList();
                var ev = new Dictionary<string, string>
                {
                    ["有线"] = string.Join(" | ", wired.Select(a => $"{a.Name} ({a.PrimaryIPv4})")),
                    ["无线"] = string.Join(" | ", wireless.Select(a => $"{a.Name} ({a.PrimaryIPv4})")),
                    ["官方要求"] = "Wired computer to 5 GHz AC or AX Wi-Fi router",
                };
                if (wired.Count > 0)
                    return Task.FromResult(new CheckResult("link-type", CheckStatus.Pass,
                        $"PC 走有线（{wired[0].Name}）", "符合官方对电脑端的要求。", ev, Array.Empty<FixAction>()));

                return Task.FromResult(new CheckResult("link-type", CheckStatus.Warn,
                    wireless.Count > 0 ? "PC 只走无线" : "没有可用的链路",
                    "官方 Computer Requirements 要求电脑走网线接 5GHz 路由器；纯无线更容易掉帧与断链。",
                    ev, Array.Empty<FixAction>(),
                    "插网线即可；工具不会禁用无线网卡，因为你可能正用它连外网。"));
            });

    private static bool IsWireless(AdapterView a) =>
        a.Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
        || a.Name.Contains("Wireless", StringComparison.OrdinalIgnoreCase)
        || a.Description.Contains("802.11", StringComparison.OrdinalIgnoreCase)
        || a.Description.Contains("Wireless", StringComparison.OrdinalIgnoreCase);

    private static bool IsWirelessName(string alias) =>
        alias.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
        || alias.Contains("Wireless", StringComparison.OrdinalIgnoreCase);

    private static bool IsVirtualName(string alias) =>
        alias.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
        || alias.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
        || alias.Contains("WSL", StringComparison.OrdinalIgnoreCase)
        || alias.Contains("VPN", StringComparison.OrdinalIgnoreCase)
        || alias.Contains("TAP", StringComparison.OrdinalIgnoreCase)
        || alias.Contains("Tunnel", StringComparison.OrdinalIgnoreCase);
}