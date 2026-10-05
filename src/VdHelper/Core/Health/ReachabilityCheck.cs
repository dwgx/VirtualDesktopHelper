using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using VdHelper.Core.Adb;
using VdHelper.Core.Checks;
using VdHelper.Core.Diagnosis;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// PC → headset reachability. Community triage starts with "can you ping the headset" and that
/// single question separates "PC is invisible" from "PC is visible but unreachable"
/// (research/09-failure-corpus/01-symptom-corpus.md R10/R02). The tool runs it so the user does
/// not have to open a console.
/// </summary>
public static class ReachabilityCheck
{
    public const string IpKey = "headsetIp";

    /// <summary>Ports to probe on the headset. Discovery is broadcast-based, so these are the
    /// Streamer's session ports; a closed port on a reachable host still proves reachability.</summary>
    private static readonly int[] ProbePorts = [38810, 38820];

    public static ICheck Create() =>
        CheckFactory.Delegate(
            new("lan-reach", "头显可达性", "PC 能不能在网络上够到头显？", "连通性"),
            async ct =>
            {
                var ip = ConfigFile.Read<string>(IpKey);
                var local = NetworkInventory.PrimaryCandidates().FirstOrDefault();
                var ev = new Dictionary<string, string>
                {
                    ["头显 IP"] = string.IsNullOrWhiteSpace(ip) ? "(未填写)" : ip,
                    ["本机地址"] = local is null ? "-" : $"{local.Name} {local.PrimaryIPv4}",
                };

                if (string.IsNullOrWhiteSpace(ip))
                    return new CheckResult("lan-reach", CheckStatus.Unknown,
                        "还没填头显 IP，无法测可达性",
                        "头显 IP 在「设置 → Wi-Fi → 连接当前网络 → IP 地址」能看到。填上就能自动测。",
                        ev, Array.Empty<FixAction>(),
                        "填了 IP 之后，这一项会直接告诉你「网络层到底通不通」，把「看不见」和「连不上」分开。");

                if (!IPAddress.TryParse(ip, out var address))
                    return new CheckResult("lan-reach", CheckStatus.Unknown,
                        "填的头显 IP 不是合法地址", ip, ev, Array.Empty<FixAction>(),
                        "例如 192.168.11.23。");

                var sameSubnet = local?.PrimaryIPv4 is not null
                    && SameSubnet(local.PrimaryIPv4, address);
                ev["同网段"] = sameSubnet ? "是" : "否";
                if (!sameSubnet)
                    ev["提示"] = "两端不在同一网段：头显可能在访客网络，或路由器开了 AP 隔离";

                var ping = await PingAsync(address, 1200, ct);
                ev["ping"] = ping.Success ? $"{ping.RoundtripMs} ms" : "不通";
                ev["ping 明细"] = ping.Detail;

                var openPorts = new List<string>();
                foreach (var port in ProbePorts)
                    if (await IsPortOpenAsync(address, port, 700, ct))
                        openPorts.Add(port.ToString());
                ev["端口探测"] = openPorts.Count > 0
                    ? string.Join(",", openPorts) + " 有响应"
                    : "38810/38820 未响应（串流未开始时本就不开，不能据此判故障）";

                if (ping.Success)
                    return new CheckResult("lan-reach", CheckStatus.Pass,
                        $"头显 {ip} 可达（ping {ping.RoundtripMs} ms）",
                        "网络层通。如果头显里还是「连不上」，问题在 VD 应用侧或账号侧，不在网络。",
                        ev, Array.Empty<FixAction>());

                return new CheckResult("lan-reach", CheckStatus.Block,
                    $"头显 {ip} ping 不通",
                    "PC 到头显的网络层就不通，VD 发现与连接都不可能成功。这是必须先解决的一环。",
                    ev, Array.Empty<FixAction>(),
                    sameSubnet
                        ? "同网段还不通：查 AP 隔离 / 访客网络 / 无线与有线隔离 / 头显连的是 5GHz 还是 2.4GHz。"
                        : "不同网段：让头显连到和 PC 同一个 SSID，并确认路由器没有开访客网络或 AP 隔离。");
            });

    public static bool SameSubnet(IPAddress a, IPAddress b)
    {
        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        if (x.Length != 4 || y.Length != 4) return false;
        // /24 comparison: routers in the home segment are effectively /24 and this avoids
        // guessing a prefix length we cannot verify.
        return x[0] == y[0] && x[1] == y[1] && x[2] == y[2];
    }

    private static async Task<(bool Success, long RoundtripMs, string Detail)> PingAsync(
        IPAddress address, int timeoutMs, CancellationToken ct)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(address, timeoutMs);
            var detail = $"{reply.Status}" +
                (reply.Address is not null ? $" -> {reply.Address}" : "") +
                (reply.RoundtripTime >= 0 ? $" {reply.RoundtripTime}ms" : "");
            return (reply.Status == IPStatus.Success, Math.Max(0, reply.RoundtripTime), detail);
        }
        catch (Exception ex) when (ex is PingException or SocketException)
        {
            return (false, 0, ex.Message);
        }
    }

    private static async Task<bool> IsPortOpenAsync(IPAddress address, int port, int timeoutMs, CancellationToken ct)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(address, port, ct)
                .AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct);
            return client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or TimeoutException)
        {
            return false;
        }
    }
}