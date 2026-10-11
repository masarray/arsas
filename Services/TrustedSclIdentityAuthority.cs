using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using System.Xml.Linq;

namespace ArIED61850Tester.Services;

/// <summary>
/// SCL identity is authoritative only if it is the same SHA-256 pinned source
/// already opened by the operator AND its complete IED/AP/LD topology matches
/// the observed MMS domains. This is deliberately outside the hot report path.
/// </summary>
public static class TrustedSclIdentityAuthority
{
    public static async Task<LiveIedIdentity?> TryMatchAsync(
        Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument liveModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(liveModel);
        if (string.IsNullOrWhiteSpace(device.SclSourcePath) ||
            string.IsNullOrWhiteSpace(device.SclSourceSha256))
            return null;

        var verified = await VerifiedSclSourceLoader.LoadAsync(
            device.SclSourcePath,device.SclSourceSha256,cancellationToken).ConfigureAwait(false);
        var source = XDocument.Parse(verified.Xml, LoadOptions.PreserveWhitespace);
        return TrustedSclIedIdentityMatcher.TryMatch(
            source,
            liveModel.LogicalDevices.Select(ld => ld.MmsDomain),
            device.IpAddress,
            device.SclIedName);
    }

    public static Iec61850DeviceIdentity ToConsumerIdentity(
        LiveIedIdentity verified, IEnumerable<string> domains)
    {
        ArgumentNullException.ThrowIfNull(verified);
        if (verified.Source != "TrustedSclExactDomainMatch" ||
            verified.Confidence != LiveIedDiscoveryConfidenceLevel.High)
            throw new InvalidDataException("SCL model identity is not verified.");

        return new Iec61850DeviceIdentity
        {
            IedName=verified.IedName,
            Source=$"ARIEC61850 {verified.Source} ({verified.Confidence})",
            LogicalDevices=domains.Distinct(StringComparer.OrdinalIgnoreCase).Select(domain =>
                new Iec61850LogicalDeviceIdentity
                {
                    Domain=domain,
                    Instance=verified.LogicalDeviceAliases.TryGetValue(domain,out var alias)
                        ? alias : throw new InvalidDataException($"Verified LD mapping missing '{domain}'.")
                }).ToArray()
        };
    }
}
