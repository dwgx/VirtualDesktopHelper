﻿﻿using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using VdHelper.Core.Model;

namespace VdHelper.Core.Adb;

/// <summary>
/// Deep headset-side probe: three root causes that the PC-side pass structurally cannot see.
/// <list type="bullet">
/// <item>A6 头显 MAC 随机化 —— 语料 R16/R17，社区多人复现、官方 FAQ 零覆盖。</item>
/// <item>F1 头显端设置（手部追踪等）—— 语料 R57，PC 侧 Game/Encode/Network/Decode 全绿、
/// ping 正常、有线、路由器同房间，根因是头显设置里的一个开关。</item>
/// <item>B4 Quest 侧 VPN —— 语料 R03 实证「Quest 上挂 VPN 时 VD 找不到 PC，关掉就找得到」，
/// 而官方 FAQ 只说 PC 侧别跑 VPN。</item>
/// </list>
/// 全部子判定合成一个 <see cref="CheckResult"/>；任一子项命中即升级状态。
/// 无设备 / adb 缺失 / 读不到 → <see cref="CheckStatus.Unknown"/>，绝不抛异常。
/// </summary>
public sealed class HeadsetDeepProbe(AdbClient adb)
{
    public const string Id = "headset-deep";

    /// <summary>MAC 形态 <c>xx:xx:xx:xx:xx:xx</c>，两侧不允许再跟十六进制字符。</summary>
    private static readonly Regex MacPattern = new(
        @"(?<![0-9A-Fa-f])([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}(?![0-9A-Fa-f])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Quest 上常见的 VPN / 代理客户端进程名。逐个比对（不是子串包含，见
    /// <see cref="IsVpnProcess"/>），避免 <c>ssd</c> 之类短名误命中无关进程。
    /// 这是一份启发式名单，不是权威注册表：命中即提示，不命中不代表干净。
    /// </summary>
    private static readonly (string Name, string Note)[] VpnProcesses =
    [
        ("ssd", "ShadowsocksDaemon"),
        ("shadowsocks", ""),
        ("ss-local", "Shadowsocks 本地客户端"),
        ("ssr", "ShadowsocksR"),
        ("v2ray", ""),
        ("v2rayNG", ""),
        ("xray", ""),
        ("sing-box", ""),
        ("clash", ""),
        ("clash.meta", ""),
        ("mihomo", "Clash Meta 内核"),
        ("nekobox", ""),
        ("hiddify", ""),
        ("outline", ""),
        ("openvpn", ""),
        ("nordvpn", ""),
        ("expressvpn", ""),
        ("protonvpn", ""),
        ("windscribe", ""),
        ("mullvad", ""),
        ("surfshark", ""),
        ("psiphon", ""),
        ("wireguard", ""),
        ("tailscaled", "Tailscale 组网：会改路由，未必是元凶"),
        ("zerotier", "ZeroTier 组网：会改路由，未必是元凶"),
    ];

    /// <summary>
    /// 头显本地设置文件（<c>research/03-quest-parameters/02-storage-and-io.md</c> §1.2 / §2）。
    /// <c>SharedStreamerSettings.json</c> 故意不在列表里：该文件不落盘，存在即残留，读它是噪声。
    /// </summary>
    private static readonly string[] SettingsFiles = ["UserSettings.json", "SharedUserSettings.json"];

    private readonly record struct Sub(string Key, CheckStatus Status, string Text);

    // ------------------------------------------------------------------ entry point

    public async Task<CheckResult> ProbeAsync(string serial, CancellationToken ct = default)
    {
        var ev = new Dictionary<string, string> { ["adb"] = adb.AdbPath };

        // 降级点 1：adb.exe 不存在时 Process.Start 会抛 Win32Exception，先拦在门外。
        if (string.IsNullOrWhiteSpace(adb.AdbPath) || !File.Exists(adb.AdbPath))
            return Unknown("找不到 adb.exe",
                "这一屏全部数据都来自 adb，本机没有可用的 adb.exe，所以没有结论——不是「检查通过」。", ev,
                "装 Android platform-tools，或在第一屏用 VIVE Hub 自带的 adb 路径；"
                + "本机已知可用路径是 D:\\Software\\VIVE Hub\\VIVE Hub\\CommonTools\\ADB\\adb.exe。");

        var devices = await TryAsync(["devices", "-l"], 8000, ct);
        ev["adb devices"] = string.Join(" ;; ", devices.Lines);
        if (devices.TimedOut)
            return Unknown("adb 无响应", "执行 `adb devices -l` 超时，8 秒内没有任何回包。", ev,
                "adb 可能被别的工具占着（关掉 Android Studio / SideQuest 再试），或数据线供电不足。");

        // 降级点 2：无设备是本屏最常见的路径，必须干净返回 Unknown 而不是空转或抛异常。
        var online = ParseDevices(devices.Lines);
        if (online.Count == 0)
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
            // Do not restate the network conclusion. The basic probe (HeadsetProbe.cs:87) has already
            // said "网络这一段是通的——<ip> ping 得到应答" in a card directly above this one, with the
            // same summary line; repeating it here made the third screen show the same paragraph
            // twice, which is half the panel. What this probe adds is only that nothing downstream
            // could run at all — so say that, and leave the wiring steps to the guidance it shares.
            return Unknown(
                reachable ? "头显在网络上，但 adb 连不上它" : "没有连着的头显",
                reachable
                    ? "和上一屏同一个结论：adb 没连上，所以头显侧的三项子判定（包、权限、设置）"
                      + "一个都跑不了——不是它们有问题，是没有可读的对象。"
                    : "`adb devices -l` 的设备列表是空的，三项子判定一个都跑不了。",
                // Same reasoning for the guidance. WiringGuidance is the same three steps the card
                // above is already showing; repeating it verbatim put the whole connect procedure on
                // screen twice. Point at it instead — the steps are one click-scroll away, not one
                // tab-switch away.
                ev, reachable ? "先把 adb 连上，步骤见上面那张卡。" : WiringGuidance);
        }

        if (online.Count > 1 && string.IsNullOrWhiteSpace(serial))
            return Unknown($"同时连着 {online.Count} 台设备，不知道该看哪一台",
                "一次只接一台，或者用 --serial 指定：\n" + string.Join("\n", online.Keys.Select(s => "  " + s)),
                ev, WiringGuidance);

        var target = string.IsNullOrWhiteSpace(serial) ? online.Keys.First() : serial.Trim();
        if (!online.TryGetValue(target, out var state))
            return Unknown($"头显 {target} 当前不在线",
                $"设备列表里没有这个序列号；现在在线的是：{string.Join("、", online.Keys)}。", ev, WiringGuidance);
        if (state != "device")
            return Unknown($"头显 {target} 状态是 {state}", DescribeState(state), ev, DescribeStateFix(state));
        ev["serial"] = target;

        var subs = new List<Sub>
        {
            await ProbeMacAsync(target, ev, ct),
            await ProbeHeadsetSettingsAsync(target, ev, ct),
            await ProbeQuestVpnAsync(target, ev, ct),
        };

        // 合成：任一 Warn → Warn；无 Warn 但有 Unknown → Unknown；全 Pass → Pass。永不 Block——
        // 这三项都不是「一定连不上」，随机 MAC 与 VPN 进程本身合法，只是可能打断绑定/发现。
        var worst = subs.Any(s => s.Status == CheckStatus.Warn) ? CheckStatus.Warn
            : subs.Any(s => s.Status == CheckStatus.Unknown) ? CheckStatus.Unknown
            : CheckStatus.Pass;

        return new CheckResult(
            Id, worst,
            string.Join("；", subs.Select(s => s.Text)),
            "三项都是 PC 侧检测读不到的头显侧事实：Wi-Fi 地址是不是随机 MAC、头显本地设置能不能读到、"
            + "Quest 上有没有挂着 VPN。读不到的项如实报 Unknown，不猜。",
            ev,
            Array.Empty<FixAction>(),
            ComposeGuidance(subs));
    }

