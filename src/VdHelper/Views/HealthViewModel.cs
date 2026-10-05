using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VdHelper.Core.Model;

namespace VdHelper.Views;

public sealed class CheckRow : INotifyPropertyChanged
{
    public required CheckResult Result { get; init; }
    public string Title { get; init; } = "";
    public CheckStatus Status => Result.Status;
    public string Summary => Result.Summary;
    public string Detail => Result.Detail;
    public string Evidence => string.Join("\n", Result.Evidence.Where(kv => !kv.Key.StartsWith('_'))
        .Select(kv => $"{kv.Key}: {kv.Value}"));
    public string FixesText => string.Join("\n\n", Result.Fixes.Select(f =>
        $"[修复/{f.Risk}] {f.Title}\n执行：{f.What}\n备份：{f.Backup}\n回滚：{f.Rollback}"));
    public string GuidanceText => Result.Guidance ?? "";
    public bool HasFixes => Result.Fixes.Count > 0;
    public bool HasGuidance => !string.IsNullOrWhiteSpace(Result.Guidance);

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
            vm.Rows.Add(new CheckRow { Result = r, Title = r.Id });
    }
}