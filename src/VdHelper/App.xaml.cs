using System.Text.Json;
using VdHelper.Core.Config;
using VdHelper.Core.Model;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Windows;
using VdHelper.Core.Health;

namespace VdHelper;

public partial class App : Application
{
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);

    /// <summary>
    /// Claims a console for a WinExe and pins the output encoding to UTF-8.
    /// <para>
    /// Order matters and is not stylistic. Writing to a redirected pipe before
    /// <c>AttachConsole</c> deadlocks the process. And leaving the encoding unset lets the console
    /// pick the OEM code page, which mangled the CJK check summaries once output was redirected —
    /// visibly, some lines came out as Latin-1 garbage while others were fine.
    /// </para>
    /// </summary>
    private static void ClaimConsole()
    {
        AttachConsole(-1);
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (IOException) { /* no console attached; the caller is writing to a file anyway */ }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args;

        // Report flags are matched BEFORE --selftest. Both run the same engine, but --selftest used
        // to win and silently swallow the output file: `--selftest --report issue.md` printed to
        // stdout and wrote nothing, with no warning. Someone pasting that into a support thread
        // would have attached an empty report. Naming a file is the more specific request, so it
        // takes precedence.
        // --report-html is checked before --report: both are prefixes of the same idea and the
        // HTML one is the rarer path, so it must not fall through to the Markdown writer.
        if (args.Contains("--report-html") || args.Contains("--report"))
        {
            ClaimConsole();
            var html = args.Contains("--report-html");
            Shutdown(await ReportExport.RunAsync(args, html));
            return;
        }

        if (args.Contains("--selftest"))
        {
            ClaimConsole();
            var exit = await SelfTest.RunAsync(args);
            Shutdown(exit);
            return;
        }

        if (args.Contains("--apply"))
        {
            ClaimConsole(); // same trick as --selftest
            Shutdown(await ApplyFix.RunAsync(args));
            return;
        }

        if (args.Contains("--set-param"))
        {
            ClaimConsole();
            Shutdown(await SetParam.RunAsync(args));
            return;
        }

        if (args.Contains("--deep-ui"))
        {
            // Exercises the exact command object the button is bound to, without depending on
            // synthetic mouse input (which does not reach the window in this environment).
            ClaimConsole();
            var vm = new Views.HealthViewModel();
            vm.DeepProbeCommand!.Failed += ex =>
                Console.Error.WriteLine("深度探测失败: " + ex);
            vm.DeepProbeCommand.Execute(null);
            var task = vm.DeepProbeCommand.ExecutionTask;
            if (task is not null) await task;
            Console.WriteLine(vm.DeepProbeText);
            Shutdown(0);
            return;
        }

        // --adb runs the headset probe from the command line. It exists so the headset side is
        // testable and pasteable without the GUI, and so a support thread can start from one
        // command rather than a screenshot.
        if (args.Contains("--adb"))
        {
            ClaimConsole();
            Shutdown(await AdbProbe.RunAsync(args));
            return;
        }

        if (args.Contains("--deep"))
        {
            ClaimConsole();
            Shutdown(await LossProbe.RunDeepAsync(args));
            return;
        }

        var tabIndex = 0;
        var i = Array.IndexOf(args, "--tab");
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed)) tabIndex = parsed;
        var window = new Views.ShellWindow(tabIndex);
        var si2 = Array.IndexOf(args, "--symptom");
        if (si2 >= 0 && si2 + 1 < args.Length)
        {
            var cls = Core.Model.SymptomCatalog.Find(args[si2 + 1]);
            if (cls is not null)
                Views.HealthViewModel.Current.Selected =
                    Views.HealthViewModel.Current.Symptoms.First(t => t.Class == cls);
        }
        MainWindow = window;
        window.Show();
    }
}

