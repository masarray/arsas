namespace ArIED61850Tester.Models;

/// <summary>
/// Immutable, metadata-only projection of model-declared Report q companions
/// onto selected signal points. The index contains no values, MMS I/O, or
/// cross-association global state. A new monitor/model gets a new index.
/// </summary>
public sealed class Iec61850ReportQualityRouteIndex
{
    private readonly Dictionary<string, IReadOnlyList<Iec61850MonitorPoint>> _points;

    public Iec61850ReportQualityRouteIndex(
        IedBrowserSchemaIndex schema, IEnumerable<Iec61850MonitorPoint> points)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(points);
        var indexed = new Dictionary<string, List<Iec61850MonitorPoint>>(
            StringComparer.Ordinal);
        foreach (var point in points)
        {
            if (string.IsNullOrWhiteSpace(point.FunctionalConstraint) ||
                !schema.TryGetQualityCompanion(
                    point.IecReference, point.FunctionalConstraint, out var qRef))
                continue;
            // An independently supplied point companion identity must NOT
            // override a different exact live-model quality declaration.
            if (!string.IsNullOrWhiteSpace(point.QualityReference) &&
                !string.Equals(
                    IedBrowserLiveValues.CanonicalReference(point.QualityReference),
                    qRef, StringComparison.Ordinal))
                continue;
            var key = Key(qRef, point.FunctionalConstraint);
            if (!indexed.TryGetValue(key, out var targets))
                indexed[key] = targets = [];
            if (!targets.Any(other => other.PointKey == point.PointKey))
                targets.Add(point);
        }
        _points = indexed.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<Iec61850MonitorPoint>)pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    public IReadOnlyList<Iec61850MonitorPoint> FindExact(
        string qualityReference, string fc)
        => _points.TryGetValue(Key(qualityReference, fc), out var points)
            ? points : [];

    private static string Key(string reference, string fc)
        => IedBrowserLiveValues.CanonicalReference(reference) + "|" +
           (fc ?? "").Trim().ToUpperInvariant();
}
