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
        if (raw is not null)
        {
            // GetRaw hands back the JSON representation, so a string arrives wrapped in quotes —
            // which is why anchoring the match at ^AQAA silently did nothing. Match on content,
            // the same way ReportWriter's backstop does, and keep the quotes out of the display.
            var shown = raw.Trim().Trim('"');
            if (System.Text.RegularExpressions.Regex.IsMatch(shown, @"AQAA[A-Za-z0-9+/=]{16,}"))
                return "(已加密的 DPAPI 密文 · 不显示)";
            return shown.Length > 60 ? shown[..60] + "…" : shown;
        }
        return "(未设置 → 用默认值 " + info.Default + ")";
    }

    internal void BindWrite()
    {
        if (!Editable) return;
        ToggleCommand = new AsyncRelayCommand(async () =>
        {
            // Same refusal the CLI already makes, and for the same reason: the Streamer keeps a
            // ~2 s debounced save, so anything written underneath it is overwritten moments later.
            // Without this guard the button reported 已写入 and the change silently reverted.
            if (System.Diagnostics.Process.GetProcessesByName("VirtualDesktop.Streamer").Length > 0)
            {
                State = "Streamer 正在运行，它有 2 秒防抖保存会把这次写入盖掉。请先退出 Streamer 再改。";
                return;
            }

            State = "写入中…";
            var s = StreamerSettings.Load();
            var next = !(s.GetBool(Info.Key) ?? false);
            try
            {
                var write = StreamerConfigWriter.Write(
                    StreamerSettings.DefaultPath,
                    Info.Key,
                    System.Text.Json.JsonDocument.Parse(next ? "true" : "false").RootElement.Clone());
                if (!write.Success)
                {
                    State = write.Message;
                }
                else
                {
                    // Post-condition. "The writer returned true" is not the same claim as "the value
                    // on disk is now what you asked for", and this project has already shipped two
                    // repairs that reported success while doing nothing.
                    var after = StreamerSettings.Load().GetBool(Info.Key);
                    State = after == next
                        ? $"已确认 {Info.Key}={next.ToString().ToLowerInvariant()}（备份 "
                          + Path.GetFileName(write.BackupPath ?? "") + "）"
                        : $"写入返回成功，但回读确认 {Info.Key} 仍是 {after?.ToString() ?? "（空）"} —— 备份已保留";
                }
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
        // Read-only first was backwards. It pushed the 18 parameters a user can actually change
        // below ninety-odd account blobs they never can, in a 111-row list -- and the default
        // scroll position landed on a wall of DPAPI ciphertext. LAN-affecting still leads, because
        // that is what this tool exists for; within that, editable before read-only.
        var ordered = ParameterCatalog.All
            .OrderByDescending(p => p.AffectsLan)
            .ThenByDescending(p => p.LivesOnPc)
            .ThenBy(p => p.ReadOnly);
        foreach (var info in ordered)
        {
            var row = new ParameterRow { Info = info, Current = ParameterRow.RenderValue(info, s) };
            row.BindWrite();
            Rows.Add(row);
        }

        var pc = ParameterCatalog.PcSide.Count;
        var ro = ParameterCatalog.All.Count(p => p.ReadOnly);
        // Load() reports a corrupt file as Exists=false with ParseError set, and "not there" and
        // "there but unreadable" are very different problems: the first is a Streamer that never
        // ran, the second is a file the Streamer may still be able to recover. Collapsing them
        // told the user the Streamer had never run when their configuration was actually damaged.
        Summary = s.Exists
            ? $"配置：{ConfigPath}（最后修改 {s.LastWriteTime:yyyy-MM-dd HH:mm:ss}）· {pc} 项 PC 侧 / {Rows.Count - pc} 项头显侧 · {ro} 项只读"
            : s.ParseError is not null
                ? $"⚠ 配置读不出来：{ConfigPath} 存在但解析失败 —— {s.ParseError}。"
                  + "下面的值全部是空的，**不要在读不出配置时点切换**；先从 .vdhelper.bak 还原。"
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