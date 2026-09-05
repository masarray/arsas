using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Builds the exact Static DataSet selection used by the report-only workflow.
///
/// A DataSetReference on a browsed/runtime alias is not sufficient authority: several
/// aliases can point at the same static FCDA/FCD member (for example cVal/instCVal or
/// structured measurement descendants). Static mode must select one presentation row
/// for each engine-authoritative DataSet membership, preserving the literal member
/// identity that appears in SCL/ARIEC.
/// </summary>
public static class Iec61850StaticDataSetAuthoritySelection
{
    public static IReadOnlySet<SignalDefinition> Build(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var model = device.LiveDiscoveryModel ?? device.SclWorkspace?.DesignModel;
        if (model is null)
            return new HashSet<SignalDefinition>(ReferenceEqualityComparer.Instance);

        var mandatory = Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(model);
        var selected = new HashSet<SignalDefinition>(ReferenceEqualityComparer.Instance);
        var signals = device.Signals.ToArray();

        foreach (var descriptor in mandatory)
        {
            var membership = descriptor.DataSetMemberships
                .OrderBy(item => item.DataSetReference, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.MemberIndex)
                .FirstOrDefault();
            if (membership is null)
                continue;

            var memberReference = FirstNonEmpty(
                membership.CanonicalMemberReference,
                membership.OriginalMemberReference,
                descriptor.DesignReference,
                descriptor.ObservedReference,
                descriptor.PrimaryValueReference);
            if (string.IsNullOrWhiteSpace(memberReference) ||
                string.IsNullOrWhiteSpace(membership.DataSetReference))
                continue;

            var candidates = signals
                .Where(signal => !signal.IsControlSignal && signal.CanPublishToRuntime)
                .Where(signal => LiteralEquals(signal.DataSetReference, membership.DataSetReference))
                .Where(signal => LiteralEquals(signal.DisplayReference, memberReference))
                .ToArray();
            if (candidates.Length == 0)
                continue;

            // Prefer the runtime row already bound to ARIEC's resolved primary value.
            // For an unresolved structured member, the inventory-created exact static row
            // wins over a generic browsed alias. Never choose by fuzzy/prefix matching.
            var chosen = candidates
                .OrderByDescending(signal =>
                    !string.IsNullOrWhiteSpace(descriptor.PrimaryValueReference) &&
                    LiteralEquals(signal.ObjectReference, descriptor.PrimaryValueReference))
                .ThenByDescending(signal =>
                    (signal.Source ?? string.Empty).Contains(
                        "mandatory static DataSet member",
                        StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(signal =>
                    string.Equals(signal.Category, "DataSet", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(signal =>
                    string.Equals(signal.Confidence, "High", StringComparison.OrdinalIgnoreCase))
                .First();

            selected.Add(chosen);
        }

        return selected;
    }

    private static bool LiteralEquals(string? left, string? right)
        => string.Equals(NormalizeLiteral(left), NormalizeLiteral(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLiteral(string? value)
        => (value ?? string.Empty).Trim();

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
