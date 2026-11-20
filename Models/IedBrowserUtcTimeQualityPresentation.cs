using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Browser-only names for engine-decoded IEC timeQuality. Never read a generic
/// timestamp string to infer clock quality or generate synthetic good flags.
/// </summary>
public static class IedBrowserUtcTimeQualityPresentation
{
    public static string FormatField(Iec61850UtcTimeEvidence? evidence, string field)
    {
        if (evidence is not { IsDecoded: true, HasWireProvenance: true })
            return "Not exposed";
        return field switch
        {
            "LeapSecondsKnown" => evidence.LeapSecondsKnown ? "true" : "false",
            "ClockFailure" => evidence.ClockFailure ? "true" : "false",
            "ClockNotSynchronized" => evidence.ClockNotSynchronized ? "true" : "false",
            "TimeAccuracy" => evidence.TimeAccuracy,
            _ => "Not exposed"
        };
    }
}