public static class SelfTest
{
    /// <summary>Exit codes: 0 streamable, 3 at risk, 4 blocked, 5 run failure.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        var sb = new StringBuilder();
        HealthReport? report = null;
        try
        {
            report = await HealthEngine.RunAsync(CancellationToken.None);
            var changes = HealthHistory.Save(report);

            // --symptom narrows the report to one user-reported failure mode. It is also the
            // natural support artefact: "run this and send me the output".
            var symptomId = string.Empty;
            var si = Array.IndexOf(args, "--symptom");
            if (si >= 0 && si + 1 < args.Length) symptomId = args[si + 1];
            var focus = SymptomCatalog.Find(symptomId);
            if (si >= 0 && si + 1 < args.Length && focus is null)
            {
                Console.WriteLine($"未知症状类 {symptomId}，可选：" +
                    string.Join("/", SymptomCatalog.All.Select(s => s.Id)));
                return 2;
            }

            if (focus is not null)
                sb.AppendLine($"症状类 {focus.Id}「{focus.Title}」：{focus.PhraseLine}\n重点：{focus.FirstLook}\n");

            IReadOnlyList<CheckResult> shown = focus is null
                ? report.Results.ToList()
                : report.Results.Where(r => focus.RelevantChecks.Contains(r.Id)).ToList();
            var hidden = report.Results.Count - shown.Count;

            sb.AppendLine($"VDHelper selftest  verdict={report.Verdict}  {report.VerdictText}"
                + (focus is null ? "" : $"  (症状类 {focus.Id}，已隐藏 {hidden} 项无关检测)"));

            // Name what did not run. The HTML report already does this; the console output is the one
            // people paste into issues, and a silently shorter list reads as full coverage.
            var pending = (focus?.RelevantChecks ?? SymptomCatalog.All.SelectMany(s => s.RelevantChecks))
                .Distinct()
                .Where(id => report.Results.All(r => r.Id != id))
                .ToList();
            if (pending.Count > 0)
                sb.AppendLine($"本轮未跑的检测（{pending.Count} 项，需要连上头显）：{string.Join("、", pending)}");
            // Lead with what to do, not with 34 rows. A verdict on its own is not actionable, and
            // the first thing people do with a long list is close the window.
            var actions = report.NextActions;
            if (actions.Count > 0)
            {
                sb.AppendLine("");
                var byKind = actions.GroupBy(a => a.Kind).ToDictionary(g => g.Key, g => g.Count());
                var mix = new List<string>();
                if (byKind.TryGetValue(NextActionKind.FixThisFirst, out var nFix)) mix.Add($"{nFix} 条先修");
                if (byKind.TryGetValue(NextActionKind.ThenThis, out var nThen)) mix.Add($"{nThen} 条再修");
                if (byKind.TryGetValue(NextActionKind.WorthKnowing, out var nKnow)) mix.Add($"{nKnow} 条值得知道");
                sb.AppendLine("接下来做什么：" + string.Join(" · ", mix));
                foreach (var a in actions)
                {
                    var tag = a.Kind switch
                    {
                        NextActionKind.FixThisFirst => "先修",
                        NextActionKind.ThenThis => "再修",
                        _ => "知道",
                    };
                    sb.AppendLine($"  [{tag}] {a.Title} — {a.What}" + (a.Caveat.Length > 0 ? $"  （{a.Caveat}）" : ""));
                }
            }
            foreach (var r in shown)
            {
                sb.AppendLine($"[{r.Status,-7}] {r.Id,-13} {r.Summary}");
                // The "why" belongs in the console too, not only in the UI: the actionable part
                // ("ERROR_DISABLED，右键启用即可") lives here and the CLI is what gets pasted around.
                if (!string.IsNullOrWhiteSpace(r.Detail))
                    sb.AppendLine($"            {r.Detail}");
                foreach (var (k, v) in r.Evidence)
                    sb.AppendLine($"            {k}: {v}");
                foreach (var f in r.Fixes)
                    sb.AppendLine($"            FIX[{f.Risk}] {f.Title} — {f.What} | rollback: {f.Rollback}");
                if (r.Guidance is not null)
                    sb.AppendLine($"            GUIDE: {r.Guidance}");
            }
            if (changes.Count > 0)
            {
                sb.AppendLine($"与上次相比有 {changes.Count} 处变化：");
                foreach (var c in changes)
                    sb.AppendLine($"  [{c.Id}] {c.Before}  ->  {c.After}");
            }
            else
            {
                sb.AppendLine("与上次相比没有变化。");
            }

            var timeline = HealthHistory.Timeline();
            if (timeline.Count > 1)
            {
                sb.AppendLine($"最近 {timeline.Count} 次体检（{timeline[0].At:MM-dd HH:mm} 起）：");
                foreach (var s in timeline)
                    sb.AppendLine($"  {s.At:MM-dd HH:mm}  {s.Verdict,-10} {s.Headline}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("selftest failed: " + ex);
        }

        var text = sb.ToString();
        Console.Write(text);
        var outIndex = Array.IndexOf(args, "--out");
        if (outIndex >= 0 && outIndex + 1 < args.Length)
            await File.WriteAllTextAsync(args[outIndex + 1], text);

        // Derived from the verdict itself, not from string-matching the report we just printed.
        // The old form meant the tool could exit 5 ("the run failed") while its own headline said
        // 本机网络体检通过 — two contradictory answers from one run — and any check whose text
        // happened to contain "verdict=Streamable" could have flipped the exit code.
        return report?.Verdict switch
        {
            HealthVerdict.Streamable => 0,
            HealthVerdict.AtRisk => 3,
            HealthVerdict.Blocked => 4,
            _ => 5,
        };
    }
}

/// <summary>Headless remediation: <c>--apply &lt;fixId&gt;</c> runs one repair and reports it.</summary>
public static class ApplyFix
{
    public static async Task<int> RunAsync(string[] args)
    {
        var index = Array.IndexOf(args, "--apply");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("usage: VdHelper.exe --apply <fixId> [--list]");
            return 2;
        }

