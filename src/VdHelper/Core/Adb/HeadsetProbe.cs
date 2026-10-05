using VdHelper.Core.Model;

namespace VdHelper.Core.Adb;

/// <summary>
/// Headset-side probe. Facts used here are sourced from
/// <c>research/06-adb-headset/01-adb-playbook.md</c> and
/// <c>research/03-quest-parameters/03-il-constants.md</c>:
/// the shipped package is <c>VirtualDesktop.Android</c> (not com.vrdesktop.streamer), and the
/// seven runtime permissions are HorizonOS scene/face/eye pairs plus notifications.
/// </summary>
public sealed class HeadsetProbe(AdbClient adb)
{
    public static readonly string[] PackageNames =
    [
        "VirtualDesktop.Android",
        "com.dwgx1.vd.recovered",
        "com.dwgx1.virtualdesktop.recovered",
        "com.vrdesktop.streamer",
    ];

    /// <summary>Runtime permissions whose absence has a known symptom.</summary>
    public static readonly (string Permission, string Symptom)[] RuntimePermissions =
    [
        ("com.oculus.horizonos.permission.USE_SCENE", "场景应用未授权，VR 焦点会被系统收回"),
        ("com.oculus.horizonos.permission.USE_SCENE", "同上（第二个授权位）"),
        ("com.oculus.horizonos.permission.FACE_TRACKING", "面部追踪缺失，VR 里部分渲染分支被关闭"),
        ("com.oculus.horizonos.permission.FACE_TRACKING", "同上（第二个授权位）"),
        ("com.oculus.horizonos.permission.EYE_TRACKING", "眼动缺失，应用会自行关闭注视点串流"),
        ("com.oculus.horizonos.permission.EYE_TRACKING", "同上（第二个授权位）"),
        ("android.permission.POST_NOTIFICATIONS", "无通知权限，后台保活受限"),
    ];

