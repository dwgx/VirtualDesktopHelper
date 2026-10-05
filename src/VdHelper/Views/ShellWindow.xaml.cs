﻿using System.Windows.Input;
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
        HealthViewModel.ClearForStreaming();

        // Stream results in as they land: the pass takes seconds and a blank list reads as a hang.
        var report = await HealthEngine.RunStreamingAsync(
            r => Dispatcher.Invoke(() => HealthViewModel.Append(r)),
            CancellationToken.None);

        ApplyReport(report, streamed: true);
    }

    private void ApplyReport(HealthReport report, bool streamed = false)
    {
        _current = this;
        // Both paths converge here. The streaming path used to return early, which meant the
        // per-status counts — and anything else derived from the finished report — only ever
        // appeared when a repair was re-run, not on a normal health pass.
        _changes = HealthHistory.Save(report);
        HealthViewModel.Publish(report);
        Verdict = report.Verdict;
        var pass = report.Results.Count(r => r.Status == CheckStatus.Pass);
        var warn = report.Results.Count(r => r.Status == CheckStatus.Warn);
        var block = report.Results.Count(r => r.Status == CheckStatus.Block);
        var timeline = HealthHistory.Timeline();
        var trend = timeline.Count >= 2
            ? timeline[^1].Verdict == timeline[^2].Verdict ? "" : " · 较上次：" + timeline[^2].Verdict + " → " + timeline[^1].Verdict
            : "";

        StatusText = $"通过 {pass} · 警告 {warn} · 阻断 {block} — {report.VerdictText}"
            + (_changes.Count > 0 ? $" · 与上次相比 {string.Join("、", _changes.Take(3).Select(c => c.Id))}" : "")
            + trend;
        Raise(nameof(Verdict));
        Raise(nameof(VerdictText));
    }
}
