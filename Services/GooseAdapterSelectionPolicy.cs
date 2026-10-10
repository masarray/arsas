using System.Net;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>A snapshot of Windows interface identity; deliberately independent of live OS APIs for replay tests.</summary>
public sealed record GooseWindowsInterfaceSnapshot(
    string Id,
    string Name,
    string Description,
    string MacAddress,
    IReadOnlyList<IPAddress> Ipv4Addresses);

public sealed record GooseAdapterRouteDecision(
    GooseAdapterOption? Adapter,
    bool IsSameHost,
    string Evidence)
{
    public bool HasAuthoritativeMatch => Adapter is not null;
}

/// <summary>
/// Pure identity-based GOOSE capture candidate selection. A successful MMS/IP route
/// is NOT evidence that an Ethernet GOOSE publisher exists on the capture interface.
/// The calling UI may start read-only observation only on an exact, unique match.
/// </summary>
public static class GooseAdapterSelectionPolicy
{
    public static GooseAdapterRouteDecision Resolve(
        IPAddress target,
        IPAddress? routedLocalAddress,
        IReadOnlyList<GooseWindowsInterfaceSnapshot> windowsInterfaces,
        IReadOnlyList<GooseAdapterOption> captureAdapters)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(windowsInterfaces);
        ArgumentNullException.ThrowIfNull(captureAdapters);

        var ownsTarget = windowsInterfaces.Where(nic => HasAddress(nic, target)).ToArray();
        var sameHost = IPAddress.IsLoopback(target) || ownsTarget.Length != 0;
        if (sameHost)
        {
            if (ownsTarget.Length != 1)
                return new(null, true,
                    "Same-PC MMS endpoint: no unique Windows adapter owns the target IP. Select a capture adapter manually; IP loopback does not prove Ethernet GOOSE.");
            return Match(ownsTarget[0], captureAdapters, true);
        }

        if (routedLocalAddress is null)
            return new(null, false, "MMS route unavailable. Select the actual GOOSE station adapter; a sole NIC is not sufficient evidence.");

        var routedInterfaces = windowsInterfaces.Where(nic => HasAddress(nic, routedLocalAddress)).ToArray();
        if (routedInterfaces.Length != 1)
            return new(null, false,
                "Windows MMS route did not resolve to one interface. Choose the GOOSE capture adapter manually.");

        return Match(routedInterfaces[0], captureAdapters, false);
    }

    /// <summary>The dedicated Npcap software pseudo-device, NOT a Windows KM-TEST virtual Ethernet NIC.</summary>
    public static bool IsNpcapPseudoLoopback(GooseAdapterOption adapter)
    {
        var name = adapter.Name.Trim();
        return name.Equals(@"\Device\NPF_Loopback", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NPF_Loopback", StringComparison.OrdinalIgnoreCase);
    }

    private static GooseAdapterRouteDecision Match(
        GooseWindowsInterfaceSnapshot nic,
        IReadOnlyList<GooseAdapterOption> adapters,
        bool sameHost)
    {
        var context = sameHost ? "Same-PC MMS simulator" : "Windows MMS route";
        var candidates = adapters.Where(adapter => !IsNpcapPseudoLoopback(adapter)).ToArray();
        var winGuid = ParseGuid(nic.Id);
        if (winGuid is not null)
        {
            var guidMatches = candidates.Where(adapter => ParseGuid(adapter.Name) == winGuid).ToArray();
            if (guidMatches.Length == 1)
                return new(guidMatches[0], sameHost,
                    $"{context}: exact Npcap/Windows interface GUID for {nic.Name}. GOOSE frames still unverified.");
            if (guidMatches.Length > 1)
                return new(null, sameHost,
                    $"{context}: multiple Npcap capture devices share the interface GUID. Choose manually.");
        }

        // MAC fallback is safe only when BOTH MACs are valid, the match is
        // unique, and a contradictory explicit interface GUID is absent.
        var mac = NormalizeValidMac(nic.MacAddress);
        if (mac is not null)
        {
            var macMatches = candidates.Where(adapter =>
                string.Equals(NormalizeValidMac(adapter.MacAddress), mac, StringComparison.Ordinal) &&
                (winGuid is null || ParseGuid(adapter.Name) is null)).ToArray();
            if (macMatches.Length == 1)
                return new(macMatches[0], sameHost,
                    $"{context}: one non-conflicting Npcap adapter matched the Windows interface MAC for {nic.Name}. GOOSE frames still unverified.");
            if (macMatches.Length > 1)
                return new(null, sameHost,
                    $"{context}: several capture adapters share the MAC. Choose the GOOSE adapter manually.");
        }

        return new(null, sameHost,
            $"{context}: no unique GUID/MAC-matched Npcap adapter for {nic.Name}. " +
            (sameHost
                ? "KM-TEST is eligible when Npcap exposes its real Ethernet interface, but the Npcap IP-loopback pseudo-device is not equivalent."
                : "The IP route is only a hint; choose the actual station Ethernet adapter."));
    }

    private static bool HasAddress(GooseWindowsInterfaceSnapshot nic, IPAddress address)
        => nic.Ipv4Addresses.Any(candidate => candidate.Equals(address));

    private static Guid? ParseGuid(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;
        var first = source.IndexOf('{');
        if (first < 0)
            return Guid.TryParse(source, out var standalone) ? standalone : null;
        var last = source.IndexOf('}', first + 1);
        return last > first && Guid.TryParse(source.AsSpan(first, last - first + 1), out var guid)
            ? guid : null;
    }

    private static string? NormalizeValidMac(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var mac = new string(input.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
        return mac.Length is 12 or 16 && mac.Any(ch => ch != '0') && mac.Any(ch => ch != 'F')
            ? mac : null;
    }
}
