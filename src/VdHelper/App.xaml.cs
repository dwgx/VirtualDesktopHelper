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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args;
        if (args.Contains("--selftest"))
        {
            AttachConsole(-1); // WinExe has no console of its own; borrow the shell's
            var exit = await SelfTest.RunAsync(args);
            Shutdown(exit);
            return;
        }

        if (args.Contains("--apply"))
        {
            AttachConsole(-1); // same trick as --selftest
            Shutdown(await ApplyFix.RunAsync(args));
            return;
        }

        if (args.Contains("--set-param"))
        {
            AttachConsole(-1);
            Shutdown(await SetParam.RunAsync(args));
            return;
        }

        if (args.Contains("--deep-ui"))
        {
            // Exercises the exact command object the button is bound to, without depending on
            // synthetic mouse input (which does not reach the window in this environment).
            // AttachConsole first: any write to a redirected pipe before this point deadlocks.
            AttachConsole(-1);
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

        if (args.Contains("--deep"))
        {
            AttachConsole(-1);
            Shutdown(await LossProbe.RunDeepAsync(args));
            return;
        }

        // --report-html is checked before --report: both are prefixes of the same idea and the
        // HTML one is the rarer path, so it must not fall through to the Markdown writer.
        if (args.Contains("--report-html") || args.Contains("--report"))
        {
            AttachConsole(-1);
            var html = args.Contains("--report-html");
            Shutdown(await ReportExport.RunAsync(args, html));
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
        try
        {
            var report = await HealthEngine.RunAsync(CancellationToken.None);
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

        return text.Contains("verdict=Streamable") ? 0
            : text.Contains("verdict=AtRisk") ? 3
            : text.Contains("verdict=Blocked") ? 4
            : 5;
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

        foreach (var result in report.Results)
        foreach (var fix in result.Fixes)
        {
            if (!wantedList && !fix.Id.Equals(wanted, StringComparison.OrdinalIgnoreCase)) continue;
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
            Console.WriteLine($"已写入 {key} = {literal}");
            Console.WriteLine($"备份：{result.BackupPath}");
            Console.WriteLine($"回滚：把 {result.BackupPath} 复制回 {Core.Config.StreamerSettings.DefaultPath}");
            return result.Success ? 0 : 6;
        }
        catch (Exception ex)
        {
            Console.WriteLine("写入失败：" + ex.Message);
            return 6;
        }
    }
}
