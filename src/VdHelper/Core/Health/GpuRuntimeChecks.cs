﻿﻿﻿﻿using System.Diagnostics;
using System.Globalization;
using VdHelper.Core.Checks;
using VdHelper.Core.Model;

namespace VdHelper.Core.Health;

/// <summary>
/// E3 (hardware encoder unavailable / disabled) and G4 (thermal or power throttling).
/// <para>
/// The corpus has real threads for both: one where the stream is black or unwatchable because the
/// encoder path is not what you think, and one where 18 months of stutter turned out to be a laptop
/// sitting hot. This machine can answer part of it — <c>nvidia-smi</c> exposes encoder session
/// count, live vs max clock and temperature — so the check is built on that and says plainly what
/// it cannot prove.
/// </para>
/// </summary>
public static class GpuRuntimeChecks
{
    private const string SmiArgs =
        // power.draw and clocks_event_reasons.active are the two fields that answer "why is it not at
        // max clock". Without them the check named a power wall it had not measured a watt of. Both
        // verified present on this machine before being asked for.
        "--query-gpu=name,clocks.current.graphics,clocks.max.graphics,temperature.gpu," +
        "utilization.gpu,encoder.stats.sessionCount,power.draw,clocks_event_reasons.active" +
        " --format=csv,noheader,nounits";

    public static IReadOnlyList<ICheck> Create() => [GpuEncoderCheck(), GpuThrottleCheck()];

    public static ICheck GpuEncoderCheck() =>
        CheckFactory.Delegate(
            new("gpu-encoder", "硬件编码器", "串流的编码会话现在在不在硬件上？", "性能"),
            async ct =>
            {
                var found = await QuerySmiAsync(ct).ConfigureAwait(false);
                if (found is null) return NoSmi("gpu-encoder");

                var f = found.Value;
                var ev = new Dictionary<string, string>
                {
                    ["GPU"] = f.Name,
                    ["编码会话数"] = f.EncoderSessions.ToString(),
                    ["来源"] = "nvidia-smi（NVIDIA 硬件编码器 NVENC）",
                };

                if (f.EncoderSessions > 0)
                    return new CheckResult("gpu-encoder", CheckStatus.Pass,
                        $"硬件编码器正在工作（{f.EncoderSessions} 个编码会话）",
                        "说明此刻确实有硬件编码会话在跑。", ev, Array.Empty<FixAction>());

                return new CheckResult("gpu-encoder", CheckStatus.Pass,
                    "硬件编码器空闲（当前 0 个编码会话）",
                    "没有会话不等于没有硬件编码器——只是此刻没人用它。"
                    + "**工具无法证明 VD 串流时用的是硬件编码还是软件编码**，那要看 Streamer 自己的日志或设置页。",
                    ev, Array.Empty<FixAction>(),
                    "要确认串流走的是硬件编码：在 Streamer 开着串流时跑一次本项，会话数应该大于 0。");
            });

