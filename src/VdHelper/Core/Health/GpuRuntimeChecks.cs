using System.Diagnostics;
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
        "--query-gpu=name,clocks.current.graphics,clocks.max.graphics,temperature.gpu," +
        "utilization.gpu,encoder.stats.sessionCount --format=csv,noheader,nounits";

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
                };

                if (f.TemperatureC >= 85)
                    return new CheckResult("gpu-throttle", CheckStatus.Warn,
                        $"GPU 温度 {f.TemperatureC}°C，当前频率只有最高频率的 {ratio:P0}",
                        "高温 + 频率上不去 = 典型的降频。画面会卡、编码延迟会涨、画质会掉。",
                        ev, Array.Empty<FixAction>(),
                        "先看散热：清灰、垫高、进风口是否被挡。这类「画质莫名变差」的帖子最后往往落到这一步。");

                if (ratio > 0 && ratio < 0.8)
                    return new CheckResult("gpu-throttle", CheckStatus.Warn,
                        $"GPU 跑在最高频率的 {ratio:P0}（{f.CurrentClock}/{f.MaxClock} MHz），温度只有 {f.TemperatureC}°C",
                        "温度不高却上不了满频，通常是**功耗墙**而不是过热：混合输出、独显没接在满功耗档、"
                        + "或者驱动限了。编码器跟着一起慢下来，码率就上不去。",
                        ev, Array.Empty<FixAction>(),
                        "检查笔记本是不是插电、显卡驱动有没有限功耗；这一项与「码率上不去」直接相关。");

                return new CheckResult("gpu-throttle", CheckStatus.Pass,
                    $"GPU 频率正常（{f.CurrentClock}/{f.MaxClock} MHz = {ratio:P0}，{f.TemperatureC}°C）",
                    "没有被温度或功耗压着。", ev, Array.Empty<FixAction>());
            });

    private readonly record struct SmiRow(
        string Name, int CurrentClock, int MaxClock, int TemperatureC, int Utilization, int EncoderSessions);

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
            if (parts.Length < 6) return null;

            return new SmiRow(
                parts[0],
                int.TryParse(parts[1], out var c) ? c : 0,
                int.TryParse(parts[2], out var m) ? m : 0,
                int.TryParse(parts[3], out var t) ? t : 0,
                int.TryParse(parts[4], out var u) ? u : 0,
                int.TryParse(parts[5], out var e) ? e : 0);
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