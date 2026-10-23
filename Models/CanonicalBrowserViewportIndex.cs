namespace ArIED61850Tester.Models;

/// <summary>
/// A per-rendered-page, immutable map from verified canonical dirty references
/// to WPF detail rows. The ledger remains the only observation authority:
/// this index stores dependency KEYS and row identities, never process values.
/// </summary>
public sealed class CanonicalBrowserViewportIndex
{
    private readonly IReadOnlyDictionary<string, IedBrowserDetailRow[]> _rowsByScope;
    private readonly bool _hasOpaqueRows;

    public CanonicalBrowserViewportIndex(IEnumerable<IedBrowserDetailRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var scopeRows = new Dictionary<string, List<IedBrowserDetailRow>>(StringComparer.Ordinal);
        var rowCount = 0;
        var opaque = false;
        foreach (var row in rows)
        {
            rowCount++;
            var dependencies = new HashSet<string>(StringComparer.Ordinal);
            Add(row.ValueReference);
            Add(row.FallbackValueReference);
            Add(row.EngineeringUnitReference);
            Add(row.ReadObjectReference);
            foreach (var candidate in row.ParentObservedChildCandidates)
                Add(candidate.Reference);
            foreach (var child in row.NameplateDcReferences)
                Add(child);
            if (row.PolarPlan is { } polar)
            {
                Add(polar.MagnitudeReference);
                Add(polar.AngleReference);
            }
            foreach (var polar in row.PhasePlans)
            {
                Add(polar.MagnitudeReference);
                Add(polar.AngleReference);
            }
            foreach (var vector in row.VectorComponents)
            {
                Add(vector.MagnitudeReference);
                Add(vector.AngleReference);
            }
            foreach (var scalar in row.ScalarComponents)
                Add(scalar.Reference);
            // A synthetic aggregate or a vendor-specific resolver without any
            // declared key cannot safely be optimized by an exact-key filter.
            if ((row.Kind is "Data" or "Object") && dependencies.Count == 0)
                opaque = true;
            foreach (var scope in dependencies)
            {
                if (!scopeRows.TryGetValue(scope, out var matches))
                    scopeRows[scope] = matches = new List<IedBrowserDetailRow>();
                matches.Add(row);
            }

            void Add(string? reference)
            {
                if (string.IsNullOrWhiteSpace(reference)) return;
                var canonical = IedBrowserLiveValues.CanonicalReference(reference);
                var scope = DataObjectScope(canonical);
                if (scope.Length == 0)
                    opaque = true;
                else
                    dependencies.Add(scope);
            }
        }
        _rowsByScope = scopeRows.ToDictionary(
            pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
        _hasOpaqueRows = opaque || rowCount == 0;
    }

    /// <summary>
    /// Returns true only when the journal covers the entire cursor range and
    /// every potentially affected row is identified. Otherwise caller must
    /// run the original full projection unchanged (fail open for UI refresh).
    /// </summary>
    public bool TryFindDirtyRows(CanonicalValueChangeBatch delta, long expectedVersion,
        out HashSet<IedBrowserDetailRow> dirty)
    {
        ArgumentNullException.ThrowIfNull(delta);
        dirty = new HashSet<IedBrowserDetailRow>(ReferenceEqualityComparer.Instance);
        if (_hasOpaqueRows || delta.RequiresFullRefresh ||
            delta.ThroughRevision != expectedVersion ||
            delta.LatestRevision != expectedVersion)
            return false;

        var dirtyScopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in delta.Changes)
        {
            if (change.Kind == CanonicalValueChangeKind.AssociationInvalidated ||
                change.FunctionalConstraint is "CF" or "DC" or "SG" or "SE" or "RP" or "BR")
                return false; // Metadata, control and shared units may affect many rows.
            var scope = DataObjectScope(change.Reference);
            if (scope.Length == 0) return false;
            dirtyScopes.Add(scope);
        }
        foreach (var scope in dirtyScopes)
        {
            if (_rowsByScope.TryGetValue(scope, out var affected))
                foreach (var row in affected) dirty.Add(row);
        }
        return true;
    }

    // The first named DO in LD/LN.DO.* is the stable IEC containment boundary.
    // Matching at DO level deliberately refreshes the whole q/t/phasor sibling
    // group, rather than risking missed quality, vector or parent summaries.
    private static string DataObjectScope(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return "";
        var slash = reference.IndexOf('/');
        if (slash < 1 || slash == reference.Length - 1) return "";
        var dot = reference.IndexOf('.', slash + 1);
        if (dot < 0 || dot == reference.Length - 1) return "";
        var nextDot = reference.IndexOf('.', dot + 1);
        var end = nextDot < 0 ? reference.Length : nextDot;
        return reference[..end];
    }
}
