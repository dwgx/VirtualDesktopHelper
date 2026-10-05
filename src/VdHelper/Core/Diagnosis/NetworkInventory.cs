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

    /// <summary>True when nothing can bind the port, i.e. something already listens on it.</summary>
    public static bool IsListening(int port)
    {
        try
        {
            using var probe = new TcpListener(IPAddress.Loopback, port);
            probe.Start();
            probe.Stop();
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }

    /// <summary>
    /// Owner process for busy ports. Spawns one PowerShell query and is only called when at least
    /// one port is already busy — the common case stays in-process.
    /// </summary>
    public static async Task<IReadOnlyDictionary<int, string>> BusyPortOwnersAsync(
        IEnumerable<int> ports, CancellationToken ct = default)
    {
        var list = string.Join(",", ports);
        var script = $"Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | "
            + $"Where-Object {{ $_.LocalPort -in @({list}) }} | ForEach-Object {{ "
            + "'$($_.LocalPort)|' + (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName }}";
        var lines = await Checks.PowerShellRunner.LinesAsync(script, ct).ConfigureAwait(false);
        var map = new Dictionary<int, string>();
        foreach (var line in lines)
        {
            var parts = line.Split('|', 2);
            if (parts.Length == 2 && int.TryParse(parts[0], out var port))
                map[port] = string.IsNullOrWhiteSpace(parts[1]) ? "未知进程" : parts[1] + ".exe";
        }
        return map;
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