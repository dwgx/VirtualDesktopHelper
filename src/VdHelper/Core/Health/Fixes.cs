﻿﻿using System.IO;
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
                ct => RunPsAsync($"Disable-NetAdapter -Name {Ps.Literal(name)} -Confirm:$false", name, ct),
                NeedsElevation: true));
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

    public static IReadOnlyList<FixAction> RestoreVdRule()
    {
        // A firewall rule scoped to a program path only works if that path is where the program
        // actually is. Build it from the resolved exe rather than the default-install constant.
        var exe = StreamerChecks.ResolveStreamerExe();
        var add = "netsh advfirewall firewall add rule name=\"Virtual Desktop Streamer\" dir=in "
            + "action=allow program=\"" + exe + "\" enable=yes profile=any";
        return
    [
        new FixAction(
            "fw-restore-vd",
            "重建 Virtual Desktop 入站放行规则",
            add,
            "不动已有规则，也不自动导出。回滚会删掉**所有**同名规则，包括你自己早先建的那条。",
            @"netsh advfirewall firewall delete rule name=""Virtual Desktop Streamer""",
            FixRisk.Medium,
            // netsh advfirewall refuses without elevation — verified on this machine, twice, both
            // returning "The requested operation requires elevation" and exit 1. PowerShellRunner
            // deliberately never elevates, so this fix used to promise a UAC prompt in the UI and
            // then fail on permissions. ElevatedAsync is the same helper streamer-restart uses.
            async ct =>
            {
                var (ok, detail) = await ElevatedAsync(add, ct).ConfigureAwait(false);
                return ok
                    ? new FixResult(true, "已重建入站放行规则", detail)
                    : new FixResult(false, "无法提权执行：" + detail, detail);
            },
            NeedsElevation: true, SelfElevates: true),
    ];
    }

    public static IReadOnlyList<FixAction> StartVdService() =>
    [
        new FixAction(
            "svc-start",
            "启动 VirtualDesktop 服务",
            "Start-Service -Name 'VirtualDesktop.Service.exe'",
            "不改启动类型；只在服务已停止时把它启动起来。",
            "Stop-Service -Name 'VirtualDesktop.Service.exe'",
            FixRisk.Low,
            async ct =>
            {
                // The service is named VirtualDesktop.Service.exe, with the suffix. -Name is an exact
                // match, so the old name never resolved: Start-Service threw NoServiceFoundForGivenName
                // every time and the read-back below never ran once. Verified on this machine —
                // Get-Service -Name 'VirtualDesktop.Service' returns nothing,
                // Get-Service -Name 'VirtualDesktop.Service.exe' returns it, Running.
                var started = await RunPsAsync("Start-Service -Name 'VirtualDesktop.Service.exe'", "VirtualDesktop.Service.exe", ct)
                    .ConfigureAwait(false);
                if (!started.Success)
                    return new FixResult(false, "启动命令未成功：" + started.Message);
                var state = await PowerShellRunner
                    .RunAsync("(Get-Service -Name 'VirtualDesktop.Service.exe' -ErrorAction SilentlyContinue).Status", ct: ct)
                    .ConfigureAwait(false);
                return state.StdOut.Contains("Running", StringComparison.OrdinalIgnoreCase)
                    ? new FixResult(true, "服务已处于 Running", state.StdOut.Trim())
                    : new FixResult(false, "命令返回 0，但复查状态不是 Running：" + state.StdOut.Trim());
            },
            NeedsElevation: true),
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
            NeedsElevation: true, SelfElevates: true),
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
            + "Start-Sleep -Seconds 2; Start-Process '" + StreamerChecks.ResolveStreamerExe() + "'";

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
    /// Quits the Streamer and confirms it is actually gone. Not elevation: the Streamer runs as the
    /// same user, so a plain Stop-Process is enough, and making the user click through UAC just to
    /// change one setting is the wrong trade. Falls back to telling them how when access is denied.
    /// </summary>
    public static async Task<FixResult> QuitStreamerVerifiedAsync(CancellationToken ct)
    {
        int? Current()
        {
            var p = Process.GetProcessesByName("VirtualDesktop.Streamer");
            var id = p.Length > 0 ? p[0].Id : (int?)null;
            foreach (var x in p) x.Dispose();
            return id;
        }

        var before = Current();
        if (before is null)
            return new FixResult(true, "Streamer 本来就没在运行，参数可以直接改。");

        // Ask for a graceful close first. Force is the fallback, not the opening move: the Streamer
        // holds session sockets, and a clean exit is what lets it release them.
        const string stop =
            "Get-Process -Name 'VirtualDesktop.Streamer' -ErrorAction SilentlyContinue "
            + "| ForEach-Object { $_.CloseMainWindow() | Out-Null }; Start-Sleep -Seconds 2; "
            + "Get-Process -Name 'VirtualDesktop.Streamer' -ErrorAction SilentlyContinue "
            + "| Stop-Process -Force";

        async Task<bool> Gone()
        {
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
                if (Current() is null) return true;
            }
            return false;
        }

        // First try without elevation. On this machine that is refused with "Access is denied"
        // because the Streamer runs elevated under its service — so the escalation below is not a
        // fallback for theory, it is the path that actually has to work.
        var plain = await PowerShellRunner.RunAsync(stop, ct: ct).ConfigureAwait(false);
        if (await Gone().ConfigureAwait(false))
            return new FixResult(true, $"Streamer 已退出（原 PID {before}）。现在可以改参数了。", plain.Combined);

        // The caller is responsible for warning about the UAC prompt before calling in here.
        var (elevatedOk, detail) = await ElevatedAsync(stop, ct).ConfigureAwait(false);
        if (!elevatedOk)
            return new FixResult(false, "提权执行失败：" + detail, plain.Combined);
        if (await Gone().ConfigureAwait(false))
            return new FixResult(true,
                $"Streamer 已退出（原 PID {before}，普通权限结束不了，改用管理员）。现在可以改参数了。",
                plain.Combined);

        return new FixResult(false,
            $"没能结束 Streamer（仍是 PID {Current()}）。它多半由 VirtualDesktop.Service 看护着立刻拉起："
            + $"先用管理员身份结束 VirtualDesktop.Service，再结束 Streamer，或 taskkill /PID {before} /F。",
            detail);
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

        var run = await PowerShellRunner.RunAsync(wrapper, false, 90_000, ct).ConfigureAwait(false);

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
            $"Start-Process '{StreamerChecks.ResolveStreamerExe()}'",
            "只启动进程，不改任何配置。",
            "Stop-Process -Name 'VirtualDesktop.Streamer' -Force",
            FixRisk.Low,
            async ct =>
            {
                int? Before() => Process.GetProcessesByName("VirtualDesktop.Streamer")
                    .Select(p => p.Id).OrderBy(x => x).FirstOrDefault() is var id && id > 0 ? id : null;
                var before = Before();
                var launched = await RunPsAsync(
                    $"Start-Process '{StreamerChecks.ResolveStreamerExe()}'",
                    "Virtual Desktop Streamer", ct).ConfigureAwait(false);
                if (!launched.Success)
                    return new FixResult(false, "启动命令未成功：" + launched.Message, launched.Message);

                for (var i = 0; i < 12; i++)
                {
                    await Task.Delay(500, ct).ConfigureAwait(false);
                    var after = Before();
                    if (after is not null)
                        return new FixResult(true,
                            before is null
                                ? $"Streamer 已启动：PID {after}"
                                : $"Streamer 在运行（PID {after}，启动前已经是 {before}）",
                            launched.Message);
                }

                return new FixResult(false,
                    "命令返回成功，但 6 秒内没有看到 VirtualDesktop.Streamer 进程。"
                    + "**进程被创建不等于它活着**——这正是本工具存在的那类故障"
                    + "（服务在跑 ≠ Streamer 起得来）。看 svc-log 与账号，必要时看服务日志。",
                    launched.Message);
            }),
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
            "msiexec /i \"<Streamer 所在目录>\\VirtualDesktop.Service.msi\" /qn，然后重启服务。"
            + "安装包按 Streamer 可执行文件的实际位置推导，不写死默认安装目录；找不到时会直接告诉你，"
            + "不会拿一个不存在的路径去跑 msiexec。",
            "先导出当前服务配置：`sc.exe qc VirtualDesktop.Service.exe > %TEMP%\\vdservice-before.txt`；安装包不删除用户数据。",
            "msiexec /i \"...\\VirtualDesktop.Service.msi\" /qn（重装即恢复默认账户绑定）",
            FixRisk.Medium,
            ct => ReinstallServiceAsync(ct),
            NeedsElevation: true),
    ];


    /// <summary>
    /// Reinstalls the VD service from the MSI that sits beside the Streamer executable. The path is
    /// derived rather than assumed: pointing msiexec at a file that is not there fails with a message
    /// that says nothing useful, so say plainly that the installer was not found.
    /// </summary>
    private static async Task<FixResult> ReinstallServiceAsync(CancellationToken ct)
    {
        var msi = Path.Combine(Path.GetDirectoryName(StreamerChecks.ResolveStreamerExe()) ?? "",
                               "VirtualDesktop.Service.msi");
        if (!File.Exists(msi))
            return new FixResult(false,
                "在 Streamer 所在目录下没找到 VirtualDesktop.Service.msi（找的是：" + msi + "）。"
                + "这多半是便携安装或安装目录不完整，请到 Virtual Desktop 官网重新下载安装包，"
                + "或先用「安装包」手动装一次服务。");

        var script = "Start-Process msiexec -ArgumentList '/i \"" + msi + "\" /qn' -Verb RunAs -Wait";
        var r = await PowerShellRunner.RunAsync(script, ct: ct).ConfigureAwait(false);
        return r.Ok
            ? new FixResult(true, "Virtual Desktop 服务已处理", r.Combined)
            : new FixResult(false, "重装失败：" + r.Combined);
    }
    private static async Task<FixResult> RunPsAsync(string script, string subject, CancellationToken ct)
    {
        var r = await PowerShellRunner.RunAsync(script, ct: ct).ConfigureAwait(false);
        return r.Ok
            ? new FixResult(true, $"{subject} 已处理", r.Combined)
            : new FixResult(false, $"{subject} 处理失败（可能需要管理员权限）", r.Combined);
    }
}