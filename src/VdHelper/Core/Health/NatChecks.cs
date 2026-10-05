using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SharpOpenNat;
using VdHelper.Core.Checks;
using VdHelper.Core.Diagnosis;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// NAT 类型 / 路由器 UPnP 能力探测，覆盖 B8（远端场景 NAT 穿透）。
/// 证据来自路由器自己的 UPnP 控制面（SharpOpenNat，MIT），全程只读：
/// 只调 <c>GetExternalIPAsync</c> / <c>GetSpecificMappingAsync</c> / <c>GetAllMappingsAsync</c>，
/// 绝不调 <c>CreatePortMapAsync</c>（社区结论：本地场景不该由工具替用户开端口，见
/// <c>research/09-failure-corpus/02-symptom-to-rootcause.md</c> §3.1 / §3.2）。
/// </summary>
public static class NatChecks
{
    /// <summary>发现窗口。SharpOpenNat 的 searcher 会一直搜到 token 取消才返回，所以耗时≈预算。</summary>
    private static readonly TimeSpan DiscoveryBudget = TimeSpan.FromSeconds(3);

    /// <summary>单次 SOAP 查询上限。路由器不实现某个 action 时会挂住，不会立刻抛异常。</summary>
    private static readonly TimeSpan QueryBudget = TimeSpan.FromSeconds(2);

    /// <summary>
    /// VD Streamer 在开启 "Allow remote connections" 时经 UPnP 申请的第一个映射端口
    /// （<c>VD-Net/Streamer/ConnectionManager.cs:240-243</c>，见
    /// <c>research/02-network-diagnosis/01-ports-and-discovery.md</c> §2.1 表第 34 行）。
    /// </summary>
    private const int ProbePort = 38810;

    /// <summary>NAT-PMP 的服务端端口（SharpOpenNat <c>PmpConstants.ServerPort</c>），用来区分设备类型。</summary>
    private const int PmpServerPort = 5351;

    public static IReadOnlyList<ICheck> Create() => [NatTypeCheck()];

    public static ICheck NatTypeCheck() =>
        CheckFactory.Delegate(
            new("nat-type", "NAT 与路由器 UPnP", "路由器支不支持 UPnP？异地时连接进得来吗？", "远程连接"),
            RunAsync);

