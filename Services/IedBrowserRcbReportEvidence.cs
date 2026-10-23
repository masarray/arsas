namespace ArIED61850Tester.Services;

/// <summary>
/// Strict, read-only projection of an existing immutable, association-scoped Report
/// continuity summary. No process values, RCB writes or report frames are duplicated.
/// </summary>
public static class IedBrowserRcbReportEvidence
{
    public static Iec61850ReportContinuityStreamSnapshot? Select(
        Iec61850ReportContinuitySnapshot? evidence,
        string rcbReference, string dataSetReference, string reportId)
    {
        if (evidence is null) return null;
        var exactRcb = Canonical(rcbReference);
        if (exactRcb.Length == 0) return null;
        var matches = evidence.Streams.Where(stream =>
            string.Equals(Canonical(stream.Rcb), exactRcb, StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(dataSetReference) ||
                string.Equals(Canonical(stream.DataSet), Canonical(dataSetReference),
                    StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(reportId) ||
                string.Equals(stream.ReportId, reportId, StringComparison.Ordinal)) &&
            stream.Frames > 0 && stream.LastFrameReceivedAtUtc.HasValue)
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static string Canonical(string? reference)
        => (reference ?? "").Trim().Replace('$', '.');
}
