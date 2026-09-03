using ArBinding = AR.Iec61850.Binding;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed record DynamicReportShadowPollCompanionEvidence(
    string? Quality,
    DateTimeOffset? DeviceTimestampUtc,
    string QualityReference,
    string TimestampReference,
    bool QualityReadAttempted,
    bool TimestampReadAttempted);

/// <summary>
/// Bounded read-only companion collector for the G2.6 independent MMS polling authority.
/// It derives only known IEC 61850 data-object sibling q/t paths from an exact persisted
/// process member, resolves them against the already-discovered live MMS directory, and
/// performs at most one q read plus one t read. Missing, unreadable, or undecodable
/// companions remain missing; no receive time, report metadata, or other fallback is used.
/// </summary>
internal static class DynamicReportShadowPollingCompanionReader
{
    private static readonly string[] KnownValueSuffixes =
    {
        ".instCVal.mag.f",
        ".cVal.mag.f",
        ".instMag.f",
        ".mag.f",
        ".stVal",
        ".general",
        ".dirGeneral",
        ".phsA",
        ".dirPhsA",
        ".phsB",
        ".dirPhsB",
        ".phsC",
        ".dirPhsC"
    };

    internal static async Task<DynamicReportShadowPollCompanionEvidence> ReadAsync(
        ArMms.MmsClientSession session,
        ArMms.MmsIedModelDirectory directory,
        ArMms.MmsFcResolvedPoint point,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(point);

        if (!TryBuildCompanionReferences(point.MmsReference, out var qualityReference, out var timestampReference))
            return Missing(string.Empty, string.Empty, false, false);

        string? quality = null;
        DateTimeOffset? deviceTimestampUtc = null;
        var qualityAttempted = false;
        var timestampAttempted = false;

        if (TryResolveExactCompanion(directory, qualityReference, point.FunctionalConstraint, out var qualityPoint))
        {
            qualityAttempted = true;
            try
            {
                var read = await session
                    .ReadSingleVariableAsync(qualityPoint.ToObjectReference(), cancellationToken)
                    .ConfigureAwait(false);
                if (read.IsSuccess && read.Value is not null)
                {
                    var decoded = ArBinding.Iec61850QualityDecoder.Decode(read.Value);
                    if (decoded.IsDecoded)
                        quality = decoded.Validity;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
            {
                // Companion metadata is optional evidence. A failed q read remains missing;
                // it must never be substituted from report-side or host-side metadata.
            }
        }

        if (TryResolveExactCompanion(directory, timestampReference, point.FunctionalConstraint, out var timestampPoint))
        {
            timestampAttempted = true;
            try
            {
                var read = await session
                    .ReadSingleVariableAsync(timestampPoint.ToObjectReference(), cancellationToken)
                    .ConfigureAwait(false);
                if (read.IsSuccess && read.Value is not null)
                {
                    var decoded = ArBinding.Iec61850TimestampDecoder.Decode(read.Value);
                    if (decoded.IsDecoded && TryFindUtcTime(read.Value, out var utcTime))
                        deviceTimestampUtc = utcTime.Value.ToUniversalTime();
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
            {
                // Same fail-closed rule as q: no observedAt/receive-time fallback.
            }
        }

        return new DynamicReportShadowPollCompanionEvidence(
            quality,
            deviceTimestampUtc,
            qualityReference,
            timestampReference,
            qualityAttempted,
            timestampAttempted);
    }

    internal static bool TryBuildCompanionReferences(
        string valueReference,
        out string qualityReference,
        out string timestampReference)
    {
        var normalized = (valueReference ?? string.Empty).Trim().Replace('$', '.');
        foreach (var suffix in KnownValueSuffixes)
        {
            if (!normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            var dataObjectReference = normalized[..^suffix.Length];
            if (string.IsNullOrWhiteSpace(dataObjectReference) || !dataObjectReference.Contains('/'))
                break;

            qualityReference = dataObjectReference + ".q";
            timestampReference = dataObjectReference + ".t";
            return true;
        }

        qualityReference = string.Empty;
        timestampReference = string.Empty;
        return false;
    }

    private static bool TryResolveExactCompanion(
        ArMms.MmsIedModelDirectory directory,
        string reference,
        string expectedFunctionalConstraint,
        out ArMms.MmsFcResolvedPoint point)
    {
        point = null!;
        if (string.IsNullOrWhiteSpace(reference) || !directory.TryFindByMmsReference(reference, out var resolved))
            return false;

        if (resolved.IsControlAttribute || resolved.IsReportAttribute ||
            !resolved.FunctionalConstraint.Equals(expectedFunctionalConstraint, StringComparison.OrdinalIgnoreCase) ||
            !NormalizeReference(resolved.MmsReference).Equals(NormalizeReference(reference), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        point = resolved;
        return true;
    }

    private static bool TryFindUtcTime(ArMms.MmsDataValue value, out ArMms.Iec61850UtcTime utcTime)
    {
        if (value.Kind == ArMms.MmsDataKind.UtcTime && value.Value is ArMms.Iec61850UtcTime direct)
        {
            utcTime = direct;
            return true;
        }

        if (value.Kind is ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array)
        {
            foreach (var child in value.Children)
            {
                if (TryFindUtcTime(child, out utcTime))
                    return true;
            }
        }

        utcTime = default;
        return false;
    }

    private static DynamicReportShadowPollCompanionEvidence Missing(
        string qualityReference,
        string timestampReference,
        bool qualityAttempted,
        bool timestampAttempted)
        => new(null, null, qualityReference, timestampReference, qualityAttempted, timestampAttempted);

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');
}
