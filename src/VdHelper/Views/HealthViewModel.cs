using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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

    public HealthViewModel()
    {
        Symptoms.Add(new SymptomTab(this, null, "全部"));
        foreach (var s in SymptomCatalog.All)
            Symptoms.Add(new SymptomTab(this, s, s.Title));
    }

    public static void Publish(HealthReport report)
    {
        _last = report;
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