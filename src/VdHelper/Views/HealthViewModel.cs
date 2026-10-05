using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VdHelper.Core.Health;
using VdHelper.Core.Model;
using VdHelper.Core.Mvvm;

namespace VdHelper.Views;

/// <summary>One repair action with its backup/rollback text and an apply button.</summary>
public sealed class FixRow : INotifyPropertyChanged
{
    private string _state = "待执行";

    public required FixAction Action { get; init; }
    public string Title => Action.Title;
    public string Risk => Action.Risk.ToString();

    public string RunHint => Action.NeedsElevation
        ? "执行中（会弹 UAC，请点「是」——本机实测这一等可能要 1 分半）"
        : "执行中…";
    public string What => Action.What;
    public string Backup => Action.Backup;
    public string Rollback => Action.Rollback;
    public string State
    {
        get => _state;
        private set { _state = value; Raise(); }
    }

    public AsyncRelayCommand? ApplyCommand { get; private set; }

    public void Bind(Func<Task<HealthReport>> refresh)
    {
        ApplyCommand = new AsyncRelayCommand(async () =>
        {
            State = RunHint;
            var result = await Action.Apply(CancellationToken.None);
            State = result.Success ? "已执行" : "失败";
            LastMessage = result.Message;
            Raise(nameof(LastMessage));
            if (result.Success)
            {
                // Re-run so the user sees the post-fix verdict instead of a stale list.
                var report = await refresh();
                HealthViewModel.Publish(report);
                ShellWindow.RefreshVerdict(report);
            }
        });
    }

    public string LastMessage { get; private set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CheckRow : INotifyPropertyChanged
{
    public required CheckResult Result { get; init; }
    public required string Title { get; init; }
    public CheckStatus Status => Result.Status;
    public string Summary => Result.Summary;

    /// <summary>
    /// What a screen reader announces for this row. Without it the expander reads as an unnamed
    /// control and the verdict is only in the colour and the shape — which is exactly the part a
    /// screen reader cannot see. The official Streamer's XAML carries no AutomationProperties.Name
    /// anywhere in its tree, so there is nothing to copy here; this is us being better than the
    /// thing we are imitating.
    /// </summary>
    public string AccessibilityName => $"{Title}，{StatusText}：{Summary}";

    private string StatusText => Status switch
    {
        CheckStatus.Pass => "通过",
        CheckStatus.Warn => "警告",
        CheckStatus.Block => "阻断",
        _ => "未知",
    };
    public string Detail => Result.Detail;
    public string EvidenceText => string.Join("\n", Result.Evidence
        .Where(kv => !kv.Key.StartsWith('_'))
        .Select(kv => $"{kv.Key}: {kv.Value}"));
    public string GuidanceText => Result.Guidance ?? "";
    public bool HasGuidance => !string.IsNullOrWhiteSpace(Result.Guidance);

    public ObservableCollection<FixRow> Fixes { get; } = new();
    public bool HasFixes => Fixes.Count > 0;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Single shared instance so the shell and the health list stay in step.</summary>
public sealed class HealthViewModel
{
    private static readonly Lazy<HealthViewModel> Instance = new(() => new HealthViewModel());
    public static HealthViewModel Current => Instance.Value;

    private static HealthReport? _last;

    public ObservableCollection<CheckRow> Rows { get; } = new();

    /// <summary>
    /// "What to do next", one line per finding. A verdict on its own is not actionable, and the
    /// first thing people do with a 34-row list is close the window.
    /// </summary>
    public ObservableCollection<NextActionRow> Actions { get; } = new();

    public string ActionsHint => Actions.Count == 0
        ? ""
        : Actions.Count(a => a.Action.Kind == NextActionKind.FixThisFirst) + " 条先修 · "
          + Actions.Count(a => a.Action.Kind == NextActionKind.ReadThisFirst) + " 条先看 · "
          + Actions.Count(a => a.Action.Kind == NextActionKind.ThenThis) + " 条再修 · "
          + Actions.Count(a => a.Action.Kind == NextActionKind.WorthKnowing) + " 条值得知道";

    private void LoadActions(HealthReport report)
    {
        Actions.Clear();
        foreach (var a in report.NextActions)
            Actions.Add(new NextActionRow(a));
        Raise(nameof(Actions));
        Raise(nameof(ActionsHint));
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public ObservableCollection<SymptomTab> Symptoms { get; } = new();

    private SymptomTab? _selected;
    public SymptomTab? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            foreach (var tab in Symptoms)
                tab.IsSelected = ReferenceEquals(tab, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SymptomHint)));
            Rebuild();
        }
    }

    /// <summary>One-line guidance under the symptom buttons; empty when showing everything.</summary>
    public string SymptomHint => _selected is null
        ? "先选你遇到的现象——这两类故障的根因几乎不重叠，选对能省掉一半排查。"
        : $"{_selected!.Class!.FirstLook}\n用户原话：{_selected.Class.PhraseLine}";


