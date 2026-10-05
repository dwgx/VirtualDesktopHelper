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
            State = "执行中…";
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

    public ObservableCollection<CheckRow> Rows { get; } = new();

    public static void Publish(HealthReport report)
    {
        var vm = Current;
        vm.Rows.Clear();
        foreach (var r in report.Results)
        {
            var row = new CheckRow { Result = r, Title = r.Id };
            foreach (var f in r.Fixes)
            {
                var fixRow = new FixRow { Action = f };
                fixRow.Bind(ShellWindow.RunHealthAsync);
                row.Fixes.Add(fixRow);
            }
            vm.Rows.Add(row);
        }
    }
}