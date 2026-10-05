using System.Diagnostics;
using System.IO;
using VdHelper.Core.Checks;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// Four more checks taken straight from the coverage audit's priority list
/// (research/12-coverage-audit/02-next-additions.md). Each covers root causes the corpus recorded
/// from real threads, and each one the community hits on while the official FAQ says nothing.
/// </summary>
public static class MachineStateChecks
{
    /// <summary>
    /// P1 / D1 + G3. Vendor "network boost" and hardware-tuning resident tools are the answer the
    /// community gives when every conventional metric is green and streaming still stutters.
    /// The two families are kept apart because the user's action differs: one rewrites the network
    /// path, the other just burns CPU and contends for the main thread.
    /// </summary>
    private static readonly string[] NetworkBoosters =
    [
        "L-Config", "LEMSService", "LenVantage", "LegionZone", "LenovoVantageService",
        "LenovoUtility", "LZService", "LenovoNetwork", "NetworkBooster", "SmartNetwork",
        "killerNetworkService", "KillerNetworkService", "KillerEngine", "GameBoost",
    ];

    private static readonly string[] HardwareTuners =
    [
        "iCUE", "ArmouryCrate", "ArmouryCrateControlInterface", "ArmouryCrate.UserSessionHelper",
        "ArmouryCrate.Service", "Synapse", "WallpaperEngine", "RGB", "TT_Rift",
        "ArmourySwAgent", "MyASUS", "AsusOptimization",
    ];

    private static readonly string[] PnPNetworkBoosters =
    [
        "L-Config", "LEMSService", "LenVantage", "LegionZone", "LenovoVantageService",
    ];

    private const string PsProc =
        "Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | "
        + "ForEach-Object { \"$($_.Name)|$($_.ProcessId)|$($_.ExecutablePath)\" }";

    private const string PsNicPower =
        "Get-NetAdapterPowerManagement -ErrorAction SilentlyContinue | "
        + "ForEach-Object { \"$($_.Name)|WakeOnMagicPacket=$($_.WakeOnMagicPacket)|WakeOnPattern=$($_.WakeOnPattern)|"
        + "DeviceSleepOnDisconnect=$($_.DeviceSleepOnDisconnect)\" }";

    // Win32_DesktopMonitor returns placeholder "Default Monitor" rows with no resolution on modern
    // Windows — counting those as "monitors OK" is close to meaningless. PnP devices give the real
    // status, which is what the gpu-pick check already proved by decoding code 22.
    private const string PsDisplays =
        "Get-PnpDevice -Class Display -ErrorAction SilentlyContinue | "
        + "ForEach-Object { \"$($_.FriendlyName)|$($_.Status)\" }; "
        + "Write-Output \"---output---\"; "
        + "Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | "
        + "ForEach-Object { \"$($_.Name)|$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)\" }";

    public static IReadOnlyList<ICheck> Create() =>
        [VendorToolCheck(), NicPowerSaveCheck(), DisplayInventoryCheck(), StreamerVersionCheck()];

