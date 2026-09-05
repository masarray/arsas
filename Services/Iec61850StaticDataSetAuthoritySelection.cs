using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Builds the exact Static DataSet selection used by the report-only workflow.
///
/// Static report-only mode is RCB-backed by definition. A static DataSet that is not
/// referenced by any configured BRCB/URCB is valid engineering inventory, but it is not
/// a live acquisition source and therefore must not inflate the monitor with permanently
/// unavailable rows. Selection is limited to exact DataSet memberships referenced by a
/// configured ReportControl in either the opened SCL design model or fresh live discovery.
///
/// A DataSetReference on a browsed/runtime alias is also not sufficient authority: several
/// aliases can point at the same static FCDA/FCD member (for example cVal/instCVal or
/// structured measurement descendants). Static mode selects one presentation row for each
/// engine-authoritative membership, preserving the literal member identity from SCL/ARIEC.
/// </summary>
public static class Iec61850StaticDataSetAuthoritySelection
{
    public static IReadOnlySet<SignalDefinition> Build(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // Do not let a partial live model erase richer opened-SCL membership evidence. This
        // is the same authority rule used for configured RCBs: design + live are additive,
        // while the live MMS association is still required later to verify/arm acquisition.
        var authorityModels = new[]
            {
                device.SclWorkspace?.DesignModel,
                device.LiveDiscoveryModel
            }
            .Where(model => model is not null)
            .Cast<LiveIedModelDiscoveryDocument>()
            .Distinct()
            .ToArray();
        if (authorityModels.Length == 0)
            return new HashSet<SignalDefinition>(ReferenceEqualityComparer.Instance);

        var reportBackedDataSets = BuildReportBackedDataSetReferences(device);
        if (reportBackedDataSets.Count == 0)
            return new HashSet<SignalDefinition>(ReferenceEqualityComparer.Instance);

        var mandatory = authorityModels
            .SelectMany(model => Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(model))
            .ToArray();
        var selected = new HashSet<SignalDefinition>(ReferenceEqualityComparer.Instance);
        var signals = device.Signals.ToArray();

        foreach (var descriptor in mandatory)
        {
            // A descriptor may carry more than one membership. Do not arbitrarily take the
            // first DataSet: choose only literal memberships that are backed by configured
            // report-control authority.
            var memberships = descriptor.DataSetMemberships
                .Where(item => reportBackedDataSets.Contains(NormalizeLiteral(item.DataSetReference)))
                .OrderBy(item => item.DataSetReference, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.MemberIndex)
                .ToArray();

            foreach (var membership in memberships)
            {
                var memberReference = FirstNonEmpty(
                    membership.CanonicalMemberReference,
                    membership.OriginalMemberReference,
                    descriptor.DesignReference,
                    descriptor.ObservedReference,
                    descriptor.PrimaryValueReference);
                if (string.IsNullOrWhiteSpace(memberReference) ||
                    string.IsNullOrWhiteSpace(membership.DataSetReference))
                {
                    continue;
                }

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
        }

        return selected;
    }

    /// <summary>
    /// Returns the literal DataSet references that have configured report-control authority.
    /// The union is deliberate: fast SCL reconnect can have a richer design model than the
    /// partial live model, while a full discovery can reveal additional valid live RCB
    /// bindings. Neither source is allowed to erase the other's exact configured evidence.
    /// </summary>
    public static IReadOnlySet<string> BuildReportBackedDataSetReferences(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddReportBackedDataSets(device.SclWorkspace?.DesignModel, result);
        AddReportBackedDataSets(device.LiveDiscoveryModel, result);
        return result;
    }

    private static void AddReportBackedDataSets(
        LiveIedModelDiscoveryDocument? model,
        HashSet<string> target)
    {
        if (model is null)
            return;

        foreach (var report in model.ReportControls)
        {
            var dataSetReference = NormalizeLiteral(report.DataSetReference);
            if (!string.IsNullOrWhiteSpace(dataSetReference))
                target.Add(dataSetReference);
        }
    }

    private static bool LiteralEquals(string? left, string? right)
        => string.Equals(NormalizeLiteral(left), NormalizeLiteral(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLiteral(string? value)
        => (value ?? string.Empty).Trim();

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
