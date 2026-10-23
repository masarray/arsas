using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// P4E correlation boundary between an already-executed IEC 61850 control request and
/// the canonical Engineering feedback row. Correlation is identity-only: the control
/// model's explicit StatusReference is converted to IEDName + IEC Telegram and must match
/// exactly one canonical row. Missing, cross-IED, or duplicate matches fail closed.
///
/// This service owns no command transport, reconnect, polling, SCL parsing, or live rows.
/// </summary>
internal static class NativeFatCommandFeedbackCorrelation
{
    internal static Iec61850MonitorPoint? Resolve(
        Iec61850MonitorDevice device,
        Iec61850ControlCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(capabilities);

        return Resolve(device, capabilities.StatusReference);
    }

    internal static Iec61850MonitorPoint? Resolve(
        Iec61850MonitorDevice device,
        string? statusReference)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (string.IsNullOrWhiteSpace(device.Name) || string.IsNullOrWhiteSpace(statusReference))
            return null;

        var feedbackTelegram = Iec61850MonitorPoint.StripIedNamePrefix(
            statusReference,
            device.Name);
        if (!NativeFatCanonicalEvidenceOverlay.TryBuildRowKey(
                device.Name,
                feedbackTelegram,
                out var expectedKey))
        {
            return null;
        }

        Iec61850MonitorPoint? match = null;
        foreach (var point in device.Points)
        {
            if (!NativeFatCanonicalEvidenceOverlay.TryBuildRowKey(point, out var candidateKey) ||
                !string.Equals(candidateKey, expectedKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Ambiguous canonical identity is unsafe for FAT evidence correlation.
            // Never pick first/last/index ordering as a tiebreaker.
            if (match != null)
                return null;

            match = point;
        }

        return match;
    }
}
