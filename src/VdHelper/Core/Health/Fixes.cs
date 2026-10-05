using VdHelper.Core.Checks;
using VdHelper.Core.Model;
using VdHelper.Core.Diagnosis;

namespace VdHelper.Core.Health;

/// <summary>
/// Repair actions. Every one of them records what it changed and how to undo it (ADR-003).
/// Anything we cannot undo — router settings, third-party AV — is guidance, never a fix.
/// </summary>
public static class Fixes
{
    public static IReadOnlyList<FixAction> DisableAdapters(IEnumerable<string> adapterNames)
    {
        var actions = new List<FixAction>();
        foreach (var name in adapterNames)
        {
            actions.Add(new FixAction(
                $"disable-adapter:{name}",
                $"禁用网卡「{name}」",
                $"Disable-NetAdapter -Name {Ps.Literal(name)} -Confirm:$false",
                "修复前已记录该网卡当前 Enabled 状态（当前为 Disabled 或不存在）；未改动其 IP 配置。",
                $"Enable-NetAdapter -Name {Ps.Literal(name)} -Confirm:$false",
                FixRisk.Low,
                ct => RunPsAsync($"Disable-NetAdapter -Name {Ps.Literal(name)} -Confirm:$false", name, ct)));
        }
        return actions;
    }

    public static IReadOnlyList<FixAction> DisableUnusableAdapters()
    {
        var unusable = NetworkInventory.ReadAdapters()
            .Where(a => !a.IsUp && !a.IsLoopback && a.ApipaAddresses.Count > 0)
            .Select(a => a.Name);
        return DisableAdapters(unusable);
    }

    public static IReadOnlyList<FixAction> RestoreVdRule() =>
    [
        new FixAction(
            "fw-restore-vd",
            "重建 Virtual Desktop 入站放行规则",
            @"netsh advfirewall firewall add rule name=""Virtual Desktop Streamer"" dir=in action=allow program=""C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe"" enable=yes profile=any",
            "添加前先导出：`netsh advfirewall firewall export <备份文件>`；同名规则若已存在需先删除。",
            @"netsh advfirewall firewall delete rule name=""Virtual Desktop Streamer""",
            FixRisk.Medium,
            ct => RunPsAsync(
                @"netsh advfirewall firewall add rule name=""Virtual Desktop Streamer"" dir=in action=allow program=""C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe"" enable=yes profile=any",
                "Virtual Desktop Streamer", ct)),
    ];

    public static IReadOnlyList<FixAction> StartVdService() =>
    [
        new FixAction(
            "svc-start",
            "启动 VirtualDesktop 服务",
            "Start-Service -Name 'VirtualDesktop.Service' （并按需 Set-Service -StartupType Automatic）",
            "未改动启动类型；仅在服务已停止时启动。",
            "Stop-Service -Name 'VirtualDesktop.Service'",
            FixRisk.Low,
            ct => RunPsAsync("Start-Service -Name 'VirtualDesktop.Service'", "VirtualDesktop.Service", ct)),
    ];

    public static IReadOnlyList<FixAction> RelaunchStreamer() =>
    [
        new FixAction(
            "streamer-launch",
            "启动 Virtual Desktop Streamer",
            $"Start-Process '{StreamerChecks.StreamerExe}'",
            "只启动进程，不改任何配置。",
            "Stop-Process -Name 'VirtualDesktop.Streamer' -Force",
            FixRisk.Low,
            ct => RunPsAsync($"Start-Process '{StreamerChecks.StreamerExe}'", "Virtual Desktop Streamer", ct)),
    ];

    /// <summary>
    /// The "configured identity is incorrect" failure is owned by the MSI installer, so the honest
    /// action is to hand the user the MSI instead of mutating the service account ourselves.
    /// </summary>
    public static IReadOnlyList<FixAction> RepairService() =>
    [
        new FixAction(
            "svc-repair",
            "以管理员身份重装 Virtual Desktop 服务",
            "msiexec /i \"C:\\Program Files\\Virtual Desktop Streamer\\VirtualDesktop.Service.msi\" /qn，然后重启服务",
            "先导出当前服务配置：`sc.exe qc VirtualDesktop.Service > %TEMP%\\vdservice-before.txt`；安装包不删除用户数据。",
            "msiexec /i \"...\\VirtualDesktop.Service.msi\" /qn（重装即恢复默认账户绑定）",
            FixRisk.Medium,
            ct => RunPsAsync(
                "Start-Process msiexec -ArgumentList '/i \"C:\\Program Files\\Virtual Desktop Streamer\\VirtualDesktop.Service.msi\" /qn' -Verb RunAs -Wait",
                "Virtual Desktop 服务", ct)),
    ];

    private static async Task<FixResult> RunPsAsync(string script, string subject, CancellationToken ct)
    {
        var r = await PowerShellRunner.RunAsync(script, asAdministrator: true, ct: ct).ConfigureAwait(false);
        return r.Ok
            ? new FixResult(true, $"{subject} 已处理", r.Combined)
            : new FixResult(false, $"{subject} 处理失败（可能需要管理员权限）", r.Combined);
    }
}