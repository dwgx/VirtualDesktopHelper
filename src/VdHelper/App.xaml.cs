﻿﻿﻿﻿﻿using System.Text.Json;
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

        // --quit-streamer exists because every write path refuses while the Streamer runs, and the
        // refusal tells the user to quit it — without giving them a way to do that from here. It is
        // not a repair for something broken: nothing is wrong, the settings file is simply locked by
        // a 2-second debounced save.
        if (args.Contains("--quit-streamer"))
        {
            ClaimConsole();
            Console.WriteLine("将结束 Virtual Desktop Streamer。当前若有串流会话，会被断开。");
            Console.WriteLine("本机的 Streamer 以管理员权限运行，所以中途会弹一次 UAC，请点「是」。");
            var qr = await Core.Health.Fixes.QuitStreamerVerifiedAsync(CancellationToken.None);
            Console.WriteLine((qr.Success ? "OK   " : "FAIL ") + qr.Message);
            if (!qr.Success && !string.IsNullOrWhiteSpace(qr.RollbackHint)) Console.WriteLine(qr.RollbackHint);
            Shutdown(qr.Success ? 0 : 6);
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
            // Validate --symptom before probing anything. This used to sit after the health run,
            // so a typo cost a full sweep of the machine: 6323 ms against 193 ms for the same typo
            // on --report, and 6818 ms for a real run. The argument is known before the first
            // probe starts; reading it afterwards is what made the two paths disagree.
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

            report = await HealthEngine.RunAsync(CancellationToken.None);
            var changes = HealthHistory.Save(report);

            // --symptom narrows the report to one user-reported failure mode. It is also the
            // natural support artefact: "run this and send me the output".

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
                if (byKind.TryGetValue(NextActionKind.ReadThisFirst, out var nRead)) mix.Add($"{nRead} 条先看");
                if (byKind.TryGetValue(NextActionKind.ThenThis, out var nThen)) mix.Add($"{nThen} 条再修");
                if (byKind.TryGetValue(NextActionKind.WorthKnowing, out var nKnow)) mix.Add($"{nKnow} 条值得知道");
                sb.AppendLine("接下来做什么：" + string.Join(" · ", mix));
                foreach (var a in actions)
                {
                    var tag = a.Kind switch
                    {
                        NextActionKind.FixThisFirst => "先修",
                        NextActionKind.ReadThisFirst => "先看",
                        NextActionKind.ThenThis => "再修",
                        _ => "知道",
                    };
                    sb.AppendLine($"  [{tag}] {a.Title} — {a.What}" + (a.Caveat.Length > 0 ? $"  （{a.Caveat}）" : ""));
                }
            }
            foreach (var r in shown)
            {
                // Everything a user can paste goes through the same Redact the reports use. This was
                // the only surface printing evidence verbatim, so a ciphertext reaching a check's
                // evidence would have been stripped from markdown and HTML and printed by --selftest.
                // The status word is the one thing that has to read the same as the reports, so the
                // console shows 通过/警告/阻断/未知 rather than Pass/Warn — pasting this into an issue
                // next to an HTML report used to leave the reader with two vocabularies.
                sb.AppendLine($"[{Reports.ReportWriter.Badge(r.Status),-4}] {r.Id,-13} {Reports.ReportWriter.Redact(r.Summary)}");
                // The "why" belongs in the console too, not only in the UI: the actionable part
                // ("ERROR_DISABLED，右键启用即可") lives here and the CLI is what gets pasted around.
                if (!string.IsNullOrWhiteSpace(r.Detail))
                    sb.AppendLine($"            {Reports.ReportWriter.Redact(r.Detail)}");
                foreach (var (k, v) in r.Evidence)
                    sb.AppendLine($"            {k}: {Reports.ReportWriter.Redact(v)}");
                foreach (var f in r.Fixes)
                    sb.AppendLine($"            FIX[{f.Risk}] {f.Title} — {Reports.ReportWriter.Redact(f.What)} | rollback: {Reports.ReportWriter.Redact(f.Rollback)}");
                if (r.Guidance is not null)
                    sb.AppendLine($"            GUIDE: {Reports.ReportWriter.Redact(r.Guidance)}");
            }
            if (changes.Count > 0)
            {
                sb.AppendLine($"与上次相比有 {changes.Count} 处变化：");
                foreach (var c in changes)
                    sb.AppendLine($"  [{c.Id}]" + StatusMove(c) + $" {c.Before}  ->  {c.After}");
            }
            else
            {
                sb.AppendLine("与上次相比没有变化。");
            }

            var timeline = HealthHistory.Timeline();
            if (timeline.Count > 1)
            {
                sb.AppendLine($"最近 {timeline.Count} 次体检（{timeline[0].At:MM-dd HH:mm} 起）：");
                for (var i = 0; i < timeline.Count; i++)
                {
                    var s = timeline[i];
                    if (i > 0 && s.Logic != timeline[i - 1].Logic)
                        sb.AppendLine($"  ── 判定规则由 v{timeline[i - 1].Logic} 变为 v{s.Logic}："
                                    + "这一段之间的结论不可直接比较 ──");
                    sb.AppendLine($"  {s.At:MM-dd HH:mm}  {s.Verdict,-10} {s.Headline}");
                }
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

    private static string StatusMove(HealthHistory.Change c) => !c.StatusMoved ? "" : "  (" + Cn(c.FromStatus) + " → " + Cn(c.ToStatus) + ")";

    private static string Cn(string? status) => status switch
    {
        "Pass" => "通过",
        "Warn" => "警告",
        "Block" => "阻断",
        "Unknown" => "未知",
        _ => status ?? "?",
    };
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
        var matches = report.Results
            .SelectMany(r => r.Fixes.Select(f => (Fix: f, Check: r)))
            .Where(x => wantedList || x.Fix.Id.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.Fix.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!wantedList)
            foreach (var group in matches)
            {
                var fix = group.First().Fix;
                var offered = group.Select(g => g.Check.Id).ToList();
                matched++;
                if (offered.Count > 1)
                    Console.WriteLine($"注意：{fix.Id} 被 {offered.Count} 个检查同时提供"
                        + $"（{string.Join("、", offered)}）——这是**同一个修复**，只执行一次。");

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

        if (wantedList)
            foreach (var group in matches)
            {
                var fix = group.First().Fix;
                var offered = group.Select(g => g.Check.Id).ToList();
                matched++;
                Console.WriteLine($"{fix.Id}  [{fix.Risk} 风险]"
                    + (fix.NeedsElevation ? "  会弹 UAC" : "  不需要管理员"));
                Console.WriteLine($"  做什么：{fix.Title}");
                Console.WriteLine(offered.Count == 1
                    ? $"  出自：{offered[0]} — {group.First().Check.Summary}"
                    : $"  出自：{string.Join("、", offered)}（同一个修复，{offered.Count} 个检查命中；执行一次）");
                if (!string.IsNullOrWhiteSpace(fix.What)) Console.WriteLine("  说明：" + fix.What);
                if (!string.IsNullOrWhiteSpace(fix.Backup)) Console.WriteLine("  备份：" + fix.Backup);
                if (!string.IsNullOrWhiteSpace(fix.Rollback)) Console.WriteLine("  回滚：" + fix.Rollback);
                Console.WriteLine($"  执行：VdHelper.exe --apply {fix.Id}");
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
            Console.WriteLine("（上面这句判定是修复之前算的。重跑一次 --selftest 看现在的状态。）" + report.VerdictText);
        return 0;
    }

    /// <summary>"通过 → 警告" next to the id, when the verdict moved regardless of the wording.</summary>
}

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
            // ReadOnly covers two different situations, and only one of them is DPAPI. Saying
            // "密文键" about the other is a fabricated reason for refusing.
            Console.WriteLine(info.Secret
                ? $"{key} 是 DPAPI 密文键，改了会清空配对，拒绝写入。"
                : $"{key} 是只读键：Streamer 自己不持久化它（源码上带 [JsonIgnore]），本机改不了。");
            return 7;
        }
        if (!info.LivesOnPc)
        {
            Console.WriteLine($"{key} 不在 PC 落盘，由头显决定，本机改不了。");
            return 7;
        }

        var streamer = System.Diagnostics.Process.GetProcessesByName("VirtualDesktop.Streamer");
        var running = streamer.Length > 0;
        var pids = string.Join(", ", streamer.Select(p => p.Id.ToString()));
        foreach (var p in streamer) p.Dispose();
        if (running)
        {
            // "先退出 Streamer" is only useful if it says how. The tray icon is where the official
            // client puts it; taskkill is the fallback when the window is not on screen.
            Console.WriteLine("Streamer 正在运行（PID " + pids + "）：它有 2 秒防抖保存，会覆盖外部写入。");
            Console.WriteLine("退出方式（任选其一）：");
            Console.WriteLine("  1. 任务栏托盘区右键 Virtual Desktop Streamer 图标 → 退出");
            Console.WriteLine("  2. 任务管理器结束 VirtualDesktop.Streamer.exe");
            Console.WriteLine("  3. taskkill /PID " + pids.Split(", ")[0] + " /F");
            Console.WriteLine("退出后重跑同一条命令；参数改动会写入并回读确认。");
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
            // Both sides must be raw JSON text. JsonElement.ToString() on a bool gives the .NET
            // "True"/"False" while the file holds "true"/"false", and the comparison below is
            // Ordinal — so every bool write succeeded and then reported exit 6, which README
            // defines as 写失败. Verified before the change in a standalone .NET 10 program:
            // onDisk "true" vs expected "True" -> match False.
            var expected = value.GetRawText();
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
            return 4;
        }

        // The third screen runs TWO probes — HeadsetProbe then HeadsetDeepProbe — and --adb used to
        // run only the deep one. So the command people paste into issues was silently a subset of
        // what the tool can see. Both now run, in the same order.
        var basic = await new Core.Adb.HeadsetProbe(client).RunAsync(serial, CancellationToken.None)
            .ConfigureAwait(false);
        var check = await new Core.Adb.HeadsetDeepProbe(client)
            .ProbeAsync(basic.Evidence.TryGetValue("serial", out var s0) ? s0 : (serial ?? ""), CancellationToken.None)
            .ConfigureAwait(false);

        foreach (var r in new[] { basic, check })
        {
            Console.WriteLine($"[{r.Status}] {r.Id}  {r.Summary}");
            if (!string.IsNullOrWhiteSpace(r.Detail))
                Console.WriteLine("  " + r.Detail.Replace("\n", "\n  "));
            foreach (var (k, v) in r.Evidence)
                Console.WriteLine($"  {k}: {v}");
            if (!string.IsNullOrWhiteSpace(r.Guidance))
            {
                var g = r.Guidance.TrimStart();
                if (g.StartsWith("指引：", StringComparison.Ordinal)) g = g["指引：".Length..];
                Console.WriteLine("  指引：" + g);
            }
            Console.WriteLine();
        }

        // 0 = both probes clean, 3 = something to look at, 4 = not connected or unusable. Judge on
        // the worse of the two now that both run, so a clean deep probe cannot mask a warned
        // basic one.
        var worst = (Status: basic.Status, Deep: check.Status);
        var rank = (CheckStatus s) => s switch
        {
            CheckStatus.Pass => 0,
            CheckStatus.Warn => 3,
            _ => 4,
        };
        return Math.Max(rank(worst.Status), rank(worst.Deep));
    }
}
