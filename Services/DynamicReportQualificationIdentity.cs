using System.Security.Cryptography;
using System.Text;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal static class DynamicReportQualificationIdentity
{
    public static ArMms.MmsDynamicReportIedIdentity Build(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        var namedIedIdentity = FirstMeaningfulNamedIdentity(device.SclIedName, device.Name);
        var canonicalIedName = FirstNonEmpty(namedIedIdentity, device.DeviceId);
        if (string.IsNullOrWhiteSpace(canonicalIedName))
            throw new InvalidOperationException("Dynamic reporting qualification requires a stable IED name or device identity.");

        // ARSAS model defaults such as "IED" are UI placeholders, not device identity.
        // When no meaningful configured/discovered name exists, bind persisted evidence to
        // the explicit endpoint instead of allowing every default-named relay to collide.
        var stableKey = !string.IsNullOrWhiteSpace(namedIedIdentity)
            ? $"ied:{namedIedIdentity.Trim()}"
            : $"endpoint:{device.IpAddress.Trim()}:{device.Port}";

        // Fingerprint only canonical ARSAS model fields that are already persisted/discovered.
        // Selection state, display value, report coverage and UI-only text are intentionally
        // excluded so the same physical model remains compatible across operator selections.
        var signalLines = fullModelSignals
            .Where(signal => !string.IsNullOrWhiteSpace(signal.ObjectReference))
            .Select(signal => string.Join('|',
                signal.ObjectReference.Trim(),
                signal.FunctionalConstraint?.Trim() ?? string.Empty,
                signal.DataType?.Trim() ?? string.Empty,
                signal.LogicalNode?.Trim() ?? string.Empty,
                signal.Name?.Trim() ?? string.Empty,
                signal.QualityReference?.Trim() ?? string.Empty,
                signal.TimestampReference?.Trim() ?? string.Empty))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(line => line, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (signalLines.Length == 0)
            throw new InvalidOperationException("Dynamic reporting qualification requires the complete discovered model, not an empty or selection-only signal list.");

        var fingerprintMaterial = new StringBuilder()
            .AppendLine("ARIEC61850-G2-ID-v1")
            .AppendLine(canonicalIedName.Trim())
            .AppendLine(namedIedIdentity)
            .AppendLine(device.SclSourceSha256?.Trim() ?? string.Empty);

        foreach (var line in signalLines)
            fingerprintMaterial.AppendLine(line);

        var fingerprintBytes = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintMaterial.ToString()));
        var fingerprint = Convert.ToHexString(fingerprintBytes).ToLowerInvariant();

        return new ArMms.MmsDynamicReportIedIdentity
        {
            StableIdentityKey = stableKey,
            ModelFingerprint = $"sha256:{fingerprint}",
            Model = canonicalIedName.Trim(),
            ProfileRevision = device.SclSourceSha256?.Trim() ?? string.Empty
        };
    }

    private static string FirstMeaningfulNamedIdentity(params string?[] values)
        => values
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && !IsPlaceholderIdentity(value))
            ?.Trim() ?? string.Empty;

    private static bool IsPlaceholderIdentity(string value)
    {
        var normalized = value.Trim();
        return normalized.Equals("IED", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("New IED", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Device", StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
