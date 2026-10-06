﻿namespace VdHelper.Core.Model;

/// <summary>
/// The symptom classes users actually type, in their own words, taken from
/// research/09-failure-corpus/02-symptom-to-rootcause.md §1. Every one of those phrases is a real
/// quote from the corpus. Community triage starts by splitting "cannot see the PC" from "can see
/// it but cannot connect" because the two have almost disjoint root causes; a tool that asks
/// this one question halves the search space before it runs anything.
/// </summary>
public sealed record SymptomClass(
    string Id,
    string Title,
    IReadOnlyList<string> UserPhrases,
    IReadOnlyList<string> RelevantChecks,
    string FirstLook)
{
    public string PhraseLine => string.Join(" / ", UserPhrases);
}

public static class SymptomCatalog
{
    public static IReadOnlyList<SymptomClass> All { get; } =
    [
        new("S1", "头显里看不见电脑",
            ["no computer found", "no computer detected", "big yellow sign", "doesn't see the PC",
             "servers unreachable, only showing local computers"],
            ["streamer-proc", "udp-discovery", "svc-vd", "svc-log", "cfg-streamer", "accounts-persisted",
             "vpn-proc", "headset-deep", "lan-reach", "fw-vd", "fw-pair", "net-profile"],
            "先看 PC 到底有没有在广播：Streamer 进程、UDP 38850、服务是否被拉起来。这一类是「PC 根本没出现」，不是网络慢。"),

        new("S2", "能看到但连不上",
            ["computer is unreachable", "PC is unreachable", "failed to see any computer at all",
             "can't connect to a computer contact support"],
            ["session-stale", "port-vd", "lan-reach", "usb-headset", "headset-deep", "fw-pair", "fw-vd", "fw-outbound",
             "fw-profile-inbound", "net-profile", "route-metric", "vpn-proc", "net-apipa",
             "net-virtual", "ics", "nat-type"],
            "电脑已经被发现，问题在后面的握手：先确认头显 IP 能不能 ping 通，再看防火墙与出口网卡。"),

        new("S3", "说不在同一网络",
            ["not on same network", "not on same local network"],
            ["lan-reach", "route-metric", "net-primary", "net-profile", "net-virtual"],
            "这一类是子网判定：用户以为同网和实际同网经常不一致，填了头显 IP 就能直接判。"),

        new("S4", "卡在测带宽",
            ["stuck on measuring bandwidth", "won't get past measuring bandwidth", "keeps measuring bandwidth"],
            ["wifi-quality", "net-loss", "link-rate", "cfg-streamer", "cfg-version", "proc-tuner",
             "av", "link-type", "vpn-proc", "net-apipa"],
            "带宽探测阶段就失败：先看网卡协商速率和自动调码率开关，不是渲染问题。"),

        new("S5", "有画面但黑的 / 没画面",
            ["black screen", "blank screen", "I can hear audio but no visuals", "shows no display"],
            ["gpu-encoder", "gpu-pick", "display-inventory", "rdp-session", "session-stale", "link-type",
             "cfg-streamer", "svc-log", "streamer-proc", "net-primary"],
            "「有声音没画面」是显示链路问题：先查有没有活动 RDP 会话占着显示器，再查 PC 是不是走了无线。"),

        new("S6", "连上就掉 / 定时卡",
            ["keeps disconnecting", "disconnects after 30 seconds", "freezes exactly at 5-7 mins",
             "60-second stutter", "worked for hours the first time, never since"],
            ["session-stale", "streamer-proc", "svc-log", "fw-defender", "net-apipa",
             "net-virtual", "ics", "lan-reach"],
            "周期性掉线要先看服务与网络抖动的来源；注意不同用户的周期完全不同，别用同一个结论套。"),

        new("S7", "画质差 / 不跟手",
            ["choppy", "stutter", "bitrate only 60-70", "black bars when turning my head", "blurry"],
            ["wifi-quality", "gpu-throttle", "gpu-encoder", "net-loss", "link-rate", "link-type", "gpu-pick", "cfg-streamer",
             "cfg-version", "proc-tuner", "route-metric", "vpn-proc", "av", "nic-powersave"],
            "画质与延迟：链路速率、接入方式、码率与编解码设置是第一梯队，别一上来换路由器。"),
    ];

    public static SymptomClass? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(s => s.Id == id);

    /// <summary>Checks that matter for a symptom, in the order the tool should walk them.</summary>
    public static IReadOnlyList<string> ChecksFor(string? symptomId) =>
        Find(symptomId)?.RelevantChecks ?? Array.Empty<string>();
}