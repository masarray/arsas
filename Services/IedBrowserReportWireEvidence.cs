using AR.Iec61850.Mms;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Protocol.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// Observe original InformationReport payload, not the flattened projector's
/// "good"/ISO strings. Only a directly reported q/t, or a single top-level
/// quality/time leaf inside one reported DO structure, may become typed Browser
/// evidence. The runtime verifies the exact DataSet/DO/FC ownership separately.
/// No generic recursive scan, sibling guess, synthetic defaults or MMS read.
/// </summary>
public static class IedBrowserReportWireEvidence
{
    public static IReadOnlyList<NativeBrowserReportRawEvidence> Extract(MmsReportFrame frame)
        => Extract(frame, null);

    /// <summary>
    /// When a current typed GVA model exists, nested report structures must
    /// use exact ordered paths rather than a depth-one q/t heuristic.
    /// Missing paths produce no synthetic proof; scoped on-expand MMS Read
    /// remains the repair path. The receiving ledger checks DataSet and FC.
    /// </summary>
    public static IReadOnlyList<NativeBrowserReportRawEvidence> Extract(
        MmsReportFrame frame, LiveIedModelDiscoveryDocument? typedModel)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var result = new List<NativeBrowserReportRawEvidence>();
        foreach (var reported in frame.Values)
        {
            var reference = IedBrowserLiveValues.CanonicalReference(reported.MemberReference);
            var fc = reported.Member?.FunctionalConstraint ?? "";
            // DataRef is optional. If present it must agree with the verified
            // DataSet member AND explicit FC, or the raw q/t is quarantined.
            if (!DataReferenceAgreesWithMember(reported.DataReference, reference, fc))
                continue;
            var payload = reported.Value;
            if (reported.FailureCode.HasValue || payload is null ||
                string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(fc))
                continue;
            var directQuality = reference.EndsWith(".q", StringComparison.Ordinal);
            var directTime = reference.EndsWith(".t", StringComparison.Ordinal);
            MmsQualityEvidence? quality = null;
            Iec61850UtcTimeEvidence? time = null;
            if (directQuality)
                quality = DecodeQuality(payload);
            else if (directTime)
                time = DecodeTime(payload);
            else if (payload.Kind == MmsDataKind.Structure &&
                     payload.Children.Count is > 0 and <= 64 && typedModel is not null)
            {
                var routes = IedBrowserTypedWireCompanionLayout.Resolve(
                    typedModel, reference, fc);
                foreach (var route in routes)
                {
                    if (!IedBrowserTypedWireCompanionLayout.TryLocate(
                            payload, route.ChildIndices, out var typedLeaf))
                        continue;
                    // Each evidence carrier is the *actual* schema-declared
                    // phase q/t leaf. Never publish one PPV-root bit to every
                    // MMXU phase via an ancestor fallback.
                    if (route.Kind == "Quality" &&
                        DecodeQuality(typedLeaf) is { } nestedQuality)
                        result.Add(new NativeBrowserReportRawEvidence(
                            route.Reference, fc, nestedQuality, null));
                    if (route.Kind == "Timestamp" &&
                        DecodeTime(typedLeaf) is { } nestedTime)
                        result.Add(new NativeBrowserReportRawEvidence(
                            route.Reference, fc, null, nestedTime));
                }
                continue;
            }
            else if (payload.Kind == MmsDataKind.Structure &&
                     payload.Children.Count is > 0 and <= 64)
            {
                // Legacy single-depth carrier only when no GVA layout exists.
                // DO(FCD) payloads usually carry stVal, q and t as direct
                // siblings. Ambiguous (multiple quality/timestamp siblings)
                // fail closed rather than borrowing one child's evidence.
                var qCount = 0;
                var tCount = 0;
                foreach (var child in payload.Children)
                {
                    var q = DecodeQuality(child);
                    if (q is not null) { quality = q; qCount++; }
                    var t = DecodeTime(child);
                    if (t is not null) { time = t; tCount++; }
                }
                if (qCount != 1) quality = null;
                if (tCount != 1) time = null;
            }
            if (quality is not null || time is not null)
                result.Add(new NativeBrowserReportRawEvidence(reference, fc, quality, time));
        }
        return result;
    }


    private static bool DataReferenceAgreesWithMember(
        string? dataReference, string memberReference, string memberFc)
    {
        if (string.IsNullOrWhiteSpace(dataReference))
            return true;
        var wire = dataReference.Trim();
        if (!string.Equals(IedBrowserLiveValues.CanonicalReference(wire),
                memberReference, StringComparison.Ordinal))
            return false;
        // The canonical ledger deliberately drops the MMS $FC$ segment.
        // Recheck this separately to reject cross-FC alias collisions.
        var fcStart = wire.IndexOf('$');
        if (fcStart < 0)
            return true;
        var fcEnd = wire.IndexOf('$', fcStart + 1);
        return fcEnd > fcStart + 1 &&
            string.Equals(wire[(fcStart + 1)..fcEnd], memberFc,
                StringComparison.OrdinalIgnoreCase);
    }

    private static MmsQualityEvidence? DecodeQuality(MmsDataValue raw)
        => raw.Kind == MmsDataKind.BitString &&
            MmsQualityDecoder.TryDecode(raw.RawValue.ToArray(), out var decoded)
            ? decoded : null;

    private static Iec61850UtcTimeEvidence? DecodeTime(MmsDataValue raw)
    {
        if (raw.Kind != MmsDataKind.UtcTime) return null;
        var decoded = Iec61850UtcTimeEvidence.Decode(raw);
        return decoded is { IsDecoded: true, HasWireProvenance: true }
            ? decoded : null;
    }
}
