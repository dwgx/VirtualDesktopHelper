using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using VdHelper.Core.Checks;
using VdHelper.Core.Diagnosis;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// Checks that read what the Streamer itself left behind. On this machine every network check
/// passes while the Streamer has been unable to start since 2026-09-07 — the only evidence is
/// <c>C:\ProgramData\Virtual Desktop\ServiceLog.txt</c>. This class is that detector.
/// </summary>
public static class StreamerChecks
{
    public const string ProgramData = @"C:\ProgramData\Virtual Desktop";
    public const string ServiceLog = ProgramData + @"\ServiceLog.txt";
    public const string StreamerLog = ProgramData + @"\StreamerLog.txt";
    public const string StreamerExe = @"C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe";

    private static readonly Regex ErrorLine =
        new(@"^(?<ts>\d{4}-\d{2}-\d{2}[^|]*)\|(?<level>ERROR|WARN)\|(?<comp>[^|]*)\|(?<msg>.*)$",
            RegexOptions.Compiled);

    public static ICheck StreamerProcessCheck() =>
        CheckFactory.Delegate(
            new("streamer-proc", "Streamer 进程", "串流进程真的在跑吗？", "进程"),
            ct =>
            {
                var running = Process.GetProcessesByName("VirtualDesktop.Streamer");
                var ev = new Dictionary<string, string>
                {
                    ["进程"] = running.Length > 0
                        ? string.Join(", ", running.Select(p => p.Id.ToString()))
                        : "未运行",
                    ["可执行文件"] = File.Exists(StreamerExe) ? StreamerExe : "未安装（" + StreamerExe + "）",
                };
                foreach (var p in running) p.Dispose();

                if (running.Length == 0)
                    return Task.FromResult(new CheckResult("streamer-proc", CheckStatus.Block,
                        "Streamer 进程没有运行——PC 侧不会广播，也不会监听串流端口",
                        "服务「在运行」不等于进程能起来。本机 2026-09-07 起就持续记录启动失败，见 svc-log 项。",
                        ev, Fixes.RelaunchStreamer(),
                        "手动双击运行 Virtual Desktop Streamer.exe 看是否报错；报错内容决定下一步。"));

                return Task.FromResult(new CheckResult("streamer-proc", CheckStatus.Pass,
                    $"Streamer 进程运行中（{running.Length} 个）",
                    "PC 侧会周期广播 UDP 38860，并监听 38850/38810-40。", ev, Array.Empty<FixAction>()));
            });

    public static ICheck ServiceLogCheck() =>
        CheckFactory.Delegate(
            new("svc-log", "服务日志", "Streamer 启动时有没有报错？", "进程"),
            ct =>
            {
                var ev = new Dictionary<string, string> { ["日志路径"] = ServiceLog };
                if (!File.Exists(ServiceLog))
                    return Task.FromResult(new CheckResult("svc-log", CheckStatus.Pass,
                        "没有服务日志（首次运行前属正常）", "正常。", ev, Array.Empty<FixAction>()));

                var lines = ReadLines(ServiceLog);
                ev["日志行数"] = lines.Count.ToString();
                var errors = lines.Select(l => ErrorLine.Match(l)).Where(m => m.Success).ToList();
                var recent = errors.Where(m => m.Groups["level"].Value == "ERROR").ToList();
                ev["错误条数"] = recent.Count.ToString();
                if (recent.Count > 0)
                    ev["最近一条"] = recent[^1].Groups["ts"].Value + " — " + Truncate(recent[^1].Groups["msg"].Value, 160);

                if (recent.Count == 0)
                    return Task.FromResult(new CheckResult("svc-log", CheckStatus.Pass,
                        "服务日志里没有 ERROR", "正常。", ev, Array.Empty<FixAction>()));

                var last = recent[^1].Groups["ts"].Value;
                return Task.FromResult(new CheckResult("svc-log", CheckStatus.Block,
                    $"服务日志有 {recent.Count} 条 ERROR，最近一次 {last}",
                    "含义：服务尝试拉起 Streamer 时被系统拒绝，网络层再正常也不会广播。这是「各项都正常但连不上」的典型原因。",
                    ev, Fixes.RepairService(),
                    "若重装服务无效，检查服务登录账户密码是否与当前系统账户一致（Uninstall/reinstall 需要管理员）。"));
            });

    public static ICheck UdpDiscoveryCheck() =>
        CheckFactory.Delegate(
            new("udp-discovery", "发现协议端口", "UDP 38850/38860 在不在？", "端口"),
            ct =>
            {
                var ev = new Dictionary<string, string>();
                try
                {
                    var udp = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners();
                    foreach (var ep in udp.Where(e => e.Port is 38850 or 38860))
                        ev["UDP " + ep.Port] = ep.Address + ":" + ep.Port;
                }
                catch (NetworkInformationException ex)
                {
                    return Task.FromResult(new CheckResult("udp-discovery", CheckStatus.Unknown,
                        "读不到 UDP 监听表", ex.Message, ev, Array.Empty<FixAction>(),
                        "需要管理员权限才能枚举全部监听套接字。"));
                }

                var listening50 = ev.ContainsKey("UDP 38850");
                var listening60 = ev.ContainsKey("UDP 38860");
                if (listening50)
                    return Task.FromResult(new CheckResult("udp-discovery", CheckStatus.Pass,
                        "UDP 38850 已监听（发现/配对协议）", "头显能通过广播找到这台 PC。", ev, Array.Empty<FixAction>()));

                return Task.FromResult(new CheckResult("udp-discovery", CheckStatus.Warn,
                    listening60 ? "只看到 UDP 38860 在广播，没看到 38850 监听" : "UDP 38850/38860 都没有活动",
                    "发现靠定向广播：PC 收 38850 的配对请求并单播回包，同时周期广播 38860。两者都没有通常意味着 Streamer 进程没在跑。",
                    ev, Array.Empty<FixAction>(),
                    "先看 streamer-proc 与 svc-log 两项；进程起来后这里会变成通过。"));
            });

    private static List<string> ReadLines(string path)
    {
        try { return File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToList(); }
        catch (IOException) { return new List<string>(); }
        catch (UnauthorizedAccessException) { return new List<string>(); }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}