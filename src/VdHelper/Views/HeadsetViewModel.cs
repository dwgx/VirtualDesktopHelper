using System.Collections.ObjectModel;
using System.ComponentModel;
using VdHelper.Core.Adb;
using VdHelper.Core.Model;
using VdHelper.Core.Mvvm;

namespace VdHelper.Views;

public sealed class HeadsetRow
{
    public required string Label { get; init; }
    public required string Value { get; init; }
}
public sealed class HeadsetViewModel : INotifyPropertyChanged
{
    private static readonly Lazy<HeadsetViewModel> Instance = new(() => new HeadsetViewModel());
    public static HeadsetViewModel Current => Instance.Value;

    private string _adbStatus = "";
    private string _summary = "";
    private CheckStatus _status = CheckStatus.Unknown;

    public ObservableCollection<HeadsetRow> Facts { get; } = new();
    public ObservableCollection<CheckRow> Checks { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }

    public HeadsetViewModel() => RefreshCommand = new AsyncRelayCommand(LoadAsync);

    /// <summary>Persisted headset IP; drives the lan-reach check on the first screen.</summary>
    public string HeadsetIp
    {
        get => Core.Adb.ConfigFile.Read<string>(Core.Health.ReachabilityCheck.IpKey) ?? "";
        set
        {
            Core.Adb.ConfigFile.Write(
                Core.Health.ReachabilityCheck.IpKey,
                string.IsNullOrWhiteSpace(value) ? "" : value.Trim());
            Raise(nameof(HeadsetIp));
            Raise(nameof(SaveHint));
        }
    }

    public string SaveHint => HeadsetIp.Length > 0 ? "已保存" : "";

    public string AdbStatus
    {
        get => _adbStatus;
        private set { _adbStatus = value; Raise(nameof(AdbStatus)); }
    }

    public string Summary
    {
        get => _summary;
        private set { _summary = value; Raise(nameof(Summary)); }
    }

    public CheckStatus Status
    {
        get => _status;
        private set { _status = value; Raise(nameof(Status)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public async Task LoadAsync()
    {
        var hits = AdbLocator.Probe();
        Facts.Clear();
        Checks.Clear();

        foreach (var (source, path) in hits)
            Facts.Add(new HeadsetRow { Label = "adb 候选 · " + source, Value = path });

        if (hits.Count == 0)
        {
            Status = CheckStatus.Warn;
            AdbStatus = "没有找到 adb.exe（本机 PATH 里没有 adb，实测）";
            Summary = "可从 Google 官方下载 platform-tools：" + AdbLocator.DownloadUrl;
            return;
        }

        var adbPath = hits[0].Path;
        AdbLocator.RememberedPath = adbPath;
        AdbStatus = adbPath;

        var client = new AdbClient(adbPath);
        var version = await client.RunAsync(["version"], 8000);
        Facts.Add(new HeadsetRow
        {
            Label = "adb 版本",
            Value = version.Ok
                ? string.Join(" ", version.Lines.Take(2))
                : "读取失败：" + version.StdErr.Trim(),
        });

        var result = await new HeadsetProbe(client).RunAsync();
        Status = result.Status;
        Summary = result.Summary;

        foreach (var (k, v) in result.Evidence)
            Facts.Add(new HeadsetRow { Label = k, Value = v });

        AddCheck(result, "headset");

        // Deep probe covers the three root causes a PC-side pass structurally cannot see:
        // headset MAC randomization, headset-side settings, headset-side VPN.
        var serial = result.Evidence.TryGetValue("serial", out var s) ? s : string.Empty;
        var deep = await new HeadsetDeepProbe(client).ProbeAsync(serial);
        AddCheck(deep, HeadsetDeepProbe.Id);

        void AddCheck(CheckResult r, string title)
        {
            var row = new CheckRow { Result = r, Title = title };
            foreach (var f in r.Fixes)
            {
                var fixRow = new FixRow { Action = f };
                fixRow.Bind(ShellWindow.RunHealthAsync);
                row.Fixes.Add(fixRow);
            }
            Checks.Add(row);
        }
    }
}