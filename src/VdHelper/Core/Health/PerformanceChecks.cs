﻿using System.Text.RegularExpressions;
using VdHelper.Core.Checks;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// Two failure modes the coverage audit (research/12-coverage-audit) found unhandled, both live on
/// this machine at the time of writing:
/// <list type="bullet">
/// <item>B3 — nothing read <c>DefaultInboundAction</c>. The existing check only looks at
/// <c>Enabled</c>, and <c>NotConfigured</c> means Block.</item>
/// <item>E5 — the GPU Windows picked for the VR path. SteamVR's dashboard was on the integrated
/// GPU here (<c>GpuPreference=0</c>), which is exactly the kind of thing that shows up as a black
/// or stuttering stream.</item>
/// </list>
/// </summary>
public static class PerformanceChecks
{
    private const string PsFirewallProfile =
        "Get-NetFirewallProfile | Select-Object Name,Enabled,DefaultInboundAction | Format-Table -AutoSize | Out-String -Width 120";

    private const string PsGpu =
        "Get-ItemProperty 'HKCU:\\SOFTWARE\\Microsoft\\DirectX\\UserGpuPreferences' -ErrorAction SilentlyContinue | " +
        "Select-Object * -ExcludeProperty PS* | Format-List | Out-String -Width 220";

    // Delimited output, not a table: matching "Error" against a table also matches the header
    // cell "ConfigManagerErrorCode", which produced a phantom faulty adapter on this machine.
    private const string PsVideo =
        "Get-CimInstance Win32_VideoController | ForEach-Object { "
        + "\"$($_.Name)|$($_.Status)|$($_.ConfigManagerErrorCode)|$($_.DriverVersion)\" }";

    public static IReadOnlyList<ICheck> Create() => [FirewallProfileInboundCheck(), GpuPreferenceCheck()];

    /// <summary>
    /// B3. Community repeatedly "solves" this by turning the whole firewall off
    /// (research/09-failure-corpus §3.3 lists that as one of the four wrong answers). The honest
    /// check is the one nobody runs: what does the profile's default inbound action actually say.
    /// </summary>
    public static ICheck FirewallProfileInboundCheck() =>
        PsCheck.Create("fw-profile-inbound", "防火墙默认入站动作", "profile 级的默认入站是放行还是丢弃？", "防火墙",
            PsFirewallProfile, CheckStatus.Warn,
            e => !Lines(e).Any(l => l.Contains("NotConfigured") || l.Contains("Block")),
            e => "profile 默认入站：" + Describe(e),
            _ => "NotConfigured 在 Windows 语义下等于 Block：任何没有单独放行规则的入站连接都会被默认丢弃。"
                + "注意这不等于「VD 被拦了」——VD 有自己的放行规则；它影响的是那些依赖临时放行的路径。",
            Array.Empty<FixAction>(),
            "要改的话自己敲：`Set-NetFirewallProfile -Profile Private -DefaultInboundAction Allow`，"
            + "回滚就是同一命令把 Allow 换成 Block。工具不自动执行——它影响这台机器所有入站连接。"
            + "自查旁证：事件查看器里查不到任何拦截记录，往往正是这一栏的问题。");

