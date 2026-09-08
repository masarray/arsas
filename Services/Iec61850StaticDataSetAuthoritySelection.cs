using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Builds the exact Static DataSet selection used by the report-only workflow.
///
/// Static report-only mode is RCB-backed by definition. A static DataSet that is not
/// referenced by an authoritative configured BRCB/URCB is valid engineering inventory,
/// but it is not a live acquisition source and therefore must not inflate the monitor with
/// permanently unavailable rows. When an SCL workspace is open, its ReportControl bindings
/// are the configuration authority; live discovery is verification only. For online-only
/// operation with no SCL workspace, live discovery becomes the configuration authority.
///
/// A DataSetReference on a browsed/runtime alias is also not sufficient authority: several
/// aliases can point at the same static FCDA/FCD member. Static mode selects one process row
/// per engine-authoritative membership when a publishable feedback leaf exists. An exact
/// control companion for that same membership is selected independently: command discovery
/// must not disappear merely because scalar feedback is unresolved. Raw control objects are
/// still rejected by the process-value runtime boundary.
/// </summary>
public static class Iec61850StaticDataSetAuthoritySelection
{
    public static IReadOnlySet<SignalDefinition> Build(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var authorityModel = device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel;
        if (authorityModel is null)
            return new HashSet<SignalDefinition>(ReferenceEqualityComparer.Instance);

        var reportBackedDataSets = BuildReportBackedDataSetReferences(device);
        if (reportBackedDataSets.Count == 0)
            return new HashSet<SignalDefinition>(ReferenceEqualityComparer.Instance);

        var mandatory = Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(authorityModel);
        var selected = new HashSet<SignalDefinition>(ReferenceEqualityComparer.Instance);
        var signals = device.Signals.ToArray();

        foreach (var descriptor in mandatory)
        {
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

                // Command retention is independent from scalar process projection. The
                // projection service has already rooted these companions in exact SCL/ARIEC
                // CDC + DataObject authority. Selecting them here enables ctlModel inspection
                // and Command Panel projection even when PrimaryValueReference is unresolved.
                foreach (var control in signals
                             .Where(signal => signal.IsControlSignal && signal.IsValidControlObject)
                             .Where(signal => LiteralEquals(signal.DataSetReference, membership.DataSetReference))
                             .Where(signal => LiteralEquals(signal.DisplayReference, memberReference)))
                {
                    selected.Add(control);
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
    /// Returns the literal DataSet references that have authoritative ReportControl backing.
    /// Opened SCL configuration wins absolutely over extra live ReportControls. Live discovery
    /// is used as configuration authority only when no SCL design model is open.
    /// </summary>
    public static IReadOnlySet<string> BuildReportBackedDataSetReferences(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configurationModel = device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel;
        AddReportBackedDataSets(configurationModel, result);
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
