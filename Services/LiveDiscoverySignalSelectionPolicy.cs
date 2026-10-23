using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Signal Selection policy for broad online MMS discovery.
///
/// Online GetNameList/VAA discovery is intentionally exhaustive. It contains process
/// values together with quality/timestamp companions, nameplate/configuration attributes,
/// substitution state, control-service structure and other engineering leaves. Those
/// objects remain available in the typed live model, but they are not independent operator
/// points and must not leak into Signal Selection.
///
/// Static DataSet membership is authoritative and therefore wins over this presentation
/// noise classifier: an object explicitly configured in a DataSet remains visible even if
/// its object-level FCD identity is not an exact runtime value leaf.
/// </summary>
public static class LiveDiscoverySignalSelectionPolicy
{
    private static readonly HashSet<string> NoiseSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "mod", "beh", "health", "eehealth", "namplt",
        "q", "t", "tm", "d", "du",
        "origin", "orcat", "orident",
        "ctlmodel", "ctlval", "ctlnum", "stseld",
        "sbo", "sbow", "oper", "cancel", "check", "test",
        "datans", "subena", "subval", "subq", "subid", "blkena",
        "configrev", "vendor", "swrev", "lnns", "numpts", "olddata"
    };

    public static bool IsVisible(SignalDefinition? signal)
    {
        if (signal is null || string.IsNullOrWhiteSpace(signal.ObjectReference))
            return false;

        if (!string.IsNullOrWhiteSpace(signal.DataSetReference))
            return true;

        if (IsProtocolOrEngineeringNoise(signal.ObjectReference))
            return false;

        return SasOperationalSignalPolicy.IsVisible(signal);
    }

    public static bool IsProtocolOrEngineeringNoise(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return true;

        var normalized = reference.Trim().Replace('$', '.');
        var slash = normalized.IndexOf('/');
        var path = slash >= 0 && slash < normalized.Length - 1
            ? normalized[(slash + 1)..]
            : normalized;
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Segment 0 is the Logical Node. Only data/control path segments are classified.
        for (var index = 1; index < segments.Length; index++)
        {
            if (NoiseSegments.Contains(segments[index]))
                return true;
        }

        return false;
    }
}
