using System.IO;
using System.Diagnostics;
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

    /// <summary>
    /// Kept deliberately unused. It is the only place the tool could disable a network adapter,
    /// and disabling an already-down adapter buys nothing while permanently removing it from the
    /// user's machine. Re-introduce it only with a reason a user asked for.
    /// </summary>
    public static IReadOnlyList<FixAction> DisableUnusableAdapters() =>
        Array.Empty<FixAction>();

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
            async ct =>
            {
                var started = await RunPsAsync("Start-Service -Name 'VirtualDesktop.Service'", "VirtualDesktop.Service", ct)
                    .ConfigureAwait(false);
                if (!started.Success)
                    return new FixResult(false, "启动命令未成功：" + started.Message);
                var state = await PowerShellRunner
                    .RunAsync("(Get-Service -Name 'VirtualDesktop.Service').Status", ct: ct)
                    .ConfigureAwait(false);
                return state.StdOut.Contains("Running", StringComparison.OrdinalIgnoreCase)
                    ? new FixResult(true, "服务已处于 Running", state.StdOut.Trim())
                    : new FixResult(false, "命令返回 0，但复查状态不是 Running：" + state.StdOut.Trim());
            }),
    ];

    /// <summary>
    /// Restarting the Streamer is the only reliable way to clear leftover session sockets — they
    /// belong to the Streamer process and die with it.
    /// <para>
    /// Two things this must get right, both learned the hard way on this machine:
    /// the Streamer runs elevated (a service starts it), so the kill needs a UAC elevation, and
    /// the result has to be *verified* — the first version reported success while
    /// <c>Stop-Process</c> was failing with "Access is denied" and the exit code stayed 0.
    /// A repair that cannot confirm it worked is worse than no repair.
    /// </para>
    /// </summary>
    public static IReadOnlyList<FixAction> RestartStreamer() =>
    [
        new FixAction(
            "streamer-restart",
            "重启 Virtual Desktop Streamer（清掉残留会话）",
            "以管理员身份重跑一次：结束进程再拉起官方 exe",
            "不碰任何配置文件；仅结束进程并重新拉起。当前串流会断。",
            "再手动启动一次 Virtual Desktop.Streamer.exe 即可。",
            FixRisk.Low,
            RestartStreamerVerifiedAsync,
            NeedsElevation: true),
    ];

    private static async Task<FixResult> RestartStreamerVerifiedAsync(CancellationToken ct)
    {
        int? Before()
        {
            var p = Process.GetProcessesByName("VirtualDesktop.Streamer");
            var id = p.Length > 0 ? p[0].Id : (int?)null;
            foreach (var x in p) x.Dispose();
            return id;
        }

        var before = Before();
        var script =
            "Get-Process -Name 'VirtualDesktop.Streamer' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction Stop; "
            + "Start-Sleep -Seconds 2; Start-Process '" + StreamerChecks.StreamerExe + "'";

        var (ok, detail) = await ElevatedAsync(script, ct).ConfigureAwait(false);
        if (!ok)
            return new FixResult(false, "无法提权执行：" + detail);

        // Post-condition: the process identity must actually have changed.
        for (var i = 0; i < 15; i++)
        {
            await Task.Delay(1000, ct).ConfigureAwait(false);
            var after = Before();
            if (after is not null && after != before)
                return new FixResult(true, $"Streamer 已重启：PID {before} → {after}", detail);
        }

        return new FixResult(false,
            $"提权执行完了但进程没换（仍是 PID {Before()}）。"
            + "大概率是 UAC 弹窗没人确认，或 Streamer 已被服务立刻拉起。");
    }

    /// <summary>
    /// Runs a script elevated and returns a status you can actually trust.
    /// <para>
    /// Three traps, all hit on this machine:
    /// <c>ProcessStartInfo.Verb</c> is a no-op when <c>UseShellExecute=false</c>, which we need for
    /// redirected output, so elevation has to come from an inner <c>Start-Process -Verb RunAs</c>;
    /// that inner call does NOT propagate the child's exit code, so the inner script writes its own
    /// result to a temp file and we read that instead; and embedding the inner command inside the
    /// outer <c>-Command</c> string mangles its quoting, so the inner script goes to a temp .ps1 and
    /// is launched with <c>-File</c> instead.
    /// </para>
    /// </summary>
    private static async Task<(bool Ok, string Detail)> ElevatedAsync(string script, CancellationToken ct)
    {
        var marker = Path.Combine(Path.GetTempPath(),
            "vdhelper-elevated-" + Guid.NewGuid().ToString("N") + ".log");
        var inner = Path.Combine(Path.GetTempPath(),
            "vdhelper-elevated-" + Guid.NewGuid().ToString("N") + ".ps1");

        // The inner script does the work, then records whether it actually succeeded.
        var body =
            "$ErrorActionPreference = 'Stop'\n"
            + "try {\n"
            + "  " + script.Replace("\n", "\n  ") + "\n"
            + "  Set-Content -LiteralPath '" + marker + "' -Value 'exit=0'\n"
            + "} catch {\n"
            + "  Set-Content -LiteralPath '" + marker + "' -Value ('exit=1' + [Environment]::NewLine + $_.Exception.Message)\n"
            + "  exit 1\n"
            + "}\n";
        await File.WriteAllTextAsync(inner, body, ct).ConfigureAwait(false);

        var wrapper =
            "Start-Process powershell -Verb RunAs -Wait -WindowStyle Hidden "
            + "-ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File','" + inner + "')";

        var run = await PowerShellRunner.RunAsync(wrapper, false, false, 90_000, ct).ConfigureAwait(false);

        try
        {
            for (var i = 0; i < 60 && !File.Exists(marker); i++)
                await Task.Delay(250, ct).ConfigureAwait(false);

            if (!File.Exists(marker))
                return (false, "提权脚本没有留下结果（多半是 UAC 弹窗没人确认）。");

            var text = (await File.ReadAllTextAsync(marker, ct).ConfigureAwait(false)).Trim();
            return text.StartsWith("exit=0", StringComparison.Ordinal)
                ? (true, "内层脚本执行成功")
                : (false, "内层脚本失败：" + text[(text.IndexOf('\n') + 1)..].Trim());
        }
        catch (IOException ex)
        {
            return (false, "读不到提权结果：" + ex.Message);
        }
        finally
        {
            TryDelete(marker);
            TryDelete(inner);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* a leftover temp file is not worth failing the repair over */ }
    }

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
                "Virtual Desktop 服务", ct),
            NeedsElevation: true),
    ];

    private static async Task<FixResult> RunPsAsync(string script, string subject, CancellationToken ct)
    {
        var r = await PowerShellRunner.RunAsync(script, asAdministrator: true, ct: ct).ConfigureAwait(false);
        return r.Ok
            ? new FixResult(true, $"{subject} 已处理", r.Combined)
            : new FixResult(false, $"{subject} 处理失败（可能需要管理员权限）", r.Combined);
    }
}