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

    /// <summary>
    /// Runtime permissions whose absence has a known symptom.
    /// <para>
    /// <c>com.oculus.*</c> and <c>horizonos.*</c> come in pairs and both are needed: older Quest OS
    /// only recognises the first, newer systems only the second — install.bat and
    /// install_template.bat:61-68 list them that way. The previous table here had seven rows but only
    /// four distinct names, three of them duplicated and labelled "同上（第二个授权位）", and each
    /// name was a malformed paste of the two prefixes into <c>com.oculus.horizonos.permission.*</c> —
    /// a string that exists on no device. The seven below are the real grant strings.
    /// </para>
    /// </summary>
    public static readonly (string Permission, string Symptom)[] RuntimePermissions =
    [
        ("com.oculus.permission.USE_SCENE", "场景应用未授权，VR 焦点会被系统收回（旧系统认这一条）"),
        ("horizonos.permission.USE_SCENE", "场景应用未授权，VR 焦点会被系统收回（新系统认这一条）"),
        ("com.oculus.permission.FACE_TRACKING", "面部追踪缺失，VR 里部分渲染分支被关闭（旧系统）"),
        ("horizonos.permission.FACE_TRACKING", "面部追踪缺失，VR 里部分渲染分支被关闭（新系统）"),
        ("com.oculus.permission.EYE_TRACKING", "眼动缺失，应用会自行关闭注视点串流（旧系统）"),
        ("horizonos.permission.EYE_TRACKING", "眼动缺失，应用会自行关闭注视点串流（新系统）"),
        ("android.permission.POST_NOTIFICATIONS", "无通知权限，后台保活受限"),
    ];

    public async Task<CheckResult> RunAsync(string? serial = null, CancellationToken ct = default)
    {
        var ev = new Dictionary<string, string> { ["adb"] = adb.AdbPath };

        var devices = await adb.RunAsync(["devices", "-l"], 8000, ct);
        ev["adb devices"] = string.Join(" ;; ", devices.Lines);
        // adb separates serial and state with a TAB, not a space. Splitting on ' ' produced
        // "2G0YC5ZHBD01XF<TAB>device" as the serial, adb rejected every -s call with
        // "device ... not found", pidof then failed, and this check reported Block — "the client is
        // installed but no process is running" — pointing the user at the client's Kill paths for a
        // reason that had nothing to do with them. HeadsetDeepProbe.ParseDevices already split on
        // any whitespace; two parsers for one input, one right and one wrong.
        var serials = devices.Lines
            .Skip(1)
            .Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        if (devices.TimedOut)
            return new CheckResult("adb", CheckStatus.Unknown, "adb 无响应", "查找 adb 超时。", ev,
                Array.Empty<FixAction>(), "adb 可能被占用；关掉其它工具再试。");

        if (serials.Count == 0)
        {
            var configured = ConfigFile.Read<string>(Health.ReachabilityCheck.IpKey);
            var reachable = false;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                try
                {
                    using var ping = new System.Net.NetworkInformation.Ping();
                    reachable = (await ping.SendPingAsync(configured, 800).ConfigureAwait(false)).Status
                        == System.Net.NetworkInformation.IPStatus.Success;
                }
                catch (System.Net.NetworkInformation.PingException) { reachable = false; }
            }
            ev["头显是否在网络上可达"] = string.IsNullOrWhiteSpace(configured)
                ? "(没填 headsetIp，无法判断)"
                : reachable ? $"是：{configured} ping 通" : $"否：{configured} ping 不通";
            return new CheckResult("adb",
                reachable ? CheckStatus.Unknown : CheckStatus.Warn,
                reachable ? "头显在网络上，但 adb 连不上它" : "没有连着的头显",
                reachable
                    ? $"**网络这一段是通的**——{configured} ping 得到应答。所以这不是网络问题，是 adb 这一段没建立。"
                    : "`adb devices -l` 的设备列表是空的。用 USB 连接头显并在头显里点「允许 USB 调试」，"
                      + "或先在 PC 上开无线调试。",
                ev, Array.Empty<FixAction>(),
                "无线方式：头显里 开发者选项 → 打开无线调试，PC 上 `adb pair <头显IP>:配对端口> <配对码>`，"
                + "再 `adb connect <头显IP>:5555`；头显 IP 可在「设置 → Wi-Fi → 连接详情」看到。");
        }

        // With two headsets — or a headset and a phone — the first serial is whichever adb happened
        // to list first, and the whole report would then be about a device the user did not mean.
        // Say so and ask, rather than measuring the wrong thing confidently.
        if (serial is { Length: > 0 } wanted && !serials.Contains(wanted))
            return new CheckResult("adb", CheckStatus.Unknown,
                $"--serial 指定的 {wanted} 不在设备列表里",
                "现在连着的是：" + string.Join("、", serials), ev, Array.Empty<FixAction>(),
                "序列号就是 `adb devices -l` 第一列那一串。");
        if (serials.Count > 1 && string.IsNullOrWhiteSpace(serial))
            return new CheckResult("adb", CheckStatus.Unknown,
                $"同时连着 {serials.Count} 台设备，不知道该看哪一台",
                "一次只接一台，或者用 --serial <序列号> 指定：\n" + string.Join("\n", serials.Select(s => "  " + s)),
                ev, Array.Empty<FixAction>(),
                "序列号就是 `adb devices -l` 第一列那一串。");
        serial = serials.FirstOrDefault(s => s.Equals(serial, StringComparison.OrdinalIgnoreCase))
            ?? serials[0];
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

        var granted = await ReadPermissionsAsync(serial, installed, ct);
        foreach (var (perm, Granted) in granted)
            ev["权限 " + perm] = Granted ? "granted" : "未授予";
        foreach (var perm in Absent)
            ev["权限 " + perm] = "该系统不认识这条（非故障）";

        var missing = granted.Where(g => !g.Granted).Select(g => g.Permission).Distinct().ToList();
        var running = new List<string>();
        if (installed.Count > 0)
            foreach (var pkg in installed)
            {
                var pid = await adb.RunAsync(["-s", serial, "shell", "pidof", pkg], 8000, ct);
                // pidof prints pids space-separated; anything else on stdout (a shell error, a
                // warning) is not a pid, and treating it as one would report a process as alive.
                var pids = (pid.Ok ? pid.StdOut : "")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(x => int.TryParse(x, out _))
                    .ToList();
                if (pids.Count > 0)
                    running.Add($"{pkg} (pid {string.Join(",", pids)})");
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
            missing.Count > 0 ? HeadsetFixes.GrantPermissions(missing, serial, installed) : Array.Empty<FixAction>(),
            missing.Count > 0 ? null : "进程在跑、权限齐全，若头显里仍列不出这台 PC，才轮到看 PC 侧（第一屏）。");
    }

    /// <summary>
    /// Per-permission granted state for the packages we found installed.
    /// <para>
    /// Read from <c>dumpsys package</c>, not from <c>pm list permissions</c>. The latter lists what
    /// packages <i>declare</i>, not what the app was <i>granted</i>, so for ordinary permissions it
    /// answers "yes" almost unconditionally — the check would have reported every permission granted
    /// on a device where none were.
    /// </para>
    /// <para>
    /// A permission that does not appear in the dump at all is reported as "该系统不认识这条" rather
    /// than as missing, because on a given Quest OS only one of the com.oculus.* / horizonos.* pair
    /// exists and demanding the other would invent a fault.
    /// </para>
    /// </summary>
    private async Task<List<(string Permission, bool Granted)>> ReadPermissionsAsync(
        string serial, IReadOnlyList<string> packages, CancellationToken ct)
    {
        var grantedSet = new HashSet<string>(StringComparer.Ordinal);
        var knownSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pkg in packages)
        {
            var r = await adb.RunAsync(["-s", serial, "shell", "dumpsys", "package", pkg], 15000, ct);
            if (!r.Ok) continue;
            var text = string.Join('\n', r.Lines);
            foreach (var line in r.Lines)
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    line.Trim(),
                    @"^(?<name>[A-Za-z0-9_.]+):\s*granted=(?<g>true|false)");
                if (!m.Success) continue;
                knownSet.Add(m.Groups["name"].Value);
                if (m.Groups["g"].Value == "true") grantedSet.Add(m.Groups["name"].Value);
            }
        }

        var results = new List<(string, bool)>();
        foreach (var (perm, _) in RuntimePermissions)
        {
            if (!knownSet.Contains(perm))
            {
                // Not this system's string at all — neither granted nor a fault.
                Absent.Add(perm);
                continue;
            }
            results.Add((perm, grantedSet.Contains(perm)));
        }
        return results;
    }

    /// <summary>Permissions this Quest OS does not have at all — neither granted nor a fault.</summary>
    private readonly HashSet<string> Absent = new(StringComparer.Ordinal);
}

