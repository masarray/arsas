using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Read-only inspection of already displayed Static DataSet points. Unlike RCB traffic
/// qualification, this is UI image completeness, not protocol/quality authority.
/// Does not read MMS, synthesize process values, or change report subscriptions.
/// </summary>
internal static class StaticDataSetInitialImageDiagnostic
{
    internal sealed record Group(
        string DataSetReference,
        string RcbReference,
        int Selected,
        int ValueVisible,
        int ValuePending,
        int QualityNotSupplied);

    internal sealed record PendingPoint(
        string Reference,
        string DataSetReference,
        string RcbReference,
        string Status,
        string Reason,
        string DisplayValueToken);

    internal sealed record Snapshot(
        int Selected,
        int ValueVisible,
        int ValuePending,
        int QualityNotSupplied,
        int QuestionableQuality,
        IReadOnlyList<Group> Groups,
        IReadOnlyList<PendingPoint> PendingPoints)
    {
        public string Summary =>
            $"displayed={ValueVisible}/{Selected}, valuePending={ValuePending}, " +
            $"qualityNotSupplied={QualityNotSupplied}, questionableQuality={QuestionableQuality}";
    }

    internal static Snapshot Evaluate(IEnumerable<Iec61850MonitorPoint> points,
        IedBrowserLiveValues? liveValues = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        // Detach a bounded in-memory snapshot. "Ready" means a process value is
        // visible; it does NOT imply q=Good, an InformationReport or physical parity.
        var rows = points
            .Where(point => !string.IsNullOrWhiteSpace(point.DataSetReference))
            .ToArray();
        // Resolve at most one canonical evidence verdict per point, then
        // reuse it for totals, group counts and pending detail.
        var observed = rows.Select(point =>
            (Point: point, Visible: HasVisibleValue(point, liveValues))).ToArray();
        var visible = observed.Count(row => row.Visible);
        var qualityMissing = rows.Count(point => !HasReportedQuality(point.Quality));
        var questionable = rows.Count(point =>
            (point.Quality ?? string.Empty).Contains("questionable", StringComparison.OrdinalIgnoreCase));
        var groups = observed
            .GroupBy(row => new
            {
                DataSet = Normalize(row.Point.DataSetReference),
                Rcb = Normalize(row.Point.ReportControlReference)
            })
            .OrderBy(group => group.Key.DataSet, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Rcb, StringComparer.Ordinal)
            .Select(group => new Group(
                group.Key.DataSet,
                group.Key.Rcb,
                group.Count(),
                group.Count(row => row.Visible),
                group.Count(row => !row.Visible),
                group.Count(row => !HasReportedQuality(row.Point.Quality))))
            .ToArray();
        var pending = observed
            .Where(row => !row.Visible)
            .Select(row => row.Point)
            .OrderBy(point => point.IecReference, StringComparer.OrdinalIgnoreCase)
            .Select(point => new PendingPoint(
                point.IecReference,
                point.DataSetReference,
                point.ReportControlReference,
                point.Status,
                point.Reason,
                point.Value))
            .ToArray();

        return new Snapshot(rows.Length, visible, rows.Length - visible,
            qualityMissing, questionable, groups, pending);
    }

    // Current-association canonical Report provenance distinguishes a
    // real "Unknown" enumeration from a missing-value UI placeholder.
    // RCB traffic, status labels, and quality alone never prove this.
    private static bool HasVisibleValue(Iec61850MonitorPoint point,
        IedBrowserLiveValues? liveValues)
    {
        if (HasVisibleValue(point.Value)) return true;
        if (!string.Equals(point.Value?.Trim(), "Unknown",
                StringComparison.OrdinalIgnoreCase) ||
            point.IsOffline || liveValues is null || liveValues.IsOffline ||
            point.LastSnapshotAssociation <= 0 ||
            liveValues.Association != point.LastSnapshotAssociation ||
            !liveValues.TryGet(point.IecReference, point.FunctionalConstraint,
                out var evidence) ||
            evidence.IsStale ||
            evidence.Association != point.LastSnapshotAssociation ||
            !string.Equals(evidence.Source, "Report", StringComparison.Ordinal) ||
            !string.Equals(evidence.Value, "Unknown", StringComparison.Ordinal))
            return false;
        return true;
    }

    internal static bool HasVisibleValue(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length > 0 && text != "-" &&
               !text.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("Pending", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HasReportedQuality(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length > 0 && text != "-" &&
               !text.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
               !text.StartsWith("Pending", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(not assigned)" :
        value.Trim().Replace('$', '.');
}
