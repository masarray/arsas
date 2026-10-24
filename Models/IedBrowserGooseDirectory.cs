using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Model directory projection only. A selected IED's SCL may declare GoCBs
/// that optional online MMS discovery does not enumerate. Do not interpret
/// SCL declarations as live GoEna, subscriber frames or permission to read.
/// </summary>
public static class IedBrowserGooseDirectory
{
    public static IReadOnlyList<LiveIedControlBlockModel> Resolve(
        LiveIedModelDiscoveryDocument? live,
        LiveIedModelDiscoveryDocument? scl)
    {
        if (live is null) return scl?.GooseControlBlocks ?? [];
        var blocks = new List<LiveIedControlBlockModel>(live.GooseControlBlocks);
        if (scl is null) return blocks;
        // Attached SCL was already selected for this IED; only LD identities
        // actually present in its live model may enrich the online directory.
        var verifiedDomains = live.LogicalDevices
            .Select(ld => ld.MmsDomain)
            .ToHashSet(StringComparer.Ordinal);
        var seen = blocks.Select(b => b.Reference)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var block in scl.GooseControlBlocks)
        {
            if (block.Reference.Length == 0 || block.Domain.Length == 0 ||
                !verifiedDomains.Contains(block.Domain) ||
                !seen.Add(block.Reference))
                continue;
            blocks.Add(block);
        }
        return blocks;
    }

    /// <summary>
    /// Recover a directory ONLY after a matching received GoCB supplies an
    /// actual wire DataSet identity. Offline SCL may enrich that exact IED
    /// domain, but no DataSet is guessed from the GoCB name or array length.
    /// Reject duplicate definitions rather than choosing the first.
    /// </summary>
    public static (LiveIedModelDiscoveryDocument Model, LiveIedDataSetModel Set)?
        ResolveWireDataSet(LiveIedModelDiscoveryDocument? live,
            LiveIedModelDiscoveryDocument? scl, string domain, string wireReference)
    {
        if (string.IsNullOrWhiteSpace(domain) ||
            string.IsNullOrWhiteSpace(wireReference))
            return null;
        static LiveIedDataSetModel? Exact(
            LiveIedModelDiscoveryDocument? source, string domain, string reference)
        {
            var matches = source?.DataSets.Where(set =>
                string.Equals(set.Domain, domain, StringComparison.Ordinal) &&
                string.Equals(IedBrowserGooseInspection.Canonical(set.Reference),
                    IedBrowserGooseInspection.Canonical(reference),
                    StringComparison.Ordinal)).Take(2).ToArray();
            return matches is { Length: 1 } ? matches[0] : null;
        }
        if (Exact(live, domain, wireReference) is { } verified)
            return (live!, verified);
        // SCL is a directory, not wire evidence. Match only a domain present
        // in the selected device's live discovery (or an offline SCL session).
        if (live is not null &&
            !live.LogicalDevices.Any(ld => ld.MmsDomain == domain))
            return null;
        return Exact(scl, domain, wireReference) is { } fallback
            ? (scl!, fallback) : null;
    }

    public static LiveIedModelDiscoveryDocument? ModelForDataSet(
        LiveIedModelDiscoveryDocument? live,
        LiveIedModelDiscoveryDocument? scl,
        LiveIedControlBlockModel block)
    {
        static bool HasExact(LiveIedModelDiscoveryDocument? candidate,
            LiveIedControlBlockModel target)
            => candidate?.DataSets.Any(set =>
                string.Equals(set.Reference, target.DataSetReference,
                    StringComparison.Ordinal) &&
                string.Equals(set.Domain, target.Domain, StringComparison.Ordinal)) == true;
        if (HasExact(live, block)) return live;
        if (HasExact(scl, block) && (live is null ||
            live.LogicalDevices.Any(ld => ld.MmsDomain == block.Domain)))
            return scl;
        return live;
    }
}
