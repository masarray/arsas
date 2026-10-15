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
