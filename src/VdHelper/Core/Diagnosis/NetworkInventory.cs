using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VdHelper.Core.Diagnosis;

/// <summary>One adapter as VDHelper sees it, before any judgement.</summary>
public sealed record AdapterView(
    string Name,
    string Description,
    OperationalStatus Status,
    string Mac,
    IReadOnlyList<IPAddress> Addresses,
    bool IsLoopback,
    bool IsVirtual,
    bool IsPseudo,
    IReadOnlyList<IPAddress> Gateways)
{
    /// <summary>A real adapter a user owns; advice to disable these is meaningful.</summary>
    public bool IsUserAdapter => !IsLoopback && !IsPseudo;

    public bool IsUp => Status == OperationalStatus.Up;

    /// <summary>169.254.0.0/16 — DHCP failure, no usable LAN address.</summary>
    public IReadOnlyList<IPAddress> ApipaAddresses =>
        Addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork && IsApipa(a)).ToList();

    private static bool IsApipa(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return b.Length == 4 && b[0] == 169 && b[1] == 254;
    }

    public IPAddress? PrimaryIPv4 =>
        Addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IsApipa(a));

    public bool HasDefaultGateway => Gateways.Count > 0;
}

/// <summary>
/// Reads adapters in-process. Name heuristics are deliberately conservative: they exist to explain
/// a check result, never to mutate anything.
/// </summary>
/// <summary>What a socket on a VD port is doing right now.</summary>
public enum PortState { Free, Bound, Listen, Established }

public sealed record PortView(int Port, PortState State, string Owner, string Peer, DateTime? Since);

public static class NetworkInventory
{
    private static readonly string[] VirtualHints =
    [
        "virtual", "hyper-v", "vethernet", "vmware", "virtualbox", "vpn", "tap", "tunnel",
        "loopback", "bluetooth", "wsan", "wsl", "zerotier", "tailscale", "docker",
    ];

    /// <summary>
    /// Windows exposes WFP/Hyper-V filter miniports through the same API as real NICs. They are
    /// not network adapters a user can use, and "disable this adapter" advice about them is
    /// actively harmful — so they are classified separately and never offered as fixes.
    /// </summary>
    private static readonly string[] PseudoHints =
    [
        "lightweight filter", "filter-", "vswitch", "wfp", "npcap", "qos packet scheduler",
        "pseudo-interface", "loopback", "teredo", "isatap", "local area connection",
    ];

    public static IReadOnlyList<AdapterView> ReadAdapters()
    {
        var list = new List<AdapterView>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            IPInterfaceProperties props;
            try { props = ni.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }
            catch (PlatformNotSupportedException) { continue; }

            var name = ni.Name;
            var desc = ni.Description;
            var mac = string.Join('-', ni.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));

