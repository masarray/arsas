using System.Globalization;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Reconstructs a single Static DataSet member value from schema-proven report leaves.
///
/// ARIEC deliberately expands structured report members such as MHAI.ThdA/ThdPPV into
/// exact scalar descendants. Static DataSet mode, however, presents one row per literal
/// FCDA/FCD membership (the same boundary an IED engineering browser shows). This
/// accumulator joins only the exact leaves named by the authoritative SCL/live schema and
/// publishes a synthetic parent update after every required leaf has been observed.
/// No MMS read, positional child guessing, prefix-only phase choice, or fuzzy DataSet
/// mapping is performed here.
/// </summary>
public sealed class StaticDataSetReportProjectionAccumulator
{
    private sealed record LeafState(
        string Reference,
        string Label,
        string Value,
        string Quality,
        string Timestamp,
        string Reason,
        DateTimeOffset UpdatedAt,
        NativeReportValueUpdate Source);

    private readonly Dictionary<string, Dictionary<string, LeafState>> _leavesByParent =
        new(StringComparer.OrdinalIgnoreCase);

    public void Reset() => _leavesByParent.Clear();

    /// <summary>
    /// Returns the updates that should be consumed by the Static DataSet runtime for one
    /// engine report update. Ordinary scalar members pass through unchanged. A semantic
    /// descendant belonging to a selected structured parent is withheld until the parent
    /// can be reconstructed from all exact schema-named leaves. If the descendant itself
    /// is also an independently selected DataSet member, its original update is preserved.
    /// </summary>
    public IReadOnlyList<NativeReportValueUpdate> Project(
        LiveIedModelDiscoveryDocument? authorityModel,
        IReadOnlyCollection<Iec61850MonitorPoint> monitoredPoints,
        NativeReportValueUpdate update)
    {
        ArgumentNullException.ThrowIfNull(monitoredPoints);
        ArgumentNullException.ThrowIfNull(update);

        if (!update.HasValue || string.IsNullOrWhiteSpace(update.Reference))
            return new[] { update };

        var updateReference = NormalizeReference(update.Reference);
        var exactSelected = monitoredPoints.Any(point =>
            ReferencesEqual(point.IecReference, update.Reference));

        var aggregateParents = monitoredPoints
            .Where(point => IsAggregate(point.IecReference))
            .Where(point => IsDescendant(updateReference, NormalizeReference(point.IecReference)))
            .ToArray();

        // A semantic child may not be assigned to an aggregate unless its parent is unique.
        // In an ambiguous model, preserve only an independently selected exact child.
        if (aggregateParents.Length != 1)
            return exactSelected ? new[] { update } : aggregateParents.Length == 0 ? new[] { update } : Array.Empty<NativeReportValueUpdate>();

        var parent = aggregateParents[0];
        if (!SchemaSafeAggregateProjectionService.TryBuildReadPlan(
                authorityModel,
                parent.IecReference,
                out var plan,
                out _))
        {
            return exactSelected ? new[] { update } : Array.Empty<NativeReportValueUpdate>();
        }

        var expectedLeaf = plan.Leaves.FirstOrDefault(leaf =>
            ReferencesEqual(leaf.Reference, update.Reference));
        if (expectedLeaf is null)
        {
            // Ignore non-authoritative siblings such as instCVal when cVal is the schema-
            // preferred engineering value. Keep it only when it is a separate selected row.
            return exactSelected ? new[] { update } : Array.Empty<NativeReportValueUpdate>();
        }

        var parentKey = NormalizeReference(parent.IecReference);
        if (!_leavesByParent.TryGetValue(parentKey, out var leaves))
        {
            leaves = new Dictionary<string, LeafState>(StringComparer.OrdinalIgnoreCase);
            _leavesByParent[parentKey] = leaves;
        }

        leaves[NormalizeReference(expectedLeaf.Reference)] = new LeafState(
            expectedLeaf.Reference,
            expectedLeaf.Label,
            update.Value,
            update.HasQuality ? update.Quality : string.Empty,
            update.HasTimestamp ? update.Timestamp : string.Empty,
            update.Reason,
            update.UpdatedAt,
            update);

        var projected = BuildParentUpdate(parent, plan, leaves);
        if (projected is null)
            return exactSelected ? new[] { update } : Array.Empty<NativeReportValueUpdate>();

        return exactSelected
            ? new[] { update, projected }
            : new[] { projected };
    }