public static class HeadsetFixes
{
    /// <summary>
    /// Grants the permissions, for real, and reports what actually happened.
    /// <para>
    /// The previous version returned <c>Task.FromResult(new FixResult(true, ...))</c> without running
    /// anything: <c>--apply headset-grant</c> printed 成功 and changed nothing on the device. On a
    /// tool whose entire value is that its verdicts can be trusted, that manufactures the exact false
    /// all-clear this project keeps removing elsewhere.
    /// </para>
    /// </summary>
    public static IReadOnlyList<FixAction> GrantPermissions(
        IEnumerable<string> permissions, string serial, IReadOnlyList<string> packages)
    {
        var perms = permissions.Distinct().ToList();
        var pkgs = packages.Where(p => !string.IsNullOrEmpty(p)).ToList();
        return
        [
            new FixAction(
                "headset-grant",
                "授予缺失的运行时权限（真执行，逐项回报结果）",
                string.Join(" ;; ", pkgs.SelectMany(p => perms
                    .Select(x => "adb -s " + serial + " shell pm grant " + p + " " + x))),
                "仅授予权限，不改其它设置；回滚用 pm revoke 逐项撤销：" + string.Join(" ;; ", perms),
                string.Join(" ;; ", pkgs.SelectMany(p => perms
                    .Select(x => "adb -s " + serial + " shell pm revoke " + p + " " + x))),
                FixRisk.Low,
                async ct =>
                {
                    var adbPath = AdbLocator.Find();
                    if (adbPath is null) return new FixResult(false, "找不到 adb.exe，没有真的执行任何命令。");
                    var adb = new AdbClient(adbPath);
                    var done = new List<string>();
                    var failed = new List<string>();
                    foreach (var pkg in pkgs)
                    {
                        foreach (var perm in perms)
                        {
                            var r = await adb.RunAsync(
                                ["-s", serial, "shell", "pm", "grant", pkg, perm], 10000, ct)
                                .ConfigureAwait(false);
                            if (r.Ok) done.Add(pkg + " " + perm);
                            else failed.Add(pkg + " " + perm + " -> "
                                + (r.StdErr.Trim() is { Length: > 0 } se ? se : r.StdOut.Trim()));
                        }
                    }
                    return failed.Count == 0 && done.Count > 0
                        ? new FixResult(true, "已授予 " + done.Count + " 项", string.Join("\n", done))
                        : new FixResult(false,
                            "成功 " + done.Count + " 项，失败 " + failed.Count + " 项：\n"
                            + string.Join("\n", failed.Take(6))
                            + "\n注意：com.oculus.* 与 horizonos.* 在同一个系统上通常只有一个存在，"
                            + "另一条会报「未知权限」——那是正常的，不是故障。",
                            string.Join("\n", done));
                }),
        ];
    }
}