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
            device.SclIedName,
            device.SclAccessPointName);
    }

    /// <summary>
    /// IP-only discovery may reuse a prior user-opened IID only after its SCL-assisted
    /// MMS association was successful, its original bytes still match the pinned
    /// SHA-256, and its IED/AP/LD domain topology uniquely matches this live session.
    /// No IP-only guess, vendor heuristic or SCL-assisted data acquisition is enabled.
    /// </summary>
    public static async Task<RememberedTrustedSclMatch?> TryMatchRememberedAsync(
        Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument liveModel,
        CancellationToken cancellationToken = default,
        string? bindingStorePath = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(liveModel);

        var candidates = bindingStorePath is null
            ? SclEndpointBindingStore.FindVerifiedSourcesForEndpoint(device.IpAddress, device.Port)
            : SclEndpointBindingStore.FindVerifiedSourcesForEndpointAtPath(
                bindingStorePath, device.IpAddress, device.Port);
        // Never pick the "most recent" plausible model from an ambiguous or
        // pathological endpoint history. Capped work protects discovery latency.
        if (candidates.Count == 0 || candidates.Count > 16)
            return null;

        var domains = liveModel.LogicalDevices.Select(ld => ld.MmsDomain).ToArray();
        RememberedTrustedSclMatch? unique = null;
        var ns = (XNamespace)"http://www.iec.ch/61850/2003/SCL";

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var source = await VerifiedSclSourceLoader.LoadAsync(
                    candidate.SourcePath, candidate.SourceSha256, cancellationToken)
                    .ConfigureAwait(false);
                var document = XDocument.Parse(source.Xml, LoadOptions.PreserveWhitespace);

                // Engine matcher guards global IED/AP ambiguity. Additionally, only
                // a single explicit AP for the remembered IED is eligible until an
                // independently authenticated AP identifier is observable from MMS.
                var matchingIeds = document.Root?.Elements(ns + "IED")
                    .Where(ied => string.Equals(
                        (string?)ied.Attribute("name"), candidate.IedName,
                        StringComparison.OrdinalIgnoreCase)).ToArray()
                    ?? Array.Empty<XElement>();
                if (matchingIeds.Length != 1)
                    continue;
                var accessPoints = matchingIeds[0].Elements(ns + "AccessPoint").ToArray();
                if (accessPoints.Length != 1 || !string.Equals(
                    (string?)accessPoints[0].Attribute("name"),
                    candidate.AccessPointName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var identity = TrustedSclIedIdentityMatcher.TryMatch(
                    document, domains, device.IpAddress, candidate.IedName,
                    candidate.AccessPointName);
                if (identity is null ||
                    identity.Source != "TrustedSclExactDomainMatch" ||
                    identity.Confidence != LiveIedDiscoveryConfidenceLevel.High)
                    continue;

                if (unique is not null)
                    return null; // competing qualified IID sources: fail closed
                unique = new RememberedTrustedSclMatch(
                    identity, source.Sha256, candidate.AccessPointName);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or InvalidDataException or
                    System.Xml.XmlException or ArgumentException)
            {
                // Missing/edited/malformed remembered files cannot rename a live IED.
                continue;
            }
        }
        return unique;
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
            LogicalDevices=domains.Distinct(StringComparer.Ordinal).Select(domain =>
                new Iec61850LogicalDeviceIdentity
                {
                    Domain=domain,
                    Instance=verified.LogicalDeviceAliases.TryGetValue(domain,out var alias)
                        ? alias : throw new InvalidDataException($"Verified LD mapping missing '{domain}'.")
                }).ToArray()
        };
    }
}

/// <summary>Association-local proof, not a persisted or inferred IEC wire name.</summary>
public sealed record RememberedTrustedSclMatch(
    LiveIedIdentity Identity, string SourceSha256, string AccessPointName);
