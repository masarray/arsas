namespace ArIED61850Tester.Models;

/// <summary>
/// Immutable, metadata-only projection of model-declared Report q companions
/// onto selected signal points. The index contains no values, MMS I/O, or
/// cross-association global state. A new monitor/model gets a new index.
/// </summary>
public sealed class Iec61850ReportQualityRouteIndex
{
    private readonly Dictionary<string, IReadOnlyList<Iec61850MonitorPoint>> _points;
    private readonly Dictionary<string, IReadOnlyList<Iec61850MonitorPoint>> _timestamps;
    // Bounded diagnostics only. Incorrect inventory hints cannot veto an
    // exact model-declared owner nor redirect quality to another owner.
    public int ConflictingHintCount { get; }
    public IReadOnlyList<string> ConflictingHintSamples { get; }

    public Iec61850ReportQualityRouteIndex(
        IedBrowserSchemaIndex schema, IEnumerable<Iec61850MonitorPoint> points)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(points);
        // One hash insertion per selected point (including multi-leaf DOs).
        // Avoid O(n²) List.Any when many process leaves share the same CDC q.
        var indexed = new Dictionary<string,
            Dictionary<string, Iec61850MonitorPoint>>(StringComparer.Ordinal);
        var timestamps = new Dictionary<string,
            Dictionary<string, Iec61850MonitorPoint>>(StringComparer.Ordinal);
        static void Add(Dictionary<string, Dictionary<string, Iec61850MonitorPoint>> index,
            string reference, Iec61850MonitorPoint selected)
        {
            var key = Key(reference, selected.FunctionalConstraint);
            if (!index.TryGetValue(key, out var targets))
                index[key] = targets =
                    new Dictionary<string, Iec61850MonitorPoint>(
                        StringComparer.OrdinalIgnoreCase);
            targets.TryAdd(selected.PointKey, selected);
        }
        var conflictingHints = 0;
        var samples = new List<string>(capacity: 3);
        foreach (var point in points)
        {
            if (string.IsNullOrWhiteSpace(point.FunctionalConstraint) ||
                !schema.IsDeclaredQualityRouteValueCarrier(
                    point.IecReference, point.FunctionalConstraint))
                continue;
            // q and t are independent Report companion channels. A qualified
            // timestamp may exist even when the CDC does not declare q.
            if (schema.TryGetTimestampCompanion(
                    point.IecReference, point.FunctionalConstraint, out var tRef))
                Add(timestamps, tRef, point);
            if (!schema.TryGetQualityCompanion(
                    point.IecReference, point.FunctionalConstraint, out var qRef))
                continue;
            // ARIEC's explicit live-model q/FC declaration is the SSOT.
            // Inventory hints can become stale when the selected signal is
            // projected from a nested CDC (TapChg.valWTr.posVal) or a
            // report DataSet. Record the conflict, but never permit a hint
            // to redirect a proven q value or erase a valid route.
            if (!string.IsNullOrWhiteSpace(point.QualityReference) &&
                !string.Equals(
                    IedBrowserLiveValues.CanonicalReference(point.QualityReference),
                    qRef, StringComparison.Ordinal))
            {
                conflictingHints++;
                if (samples.Count < 3)
                {
                    static string Bounded(string value) =>
                        value.Length <= 200 ? value : value[..200] + "…";
                    samples.Add(Bounded(point.IecReference) +
                        " [" + point.FunctionalConstraint + "]: inventory=" +
                        Bounded(point.QualityReference) + ", model=" + Bounded(qRef));
                }
            }
            Add(indexed, qRef, point);
        }
        _points = indexed.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<Iec61850MonitorPoint>)pair.Value.Values.ToArray(),
            StringComparer.Ordinal);
        _timestamps = timestamps.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<Iec61850MonitorPoint>)pair.Value.Values.ToArray(),
            StringComparer.Ordinal);
        ConflictingHintCount = conflictingHints;
        ConflictingHintSamples = samples.ToArray();
    }

    public IReadOnlyList<Iec61850MonitorPoint> FindExact(
        string qualityReference, string fc)
        => _points.TryGetValue(Key(qualityReference, fc), out var points)
            ? points : [];

    public IReadOnlyList<Iec61850MonitorPoint> FindTimestampExact(
        string timestampReference, string fc)
        => _timestamps.TryGetValue(Key(timestampReference, fc), out var points)
            ? points : [];

    private static string Key(string reference, string fc)
        => IedBrowserLiveValues.CanonicalReference(reference) + "|" +
           (fc ?? "").Trim().ToUpperInvariant();
}