    private static async Task<CheckResult> RunAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var ev = new Dictionary<string, string>();
        var errors = new List<string>();
        try
        {
            ev["本机网关"] = string.Join(" | ",
                HealthChecks.RealAdapters().Where(a => a.HasDefaultGateway)
                    .Select(a => $"{a.Name}={a.PrimaryIPv4} → {string.Join("/", a.Gateways.Select(g => g.ToString()))}"));
            ev["本机 TCP 38810"] = NetworkInventory.IsListening(ProbePort) ? "在监听" : "没在监听";

            var devices = await DiscoverAsync(ct).ConfigureAwait(false);
            ev["发现耗时"] = $"{sw.ElapsedMilliseconds} ms（预算 {DiscoveryBudget.TotalMilliseconds:F0} ms，"
                           + "SharpOpenNat 搜满整个窗口才返回）";
            if (devices.Count == 0)
                return NotFound(sw, ev);

            var device = devices.FirstOrDefault(d => d.HostEndPoint.AddressFamily == AddressFamily.InterNetwork)
                         ?? devices[0];
            ev["NAT 设备"] = device.HostEndPoint.ToString();
            ev["设备类型"] = device.HostEndPoint.Port == PmpServerPort ? "NAT-PMP (5351)" : "UPnP / SSDP";
            ev["设备内地址"] = device.LocalAddress.ToString();
            if (devices.Count > 1)
                ev["其它设备"] = string.Join(" ;; ", devices.Skip(1).Select(d => d.HostEndPoint.ToString()));

            // ---- 以下都是只读查询；每项独立超时 + 独立兜底，路由器不支持时只记异常不中断。
            var ext = await Query<IPAddress?>(async t => await device.GetExternalIPAsync(t), ct, errors, "外网 IP")
                .ConfigureAwait(false);
            ev["外网 IP"] = ext?.ToString() ?? "(读不到)";
            ev["外网 IP 判定"] = ext is null
                ? "未知"
                : IsPrivate(ext) ? "私有地址段 → 这台路由器自己还在上级 NAT 后面（双层 NAT）" : "公网地址";

            var mapping = await Query<Mapping?>(
                async t => await device.GetSpecificMappingAsync(Protocol.Tcp, ProbePort, t),
                ct, errors, $"TCP {ProbePort} 映射").ConfigureAwait(false);
            ev[$"TCP {ProbePort} 映射"] = mapping?.ToString() ?? "(路由器上没有这条入站映射)";

            var all = await Query<Mapping[]?>(async t => await device.GetAllMappingsAsync(t), ct, errors, "映射表")
                .ConfigureAwait(false);
            ev["映射表"] = all is null ? "(读不到)"
                : $"{all.Length} 条" + (all.Length > 0 ? "：" + string.Join(" ;; ", all.Take(6).Select(m => m.ToString())) : "");

            ev["原始异常"] = errors.Count == 0 ? "(无)" : string.Join(" ;; ", errors);
            ev["总耗时"] = $"{sw.ElapsedMilliseconds} ms";

            // 判定：映射拿得到 = Open；设备在、但没有 38810 的入站映射 = Cone/Restricted。
            // 两种 NAT 类型都不影响同网段串流，所以找到设备就判 Pass，永远不判 Block/Warn：
            // HealthReport 把任何 Warn 汇总成「有隐患：能串但可能不稳或掉帧」，把一个只跟异地有关
            // 的事实说成掉帧元凶是假的。双层 NAT 的信息放进 Summary 与 Guidance，不抬状态。
            var natType = mapping is not null ? "Open" : "Cone/Restricted";
            var doubleNat = ext is not null && IsPrivate(ext);
            var summary = $"路由器支持 UPnP（{ev["设备类型"]}），NAT 类型 {natType}";
            if (doubleNat)
                summary += $"；外网 IP 是 {ext}（私有段），上级还有一层 NAT——这只影响异地连接，不影响同网段";

            var detail = mapping is not null
                ? "路由器上已经有这条入站映射，说明 UPnP 控制面可用、且 38810 这类入站端口能穿过本级 NAT。"
                : "路由器认得 UPnP 请求、映射表也能读，但里面没有 38810 的入站映射。"
                  + "注意这不等于路由器有问题：Streamer 没勾 \"Allow remote connections\" 或 Streamer 根本没在跑时，"
                  + "本来就不会有这条映射（ConnectionManager.cs:238-243 只在开远程时才申请）。";
            detail += "同网段串流走的是 UDP 255.255.255.255:38860 空广播 + PC 收 38850，NAT 类型对它没有影响。";

            return new CheckResult(
                "nat-type", CheckStatus.Pass, summary, detail, ev,
                Array.Empty<FixAction>(), RemoteGuidance(doubleNat));
        }
        catch (Exception ex)
        {
            // 任何未预料的异常都不能让整个体检卡死或崩掉。
            ev["原始异常"] = string.Join(" ;; ", errors.Append($"{ex.GetType().FullName}: {ex.Message}"));
            ev["总耗时"] = $"{sw.ElapsedMilliseconds} ms";
            return new CheckResult("nat-type", CheckStatus.Unknown, "NAT 探测没能完成",
                "路由器没响应 UPnP 探测，或探测过程中抛了异常。这既不是故障也不是通过——这一项判为「不知道」。",
                ev, Array.Empty<FixAction>(), LocalGuidance);
        }
    }

    /// <summary>
    /// 一次性把 UPnP 与 NAT-PMP 两个协议都搜一遍：SharpOpenNat 的
    /// <c>DiscoverDevicesAsync</c> 会并行启动两个 searcher，所以一次调用就覆盖两种协议，
    /// 分两次搜则要把 3 秒窗口付两遍。找不到设备时它返回空集合（不抛
    /// <c>NatDeviceNotFoundException</c>，那是 <c>DiscoverDeviceAsync</c> 的行为）。
    /// </summary>
    private static async Task<IReadOnlyList<INatDevice>> DiscoverAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DiscoveryBudget);
        var devices = await OpenNat.Discoverer
            .DiscoverDevicesAsync(PortMapper.Upnp | PortMapper.Pmp, cts.Token).ConfigureAwait(false);
        return devices.ToList();
    }

    /// <summary>单项只读查询：独立超时、独立异常兜底，失败只记进 <paramref name="errors"/>，不中断整项检查。</summary>
    private static async Task<T?> Query<T>(
        Func<CancellationToken, Task<T>> query, CancellationToken ct, List<string> errors, string label)
        where T : class?
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(QueryBudget);
        try
        {
            return await query(cts.Token).ConfigureAwait(false);
        }
        catch (NotSupportedException ex)
        {
            errors.Add($"{label}: NotSupportedException — {ex.Message}");
            return null;
        }
        catch (OperationCanceledException)
        {
            errors.Add($"{label}: 超时（>{QueryBudget.TotalMilliseconds:F0} ms，路由器没回这个 UPnP action）");
            return null;
        }
        catch (Exception ex)
        {
            errors.Add($"{label}: {ex.GetType().Name} — {ex.Message}");
            return null;
        }
    }

    private static CheckResult NotFound(Stopwatch sw, Dictionary<string, string> ev)
    {
        ev["NAT 设备"] = "(没找到)";
        ev["原始异常"] = "(无异常，只是没有任何设备回应 SSDP / NAT-PMP)";
        ev["总耗时"] = $"{sw.ElapsedMilliseconds} ms";
        return new CheckResult("nat-type", CheckStatus.Unknown,
            "没找到支持 UPnP 的路由器（路由器关了 UPnP，或被防火墙/杀软过滤了 SSDP 广播）",
            "这不是故障。局域网串流不需要 UPnP；只有异地连接才用得上它。",
            ev, Array.Empty<FixAction>(), LocalGuidance);
    }

    /// <summary>本机同网段场景的正确说法：什么都不用改。</summary>
    private const string LocalGuidance =
        "同网段使用不需要开 UPnP，也不需要转发任何端口：VD 的局域网发现走 UDP 255.255.255.255:38860 的空广播，"
        + "PC 收 UDP 38850 应答，NAT 在这两步上都不参与。社区两个常见错误解法：一是去路由器转发 TCP 38810-40"
        + "（语料 R14/R21：转发后照样 \"computer unreachable\"），二是把 UPnP 当成 Windows 的\"网络发现\"去开"
        + "（R53），这两件事完全无关。先看广播与防火墙，端口转发排最后。";

    private static string RemoteGuidance(bool doubleNat) =>
        LocalGuidance
        + "Open NAT 只在异地连接时才有意义：那时需要精确转发 TCP 38810/38820/38830/38840 到 PC 的固定 IP，"
        + "或让 Streamer 勾 \"Allow remote connections\" 借 UPnP 自动映射。"
        + "不要开 DMZ——它把整台 PC 暴露给整个互联网（语料 R23 的警告是对的，而\"开 DMZ\"那条建议只存在于二手转述里）。"
        + (doubleNat
            ? "本机外网 IP 落在私有段，说明路由器后面还有一层 NAT（多半是运营商光猫）：只在路由器上转发端口是不够的，"
            + "上级设备也要参与，或者改用 Streamer 的云中继。"
            : string.Empty);

    /// <summary>RFC1918：10/8、172.16/12、192.168/16。</summary>
    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168);
    }
}