namespace ArIED61850Tester.Services;

/// <summary>
/// Matches one configured SCL ReportControl declaration to the concrete RCB object exposed
/// by the live MMS server.
///
/// IEC 61850 SCL may describe one indexed ReportControl family while the server exposes
/// concrete instances such as Buffer01 / Buffer02. Exact identity always wins. The only
/// accepted fallback is a decimal indexed instance of the same literal RCB family; arbitrary
/// same-DataSet RCBs are never treated as substitutes.
/// </summary>
public static class Iec61850StaticRcbReferenceMatcher
{
    public static bool IsExact(string? configuredReference, string? liveReference)
        => string.Equals(
            Normalize(configuredReference),
            Normalize(liveReference),
            StringComparison.OrdinalIgnoreCase);

    public static bool IsConfiguredOrIndexedInstance(string? configuredReference, string? liveReference)
    {
        var configured = Normalize(configuredReference);
        var live = Normalize(liveReference);
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(live))
            return false;
        if (string.Equals(configured, live, StringComparison.OrdinalIgnoreCase))
            return true;

        SplitLeaf(configured, out var configuredParent, out var configuredLeaf);
        SplitLeaf(live, out var liveParent, out var liveLeaf);
        if (!string.Equals(configuredParent, liveParent, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(configuredLeaf) ||
            string.IsNullOrWhiteSpace(liveLeaf))
        {
            return false;
        }

        // If the configured declaration already ends in a digit, treat it as a concrete
        // object and require exact identity. This avoids accidental prefix matches such as
        // Buffer0 -> Buffer01.
        if (char.IsDigit(configuredLeaf[^1]) ||
            !liveLeaf.StartsWith(configuredLeaf, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = liveLeaf[configuredLeaf.Length..];
        return suffix.Length > 0 && suffix.All(char.IsDigit);
    }

    public static int MatchRank(string? configuredReference, string? liveReference)
    {
        if (IsExact(configuredReference, liveReference))
            return 0;
        return IsConfiguredOrIndexedInstance(configuredReference, liveReference) ? 1 : int.MaxValue;
    }

    public static string Normalize(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static void SplitLeaf(string reference, out string parent, out string leaf)
    {
        var separator = reference.LastIndexOf('.');
        if (separator < 0)
        {
            parent = string.Empty;
            leaf = reference;
            return;
        }

        parent = reference[..separator];
        leaf = reference[(separator + 1)..];
    }
}