    public static ICheck GpuThrottleCheck() =>
        CheckFactory.Delegate(
            new("gpu-throttle", "GPU 是否被压着", "显卡现在跑在最高频率吗？", "性能"),
            async ct =>
            {
                var found = await QuerySmiAsync(ct).ConfigureAwait(false);
                if (found is null) return NoSmi("gpu-throttle");

                var f = found.Value;
                var ratio = f.MaxClock > 0 ? f.CurrentClock / (double)f.MaxClock : 0;
                var ev = new Dictionary<string, string>
                {
                    ["GPU"] = f.Name,
                    ["当前频率"] = f.CurrentClock + " MHz",
                    ["最高频率"] = f.MaxClock + " MHz",
                    ["频率比"] = ratio.ToString("P0", CultureInfo.InvariantCulture),
                    ["温度"] = f.TemperatureC + " °C",
                    ["GPU 占用"] = f.Utilization + " %",
                    ["功耗"] = f.PowerDrawW is null ? "读不到" : f.PowerDrawW.Value.ToString("0.0", CultureInfo.InvariantCulture) + " W",
                    ["降频原因位域"] = f.ThrottleReasons is null
                        ? "读不到（驱动未报此字段）"
                        : "0x" + f.ThrottleReasons.Value.ToString("X", CultureInfo.InvariantCulture),
                };

                // Both facts are already measured here — temperature and the clock ratio — so state
                // both and let the driver say why. This used to read "高温 + 频率上不去 = 典型的降频"
                // while branching on temperature alone, and the reason line went on to name three
                // causes the check had not measured. The reason bits are right there in f.
                if (f.TemperatureC >= 85)
                    return new CheckResult("gpu-throttle", CheckStatus.Warn,
                        $"GPU {f.TemperatureC}°C，当前频率 {f.CurrentClock}/{f.MaxClock} MHz = {ratio:P0}",
                        (f.ThrottleReasons is null
                            ? "温度已越过 85°C。但驱动没有报 clocks_event_reasons，"
                              + "所以「是不是因为热才降频」这一项**没测成**，不猜。"
                            : $"温度 {f.TemperatureC}°C 已越过 85°C，驱动报的降频原因："
                              + string.Join("、", DescribeReasons(f.ThrottleReasons.Value)) + "。")
                        + (f.PowerDrawW is null ? "" : $"功耗 {f.PowerDrawW:0.#} W。")
                        + "温度高本身就会掉频、画面卡、编码延迟涨。",
                        ev, Array.Empty<FixAction>(),
                        "先看散热：清灰、垫高、进风口是否被挡。这类「画质莫名变差」的帖子最后往往落到这一步。");

                // Judge on what the driver says it is doing, not on the clock ratio alone. A card
                // at 47% load is supposed to sit below its boost clock; that is not a fault and it is
                // certainly not a power wall.
                if (f.ThrottleReasons is null)
                    return new CheckResult("gpu-throttle", CheckStatus.Unknown,
                        $"GPU 跑在最高频率的 {ratio:P0}（{f.CurrentClock}/{f.MaxClock} MHz），"
                        + "但驱动没有报告 clocks_event_reasons，这一项无法判断原因——"
                        + "不猜。频率比低本身在低负载下是正常的。",
                        "这一项以前在没有读 clocks_event_reasons 的情况下把原因写成「通常是功耗墙」——"
                        + "一个瓦都没量。现在字段读不到，就如实说读不到。",
                        ev, Array.Empty<FixAction>(),
                        "频率比低不等于出问题了。这一项要等驱动报出降频原因才判得。");

                var reasons = f.ThrottleReasons.Value;
                var real = reasons & RealThrottle;
                if (real != 0)
                {
                    var why = string.Join("、", DescribeReasons(reasons));
                    var watts = f.PowerDrawW is null ? "" : $"，当前功耗 {f.PowerDrawW:0.#} W";
                    return new CheckResult("gpu-throttle", CheckStatus.Warn,
                        $"GPU 被压在最高频率的 {ratio:P0}（{f.CurrentClock}/{f.MaxClock} MHz）"
                        + $"（占用 {f.Utilization}%，温度 {f.TemperatureC}°C{watts}）。"
                        + $"驱动报的降频原因：{why}。",
                        "原因来自 nvidia-smi 的 clocks_event_reasons.active 位域，是驱动自己报的，"
                        + "不是从频率比推出来的。",
                        ev, Array.Empty<FixAction>(),
                        "这是驱动自己报的原因，不是推断。看那一条决定下一步。");
                }

                // No throttle reason set. If the card is busy and still not boosting, that is the
                // interesting case — and it is not one this tool has an explanation for.
                if (ratio > 0 && ratio < 0.8 && f.Utilization >= 60)
                    return new CheckResult("gpu-throttle", CheckStatus.Warn,
                        $"GPU 占用 {f.Utilization}% 却只跑在最高频率的 {ratio:P0}"
                        + $"（{f.CurrentClock}/{f.MaxClock} MHz，温度 {f.TemperatureC}°C）。"
                        + "驱动没有报任何降频原因，所以这不是功耗墙也不是过热——"
                        + "原因不明，如实写在这里。",
                        "占用不低、频率没上去、而驱动说它没有降频。这三种同时成立时，"
                        + "功耗墙和过热都可以排除，但本工具给不出剩下的那种原因。",
                        ev, Array.Empty<FixAction>(),
                        "负载不低但没跑满频率，而驱动说它没被限制。这一项本工具给不出原因，"
                        + "需要看驱动侧或第三方工具。");

                return new CheckResult("gpu-throttle", CheckStatus.Pass,
                        $"GPU 跑在最高频率的 {ratio:P0}，占用 {f.Utilization}%、"
                        + $"温度 {f.TemperatureC}°C。"
                        + "驱动没有报任何降频原因——不是被压着。",
                        $"占用 {f.Utilization}%、降频原因位域 0x{reasons:X}，两条都不支持「被压着」的判断。",
                        ev, Array.Empty<FixAction>());

            });

