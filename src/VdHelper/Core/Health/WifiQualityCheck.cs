﻿using VdHelper.Core.Checks;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// Wireless quality for the link the headset actually uses. The headset reaches this PC over
/// Wi-Fi on a typical setup, and the corpus has real threads where a 5 GHz channel that is too
/// narrow or too crowded produced stutter while every wired metric looked perfect.
/// <para>
/// Deliberately a single source — <c>netsh wlan show interfaces</c> — because that is the one
/// query that reports band, channel and negotiated rate together, and a check whose fields come
/// from three different APIs can disagree with itself.
/// </para>
/// </summary>
public static class WifiQualityCheck
{
    /// <summary>Sample of the real output shape, used to exercise the parser without a live link.</summary>
    public const string SampleOutput = """
        There is 1 interface on the system:

            Name                   : Wi-Fi
            Description            : Intel(R) Wi-Fi 7 BE200 320MHz
            GUID                   : 8dd8af39-5d3a-41ab-9663-ff2edcb153b5
            Physical address       : 94:b6:09:8d:27:28
            Interface type         : Primary
            State                  : connected
            Radio status           : Hardware On
                                     Software On
            SSID                   : HomeNet
            BSSID                  : aa:bb:cc:dd:ee:ff
            Network type           : Infrastructure
            Radio type             : 802.11ax
            Authentication         : WPA2-Personal
            Cipher                 : CCMP
            Connection mode        : Profile
            Channel                : 149
            Receive rate (Mbps)    : 866.7
            Transmit rate (Mbps)   : 866.7
            Signal                 : 97%
            Profile                : HomeNet
        """;

    /// <summary>Band implied by the negotiated rate and radio type, since netsh has no band field.</summary>
    public static string BandOf(string radioType, string receiveRate)
    {
        if (!double.TryParse(receiveRate, out var mbps)) return "(速率读不出来)";
        if (radioType.Contains("802.11be", StringComparison.OrdinalIgnoreCase)) return mbps > 400 ? "6 GHz (Wi-Fi 7)" : "5/6 GHz (Wi-Fi 7)";
        if (radioType.Contains("802.11ax", StringComparison.OrdinalIgnoreCase)) return mbps > 400 ? "6 GHz (Wi-Fi 6E)" : "5 GHz (Wi-Fi 6)";
        if (radioType.Contains("802.11ac", StringComparison.OrdinalIgnoreCase)) return "5 GHz";
        if (radioType.Contains("802.11n", StringComparison.OrdinalIgnoreCase)) return mbps > 100 ? "5 GHz" : "2.4 GHz";
        return "(无线类型 " + radioType + ")";
    }

