namespace ArIED61850Tester.Services;

internal sealed record ComtradePhasorChannelDescriptor(
    uint Index,
    int Role,
    int PhaseRole,
    string Label,
    string Phase,
    string Circuit,
    string Units);

internal static class ComtradePhasorWorkspaceMath
{
    internal const int RoleVoltage = 1;
    internal const int RoleCurrent = 2;
    internal const int PhaseL1 = 1;
    internal const int PhaseL2 = 2;
    internal const int PhaseL3 = 3;
    internal const int PhaseNeutral = 4;

    internal static IReadOnlyList<ComtradePhasorChannelDescriptor> SelectRoleSet(
        IEnumerable<ComtradePhasorChannelDescriptor> channels,
        int role)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var candidates = channels
            .Where(channel => channel.Role == role && IsDisplayPhase(channel.PhaseRole))
            .OrderBy(channel => channel.Index)
            .ToArray();
        if (candidates.Length == 0)
            return Array.Empty<ComtradePhasorChannelDescriptor>();

        // A polar diagram has one radial scale, therefore do not silently compare unlike
        // engineering units. Prefer the most complete same-circuit + same-unit phase set.
        var coherentGroups = candidates
            .GroupBy(channel => (Circuit: Normalize(channel.Circuit), Units: Normalize(channel.Units)))
            .Select(group => new
            {
                Items = group.ToArray(),
                PhaseCount = group.Select(item => item.PhaseRole).Distinct().Count(),
                FirstIndex = group.Min(item => item.Index)
            })
            .OrderByDescending(group => group.PhaseCount)
            .ThenBy(group => group.FirstIndex)
            .ToArray();

        var chosen = coherentGroups[0].Items;
        if (coherentGroups[0].PhaseCount < 2)
        {
            // Some recorders leave circuit blank/inconsistent per phase. In that case retain the
            // dimension-safe unit boundary and choose the unit family with the best phase coverage.
            chosen = candidates
                .GroupBy(channel => Normalize(channel.Units))
                .Select(group => new
                {
                    Items = group.ToArray(),
                    PhaseCount = group.Select(item => item.PhaseRole).Distinct().Count(),
                    FirstIndex = group.Min(item => item.Index)
                })
                .OrderByDescending(group => group.PhaseCount)
                .ThenBy(group => group.FirstIndex)
                .First()
                .Items;
        }

        return chosen
            .GroupBy(channel => channel.PhaseRole)
            .Select(group => group.OrderBy(channel => channel.Index).First())
            .OrderBy(channel => PhaseRank(channel.PhaseRole))
            .ThenBy(channel => channel.Index)
            .ToArray();
    }

    internal static int PhaseRoleFromCanonicalName(string? phase)
        => (phase ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "L1" or "A" => PhaseL1,
            "L2" or "B" => PhaseL2,
            "L3" or "C" => PhaseL3,
            "N" or "E" => PhaseNeutral,
            _ => 0
        };

    internal static string CanonicalPhaseName(int phaseRole, string? fallback = null)
        => phaseRole switch
        {
            PhaseL1 => "L1",
            PhaseL2 => "L2",
            PhaseL3 => "L3",
            PhaseNeutral => "E",
            _ => string.IsNullOrWhiteSpace(fallback) ? "Other" : fallback.Trim()
        };

    private static bool IsDisplayPhase(int phaseRole)
        => phaseRole is PhaseL1 or PhaseL2 or PhaseL3 or PhaseNeutral;

    private static int PhaseRank(int phaseRole)
        => phaseRole switch
        {
            PhaseL1 => 0,
            PhaseL2 => 1,
            PhaseL3 => 2,
            PhaseNeutral => 3,
            _ => 9
        };

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();
}
