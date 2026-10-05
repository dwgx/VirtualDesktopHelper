using VdHelper.Core.Health;
using System.IO;
using VdHelper.Core.Model;
using VdHelper.Reports;

namespace VdHelper;

/// <summary>
/// Headless report export: <c>--report &lt;file&gt;</c> writes Markdown, <c>--report-html &lt;file&gt;</c>
/// writes a single-file HTML page. Both accept <c>--symptom Sx</c> to narrow the report to one
/// class of user-reported failure, which is the form that is actually worth pasting into a thread:
/// "PC unreachable" and "freezes every 5 minutes" share nothing, and a stranger reading the report
/// needs to see which of the two the sender hit.
/// <para>
/// Exit codes match <see cref="SelfTest"/> (0 streamable, 3 at risk, 4 blocked, 5 run failure) so a
/// CI job can gate on the verdict and a user gets the same number from both commands. Exit 2 means
/// the arguments were wrong, which is a different thing from a machine being unhealthy.
/// </para>
/// </summary>
public static class ReportExport
{
    public static async Task<int> RunAsync(string[] args, bool html)
    {
        var flag = html ? "--report-html" : "--report";
        var index = Array.IndexOf(args, flag);
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine($"usage: VdHelper.exe {flag} <输出文件> [--symptom S1..S7]");
            return 2;
        }
        var path = args[index + 1];

        SymptomClass? focus = null;
        var si = Array.IndexOf(args, "--symptom");
        if (si >= 0 && si + 1 < args.Length)
        {
            focus = SymptomCatalog.Find(args[si + 1]);
            if (focus is null)
            {
                Console.WriteLine($"未知症状类 {args[si + 1]}，可选："
                    + string.Join("/", SymptomCatalog.All.Select(s => s.Id)));
                return 2;
            }
        }

        var report = await HealthEngine.RunAsync(CancellationToken.None);
        // The report claims "no change since last run", so it has to be compared against the
        // previous run — which means saving this one first, exactly as --selftest does.
        var changes = HealthHistory.Save(report);

        var text = html
            ? ReportWriter.Html(report, changes, focus)
            : ReportWriter.Markdown(report, changes, focus);

        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // Explicit UTF-8 without BOM: the HTML declares <meta charset="utf-8">, and a BOM in front
        // of it renders as a stray character on some forum markdown renderers.
        await File.WriteAllTextAsync(full, text, new System.Text.UTF8Encoding(false));

        Console.WriteLine($"已写出 {(html ? "HTML" : "Markdown")} 报告：{full}（{text.Length} 字符）");
        Console.WriteLine($"结论 verdict={report.Verdict}  {report.VerdictText}");

        return report.Verdict switch
        {
            HealthVerdict.Streamable => 0,
            HealthVerdict.AtRisk => 3,
            HealthVerdict.Blocked => 4,
            _ => 5,
        };
    }
}
