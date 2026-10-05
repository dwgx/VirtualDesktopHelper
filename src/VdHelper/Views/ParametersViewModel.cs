using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VdHelper.Core.Config;
using VdHelper.Core.Mvvm;

namespace VdHelper.Views;

public sealed class ParameterRow : INotifyPropertyChanged
{
    private string _current = "";
    private string _state = "";

    public required ParameterInfo Info { get; init; }
    public string Key => Info.Key;
    public string Effect => Info.Effect;
    public string DefaultText => Info.Default;
    public string RangeText => Info.Range;
    public string SourceText => Info.Source;

    public bool LivesOnPc => Info.LivesOnPc;
    public bool ReadOnly => Info.ReadOnly;
    public bool IsBool => Info.Type.StartsWith("bool", StringComparison.OrdinalIgnoreCase);
    public bool Editable => Info.LivesOnPc && !Info.ReadOnly && IsBool;

    public string Badges
    {
        get
        {
            var tags = new List<string>();
            if (ReadOnly) tags.Add("只读");
            if (Info.Caution) tags.Add("谨慎");
            if (Info.NeedsRestart) tags.Add("需重启");
            if (Info.NeedsReconnect) tags.Add("需重连");
            if (Info.AffectsLan) tags.Add("影响发现/连接");
            if (Info.AffectsQuality) tags.Add("影响画质");
            if (!LivesOnPc) tags.Add("头显侧");
            return string.Join(" · ", tags);
        }
    }

    public string Current
    {
        get => _current;
        set { _current = value; Raise(); }
    }

    public string State
    {
        get => _state;
        private set { _state = value; Raise(); }
    }

    public AsyncRelayCommand? ToggleCommand { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    internal static string RenderValue(ParameterInfo info, StreamerSettings s)
    {
        if (!info.LivesOnPc)
            return "本机不落盘（由头显决定）";

        var b = s.GetBool(info.Key);
        if (b is not null) return b.Value ? "true" : "false";
        var raw = s.GetRaw(info.Key);
        if (raw is not null) return raw.Length > 60 ? raw[..60] + "…" : raw;
        return "(未设置 → 用默认值 " + info.Default + ")";
    }

    internal void BindWrite()
    {
        if (!Editable) return;
        ToggleCommand = new AsyncRelayCommand(async () =>
        {
            State = "写入中…";
            var s = StreamerSettings.Load();
            var next = !(s.GetBool(Info.Key) ?? false);
            try
            {
                var write = StreamerConfigWriter.Write(
                    StreamerSettings.DefaultPath,
                    Info.Key,
                    System.Text.Json.JsonDocument.Parse(next ? "true" : "false").RootElement.Clone());
                State = write.Success ? "已写入（备份 " + Path.GetFileName(write.BackupPath ?? "") + "）" : write.Message;
            }
            catch (Exception ex)
            {
                State = "写入失败：" + ex.Message;
            }
            var reloaded = StreamerSettings.Load();
            Current = RenderValue(Info, reloaded);
        });
    }
}

public sealed class ParametersViewModel
{
    private static readonly Lazy<ParametersViewModel> Instance = new(() => new ParametersViewModel());
    public static ParametersViewModel Current => Instance.Value;

    public ObservableCollection<ParameterRow> Rows { get; } = new();

    public string Summary { get; private set; } = "";
    public string ConfigPath => StreamerSettings.DefaultPath;
    public bool ConfigExists => StreamerSettings.Load().Exists;

    public void Load()
    {
        var s = StreamerSettings.Load();
        Rows.Clear();
        var ordered = ParameterCatalog.All
            .OrderByDescending(p => p.AffectsLan)
            .ThenByDescending(p => p.LivesOnPc)
            .ThenByDescending(p => p.ReadOnly);
        foreach (var info in ordered)
        {
            var row = new ParameterRow { Info = info, Current = ParameterRow.RenderValue(info, s) };
            row.BindWrite();
            Rows.Add(row);
        }

        var pc = ParameterCatalog.PcSide.Count;
        var ro = ParameterCatalog.All.Count(p => p.ReadOnly);
        Summary = s.Exists
            ? $"配置：{ConfigPath}（最后修改 {s.LastWriteTime:yyyy-MM-dd HH:mm:ss}）· {pc} 项 PC 侧 / {Rows.Count - pc} 项头显侧 · {ro} 项只读"
            : $"未找到 {ConfigPath}——Streamer 从未正常运行过";
        Notify();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConfigPath)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConfigExists)));
    }
}