    public static ICheck VendorToolCheck() =>
        CheckFactory.Delegate(
            new("proc-tuner", "厂商调校工具", "有没有会改网络路径或抢 CPU 的常驻工具？", "进程"),
            async ct =>
            {
                var lines = await PowerShellRunner.LinesAsync(PsProc, ct).ConfigureAwait(false);
                var (boosters, tuners) = Match(lines, NetworkBoosters, HardwareTuners);
                var ev = new Dictionary<string, string>
                {
                    ["进程总数"] = lines.Count.ToString(),
                    ["网络加速类命中"] = Describe(boosters),
                    ["硬件调校类命中"] = Describe(tuners),
                    ["说明"] = "这两类是社区里「常规指标全绿但串流仍然卡」时最常被指出的真凶，官方 FAQ 对此零字提及。",
                };

                if (boosters.Count == 0 && tuners.Count == 0)
                    return new CheckResult("proc-tuner", CheckStatus.Pass,
                        "没有发现厂商网络加速或硬件调校常驻工具",
                        "这一项干净。", ev, Array.Empty<FixAction>());

                var parts = new List<string>();
                if (boosters.Count > 0)
                    parts.Add($"网络加速类命中 {boosters.Count} 个（会改网络路径）");
                if (tuners.Count > 0)
                    parts.Add($"硬件调校类命中 {tuners.Count} 个（占 CPU、抢主线程）");
                return new CheckResult("proc-tuner", CheckStatus.Warn, string.Join("；", parts),
                    "这不是它们一定有问题，而是**排查这类症状时应该第一个排除的对象**："
                    + "先临时停用再测一次，如果症状消失就是它。这是诊断手段，不是永久建议。",
                    ev, Array.Empty<FixAction>(),
                    "工具不自动停这些服务——停 Armoury Crate 之类会连带关掉风扇控制，属于破坏性操作。"
                    + "请你自己在「设置 → 应用」里临时退出对应软件再重测。");
            });

