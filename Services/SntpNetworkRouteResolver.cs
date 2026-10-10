using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ArIED61850Tester.Services;

public sealed record SntpNetworkBinding(
    IPAddress LocalAddress,
    IPAddress SubnetMask,
    IPAddress? DirectedBroadcast,
    string InterfaceName,
    string InterfaceId)
{
    public string Summary => DirectedBroadcast == null
        ? $"{InterfaceName} • {LocalAddress}"
        : $"{InterfaceName} • {LocalAddress} → {DirectedBroadcast}";
}

/// <summary>
/// Selects the Windows IPv4 interface that routes to an IED and derives its directed broadcast address.
/// </summary>
public static class SntpNetworkRouteResolver
{
    /// <summary>
    /// Enumerates the PC's actual active IPv4 unicast addresses. Never uses an
    /// IED endpoint as a substitute for a server listen address. Keeps aliases.
    /// </summary>
    public static IReadOnlyList<SntpNetworkBinding> GetLocalBindings()
        => GetIpv4Candidates()
            .Select(Build)
            .OrderBy(item => item.InterfaceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.LocalAddress.ToString(), StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Re-checks the chosen address and its adapter immediately before binding.
    /// Detects disconnected NICs, stale saved IPs and moved alias addresses.
    /// </summary>
    public static SntpNetworkBinding ResolveForLocal(IPAddress localAddress, string? interfaceId = null)
        => SelectLocalBinding(localAddress, interfaceId, GetLocalBindings());

    /// <summary>
    /// Pure canonical IP+NIC selector so alias and duplicate-interface cases
    /// can be unit-tested without touching the host network or UDP port.
    /// </summary>
    public static SntpNetworkBinding SelectLocalBinding(
        IPAddress localAddress, string? interfaceId, IReadOnlyList<SntpNetworkBinding> available)
    {
        ArgumentNullException.ThrowIfNull(localAddress);
        ArgumentNullException.ThrowIfNull(available);
        if (localAddress.AddressFamily != AddressFamily.InterNetwork ||
            IPAddress.IsLoopback(localAddress) || localAddress.Equals(IPAddress.Any))
            throw new ArgumentException("Select an active non-loopback PC IPv4 address.", nameof(localAddress));
        var matches = available
            .Where(item => item.LocalAddress.Equals(localAddress) &&
                (interfaceId is null || item.InterfaceId.Equals(interfaceId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"PC address {localAddress} is not uniquely assigned to an active network interface. Refresh the IP list.");
        return matches[0];
    }

    public static SntpNetworkBinding ResolveForRemote(IPAddress remoteAddress)
    {
        ArgumentNullException.ThrowIfNull(remoteAddress);
        if (remoteAddress.AddressFamily != AddressFamily.InterNetwork)
            throw new NotSupportedException("ARSAS SNTP P0 currently serves IPv4 station-bus endpoints.");

        var interfaces = GetIpv4Candidates();

        // Ask Windows which local address it would route to the IED. UDP Connect does not
        // transmit a datagram; it only selects a route/local endpoint for this socket.
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(new IPEndPoint(remoteAddress, 123));
            if (probe.LocalEndPoint is IPEndPoint localEndPoint)
            {
                var routed = interfaces.FirstOrDefault(candidate => candidate.Address.Equals(localEndPoint.Address));
                if (routed != null)
                    return Build(routed);
            }
        }
        catch (SocketException)
        {
            // Continue with a deterministic same-subnet fallback below.
        }

        foreach (var candidate in interfaces)
        {
            if (IsSameSubnet(candidate.Address, remoteAddress, candidate.Mask))
                return Build(candidate);
        }

        throw new InvalidOperationException($"Windows could not resolve an active IPv4 station-bus route to {remoteAddress}.");
    }

    public static IPAddress? ComputeDirectedBroadcast(IPAddress address, IPAddress mask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        if (addressBytes.Length != 4 || maskBytes.Length != 4)
            throw new NotSupportedException("Directed broadcast calculation currently supports IPv4 only.");

        var hostBits = 0;
        var result = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            hostBits += 8 - CountBits(maskBytes[i]);
            result[i] = (byte)(addressBytes[i] | ~maskBytes[i]);
        }

        // /31 and /32 have no useful directed broadcast target for this workflow.
        return hostBits < 2 ? null : new IPAddress(result);
    }

    public static bool IsSameSubnet(IPAddress first, IPAddress second, IPAddress mask)
    {
        var firstBytes = first.GetAddressBytes();
        var secondBytes = second.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        if (firstBytes.Length != 4 || secondBytes.Length != 4 || maskBytes.Length != 4)
            return false;

        for (var i = 0; i < 4; i++)
        {
            if ((firstBytes[i] & maskBytes[i]) != (secondBytes[i] & maskBytes[i]))
                return false;
        }

        return true;
    }

    private static List<Ipv4Candidate> GetIpv4Candidates()
    {
        var candidates = new List<Ipv4Candidate>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                    unicast.IPv4Mask == null ||
                    IPAddress.Any.Equals(unicast.Address) ||
                    IPAddress.Loopback.Equals(unicast.Address))
                    continue;

                candidates.Add(new Ipv4Candidate(
                    unicast.Address,
                    unicast.IPv4Mask,
                    networkInterface.Name,
                    networkInterface.Id));
            }
        }

        return candidates;
    }

    private static SntpNetworkBinding Build(Ipv4Candidate candidate)
        => new(
            candidate.Address,
            candidate.Mask,
            ComputeDirectedBroadcast(candidate.Address, candidate.Mask),
            candidate.InterfaceName,
            candidate.InterfaceId);

    private static int CountBits(byte value)
    {
        var count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }

        return count;
    }

    private sealed record Ipv4Candidate(
        IPAddress Address,
        IPAddress Mask,
        string InterfaceName,
        string InterfaceId);
}
