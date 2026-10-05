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

        var tabIndex = 0;
        var i = Array.IndexOf(args, "--tab");
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed)) tabIndex = parsed;
        var window = new Views.ShellWindow(tabIndex);
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
            sb.AppendLine($"VDHelper selftest  verdict={report.Verdict}  {report.VerdictText}");
            foreach (var r in report.Results)
            {
                sb.AppendLine($"[{r.Status,-7}] {r.Id,-13} {r.Summary}");
                foreach (var (k, v) in r.Evidence)
                    sb.AppendLine($"            {k}: {v}");
                foreach (var f in r.Fixes)
                    sb.AppendLine($"            FIX[{f.Risk}] {f.Title} — {f.What} | rollback: {f.Rollback}");
                if (r.Guidance is not null)
                    sb.AppendLine($"            GUIDE: {r.Guidance}");
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