    /// <summary>Throttle reasons, null when the driver did not report the field.</summary>
    private static ulong? TryParseReasons(string cell)
    {
        var s = cell.Trim();
        // The driver prints 0x0000000000000000. A [N/A] cell means the field is unavailable, which is
        // not the same as "no throttling" and must not be read as 0.
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ulong.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return v;
        return ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    // NVIDIA clocks_event_reasons bits. Idle, ApplicationsClocksSetting and DisplayClockSetting are
    // normal states, not throttling; the rest are reasons the card is held below its boost clock.
    private const ulong GpuIdle = 1UL << 0;
    private const ulong ApplicationsClocks = 1UL << 1;
    private const ulong SwPowerCap = 1UL << 2;
    private const ulong HwSlowdown = 1UL << 3;
    private const ulong SyncBoost = 1UL << 4;
    private const ulong SwThermalSlowdown = 1UL << 5;
    private const ulong HwThermalSlowdown = 1UL << 6;
    private const ulong HwPowerBrake = 1UL << 7;
    private const ulong DisplayClockSetting = 1UL << 8;

    /// <summary>Bits that mean the card is actually being held down.</summary>
    private const ulong RealThrottle =
        SwPowerCap | HwSlowdown | SwThermalSlowdown | HwThermalSlowdown | HwPowerBrake;

    private static IEnumerable<string> DescribeReasons(ulong r)
    {
        if ((r & SwPowerCap) != 0) yield return "软件功耗墙 (SW Power Cap)";
        if ((r & HwPowerBrake) != 0) yield return "硬件功耗刹车 (HW Power Brake)";
        if ((r & SwThermalSlowdown) != 0) yield return "软件温度降频 (SW Thermal)";
        if ((r & HwThermalSlowdown) != 0) yield return "硬件温度降频 (HW Thermal)";
        if ((r & HwSlowdown) != 0) yield return "硬件减速 (HW Slowdown)";
        if ((r & SyncBoost) != 0) yield return "Sync Boost 生效";
        if ((r & ApplicationsClocks) != 0) yield return "应用时钟档位已设定";
        if ((r & DisplayClockSetting) != 0) yield return "显示时钟档位已设定";
        if ((r & GpuIdle) != 0) yield return "GPU 空闲";
    }

    private readonly record struct SmiRow(
        string Name, int CurrentClock, int MaxClock, int TemperatureC, int Utilization, int EncoderSessions,
        double? PowerDrawW, ulong? ThrottleReasons);

    private static async Task<SmiRow?> QuerySmiAsync(CancellationToken ct)
    {
        var exe = Locate();
        if (exe is null) return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in SmiArgs.Split(' ')) psi.ArgumentList.Add(a);

            using var p = new Process { StartInfo = psi };
            p.Start();
            var text = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);

            var first = text.Split('\r', '\n').FirstOrDefault(l => l.Trim().Length > 0);
            if (first is null) return null;
            var parts = first.Split(',').Select(x => x.Trim()).ToArray();
            if (parts.Length < 8) return null;

            return new SmiRow(
                parts[0],
                int.TryParse(parts[1], out var c) ? c : 0,
                int.TryParse(parts[2], out var m) ? m : 0,
                int.TryParse(parts[3], out var t) ? t : 0,
                int.TryParse(parts[4], out var u) ? u : 0,
                int.TryParse(parts[5], out var e) ? e : 0,
                double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : null,
                TryParseReasons(parts[7]));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                      or InvalidOperationException or System.IO.IOException)
        {
            return null;
        }
    }

    private static string? Locate()
    {
        foreach (var candidate in new[]
        {
            @"C:\Windows\System32\nvidia-smi.exe",
            @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
        })
            if (System.IO.File.Exists(candidate)) return candidate;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = System.IO.Path.Combine(dir.Trim(), "nvidia-smi.exe");
            if (System.IO.File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Honest degradation: no NVIDIA SMI means no PC-side evidence, not a pass. The result keeps
    /// the calling check's own id so the report never grows two identically-keyed rows.
    /// </summary>
    private static CheckResult NoSmi(string id) =>
        new(id, CheckStatus.Unknown,
            "读不到 GPU 运行状态（本机没有 nvidia-smi）",
            "编码会话数、实时频率与温度这三样只有 NVIDIA 工具链能读。",
            new Dictionary<string, string> { ["说明"] = "NVIDIA 显卡装驱动后通常自带 nvidia-smi；没有它一般是 AMD/Intel 或精简系统。" },
            Array.Empty<FixAction>(),
            "AMD/Intel 卡的对应信息在「任务管理器 → 性能」里看，工具暂时读不到。");
}