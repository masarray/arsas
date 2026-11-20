namespace ArIED61850Tester.Services;

/// <summary>
/// Prevent an incomplete live MMS directory from masquerading as proof that
/// the peer has no configured ReportControl. SCL describes a design; live
/// authority still requires separate readback and actual Report ingress.
/// </summary>
public static class StaticReportEvidencePolicy
{
    public sealed record MissingRcbEvidence(
        string Label, string SourceMode, string PointStatus, string Explanation);

    public static MissingRcbEvidence ClassifyMissingRcb(bool hasOpenedSclDesign) =>
        hasOpenedSclDesign
            ? new(
                "NO RCB IN OPEN SCL",
                "Static DataSet: no RCB in open SCL",
                "Unavailable / no RCB in open SCL",
                "The opened SCL has no matching ReportControl. Live MMS may differ; do not assume the relay lacks an RCB.")
            : new(
                "NO VERIFIED LIVE RCB→DATASET BINDING (UNVERIFIED)",
                "Static DataSet: no verified live RCB/DataSet binding",
                "Unverified / live RCB binding incomplete",
                "Observed RCB instances may still lack a live-read DatSet binding; live enumeration may be partial on large MMS domains. This does not prove the IED has no RCB or cannot report; no cyclic MMS process polling is authorized.");

    /// <summary>
    /// User-facing coverage text never says a DataSet is report-ready merely
    /// because its design/live inventory contains an RCB-to-DataSet reference.
    /// Wire availability still depends on exact readback and received frames.
    /// </summary>
    public static string DescribeDataSetCoverage(
        int dataSetCount, int observedRcbBoundDataSetCount, bool hasOpenedSclDesign)
    {
        if (dataSetCount <= 0)
            return "No static DataSets";

        var represented = Math.Clamp(observedRcbBoundDataSetCount, 0, dataSetCount);
        if (represented == 0)
            return hasOpenedSclDesign
                ? $"{dataSetCount} DataSet(s) · no RCB in open SCL"
                : $"{dataSetCount} DataSet(s) · live RCB/DataSet binding unverified";

        var source = hasOpenedSclDesign ? "SCL-configured" : "live-observed";
        return $"{represented}/{dataSetCount} DataSet(s) with {source} RCB/DataSet binding · Report ingress unverified";
    }

    /// <summary>
    /// The old production engine exposes page-limit exhaustion through its
    /// diagnostic message, not a typed stop reason. Until engine contracts can
    /// be promoted without changing the protected pin, strictly require an
    /// observed initial 64-page stop AND unresolved selected static segments.
    /// No retry for healthy RCBs, name-limit exhaustion, other MMS faults,
    /// or a lost association. The caller is responsible for one-shot execution.
    /// </summary>
    public static bool ShouldExpandTargetedNameList(
        bool associationReady,
        bool reconciliationPartial,
        int unresolvedSelectedSegments,
        IReadOnlyList<string> reconciliationWarnings)
    {
        ArgumentNullException.ThrowIfNull(reconciliationWarnings);
        return associationReady && reconciliationPartial &&
               unresolvedSelectedSegments > 0 &&
               reconciliationWarnings.Any(warning =>
                   warning.Contains("NamedVariable enumeration is partial", StringComparison.Ordinal) &&
                   warning.Contains("page limit 64 reached", StringComparison.Ordinal));
    }
}