    public async Task<CheckResult> RunAsync(CancellationToken ct = default)
    {
        var ev = new Dictionary<string, string> { ["adb"] = adb.AdbPath };

        var devices = await adb.RunAsync(["devices", "-l"], 8000, ct);
        ev["adb devices"] = string.Join(" ;; ", devices.Lines);
        var serials = devices.Lines
            .Skip(1)
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        if (devices.TimedOut)
            return new CheckResult("adb", CheckStatus.Unknown, "adb 无响应", "查找 adb 超时。", ev,
                Array.Empty<FixAction>(), "adb 可能被占用；关掉其它工具再试。");

        if (serials.Count == 0)
            return new CheckResult("adb", CheckStatus.Warn, "没有连着的头显",
                "用 USB 连接头显并在头显里点「允许 USB 调试」，或先在 PC 上开无线调试。",
                ev, Array.Empty<FixAction>(),
                "无线方式：`adb tcpip 5555` 后 `adb connect <头显IP>:5555`；头显 IP 可在「设置 → Wi-Fi → 连接详情」看到。");

        var serial = serials[0]!;
        ev["serial"] = serial;

        var probes = new (string Id, string Label, string[] Args)[]
        {
            ("props", "系统版本", ["shell", "getprop", "ro.build.version.release"]),
            ("model", "型号", ["shell", "getprop", "ro.product.model"]),
            ("wlan", "Wi-Fi 接口", ["shell", "ip", "-4", "addr", "show", "wlan0"]),
            ("proxy", "全局代理", ["shell", "settings", "get", "global", "http_proxy"]),
            ("packages", "已装包", ["shell", "pm", "list", "packages"]),
        };

        foreach (var (id, label, args) in probes)
        {
            var r = await adb.RunAsync(["-s", serial, .. args], 8000, ct);
            ev[label] = r.Ok
                ? string.Join(" ;; ", r.Lines)
                : "读取失败: " + (r.StdErr.Trim() is { Length: > 0 } e ? e : r.StdOut.Trim());
        }

        var packages = ev.TryGetValue("已装包", out var pkgs) ? pkgs : "";
        var installed = PackageNames.Where(p => packages.Contains(p, StringComparison.OrdinalIgnoreCase)).ToList();

        var granted = await ReadPermissionsAsync(serial, ct);
        foreach (var (perm, Granted) in granted)
            ev["权限 " + perm] = Granted ? "granted" : "未授予";

        var missing = granted.Where(g => !g.Granted).Select(g => g.Permission).Distinct().ToList();
        var running = new List<string>();
        if (installed.Count > 0)
            foreach (var pkg in installed)
            {
                var pid = await adb.RunAsync(["-s", serial, "shell", "pidof", pkg], 8000, ct);
                var pidText = pid.Ok ? pid.StdOut.Trim() : "";
                if (pidText.Length > 0)
                    running.Add($"{pkg} (pid {pidText})");
                else
                    ev["进程 " + pkg] = "未在运行";
            }
        ev["VD 进程存活数"] = $"{running.Count} / {installed.Count}";
        if (running.Count > 0)
            ev["存活的进程"] = string.Join(" ;; ", running);

        if (installed.Count == 0)
            return new CheckResult("adb", CheckStatus.Warn, "头显已连接，但没找到 Virtual Desktop 客户端",
                $"查了 {PackageNames.Length} 个可能包名都没有。", ev, Array.Empty<FixAction>(),
                "头显里打开 Virtual Desktop 应用确认装的是哪个包；补丁基线的包名与官方不同。");

        // Liveness outranks permissions in the headline. "The app is not running" and "the app is
        // running but the list is empty" point at completely different fixes, and from the PC side
        // both look like "no computer found". Say which one this is.
        var notRunning = installed.Count > 0 && running.Count == 0;
        if (notRunning)
            return new CheckResult("adb", CheckStatus.Block,
                "客户端装了，但没有进程在运行",
                "**这是「列表空」和「网络不通」的分水岭。**客户端在取不到账号身份时会自己杀掉进程"
                + "（NetworkManager.cs:184-186 与 :212-214 两条 Kill 路径）；补丁把那处 Kill NOP 成了 ret"
                + "（binary_patch.py:226-254），于是补丁基线不闪退，只是安静地列出零台电脑。"
                + "进程不在 = 先查应用本身，别去动路由器。",
                ev, Array.Empty<FixAction>(),
                "先在头显里手动打开 Virtual Desktop 看着它启动：秒退说明客户端自己退了，"
                + "那是补丁/账号层面的问题，不在 VDHelper 的网络检测范围内。");

        var status = missing.Count > 0 ? CheckStatus.Block : CheckStatus.Pass;
        var summary = missing.Count > 0
            ? $"{installed.Count} 个客户端包在运行，缺 {missing.Count} 项运行时权限"
            : $"{installed.Count} 个客户端包在运行，{granted.Count} 项权限齐全";
        return new CheckResult("adb", status, summary,
            "权限缺失的表现：30 秒后 VR 焦点被系统收回、注视点串流被自动关闭、面部追踪分支不执行。",
            ev,
            missing.Count > 0 ? HeadsetFixes.GrantPermissions(missing) : Array.Empty<FixAction>(),
            missing.Count > 0 ? null : "进程在跑、权限齐全，若头显里仍列不出这台 PC，才轮到看 PC 侧（第一屏）。");
    }

    private async Task<List<(string Permission, bool Granted)>> ReadPermissionsAsync(
        string serial, CancellationToken ct)
    {
        var r = await adb.RunAsync(["-s", serial, "shell", "pm", "list", "permissions"], 8000, ct);
        var grantedLines = string.Join('\n', r.Lines);
        var results = new List<(string, bool)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (perm, _) in RuntimePermissions)
        {
            if (seen.Add(perm))
                results.Add((perm, grantedLines.Contains(perm, StringComparison.Ordinal)));
        }
        return results;
    }
}

public static class HeadsetFixes
{
    public static IReadOnlyList<FixAction> GrantPermissions(IEnumerable<string> permissions) =>
    [
        new FixAction(
            "headset-grant",
            "授予缺失的运行时权限",
            "adb -s <serial> shell pm grant <package> <permission>",
            "仅授予权限，不改其它设置；可用 pm revoke <package> <permission> 逐项撤销。",
            "adb -s <serial> shell pm revoke <package> <permission>",
            FixRisk.Low,
            ct => Task.FromResult(new FixResult(true,
                "用 pm grant 逐项授予：" + string.Join("、", permissions)))),
    ];
}