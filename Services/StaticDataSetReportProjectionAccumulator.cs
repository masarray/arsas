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

    private sealed record CompanionMetadata(
        string Quality,
        string Timestamp,
        DateTimeOffset UpdatedAt);

    private readonly Dictionary<string, Dictionary<string, LeafState>> _leavesByParent =
        new(StringComparer.OrdinalIgnoreCase);

    // IEC 61850 report projection may publish q/t companion evidence before the scalar
    // value leaf produced by semantic expansion. Keep that report evidence session-local
    // and join it back to the later value update. This is deliberately not an MMS read or
    // a synthetic "good" quality default: only q/t already supplied by the report can be
    // carried forward.
    private readonly Dictionary<string, CompanionMetadata> _metadataByReference =
        new(StringComparer.OrdinalIgnoreCase);

    // Membership is immutable for one monitor selection. Prepare once, then
    // resolve descendants via their ancestor path rather than walking every
    // selected IED point and rebuilding a query per InformationReport leaf.
    // This is an index of identities, NEVER another value store.
    private IReadOnlyCollection<Iec61850MonitorPoint>? _preparedPoints;
    private LiveIedModelDiscoveryDocument? _preparedModel;
    private HashSet<string>? _exactSelected;
    private Dictionary<string, Iec61850MonitorPoint[]>? _aggregateByReference;
    private readonly Dictionary<string, SchemaSafeAggregateProjectionService.ReadPlan>
        _plannedAggregates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unsupportedAggregates =
        new(StringComparer.OrdinalIgnoreCase);

    public void Prepare(LiveIedModelDiscoveryDocument? model,
        IReadOnlyCollection<Iec61850MonitorPoint> monitoredPoints)
    {
        ArgumentNullException.ThrowIfNull(monitoredPoints);
        ClearPrepared();
        _preparedModel = model;
        _preparedPoints = monitoredPoints;
        _exactSelected = new HashSet<string>(monitoredPoints.Select(point =>
            NormalizeReference(point.IecReference)), StringComparer.Ordinal);
        _aggregateByReference = monitoredPoints
            .Where(point => IsAggregate(point.IecReference))
            .GroupBy(point => NormalizeReference(point.IecReference), StringComparer.Ordinal)
            // Retain up to two claims: ambiguous identities MUST stay
            // ambiguous. Dropping duplicates would mistakenly pass the
            // incoming leaf as if it had no known aggregate parent.
            .ToDictionary(group => group.Key,
                group => group.Take(2).ToArray(), StringComparer.Ordinal);
    }

    public void ClearPrepared()
    {
        _preparedPoints = null;
        _preparedModel = null;
        _exactSelected = null;
        _aggregateByReference = null;
        _plannedAggregates.Clear();
        _unsupportedAggregates.Clear();
    }

    public void Reset()
    {
        _leavesByParent.Clear();
        _metadataByReference.Clear();
    }

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

        if (string.IsNullOrWhiteSpace(update.Reference))
            return new[] { update };

        var updateReference = NormalizeReference(update.Reference);
        RememberCompanionMetadata(updateReference, update);
        var effectiveUpdate = EnrichFromCompanionMetadata(updateReference, update);

        if (!effectiveUpdate.HasValue)
            return new[] { effectiveUpdate };

        var usePrepared = ReferenceEquals(monitoredPoints, _preparedPoints) &&
            ReferenceEquals(authorityModel, _preparedModel) &&
            _exactSelected is not null && _aggregateByReference is not null;
        var exactSelected = usePrepared
            ? _exactSelected!.Contains(updateReference)
            : monitoredPoints.Any(point =>
                ReferencesEqual(point.IecReference, effectiveUpdate.Reference));

        // Bounded reference-depth guard: an abnormal wire path must not
        // escape parent validation by overflowing the fast ancestor search.
        if (usePrepared && (updateReference.Length > 1024 ||
                            updateReference.Count(ch => ch == '.') > 32))
            return exactSelected
                ? new[] { effectiveUpdate }
                : Array.Empty<NativeReportValueUpdate>();

        var aggregateParents = usePrepared
            ? FindPreparedAggregateParents(updateReference)
            : monitoredPoints
                .Where(point => IsAggregate(point.IecReference))
                .Where(point => IsDescendant(updateReference, NormalizeReference(point.IecReference)))
                .ToArray();

        // A semantic child may not be assigned to an aggregate unless its parent is unique.
        // In an ambiguous model, preserve only an independently selected exact child.
        if (aggregateParents.Length != 1)
            return exactSelected ? new[] { effectiveUpdate } : aggregateParents.Length == 0 ? new[] { effectiveUpdate } : Array.Empty<NativeReportValueUpdate>();

        var parent = aggregateParents[0];
        var parentKey = NormalizeReference(parent.IecReference);
        if (usePrepared && _unsupportedAggregates.Contains(parentKey))
            return exactSelected ? new[] { effectiveUpdate } : Array.Empty<NativeReportValueUpdate>();

        if (!usePrepared || !_plannedAggregates.TryGetValue(parentKey, out var plan))
        {
            if (!SchemaSafeAggregateProjectionService.TryBuildReadPlan(
                    authorityModel, parent.IecReference, out plan, out _))
            {
                if (usePrepared)
                    _unsupportedAggregates.Add(parentKey);
                return exactSelected ? new[] { effectiveUpdate } : Array.Empty<NativeReportValueUpdate>();
            }

            if (usePrepared)
                _plannedAggregates[parentKey] = plan;
        }

        var expectedLeaf = plan.Leaves.FirstOrDefault(leaf =>
            ReferencesEqual(leaf.Reference, effectiveUpdate.Reference));
        if (expectedLeaf is null)
        {
            // Ignore non-authoritative siblings such as instCVal when cVal is the schema-
            // preferred engineering value. Keep it only when it is a separate selected row.
            return exactSelected ? new[] { effectiveUpdate } : Array.Empty<NativeReportValueUpdate>();
        }

        if (!_leavesByParent.TryGetValue(parentKey, out var leaves))
        {
            leaves = new Dictionary<string, LeafState>(StringComparer.OrdinalIgnoreCase);
            _leavesByParent[parentKey] = leaves;
        }

        leaves[NormalizeReference(expectedLeaf.Reference)] = new LeafState(
            expectedLeaf.Reference,
            expectedLeaf.Label,
            effectiveUpdate.Value,
            effectiveUpdate.HasQuality ? effectiveUpdate.Quality : string.Empty,
            effectiveUpdate.HasTimestamp ? effectiveUpdate.Timestamp : string.Empty,
            effectiveUpdate.Reason,
            effectiveUpdate.UpdatedAt,
            effectiveUpdate);

        var projected = BuildParentUpdate(parent, plan, leaves);
        if (projected is null)
            return exactSelected ? new[] { effectiveUpdate } : Array.Empty<NativeReportValueUpdate>();

        return exactSelected
            ? new[] { effectiveUpdate, projected }
            : new[] { projected };
    }

    private Iec61850MonitorPoint[] FindPreparedAggregateParents(string descendant)
    {
        // IEC semantic member paths have bounded dot-separated depth in
        // practice. Explicit cap also protects against malicious/unexpected
        // MMS references while never guessing an ancestor on overflow.
        if (_aggregateByReference is null || descendant.Length > 1024)
            return [];
        Iec61850MonitorPoint? first = null;
        var depth = 0;
        for (var dot = descendant.LastIndexOf('.'); dot > 0;
             dot = descendant.LastIndexOf('.', dot - 1))
        {
            if (++depth > 32)
                return [];
            if (!_aggregateByReference.TryGetValue(descendant[..dot], out var candidates))
                continue;
            if (candidates.Length > 1)
                return candidates; // duplicate selected parent: fail closed
            var parent = candidates[0];
            if (first is not null)
                return [first, parent]; // overlapping selected parents: fail closed
            first = parent;
        }
        return first is null ? [] : [first];
    }

    private void RememberCompanionMetadata(string updateReference, NativeReportValueUpdate update)
    {
        if ((!update.HasQuality || !IsUsefulMetadata(update.Quality)) &&
            (!update.HasTimestamp || !IsUsefulMetadata(update.Timestamp)))
        {
            return;
        }

        foreach (var key in MetadataKeys(updateReference))
        {
            _metadataByReference.TryGetValue(key, out var previous);
            var quality = update.HasQuality && IsUsefulMetadata(update.Quality)
                ? update.Quality.Trim()
                : previous?.Quality ?? string.Empty;
            var timestamp = update.HasTimestamp && IsUsefulMetadata(update.Timestamp)
                ? update.Timestamp.Trim()
                : previous?.Timestamp ?? string.Empty;
            var updatedAt = update.UpdatedAt == default
                ? previous?.UpdatedAt ?? default
                : update.UpdatedAt;

            _metadataByReference[key] = new CompanionMetadata(quality, timestamp, updatedAt);
        }
    }

    private NativeReportValueUpdate EnrichFromCompanionMetadata(
        string updateReference,
        NativeReportValueUpdate update)
    {
        if (!update.HasValue || (update.HasQuality && update.HasTimestamp))
            return update;

        CompanionMetadata? metadata = null;
        foreach (var key in MetadataKeys(updateReference))
        {
            if (_metadataByReference.TryGetValue(key, out metadata))
                break;
        }

        if (metadata is null)
            return update;

        var quality = update.HasQuality && IsUsefulMetadata(update.Quality)
            ? update.Quality
            : metadata.Quality;
        var timestamp = update.HasTimestamp && IsUsefulMetadata(update.Timestamp)
            ? update.Timestamp
            : metadata.Timestamp;
        var hasQuality = update.HasQuality || IsUsefulMetadata(quality);
        var hasTimestamp = update.HasTimestamp || IsUsefulMetadata(timestamp);

        if (hasQuality == update.HasQuality &&
            hasTimestamp == update.HasTimestamp &&
            string.Equals(quality, update.Quality, StringComparison.Ordinal) &&
            string.Equals(timestamp, update.Timestamp, StringComparison.Ordinal))
        {
            return update;
        }

        return new NativeReportValueUpdate
        {
            Reference = update.Reference,
            FunctionalConstraint = update.FunctionalConstraint,
            Value = update.Value,
            Quality = quality,
            Timestamp = timestamp,
            Reason = update.Reason,
            Source = update.Source,
            ProjectionStatus = update.ProjectionStatus,
            HasValue = update.HasValue,
            HasQuality = hasQuality,
            HasTimestamp = hasTimestamp,
            ReportControlReference = update.ReportControlReference,
            ReportId = update.ReportId,
            DataSetReference = update.DataSetReference,
            SequenceNumber = update.SequenceNumber,
            ConfRev = update.ConfRev,
            ReportTimestamp = update.ReportTimestamp,
            UpdatedAt = update.UpdatedAt
        };
    }

    private static IEnumerable<string> MetadataKeys(string normalizedReference)
    {
        if (string.IsNullOrWhiteSpace(normalizedReference))
            yield break;

        yield return normalizedReference;

        var scope = MetadataScope(normalizedReference);
        if (!scope.Equals(normalizedReference, StringComparison.OrdinalIgnoreCase))
            yield return scope;
    }

    private static string MetadataScope(string normalizedReference)
    {
        var suffixes = new[]
        {
            ".instcval.mag.f",
            ".cval.mag.f",
            ".instmag.f",
            ".mag.f",
            ".q",
            ".t"
        };

        foreach (var suffix in suffixes)
        {
            if (normalizedReference.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return normalizedReference[..^suffix.Length];
        }

        return normalizedReference;
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
            HasQuality = IsUsefulMetadata(quality),
            HasTimestamp = IsUsefulMetadata(timestamp),
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
            .Where(IsUsefulMetadata)
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

    private static bool IsUsefulMetadata(string? value)
    {
        if (!IsUseful(value))
            return false;

        var normalized = value!.Trim();
        return !normalized.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
               !normalized.StartsWith("Pending", StringComparison.OrdinalIgnoreCase);
    }
}