    // ------------------------------------------------------------------ A6 MAC randomization

    // 三个子判定都要经 TryAsync 跑 adb，所以是实例方法（不是 static）。
    private async Task<Sub> ProbeMacAsync(string serial, Dictionary<string, string> ev, CancellationToken ct)
    {
        const string key = "A6 头显 Wi-Fi MAC";
        var mac = "";
        var source = "";

        // 命令 1（首选）：直接读 sysfs。
        // 降级：部分 Android 版本/HorizonOS 上 shell 对 /sys/class/net 的读取被 SELinux 拒
        // （"Permission denied" 或空输出），此时必须换命令而不是把空值当结论。
        var r1 = await TryAsync(["-s", serial, "shell", "cat", "/sys/class/net/wlan0/address"], 8000, ct);
        if (TryParseMac(r1.StdOut, out var m1)) { mac = m1; source = "cat /sys/class/net/wlan0/address"; }
        else ev["A6 sysfs 读取"] = DescribeFailure(r1);

        // 命令 2（兜底）：`ip addr show` 的 `link/ether` 行，不同 Android 版本都稳定输出。
        if (mac.Length == 0)
        {
            var r2 = await TryAsync(["-s", serial, "shell", "ip", "addr", "show", "wlan0"], 8000, ct);
            if (TryParseMac(r2.StdOut, out var m2)) { mac = m2; source = "ip addr show wlan0 的 link/ether"; }
            else ev["A6 ip addr 读取"] = DescribeFailure(r2);
        }

        // 降级点 3：两条命令都读不到 → Unknown。不猜 MAC、不假装通过。
        if (mac.Length == 0)
            return new Sub(key, CheckStatus.Unknown, "读不到头显 Wi-Fi MAC（两条命令都没拿到地址）");

        var b0 = Convert.ToInt32(mac[..2], 16);
        var multicast = (b0 & 0x01) != 0;   // 组播位。真实网卡 MAC 必为 0。
        var local = (b0 & 0x02) != 0;       // 本地管理位。RFC 4300 的隐私地址必为 1。
        ev[key] = $"{mac}（来源：{source}；首字节 0x{b0:X2}：组播位={(multicast ? 1 : 0)}、本地管理位={(local ? 1 : 0)}）";

        // 判定：首字节最低位为 1 → 随机 MAC（brief 给的判据）。同时接受本地管理位为 1：
        // Android 隐私 MAC 的规范位是 0x02，只判 0x01 会漏掉 02/06/0A/… 这一整类真实形态。
        if (multicast || local)
            return new Sub(key, CheckStatus.Warn,
                $"头显 Wi-Fi 用的是随机 MAC（{mac}）；如果路由器做了 MAC 过滤或按 MAC 绑定，会让它每次拿到不同地址");

        return new Sub(key, CheckStatus.Pass, $"头显 Wi-Fi MAC 是厂商分配地址（{mac}）");
    }