        var report = await HealthEngine.RunAsync(CancellationToken.None);
        var wanted = args[index + 1];
        var wantedList = wanted == "--list";
        var matched = 0;

        foreach (var result in report.Results)
        foreach (var fix in result.Fixes)
        {
            if (!wantedList && !fix.Id.Equals(wanted, StringComparison.OrdinalIgnoreCase)) continue;
            matched++;
            if (wantedList)
            {
                Console.WriteLine($"{fix.Id}\t[{fix.Risk}]\t{result.Id}\t{fix.Title}");
                continue;
            }

            Console.WriteLine($"执行 {fix.Id} — {fix.Title}");
            Console.WriteLine($"  命令：{fix.What}");
            Console.WriteLine($"  备份：{fix.Backup}");
            Console.WriteLine($"  回滚：{fix.Rollback}");
            var outcome = await fix.Apply(CancellationToken.None);
            Console.WriteLine(outcome.Success
                ? $"  结果：成功 — {outcome.Message}"
                : $"  结果：失败 — {outcome.Message}");
            if (!outcome.Success) return 6;
        }

        // A typo'd fix id used to fall through the loop, print the verdict and exit 0 — the tool
        // reporting success for doing nothing. A fix that is offered only when its condition is
        // present legitimately matches nothing, so say which ones exist and return a distinct code.
        if (!wantedList && matched == 0)
        {
            var available = report.Results.SelectMany(r => r.Fixes).Select(f => f.Id).ToList();
            Console.WriteLine($"没有匹配的修复项：{wanted}");
            Console.WriteLine(available.Count == 0
                ? "当前这轮体检没有提供任何可自动修复的项（用 --apply --list 查看条件）。"
                : $"本轮可用的修复项：{string.Join("、", available)}");
            return 9;
        }

