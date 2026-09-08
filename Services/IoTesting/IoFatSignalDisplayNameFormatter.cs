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
        return point.DisplaySignalName;
    }

    public static string Format(string? signalName, string? iecReference)
        => IoSignalDisplayName.Format(signalName, iecReference);
}
