using AR.Iec61850.Binding;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// Uses the ARIEC61850 domain-set identity resolver as the single source of truth.
/// MMS Logical Device domains commonly contain IEDName + LDInst; they must not be
/// truncated at the first digit because product names frequently contain digits.
/// </summary>
public static class Iec61850DeviceIdentityResolver
{
    public static Iec61850DeviceIdentity Resolve(
        ArMms.MmsDiscoveryResult? discovery,
        LiveIedModelDiscoveryDocument? liveModel,
        IEnumerable<SignalDefinition> signals)
    {
        var domains = new List<string>();
        if (liveModel != null)
            domains.AddRange(liveModel.LogicalDevices.Select(x => x.MmsDomain));
        if (discovery != null)
            domains.AddRange(discovery.IedDirectory.LogicalDevices.Keys);
        domains.AddRange(signals.Select(signal => ExtractDomain(signal.ObjectReference)));

        var normalizedDomains = domains
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var host = liveModel?.Host ?? string.Empty;
        var engineIdentity = Iec61850IdentityResolver.ResolveFromDomains(
            normalizedDomains,
            host,
            liveModel?.IedName);

        return new Iec61850DeviceIdentity
        {
            IedName = engineIdentity.IedName,
            Source = $"ARIEC61850 {engineIdentity.Source} ({engineIdentity.Confidence})",
            LogicalDevices = normalizedDomains
                .Select(domain => new Iec61850LogicalDeviceIdentity
                {
                    Domain = domain,
                    Instance = Iec61850IdentityResolver.DisplayLogicalDevice(engineIdentity, domain)
                })
                .ToArray()
        };
    }

    private static string ExtractDomain(string? objectReference)
    {
        var text = (objectReference ?? string.Empty).Trim().Replace('$', '.');
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var slash = text.IndexOf('/');
        return slash > 0 ? text[..slash] : string.Empty;
    }
}

public sealed class Iec61850DeviceIdentity
{
    public string IedName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public IReadOnlyList<Iec61850LogicalDeviceIdentity> LogicalDevices { get; init; } = Array.Empty<Iec61850LogicalDeviceIdentity>();
}

public sealed class Iec61850LogicalDeviceIdentity
{
    public string Domain { get; init; } = string.Empty;
    public string Instance { get; init; } = string.Empty;
}