    // ------------------------------------------------------------------ F1 headset-side settings

    private async Task<Sub> ProbeHeadsetSettingsAsync(
        string serial, Dictionary<string, string> ev, CancellationToken ct)
    {
        const string key = "F1 头显本地设置";
        var pkgs = await TryAsync(["-s", serial, "shell", "pm", "list", "packages"], 8000, ct);
        var pkg = HeadsetProbe.PackageNames.FirstOrDefault(p =>
            pkgs.Lines.Any(l => l.Contains("package:" + p, StringComparison.OrdinalIgnoreCase)));
        if (pkg is null)
            return new Sub(key, CheckStatus.Unknown,
                "头显里没找到 VD 客户端包，设置文件无从谈起（查过 " +
                $"{HeadsetProbe.PackageNames.Length} 个包名）");

        // research/03-quest-parameters/02-storage-and-io.md §1.2：设置落在 `$HOME/.config/Virtual Desktop/`，
        // $HOME 的两种可能形态各给一个候选，逐个试，不猜。
        var dirs = new[]
        {
            $"/data/user/0/{pkg}/files/.config/Virtual Desktop",
            $"/data/data/{pkg}/files/.config/Virtual Desktop",
            $"/storage/emulated/0/Android/data/{pkg}/files/.config/Virtual Desktop",
        };

        var read = 0;
        var handTracking = false;
        var handKeySeen = false;
        var readFiles = new List<string>();

        foreach (var file in SettingsFiles)
        {
            var got = false;
            foreach (var dir in dirs)
            {
                var full = dir + "/" + file;
                var content = await ReadAsPackageAsync(serial, pkg, full, ev, ct);
                if (content is null) continue;
                got = true;
                read++;
                readFiles.Add(file);
                ev[key + " / " + file] = DescribeJson(file, content, ref handTracking, ref handKeySeen);
                break;
            }

            // 降级点 4：run-as 不可用（APK 非 debuggable 且设备未 root）时全部候选读空，
            // 此时如实报 Unknown 并说清前置条件，不编造路径、不假装读到了。
            if (!got)
                ev[key + " / " + file] =
                    $"读不到（试过 {dirs.Length} 个候选目录的 run-as / su 两种方式）；"
                    + "需要 patched APK 可调试（android:debuggable=true）或设备已 root";
        }

        if (read == 0)
            return new Sub(key, CheckStatus.Unknown,
                "头显本地设置读不到（需要 patched APK 可调试或已 root）");

        var missingFiles = SettingsFiles.Where(f => !readFiles.Contains(f)).ToList();
        var summary = $"头显设置读到 {read}/{SettingsFiles.Length} 个文件"
            + (missingFiles.Count > 0 ? "（没读到：" + string.Join("、", missingFiles) + "）" : "");

        // The key not being present is not the key being off. F1's root cause is hand tracking left
        // on, and if this Quest OS simply does not carry HandTracking/UseMultiModal in either file
        // then the check has not measured the thing it exists to measure. Pass was wrong for it.
        if (!handKeySeen)
            return new Sub(key, CheckStatus.Unknown,
                summary + "，但两个候选文件里都没有 HandTracking/UseMultiModal 键——"
                + "所以「手部追踪是开着还是关着」这一项**没测成**，不是「正常」。");

        if (handTracking)
            return new Sub(key, CheckStatus.Warn,
                summary + "，且手部追踪是开着的——PC 侧全绿时它就是每 60 秒卡一下的那种根因");
        return new Sub(key, CheckStatus.Pass, summary + "，且手部追踪明确是关着的。");
    }

