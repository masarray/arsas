using AR.Iec61850.Mms;
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
                     payload.Children.Count is > 0 and <= 64)
            {
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