            var gateways = new List<IPAddress>();
            try
            {
                if (props.GatewayAddresses is { Count: > 0 } gw)
                    gateways.AddRange(gw.Select(g => g.Address).Where(a => a is not null).Select(a => a!));
            }
            catch (NetworkInformationException) { }
            catch (PlatformNotSupportedException) { }
            var hay = (name + " " + desc).ToLowerInvariant();
            list.Add(new AdapterView(
                name,
                desc,
                ni.OperationalStatus,
                mac,
                props.UnicastAddresses.Select(u => u.Address).ToList(),
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback,
                VirtualHints.Any(h => hay.Contains(h, StringComparison.Ordinal)),
                PseudoHints.Any(h => hay.Contains(h, StringComparison.Ordinal)),
                gateways));
        }
        return list;
    }

    public static IReadOnlyList<AdapterView> PrimaryCandidates() =>
        ReadAdapters().Where(a => a.IsUp && !a.IsLoopback && a.PrimaryIPv4 is not null).ToList();

    public static IReadOnlyList<int> VdPorts { get; } = [38810, 38820, 38830, 38840];

    // Verified against Windows on a live session: local 38810/20/30/40 -> 192.168.11.14 on
    // 37455/40227/39715/38891, all Established. Note the direction before trying to reproduce it —
    // those are the LOCAL ports the Streamer binds, and the headset answers on its own ephemeral
    // ports. Filtering on RemotePort instead of LocalPort returns zero, which looks exactly like a
    // session that never existed. See notes/2026-10-06-live-session-and-peer-direction.md.
    /// <summary>
    /// Same /24 — enough to tell a LAN peer from a cloud relay, and no more than claimed.
    /// <para>
    /// One definition, because two checks were answering opposite questions about the same socket
    /// set. The Streamer opens an outbound connection to public endpoints such as 40.89.161.236:38812
    /// whenever it starts; that is Established and it is not a stream. session-stale said so in a
    /// paragraph of comment while udp-discovery, reading the same PortState.Established set at the
    /// same moment, printed 串流中. session-stale had its own private copy of this test, which is how
    /// the two drifted apart.
    /// </para>
    /// </summary>
    public static bool IsLanPeer(System.Net.IPAddress remote, System.Net.IPAddress local)
    {
        var x = remote.GetAddressBytes();
        var y = local.GetAddressBytes();
        if (x.Length != 4 || y.Length != 4) return false;
        for (var i = 0; i < 3; i++) if (x[i] != y[i]) return false;
        return true;
    }

    /// <summary>
    /// One snapshot of every socket on the VD ports, in ANY state.
    /// <para>
    /// The earlier version filtered to <c>-State Listen</c> and therefore reported all four
    /// ports as free while the Streamer was actually holding them as Bound sockets and had a
    /// live session up. Binding a probe socket was worse: it fails with WSAEACCES whenever the
    /// port is held at all, which reads exactly like "the port is busy" but cannot tell who holds
    /// it. So: ask Windows once, look at every state, and report the state rather than guessing.
    /// </para>
    /// </summary>
    public static async Task<(IReadOnlyList<PortView> Ports, string Raw)> ObserveVdPortsAsync(
        IEnumerable<int> ports, CancellationToken ct = default)
    {
        var wanted = ports.ToHashSet();
        var script =
            "Get-NetTCPConnection -ErrorAction SilentlyContinue | "
            + "Where-Object { $_.LocalPort -in @(" + string.Join(",", wanted) + ") } | "
            + "ForEach-Object { \"$($_.LocalPort)|$($_.State)|$($_.OwningProcess)|\" "
            + "+ \"$($_.RemoteAddress):$($_.RemotePort)|\" "
            + "+ \"$(($_.CreationTime).ToString('yyyy-MM-dd HH:mm:ss'))|\" "
            + "+ (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName }";

        var lines = await Checks.PowerShellRunner.LinesAsync(script, ct).ConfigureAwait(false);
        var views = new List<PortView>();
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 6 || !int.TryParse(parts[0].Trim(), out var port)) continue;
            var state = parts[1].Trim() switch
            {
                "Established" => PortState.Established,
                "Listen" => PortState.Listen,
                "Bound" => PortState.Bound,
                _ => PortState.Bound,
            };
            var since = DateTime.TryParse(parts[4].Trim(), out var parsed) ? parsed : (DateTime?)null;
            views.Add(new PortView(port, state, parts[5].Trim(), parts[3].Trim(), since));
        }

        // One row per port: an Established session outranks a Bind socket on the same port.
        var merged = new List<PortView>();
        foreach (var port in wanted)
        {
            var forPort = views.Where(v => v.Port == port).ToList();
            merged.Add(forPort.FirstOrDefault(v => v.State == PortState.Established)
                       ?? forPort.FirstOrDefault(v => v.State == PortState.Listen)
                       ?? forPort.FirstOrDefault(v => v.State == PortState.Bound)
                       ?? new PortView(port, PortState.Free, "-", "-", null));
        }
        return (merged, string.Join(" ;; ", lines));
    }

    public static string? ResolveProcessName(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return p.ProcessName + ".exe";
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}