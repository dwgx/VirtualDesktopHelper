using System.Windows.Input;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using VdHelper.Core.Health;
using VdHelper.Core.Model;

namespace VdHelper.Views;

public partial class ShellWindow : Window, INotifyPropertyChanged
{
    private string _statusText = "准备中…";

    public ShellWindow(int initialTab = 0)
    {
        InitializeComponent();
        DataContext = this;
        Tabs.SelectedIndex = initialTab;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; Raise(); }
    }

    public HealthVerdict Verdict { get; private set; } = HealthVerdict.Unknown;

    public string VerdictText => Verdict switch
    {
        HealthVerdict.Streamable => "可串流",
        HealthVerdict.AtRisk => "有隐患",
        HealthVerdict.Blocked => "阻断",
        _ => "未知",
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static ShellWindow? _current;
    private IReadOnlyList<Core.Health.HealthHistory.Change> _changes = Array.Empty<Core.Health.HealthHistory.Change>();

    /// <summary>Entry point used by fix rows after they apply a repair.</summary>
    public static Task<HealthReport> RunHealthAsync() => HealthEngine.RunAsync(CancellationToken.None);

    internal static void RefreshVerdict(HealthReport report) => _current?.ApplyReport(report);

    internal async Task RefreshAsync()
    {
        StatusText = "正在体检…";
        ApplyReport(await HealthEngine.RunAsync(CancellationToken.None));
    }

    private void ApplyReport(HealthReport report)
    {
        _current = this;
        _changes = HealthHistory.Save(report);
        HealthViewModel.Publish(report);
        Verdict = report.Verdict;
        var pass = report.Results.Count(r => r.Status == CheckStatus.Pass);
        var warn = report.Results.Count(r => r.Status == CheckStatus.Warn);
        var block = report.Results.Count(r => r.Status == CheckStatus.Block);
        StatusText = $"通过 {pass} · 警告 {warn} · 阻断 {block} — {report.VerdictText}"
            + (_changes.Count > 0 ? $" · 与上次相比 {string.Join("、", _changes.Take(3).Select(c => c.Id))}" : "");
        Raise(nameof(Verdict));
        Raise(nameof(VerdictText));
    }
}