    /// <summary>
    /// Parses <c>netsh wlan show interfaces</c>. Multi-line values such as "Radio status" continue
    /// on an indented line with no key, so anything without a colon belongs to the previous key.
    /// </summary>
    public static Dictionary<string, string> Parse(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? last = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0) continue;
            var idx = line.IndexOf(':');
            // A continuation line has no key, or its colon sits far right of where keys start.
            if (idx > 0 && idx < 40 && line[..idx].Trim().Length > 0 && line[..idx].Trim().Length < 24)
            {
                last = line[..idx].Trim();
                fields[last] = line[(idx + 1)..].Trim();
            }
            else if (last is not null && fields.TryGetValue(last, out var so))
            {
                fields[last] = (so + " " + line.Trim()).Trim();
            }
        }
        return fields;
    }

    public static ICheck Create() =>
        CheckFactory.Delegate(
            new("wifi-quality", "无线链路质量", "头显走 Wi-Fi 时，这台的无线链路状况如何？", "无线"),
            async ct =>
            {
                var text = await PowerShellRunner
                    .LinesAsync("netsh wlan show interfaces 2>&1 | Out-String")
                    .ConfigureAwait(false);
                var joined = string.Join("\n", text);
                var ev = new Dictionary<string, string>();
                var f = Parse(joined);

                var state = f.GetValueOrDefault("State", "").Trim();
                ev["无线接口数"] = f.ContainsKey("Name") ? "有" : "0";
                if (f.TryGetValue("Name", out var nic)) ev["适配器"] = nic;
                ev["状态"] = state.Length == 0 ? "(读不到)" : state;
                if (f.TryGetValue("Description", out var desc)) ev["型号"] = desc;

                if (state.Length == 0)
                    return new CheckResult("wifi-quality", CheckStatus.Unknown, "读不到无线接口状态",
                        "netsh 没有返回 State 字段。**这一项没有测到任何东西**，"
                        + "不是「无线没问题」。",
                        ev, Array.Empty<FixAction>(),
                        "这台机器可能没有无线网卡，或 netsh 被策略限制。");

                if (!state.StartsWith("connected", StringComparison.OrdinalIgnoreCase))
                    // Not Pass. The link was never measured, and Pass is what the verdict counts as
                    // evidence — a check that measured nothing must not pad the passing column.
                    return new CheckResult("wifi-quality", CheckStatus.Unknown,
                        "无线未连接（" + state + "），这一项没有测到任何链路数据",
                        "本机当前不是走无线，所以无线链路这一段不在串流路径上——"
                        + "**但这也意味着这项什么都没测到**，不是「无线已验证正常」。",
                        ev, Array.Empty<FixAction>(),
                        "连上 Wi-Fi 再跑一次才有意义。若头显走 Wi-Fi 而本机走有线，那一段无线链路在路由器/头显那边，本项测不到。");

                var radio = f.GetValueOrDefault("Radio type", "");
                var rx = f.GetValueOrDefault("Receive rate (Mbps)", "");
                var tx = f.GetValueOrDefault("Transmit rate (Mbps)", "");
                var channel = f.GetValueOrDefault("Channel", "");
                var signal = f.GetValueOrDefault("Signal", "");
                var band = BandOf(radio, rx);

                ev["SSID"] = f.GetValueOrDefault("SSID", "(未连接/隐藏)");
                ev["频段"] = band;
                ev["信道"] = channel.Length == 0 ? "(读不到)" : channel;
                ev["协商速率"] = $"收 {rx} / 发 {tx} Mbps";
                ev["信号"] = signal.Length == 0 ? "(读不到)" : signal;

                // Two lists. `warnings` means something is wrong and can cost you frames; `notes`
                // is just what was measured. The 5 GHz high-channel line used to go into this one
                // list while reading "通常是较空闲的选择" — a compliment filed as a warning, so the
                // verdict said 无线链路有可疑之处 about the best channel available and the guidance
                // three lines below recommended that same channel.
                var warnings = new List<string>();
                var notes = new List<string>();
                if (signal.EndsWith('%') && int.TryParse(signal.TrimEnd('%'), out var pct) && pct < 60)
                    warnings.Add($"信号只有 {pct}%，弱信号下丢包与重传都会上升，而视频流对这两者最敏感。");

                var channelNum = int.TryParse(channel, out var ch) ? ch : 0;
                var selfContradictory = channelNum is >= 1 and <= 14 && rx.Length > 0
                    && double.TryParse(rx, out var r) && r > 100;
                if (selfContradictory)
                    warnings.Add("连的是 2.4 GHz 频段但协商速率却高于 100 Mbps，数值自相矛盾，请以实际频段为准。");
                if (channelNum is >= 36 and <= 48)
                    warnings.Add("信道 " + channelNum + " 属于 DFS 频段，部分路由器上会因雷达检测而短暂静默，表现为周期性卡顿。");
                if (channelNum is >= 149 and <= 177)
                    notes.Add("信道 " + channelNum + " 在 5 GHz 高信道段，通常是较空闲的选择（这不是问题）。");
                // Not on a self-inconsistent read. That branch has already told the user the numbers
                // disagree and to trust the band over the rate; adding "and you really are on crowded
                // 2.4 GHz" two lines later asserts the conclusion it just withdrew.
                if (channelNum is >= 1 and <= 14 && !selfContradictory)
                    warnings.Add($"连的是 2.4 GHz（信道 {channelNum}）。这个频段在住宅环境里通常最拥挤，"
                        + "吞吐会高、干扰也多；头显在这种链路上更容易出现卡顿。");

                var detail = notes.Count > 0 ? string.Join("；", notes) : "";

                if (warnings.Count == 0)
                    return new CheckResult("wifi-quality", CheckStatus.Pass,
                        $"无线链路正常（{band}，信道 {channel}，信号 {signal}）",
                        "这几项没有明显的丢包来源。" + detail
                        + "**但这一项没有测过丢包**——它只读了频段/信道/信号/协商速率。"
                        + "真要区分丢包，跑「深度探测」（--deep）跑 20 次。",
                        ev, Array.Empty<FixAction>());

                return new CheckResult("wifi-quality", CheckStatus.Warn,
                    $"无线链路有可疑之处：{band}，信道 {channel}，信号 {signal}",
                    string.Join("；", warnings) + (detail.Length > 0 ? "。" + detail : ""),
                    ev, Array.Empty<FixAction>(),
                    "画面卡顿但有线指标全绿时，无线链路是下一站。"
                    + "换到 5 GHz 的高信道段（149/153/157/161）通常能避开拥挤频段；"
                    + "工具不自动改你的无线设置——换信道会影响整屋子的设备，该由你决定。");
            });
}