    /// <summary>E5/E6. Which GPU Windows picked for the VR path, and whether the drivers are healthy.</summary>
    public static ICheck GpuPreferenceCheck() =>
        CheckFactory.Delegate(
            new("gpu-pick", "GPU 选择", "VR 跑在核显还是独显？", "性能"),
            async ct =>
            {
                var prefs = await PowerShellRunner.LinesAsync(PsGpu, ct).ConfigureAwait(false);
                var video = await PowerShellRunner.LinesAsync(PsVideo, ct).ConfigureAwait(false);
                var ev = new Dictionary<string, string>
                {
                    ["UserGpuPreferences"] = string.Join(" ;; ", prefs),
                    ["显示适配器"] = string.Join(" ;; ", video),
                };

                // Windows' GpuPreference: 0 = let Windows decide, 1 = power saving (iGPU),
                // 2 = high performance (dGPU). SteamVR's own processes are what VD rides on.
                var vrLines = prefs.Where(l =>
                    l.Contains("vrdashboard", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("vrserver", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("VirtualDesktop", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("steamvr", StringComparison.OrdinalIgnoreCase)).ToList();
                ev["VR 相关条目"] = vrLines.Count == 0 ? "(没有为 VR 程序设过偏好，由 Windows 自己决定)" : string.Join(" ;; ", vrLines);

                var onIgpu = vrLines.Any(l => l.Contains("GpuPreference=1", StringComparison.OrdinalIgnoreCase));
                var adapters = ParseAdapters(video);
                ev["显示适配器"] = string.Join(" ;; ",
                    adapters.Select(a => $"{a.Name}({a.Status}{(a.Status == "Error" ? " code=" + a.ErrorCode : "")})"));

                var unhealthy = adapters.Where(a => a.Status != "OK").ToList();

                if (unhealthy.Count > 0)
                    return new CheckResult("gpu-pick", CheckStatus.Warn,
                        string.Join("、", unhealthy.Select(a => a.Name)),
                        "Windows 把 ConfigManagerErrorCode 直接给了出来："
                        + $"{DescribeErrorCodes(unhealthy)}。虚拟显示器驱动被禁用或报错时，串流会黑屏、只有声音、或者「连上但没画面」。",
                        ev, Array.Empty<FixAction>(),
                        "设备管理器 → 显示适配器 → 找到这些设备：被禁用就右键启用，黄色叹号就重装驱动。"
                        + "工具不自动改驱动——改错了会连画面都出不来。");

                if (onIgpu)
                    return new CheckResult("gpu-pick", CheckStatus.Warn,
                        "有 VR 程序被指定用核显（GpuPreference=1）",
                        "核显跑 VR 合成必然掉帧、画面撕裂或延迟变差。这是社区里「换了显卡还是卡」一类问题的常见真凶。",
                        ev, Array.Empty<FixAction>(),
                        "在「设置 → 系统 → 显示 → 图形」里把对应程序改成「高性能」（独显）。"
                        + "工具不自动改——这会影响整机图形性能，应当由你决定。");

                // Zero adapters is not "zero unhealthy adapters". The whole point of this check is
                // spotting a display driver in Error, and a query that returns nothing — cmdlet
                // missing, WMI unavailable, access denied — used to fall straight through to Pass
                // and certify the displays it never saw.
                if (adapters.Count == 0)
                    return new CheckResult("gpu-pick", CheckStatus.Unknown, "读不到显示适配器",
                        "查询没有返回任何一块显示适配器。**没读到设备不等于设备没问题**——"
                        + "这一项既没有发现异常，也没有真的看过任何一块显卡。",
                        ev, Array.Empty<FixAction>(),
                        "以管理员身份重试；仍然读不到就开「设备管理器 → 显示适配器」，那里是准的。");

                return new CheckResult("gpu-pick", CheckStatus.Pass,
                    $"没有 VR 程序被指定到核显（{adapters.Count} 块显示适配器均为正常状态）",
                    vrLines.Count == 0
                        ? "没人为 VR 程序设过 GPU 偏好，由 Windows 按负载自动选；这台机有独显，一般会选独显。"
                        : "已为 VR 程序设置过高性能偏好。",
                    ev, Array.Empty<FixAction>());
            });

    private sealed record AdapterRow(string Name, string Status, string ErrorCode, string DriverVersion);

    private static IReadOnlyList<AdapterRow> ParseAdapters(IReadOnlyList<string> lines)
    {
        var rows = new List<AdapterRow>();
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 4) continue;
            rows.Add(new AdapterRow(parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), parts[3].Trim()));
        }
        return rows;
    }

    private static IReadOnlyList<string> Lines(IReadOnlyDictionary<string, string> e) =>
        e.TryGetValue("_lines", out var raw) && raw.Length > 0
            ? raw.Split(" ;; ", StringSplitOptions.None)
            : Array.Empty<string>();

    /// <summary>
    /// Turns ConfigManagerErrorCode into words. 22 in particular is ERROR_DISABLED, which means
    /// somebody (or an updater) turned the device off — very different from a missing driver, and
    /// the fix is completely different too.
    /// </summary>
    private static string DescribeErrorCodes(IReadOnlyList<AdapterRow> adapters) =>
        adapters.Count == 0
            ? "(未读到错误码)"
            : string.Join("；", adapters.Select(a => $"{a.Name}={a.ErrorCode} {DescribeCode(a.ErrorCode)}"));

    private static string DescribeCode(string code) => code switch
    {
        "22" => "= ERROR_DISABLED，设备被禁用（右键启用即可）",
        "28" or "1168" => "= 驱动未安装/不匹配",
        "31" => "= 设备不可用",
        "43" => "= 系统报告有问题",
        "0" => "= 正常",
        _ => "(见微软 ConfigManagerErrorCode 表)",
    };

    private static string Describe(IReadOnlyDictionary<string, string> e) =>
        string.Join(" / ", Lines(e)
            .Where(l => l.Contains("True", StringComparison.OrdinalIgnoreCase)
                        || l.Contains("False", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Trim()));
}