    /// <summary>
    /// 读一个属于 app 私有目录的文件。优先 <c>run-as</c>（debuggable APK），
    /// 再退 <c>su</c>（已 root）。两条都失败返回 null，交给调用方降级。
    /// 命令形态照抄 research/03-quest-parameters/02-storage-and-io.md §3.1 / §3.2；
    /// 路径里的空格由 adb 自己的参数转义处理（adb ≥ 1.0.31 的 escape_arg）。
    /// </summary>
    private async Task<string?> ReadAsPackageAsync(
        string serial, string pkg, string fullPath, Dictionary<string, string> ev, CancellationToken ct)
    {
        var r = await TryAsync(["-s", serial, "exec-out", "run-as", pkg, "cat", fullPath], 8000, ct);
        if (r.Ok && r.StdOut.Trim().Length > 0) return r.StdOut;

        ev[$"F1 run-as 读取 {fullPath}"] = DescribeFailure(r);

        var s = await TryAsync(["-s", serial, "exec-out", "su", "-c", "cat " + fullPath], 8000, ct);
        if (s.Ok && s.StdOut.Trim().Length > 0) return s.StdOut;

        ev[$"F1 su 读取 {fullPath}"] = DescribeFailure(s);
        return null;
    }

    /// <summary>
    /// 只报「文件大小 + 顶层键名」，不解析出全部值；只有 <c>HandTracking</c> /
    /// <c>UseMultiModal</c> 这一个 F1 相关的布尔值被读出来用于判定（research §2.1 / §2.3）。
    /// 密文、令牌类键名照样列出来（名字不是秘密），但一律不显示其值。
    /// </summary>
    private static string DescribeJson(
        string file, string content, ref bool handTracking, ref bool handKeySeen)
    {
        var bytes = System.Text.Encoding.UTF8.GetByteCount(content);
        var keys = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    keys.Add(p.Name);
                    if (p.NameEquals("HandTracking") || p.NameEquals("UseMultiModal"))
                    {
                        handKeySeen = true;
                        if (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            handTracking |= p.Value.GetBoolean();
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            // 文件存在但不是可解析的 JSON（如被截断/正在写盘）：只报大小，如实标注。
            return $"{file}：{bytes} 字节；不是可解析的 JSON（{ex.Message}），键名读不到";
        }

        const int cap = 40;
        var shown = keys.Take(cap);
        var suffix = keys.Count > cap ? $" …共 {keys.Count} 个键" : string.Empty;
        var hand = handKeySeen ? "；HandTracking/UseMultiModal = " + (handTracking ? "开" : "关") : string.Empty;
        return $"{file}：{bytes} 字节，{keys.Count} 个顶层键（{string.Join("、", shown)}{suffix}）{hand}";
    }

    // ------------------------------------------------------------------ B4 Quest-side VPN

    private async Task<Sub> ProbeQuestVpnAsync(
        string serial, Dictionary<string, string> ev, CancellationToken ct)
    {
        const string key = "B4 Quest 侧 VPN";

        // 命令 1（首选）：一次 `ps -A` 拿全量进程，逐个比对名单。
        var ps = await TryAsync(["-s", serial, "shell", "ps", "-A"], 8000, ct);
        var names = ReadProcessNames(ps);
        if (names.Count > 0)
            ev[key + " 进程样本"] = $"{names.Count} 个进程，例如：" + string.Join("、", names.Take(12));

        var hits = new List<string>();
        var unreadable = new List<string>();
        foreach (var (name, note) in VpnProcesses)
        {
            // 降级点 5：`ps -A` 读不到（部分镜像裁掉 ps、或格式不同）时逐个 `pidof` 兜底。
            // pidof, not "non-empty". research/06-adb-headset/01-adb-playbook.md:257-259 records
            // that adb shell prints the literal `no process` when there is no such process — which is
            // non-empty, so this reported a VPN running on a clean headset whenever ps -A was
            // unavailable, and told the user to go quit it. HeadsetProbe was already fixed to parse
            // pids; this fallback was left behind.
            bool live;
            if (names.Count > 0)
            {
                live = names.Any(n => IsVpnProcess(n, name));
            }
            else
            {
                var r = await TryAsync(["-s", serial, "shell", "pidof", name], 6000, ct);
                var pids = r is { Ok: true }
                    ? r.StdOut.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                              .Where(x => int.TryParse(x, out _)).ToList()
                    : new List<string>();
                live = pids.Count > 0;
                if (r is { Ok: true } && pids.Count == 0 && r.StdOut.Trim().Length > 0)
                    unreadable.Add($"{name}: pidof 返回了「{r.StdOut.Trim()}」，不是 pid");
            }
            if (live) hits.Add(note.Length > 0 ? name + "（" + note + "）" : name);
        }

        if (names.Count == 0)
            ev[key + " 取进程方式"] = "ps -A 读不到，已逐个用 pidof 兜底查了 " + VpnProcesses.Length + " 个名字";
        if (unreadable.Count > 0)
            ev[key + " pidof 非 pid 输出"] = string.Join(" ;; ", unreadable);

        if (hits.Count == 0)
        {
            return names.Count == 0
                ? new Sub(key, CheckStatus.Unknown, "进程列表读不到，VPN 未能判定")
                : new Sub(key, CheckStatus.Pass, $"Quest 上没查到 VPN/代理进程（逐个查了 {VpnProcesses.Length} 个名字）");
        }

        ev[key] = "命中：" + string.Join("、", hits);
        return new Sub(key, CheckStatus.Warn,
            $"Quest 上挂着 VPN/代理进程：{string.Join("、", hits)}——"
            + "这是社区实证的独有根因，官方 FAQ 没覆盖");
    }

    /// <summary>
    /// `ps -A` 的最后一列是进程名（形态 <c>USER PID PPID VSZ RSS WCHAN ADDR S NAME</c>，
    /// 见 research/06-adb-headset/01-adb-playbook.md §3.2）。
    /// NAME 可能被内核截断到 15 字符，所以只取最后一列、不按列数硬解析。
    /// </summary>
    private static List<string> ReadProcessNames(AdbClient.Result r)
    {
        var names = new List<string>();
        if (!r.Ok) return names;
        foreach (var line in r.Lines)
        {
            if (line.StartsWith("USER", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("PID", StringComparison.OrdinalIgnoreCase)) continue;
            var last = line.LastIndexOf(' ');
            if (last < 0) { names.Add(line); continue; }
            var token = line[(last + 1)..].Trim();
            if (token.Length > 0) names.Add(token);
        }
        return names;
    }

    /// <summary>
    /// 进程名比对：全等，或名单名是进程名的前缀且多出的部分全是数字（覆盖 <c>nordvpn3</c>、
    /// <c>openvpn3</c> 这类带版本号的进程名）。刻意不用子串包含——<c>ssd</c>、<c>v2ray</c>
    /// 这类短名用子串会误命中大量无关进程。
    /// </summary>
    private static bool IsVpnProcess(string processName, string candidate)
    {
        if (processName.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return true;
        if (!processName.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) return false;
        var extra = processName.AsSpan(candidate.Length);
        if (extra.Length == 0 || extra.Length > 3) return false;
        foreach (var c in extra)
            if (!char.IsAsciiDigit(c)) return false;
        return true;
    }

    // ------------------------------------------------------------------ shared plumbing

    /// <summary>
    /// 跑 adb 并把任何宿主异常（adb.exe 被删、权限不足、进程启动失败）折叠成一次
    /// 非零退出码。AdbClient.RunAsync 本身不 catch 这些异常，不包一层就会把
    /// 「本机没装 adb」变成一次未处理异常弹窗。
    /// </summary>
    private async Task<AdbClient.Result> TryAsync(IReadOnlyList<string> args, int timeoutMs, CancellationToken ct)
    {
        try
        {
            return await adb.RunAsync(args, timeoutMs, ct);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or InvalidOperationException or IOException or UnauthorizedAccessException
            or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return new AdbClient.Result(-1, "", ex.GetType().Name + ": " + ex.Message, TimedOut: false);
        }
    }

    /// <summary>把 `adb devices -l` 解析成 序列号 → 状态，按行内第二个字段精确比对状态列。</summary>
    private static Dictionary<string, string> ParseDevices(IReadOnlyList<string> lines)
    {
        var devices = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(1))
        {
            if (line.StartsWith('*')) continue;
            var cols = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length < 2) continue;
            devices[cols[0]] = cols[1];
        }
        return devices;
    }

    private static bool TryParseMac(string? text, out string mac)
    {
        mac = "";
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = MacPattern.Match(text);
        if (!m.Success) return false;
        mac = m.Value.ToLowerInvariant();
        return true;
    }

    private static string DescribeFailure(AdbClient.Result r) =>
        r.TimedOut ? "超时（8 秒无回包）"
        : r.StdErr.Trim() is { Length: > 0 } e ? "stderr: " + e
        : r.StdOut.Trim() is { Length: > 0 } o ? "stdout: " + o
        : $"exit={r.ExitCode}，无输出";

    private static string DescribeState(string state) => state switch
    {
        "unauthorized" => "头显上还没点「允许 USB 调试」，adb 被拒绝。",
        "offline" => "adb 连得上但拿不到响应，通常是头显睡着或线刚插上还没握手。",
        "no permissions" => "这台机器缺 adb 的 USB 驱动授权规则。",
        _ => $"设备状态是 {state}，不是就绪的 device。",
    };

    private static string DescribeStateFix(string state) => state switch
    {
        "unauthorized" => "拔插数据线，在头显弹窗里点「允许 USB 调试」并勾「始终允许」；"
            + "没看到弹窗就去 设置 → 关于 → 开发者选项 确认 USB 调试已打开。",
        "offline" => "戴上头显唤醒屏幕（头显睡着时不维持 adb 会话），"
            + "或用 `adb kill-server && adb start-server` 后重连。",
        "no permissions" => "换一条原装数据线/换 USB 口；仍不行去 设备管理器 装 OEM 的 adb 驱动。",
        _ => WiringGuidance,
    };

    private const string WiringGuidance =
        "① USB-C 接 PC，② 头显里点「允许 USB 调试」（勾「始终允许」），"
        + "③ 回到本屏重跑。无线方式：先在头显 开发者选项 打开无线调试，"
        + "PC 上 `adb pair <头显IP:配对端口> <配对码>`，再 `adb connect <头显IP>:5555`；"
        + "头显 IP 在 设置 → Wi-Fi → 连接详情 里看。";

    private static string? ComposeGuidance(IReadOnlyList<Sub> subs)
    {
        var parts = new List<string>();

        if (subs.Any(s => s.Key.StartsWith("A6", StringComparison.Ordinal) && s.Status == CheckStatus.Warn))
            parts.Add("随机 MAC 本身合法，不是故障；只有当路由器按 MAC 过滤/绑定时它才打断连接。"
                + "改路由器端的白名单/绑定，或在头显 Wi-Fi 设置里关掉「使用随机 MAC / 私有 MAC 地址」。"
                + "不要靠手改头显 IP 去对齐 PC 网段——那是把 IP 冲突的风险转嫁给头显（研究 §3.5）。");

        if (subs.Any(s => s.Key.StartsWith("F1", StringComparison.Ordinal) && s.Status == CheckStatus.Warn))
            parts.Add("头显里 虚拟桌面 → 设置 → 输入（Input）→ 手部追踪 关掉再试。"
                + "语料 R57 的原话是「关掉它，卡顿完全消失」——那一例 PC 侧 Game/Encode/Network/Decode 全稳定、"
                + "ping 正常、有线、路由器同房间，所以关防火墙、换路由器、改 BIOS 都不会有用（研究 §3.3 / §3.10）。");

        if (subs.Any(s => s.Key.StartsWith("F1", StringComparison.Ordinal) && s.Status == CheckStatus.Unknown))
            parts.Add("想读到头显设置需要 patched APK 带 android:debuggable=true（或设备已 root），"
                + "官方 APK 两者都没有，所以这一项在官方版本上永远是 Unknown——这是前置条件缺失，不是故障。");

        if (subs.Any(s => s.Key.StartsWith("B4", StringComparison.Ordinal) && s.Status == CheckStatus.Warn))
            parts.Add("在头显里把 VPN/代理应用退掉（不是切到「未连接」，是退出应用；"
                + "很多客户端退到后台仍在改路由）。这是社区实证的独有根因：官方 FAQ 只说 PC 侧别跑 VPN，"
                + "语料 R03 里正是 Quest 侧挂着 VPN 导致 VD 找不到 PC，关掉立刻找得到。");

        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }

    private static CheckResult Unknown(string summary, string detail, Dictionary<string, string> ev, string guidance) =>
        new(Id, CheckStatus.Unknown, summary, detail, ev, Array.Empty<FixAction>(), guidance);
}