    /// <summary>
    /// On-demand deep probe. The pass itself takes only a quick loss sample because it runs on every
    /// health check; this is the 20-sample run you press when the picture keeps stuttering.
    /// </summary>
    public AsyncRelayCommand? DeepProbeCommand { get; private set; }

    private string _deepText = "";
    public string DeepProbeText
    {
        get => _deepText;
        private set { _deepText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeepProbeText))); }
    }

    public HealthViewModel()
    {
        Symptoms.Add(new SymptomTab(this, null, "全部"));
        foreach (var s in SymptomCatalog.All)
            Symptoms.Add(new SymptomTab(this, s, s.Title));

        DeepProbeCommand = new AsyncRelayCommand(async () =>
        {
            DeepProbeText = "正在做深度丢包探测（每目标 20 次采样，约 20 秒）…";
            var lines = new List<string>();
            var worst = 0.0;
            foreach (var (label, host) in LossProbe.Targets())
            {
                var s = await LossProbe.MeasureAsync(host, LossProbe.DefaultSamples, 800).ConfigureAwait(true);
                worst = Math.Max(worst, s.LossPercent);
                lines.Add($"{label} {host}  {s.Received}/{s.Sent} 收到 · 丢包 {s.LossPercent:F0}% · "
                    + $"延迟 {s.MinMs:F0}/{s.AvgMs:F0}/{s.MaxMs:F0} ms · 抖动 {s.JitterMs:F1} ms");
            }
            lines.Add(worst >= 5
                ? "测到丢包。网关也丢 → 问题在 PC 到路由器这一段；只有头显丢 → Wi-Fi 那一段。"
                : "这一次没测到丢包。随机丢包不会每次都赶上，画面卡的时候再按一次。");
            DeepProbeText = string.Join("\n", lines);
        });
    }

    public static void Publish(HealthReport report)
    {
        _last = report;
        Current.LoadActions(report);
        Current.Rebuild();
    }

    /// <summary>
    /// Appends a single result as it lands. A pass takes several seconds; showing rows as they
    /// arrive beats a blank list with "正在体检…".
    /// </summary>
    public static void Append(CheckResult result)
    {
        var vm = Current;
        var row = new CheckRow { Result = result, Title = result.Id };
        foreach (var f in result.Fixes)
        {
            var fixRow = new FixRow { Action = f };
            fixRow.Bind(ShellWindow.RunHealthAsync);
            row.Fixes.Add(fixRow);
        }
        vm.Rows.Add(row);
        vm.PropertyChanged?.Invoke(vm, new PropertyChangedEventArgs(nameof(Rows)));
    }

    public static void ClearForStreaming()
    {
        Current.Actions.Clear();
        Current.Raise(nameof(Actions));
        Current.Rows.Clear();
        Current.PropertyChanged?.Invoke(Current, new PropertyChangedEventArgs(nameof(Rows)));
    }

    private void Rebuild()
    {
        if (_last is null) return;
        var keep = _selected?.Class?.RelevantChecks;
        Rows.Clear();
        foreach (var r in _last.Results)
        {
            if (keep is not null && !keep.Contains(r.Id)) continue;
            var row = new CheckRow { Result = r, Title = r.Id };
            foreach (var f in r.Fixes)
            {
                var fixRow = new FixRow { Action = f };
                fixRow.Bind(ShellWindow.RunHealthAsync);
                row.Fixes.Add(fixRow);
            }
            Rows.Add(row);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>A clickable symptom chip; "全部" clears the filter.</summary>
public sealed class SymptomTab : INotifyPropertyChanged
{
    public SymptomTab(HealthViewModel owner, SymptomClass? cls, string label)
    {
        Class = cls;
        Label = label;
        SelectCommand = new RelayCommand(() => owner.Selected = this);
    }

    public SymptomClass? Class { get; }
    public string Label { get; }

    /// <summary>Bound from the chip's MouseBinding.</summary>
    public ICommand SelectCommand { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
/// <summary>One line in the health view's "what to do next" panel.</summary>
public sealed class NextActionRow
{
    public NextActionRow(NextAction action) => Action = action;

    public NextAction Action { get; }

    public string Title => Action.Title;

    public string What => Action.What;

    public string Tag => Action.Kind switch
    {
        NextActionKind.FixThisFirst => "先修",
        NextActionKind.ReadThisFirst => "先看",
        NextActionKind.ThenThis => "再修",
        _ => "知道",
    };

    public string Caveat => Action.Caveat;

    public bool HasCaveat => !string.IsNullOrWhiteSpace(Caveat);

    /// <summary>Only the two actionable kinds get a colour; "知道" must not look urgent.</summary>
    public string Accent => Action.Kind == NextActionKind.FixThisFirst ? "#FF5B6E"
        : Action.Kind == NextActionKind.ThenThis ? "#FFCC66"
        : "#8B93A8";
}
