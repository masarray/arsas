using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Compatibility facade for existing FAT call sites. The semantic naming rule itself lives in
/// IoSignalDisplayName so UI, reports, tests, and Engineering presentation share one ARSAS-owned
/// authority without changing persisted IEC identity or the pinned ARIEC61850 engine.
/// </summary>
public static class IoFatSignalDisplayNameFormatter
{
    public static string Format(IoTestPointPlan point)
    {
        ArgumentNullException.ThrowIfNull(point);

        // Presentation needs the richest original IEC context, not necessarily the reference
        // selected for event-log/report lookup. SourceIecReference and DA metadata preserve
        // phsA/phsB/phsC and LN ownership even when a lookup alias was normalized shorter.
        var semanticReference = FirstNonBlank(
            point.SourceIecReference,
            point.ReportDisplayReference,
            point.ObjectReference,
            point.EventLogSearchReference,
            point.ReportIecReference);

        return IoSignalDisplayName.Format(
            point.SignalName,
            semanticReference,
            point.LogicalNode,
            point.DataObject,
            point.DataAttribute);
    }

    public static string Format(string? signalName, string? iecReference)
        => IoSignalDisplayName.Format(signalName, iecReference);

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