    public static ICheck NicPowerSaveCheck() =>
        CheckFactory.Delegate(
            new("nic-powersave", "网卡节能", "网卡会不会在空闲时睡着？", "物理链路"),
            async ct =>
            {
                var lines = await PowerShellRunner.LinesAsync(PsNicPower, ct).ConfigureAwait(false);
                var ev = new Dictionary<string, string>
                {
                    ["电源管理属性"] = string.Join(" ;; ", lines),
                    ["说明"] = "「一段时间后突然 PC 不可达」这一类症状，先看这里。",
                };
                if (lines.Count == 0)
                    return new CheckResult("nic-powersave", CheckStatus.Unknown, "读不到网卡电源管理属性",
                        "部分驱动不通过 WMI 暴露这一组属性。", ev, Array.Empty<FixAction>(),
                        "可以到设备管理器 → 网卡 → 电源管理，看「允许计算机关闭此设备以节约电源」是否勾着。");

                var risky = lines.Where(l =>
                    l.Contains("WakeOnMagicPacket=False", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("WakeOnPattern=False", StringComparison.OrdinalIgnoreCase)).ToList();
                ev["需要关注的网卡"] = risky.Count == 0 ? "(没有关闭唤醒的)" : string.Join(" ;; ", risky);

                if (risky.Count == 0)
                    return new CheckResult("nic-powersave", CheckStatus.Pass,
                        "没有网卡关闭了网络唤醒", "休眠唤醒后网卡正常回来。", ev, Array.Empty<FixAction>());

                return new CheckResult("nic-powersave", CheckStatus.Warn,
                    $"{risky.Count} 块网卡关闭了唤醒能力",
                    "休眠后网卡不唤醒，表现是「睡一觉起来头显就连不上，得先碰一下电脑」。",
                    ev, Array.Empty<FixAction>(),
                    "设备管理器 → 网卡 → 属性 → 电源管理，勾上「允许此设备唤醒计算机」与「只允许幻数据包唤醒计算机」。"
                    + "工具不自动改：改电源设置会影响整机续航与发热。");
            });

    public static ICheck DisplayInventoryCheck() =>
        CheckFactory.Delegate(
            new("display-inventory", "显示器枚举", "PC 上有几块屏、状态正常吗？", "显示"),
            async ct =>
            {
                var lines = await PowerShellRunner.LinesAsync(PsDisplays, ct).ConfigureAwait(false);
                var devices = lines.Where(l => l.Contains('|') && !l.StartsWith("---", StringComparison.Ordinal))
                    .Where(l => !l.Split('|')[1].Contains("x", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var outputs = lines.Where(l => l.Contains('|') && l.Split('|')[1].Contains('x')).ToList();
                var ev = new Dictionary<string, string>
                {
                    ["显示设备 (PnP)"] = devices.Count == 0 ? "(没读到)" : string.Join(" ;; ", devices),
                    ["当前输出分辨率"] = outputs.Count == 0 ? "(没读到：可能没有接物理显示器)" : string.Join(" ;; ", outputs),
                };
                if (devices.Count == 0)
                    return new CheckResult("display-inventory", CheckStatus.Unknown, "读不到显示设备",
                        "Get-PnpDevice 没返回显示类设备。", ev, Array.Empty<FixAction>(),
                        "开设备管理器看一眼「显示适配器」里有什么，工具读不到时以那里为准。");

                var bad = devices.Where(d => !d.Split('|')[1].Equals("OK", StringComparison.OrdinalIgnoreCase)).ToList();
                if (bad.Count == 0)
                    return new CheckResult("display-inventory", CheckStatus.Pass,
                        $"{devices.Count} 个显示设备全部正常"
                        + (outputs.Count > 0 ? "，输出分辨率 " + outputs[0].Split('|')[1] : "（未读到输出分辨率）"),
                        "画面输出这条链路没发现异常。", ev, Array.Empty<FixAction>());

                return new CheckResult("display-inventory", CheckStatus.Warn,
                    string.Join("、", bad.Select(b => b.Split('|')[0] + "(" + b.Split('|')[1] + ")")),
                    "串流「连上但没画面」有一类原因就是输出设备里有异常项占着位置。"
                    + "具体错误码看 gpu-pick 那项。",
                    ev, Array.Empty<FixAction>(),
                    "设备管理器 → 显示适配器：不需要的虚拟显示器可以禁用；黄色叹号要重装驱动。");
            });

    /// <summary>
    /// P6. The Streamer version is half of the "both ends must be the same version" advice that the
    /// developer gives repeatedly in the corpus; the Quest-side half needs a headset, this half does not.
    /// </summary>
    public static ICheck StreamerVersionCheck() =>
        CheckFactory.Delegate(
            new("cfg-version", "Streamer 版本", "PC 端装的是哪个版本？", "配置"),
            ct =>
            {
                var exe = StreamerChecks.StreamerExe;
                var ev = new Dictionary<string, string> { ["可执行文件"] = exe };
                if (!File.Exists(exe))
                    return Task.FromResult(new CheckResult("cfg-version", CheckStatus.Unknown,
                        "没找到 Virtual Desktop Streamer.exe",
                        "PC 端没装 Streamer（或装在别的路径）。", ev, Array.Empty<FixAction>(),
                        "patched 基线也需要 PC 端 Streamer 才能串流。"));

                var fvi = FileVersionInfo.GetVersionInfo(exe);
                ev["FileVersion"] = fvi.FileVersion ?? "-";
                ev["ProductVersion"] = fvi.ProductVersion ?? "-";
                ev["ProductName"] = fvi.ProductName ?? "-";
                ev["文件时间"] = File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm");

                return Task.FromResult(new CheckResult("cfg-version", CheckStatus.Pass,
                    $"Streamer 版本 {fvi.FileVersion}",
                    "把它和头显里 VD 应用的版本比一比：开发者反复强调两端要同版本，"
                    + "商店版与 beta 混装是最常见的一种。", ev, Array.Empty<FixAction>()));
            });

    private static (List<string> A, List<string> B) Match(IReadOnlyList<string> lines, string[] a, string[] b)
    {
        var first = new List<string>();
        var second = new List<string>();
        foreach (var line in lines)
        {
            var name = line.Split('|')[0];
            if (a.Any(x => name.StartsWith(x, StringComparison.OrdinalIgnoreCase))) first.Add(line);
            else if (b.Any(x => name.StartsWith(x, StringComparison.OrdinalIgnoreCase))) second.Add(line);
        }
        return (first, second);
    }

    private static string Describe(IReadOnlyList<string> hits) =>
        hits.Count == 0 ? "(无)" : string.Join(" ;; ", hits.Take(6));
}