        if (!wantedList)
            Console.WriteLine(report.VerdictText);
        return 0;
    }
}
/// <summary>
/// Headless parameter write: <c>--set-param &lt;key&gt; &lt;jsonValue&gt;</c>.
/// Refuses to touch the file while the Streamer is running — its 2 s debounced save would
/// overwrite us (research/04-streamer-settings), so a silent data loss is not an option.
/// </summary>
public static class SetParam
{
    public static async Task<int> RunAsync(string[] args)
    {
        var i = Array.IndexOf(args, "--set-param");
        if (i < 0 || i + 2 >= args.Length)
        {
            Console.WriteLine("usage: VdHelper.exe --set-param <key> <jsonValue>");
            return 2;
        }

        var key = args[i + 1];
        var literal = args[i + 2];

        var info = ParameterCatalog.All.FirstOrDefault(p =>
            p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (info is null)
        {
            Console.WriteLine($"未知参数 {key}（不是调研表里的 111 个键之一）");
            return 2;
        }
        if (info.ReadOnly)
        {
            Console.WriteLine($"{key} 是 DPAPI 密文键，改了会清空配对，拒绝写入。");
            return 7;
        }
        if (!info.LivesOnPc)
        {
            Console.WriteLine($"{key} 不在 PC 落盘，由头显决定，本机改不了。");
            return 7;
        }

        var streamer = System.Diagnostics.Process.GetProcessesByName("VirtualDesktop.Streamer");
        var running = streamer.Length > 0;
        foreach (var p in streamer) p.Dispose();
        if (running)
        {
            Console.WriteLine("Streamer 正在运行：它有 2 秒防抖保存，会覆盖外部写入。先退出 Streamer 再试。");
            return 8;
        }

        try
        {
            var value = JsonDocument.Parse(literal).RootElement.Clone();
            var result = Core.Config.StreamerConfigWriter.Write(
                Core.Config.StreamerSettings.DefaultPath, key, value);
            if (!result.Success)
            {
                // It used to print 已写入 / 备份 / 回滚 first and only then look at Success, so a
                // failed write announced itself as a successful one and the user was left believing
                // the file had changed.
                Console.WriteLine($"写入失败：{result.Message}");
                return 6;
            }

            Console.WriteLine($"备份：{result.BackupPath}");
            Console.WriteLine($"回滚：把 {result.BackupPath} 复制回 {Core.Config.StreamerSettings.DefaultPath}");

            // Post-condition: re-read and compare, rather than trusting the writer's return.
            var after = Core.Config.StreamerSettings.Load();
            var expected = value.ToString();
            var actual = after.GetRaw(key);
            var matches = string.Equals(actual?.Trim('"'), expected.Trim('"'), StringComparison.Ordinal);
            if (!matches)
            {
                Console.WriteLine($"写入返回成功，但回读确认 {key} 是 {actual ?? "(空)"}，不是 {expected}。备份已保留，未回滚。");
                return 6;
            }

            Console.WriteLine($"已确认 {key} = {expected}（回读一致）");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("写入失败：" + ex.Message);
            return 6;
        }
    }
}


/// <summary>Headless run of the adb side: everything the third screen would do, in text.</summary>
public static class AdbProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        var i = Array.IndexOf(args, "--serial");
        var serial = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;

        var adbPath = Core.Adb.AdbLocator.Find();
        var client = adbPath is null ? null : new Core.Adb.AdbClient(adbPath);
        if (client is null)
        {
            Console.WriteLine("找不到 adb.exe（本机实测 PATH 里没有）。");
            Console.WriteLine("装 Android platform-tools，或把 adb 放进 PATH 后重试；"
                + "已知位置见 src/VdHelper/Core/Adb/AdbClient.cs 的候选表。");
            return 3;
        }

        var check = await new Core.Adb.HeadsetDeepProbe(client).ProbeAsync(serial ?? "", CancellationToken.None)
            .ConfigureAwait(false);

        Console.WriteLine($"[{check.Status}] {check.Id}  {check.Summary}");
        if (!string.IsNullOrWhiteSpace(check.Detail))
            Console.WriteLine("  " + check.Detail.Replace("\n", "\n  "));
        foreach (var (k, v) in check.Evidence)
            Console.WriteLine($"  {k}: {v}");
        if (!string.IsNullOrWhiteSpace(check.Guidance))
            Console.WriteLine("  指引：" + check.Guidance);
        foreach (var f in check.Fixes)
            Console.WriteLine($"  可修: {f.Id} — {f.Title}（风险 {f.Risk}）");

        // 3 = adb missing or no device, 4 = device reachable but something is wrong, 0 = fine.
        return check.Status == CheckStatus.Pass ? 0
            : check.Status == CheckStatus.Warn ? 3 : 4;
    }
}
