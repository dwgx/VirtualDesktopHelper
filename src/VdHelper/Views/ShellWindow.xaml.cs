using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using VdHelper.Core.Health;
using VdHelper.Core.Model;

namespace VdHelper.Views;

public partial class ShellWindow : Window, INotifyPropertyChanged
{
    private string _statusText = "准备中…";

    public ShellWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += async (_, _) => await RefreshAsync();
    }

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

    internal async Task RefreshAsync()
    {
        StatusText = "正在体检…";
        var report = await HealthEngine.RunAsync(CancellationToken.None);
        HealthViewModel.Publish(report);
        Verdict = report.Verdict;
        var pass = report.Results.Count(r => r.Status == CheckStatus.Pass);
        var warn = report.Results.Count(r => r.Status == CheckStatus.Warn);
        var block = report.Results.Count(r => r.Status == CheckStatus.Block);
        StatusText = $"通过 {pass} · 警告 {warn} · 阻断 {block} — {report.VerdictText}";
        Raise(nameof(Verdict));
        Raise(nameof(VerdictText));
    }
}