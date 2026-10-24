using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Immutable, observational per-RCB initial-image triage. Never combines
/// another RCB's actual ingress with this DataSet, issues a GI, enables
/// RptEna, polls process values, or fabricates missing members.
/// </summary>
internal static class StaticDataSetRcbImageTriage
{
    internal sealed record Row(
        string DataSet, string Rcb, int Selected, int Visible, int Pending,
        int QualityNotSupplied, long Frames, bool? RptEnaWriteAccepted,
        bool? GiWriteAccepted, long Overflow)
    {
        internal bool HasActualReportFrames => Frames > 0;
        internal string Summary =>
            $"{DataSet} / {Rcb}: initial image {Visible}/{Selected}, pending={Pending}, " +
            $"q-unverified={QualityNotSupplied}, actualFrames={Frames}, " +
            $"RptEnaWrite={Proof(RptEnaWriteAccepted)}, " +
            $"GiWrite={Proof(GiWriteAccepted)}, overflowObserved={Overflow}";
        private static string Proof(bool? value) => value switch
        {
            true => "accepted",
            false => "rejected",
            _ => "unverified"
        };
    }

    internal static IReadOnlyList<Row> Evaluate(
        IEnumerable<Iec61850MonitorPoint> points,
        Iec61850ReportContinuitySnapshot? continuity,
        int maxGroups = 16)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (maxGroups is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(maxGroups));

        // A static member's current displayed value and actual RCB receive
        // frame are independent evidence. Do not infer the latter from former.
        var image = StaticDataSetInitialImageDiagnostic.Evaluate(points);
        return image.Groups.Take(maxGroups).Select(group =>
        {
            var stream = IedBrowserRcbReportEvidence.Select(
                continuity, group.RcbReference, group.DataSetReference, "");
            return new Row(
                group.DataSetReference, group.RcbReference, group.Selected,
                group.ValueVisible, group.ValuePending, group.QualityNotSupplied,
                stream?.Frames ?? 0L, stream?.RptEnaWriteAccepted,
                stream?.GiWriteAccepted, stream?.Overflow ?? 0L);
        }).ToArray();
    }
}
