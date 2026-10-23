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
        string Reason);

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

    internal static Snapshot Evaluate(IEnumerable<Iec61850MonitorPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        // Detach a bounded in-memory snapshot. "Ready" means a process value is
        // visible; it does NOT imply q=Good, an InformationReport or physical parity.
        var rows = points
            .Where(point => !string.IsNullOrWhiteSpace(point.DataSetReference))
            .ToArray();
        var visible = rows.Count(point => HasVisibleValue(point.Value));
        var qualityMissing = rows.Count(point => !HasReportedQuality(point.Quality));
        var questionable = rows.Count(point =>
            (point.Quality ?? string.Empty).Contains("questionable", StringComparison.OrdinalIgnoreCase));
        var groups = rows
            .GroupBy(point => new
            {
                DataSet = Normalize(point.DataSetReference),
                Rcb = Normalize(point.ReportControlReference)
            })
            .OrderBy(group => group.Key.DataSet, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Rcb, StringComparer.Ordinal)
            .Select(group => new Group(
                group.Key.DataSet,
                group.Key.Rcb,
                group.Count(),
                group.Count(point => HasVisibleValue(point.Value)),
                group.Count(point => !HasVisibleValue(point.Value)),
                group.Count(point => !HasReportedQuality(point.Quality))))
            .ToArray();
        var pending = rows
            .Where(point => !HasVisibleValue(point.Value))
            .OrderBy(point => point.IecReference, StringComparer.OrdinalIgnoreCase)
            .Select(point => new PendingPoint(
                point.IecReference,
                point.DataSetReference,
                point.ReportControlReference,
                point.Status,
                point.Reason))
            .ToArray();

        return new Snapshot(rows.Length, visible, rows.Length - visible,
            qualityMissing, questionable, groups, pending);
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