    private static NativeReportValueUpdate? BuildParentUpdate(
        Iec61850MonitorPoint parent,
        SchemaSafeAggregateProjectionService.ReadPlan plan,
        IReadOnlyDictionary<string, LeafState> leaves)
    {
        if (plan.Kind.Equals("ThreePhaseThd", StringComparison.OrdinalIgnoreCase))
        {
            var ordered = new List<LeafState>(plan.Leaves.Count);
            foreach (var expected in plan.Leaves)
            {
                if (!leaves.TryGetValue(NormalizeReference(expected.Reference), out var leaf) ||
                    !TryParseNumeric(leaf.Value, out _))
                {
                    return null;
                }
                ordered.Add(leaf);
            }

            var display = string.Join(", ", ordered.Select(leaf =>
            {
                TryParseNumeric(leaf.Value, out var numeric);
                return $"{leaf.Label}={numeric.ToString("0.######", CultureInfo.InvariantCulture)}";
            }));

            return Synthesize(
                parent.IecReference,
                display,
                "schema-safe-report-three-phase-aggregate",
                ordered);
        }

        if (plan.Kind.Equals("DemandEnergy", StringComparison.OrdinalIgnoreCase))
        {
            var expected = plan.Leaves.SingleOrDefault();
            if (expected is null ||
                !leaves.TryGetValue(NormalizeReference(expected.Reference), out var leaf) ||
                !TryParseNumeric(leaf.Value, out var numeric))
            {
                return null;
            }

            var display = numeric.ToString("0.######", CultureInfo.InvariantCulture);
            return Synthesize(
                parent.IecReference,
                display,
                "schema-safe-report-demand-energy-aggregate",
                new[] { leaf });
        }

        return null;
    }

    private static NativeReportValueUpdate Synthesize(
        string parentReference,
        string value,
        string projectionStatus,
        IReadOnlyList<LeafState> leaves)
    {
        var latest = leaves
            .OrderByDescending(leaf => leaf.UpdatedAt)
            .First();
        var quality = SharedUseful(leaves.Select(leaf => leaf.Quality));
        var timestamp = SharedUseful(leaves.Select(leaf => leaf.Timestamp));
        var reasons = leaves
            .Select(leaf => leaf.Reason)
            .Where(IsUseful)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new NativeReportValueUpdate
        {
            Reference = parentReference,
            FunctionalConstraint = latest.Source.FunctionalConstraint,
            Value = value,
            Quality = quality,
            Timestamp = timestamp,
            Reason = reasons.Length == 0
                ? "schema-safe static DataSet report aggregate"
                : string.Join(",", reasons),
            Source = "report",
            ProjectionStatus = projectionStatus,
            HasValue = true,
            HasQuality = IsUseful(quality),
            HasTimestamp = IsUseful(timestamp),
            ReportControlReference = latest.Source.ReportControlReference,
            ReportId = latest.Source.ReportId,
            DataSetReference = latest.Source.DataSetReference,
            SequenceNumber = latest.Source.SequenceNumber,
            ConfRev = latest.Source.ConfRev,
            ReportTimestamp = latest.Source.ReportTimestamp,
            UpdatedAt = leaves.Max(leaf => leaf.UpdatedAt)
        };
    }

    private static string SharedUseful(IEnumerable<string> values)
    {
        var useful = values
            .Where(IsUseful)
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return useful.Length == 1 ? useful[0] : string.Empty;
    }

    private static bool IsAggregate(string? reference)
        => SignalDefinition.IsThreePhaseMeasurementAggregate(reference) ||
           SignalDefinition.IsDemandEnergyAggregate(reference);

    private static bool IsDescendant(string child, string parent)
        => !string.IsNullOrWhiteSpace(child) &&
           !string.IsNullOrWhiteSpace(parent) &&
           child.Length > parent.Length &&
           child.StartsWith(parent + ".", StringComparison.OrdinalIgnoreCase);

    private static bool ReferencesEqual(string? left, string? right)
        => NormalizeReference(left).Equals(NormalizeReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty)
            .Trim()
            .Replace('$', '.')
            .Replace("..", ".")
            .ToLowerInvariant();

    private static bool TryParseNumeric(string? value, out double numeric)
    {
        var token = (value ?? string.Empty)
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        return double.TryParse(
            token,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out numeric);
    }

    private static bool IsUseful(string? value)
        => !string.IsNullOrWhiteSpace(value) && value != "-";
}
