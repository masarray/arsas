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
        internal bool? SetupSucceeded { get; init; }
        internal bool? SetupRptEnaWriteAccepted { get; init; }
        internal bool? SetupGiWriteAccepted { get; init; }
        internal DateTimeOffset? SetupReturnedAtUtc { get; init; }
        internal bool HasActualReportFrames => Frames > 0;
        internal bool HasDisplayedPointAttribution { get; init; } = true;
        internal string Summary =>
            $"{DataSet} / {Rcb}: " +
            (HasDisplayedPointAttribution
                ? $"initial image {Visible}/{Selected}"
                : "initial image NOT ATTRIBUTED (overlapping selected point rows)") +
            $", pending={Pending}, " +
            $"q-unverified={QualityNotSupplied}, actualFrames={Frames}, " +
            $"RptEnaWrite={Proof(RptEnaWriteAccepted)}, " +
            $"GiWrite={Proof(GiWriteAccepted)}, overflowObserved={Overflow}; " +
            $"activationCall={CallStatus(SetupSucceeded)}, " +
            $"setupRptEnaWrite={Proof(SetupRptEnaWriteAccepted)}, " +
            $"setupGiWrite={Proof(SetupGiWriteAccepted)}, " +
            $"activationReturnedUtc={SetupReturnedAtUtc?.ToString("O") ?? "unknown"}";
        private static string CallStatus(bool? success) => success switch
        {
            true => "success",
            false => "failed",
            _ => "unknown"
        };
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
        int maxGroups = 16,
        IReadOnlyList<Iec61850RcbActivationEvidence>? activations = null,
        IedBrowserLiveValues? liveValues = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (maxGroups is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(maxGroups));

        // A static member's current displayed value and actual RCB receive
        // frame are independent evidence. Do not infer the latter from former.
        var image = StaticDataSetInitialImageDiagnostic.Evaluate(points, liveValues);
        var groups = image.Groups
            .Select(group => (Group: group, HasDisplayedAttribution: true))
            .ToList();
        // Live RCB lifecycle is independent of the chosen single UI owner
        // for duplicate selected DataSet members. Do not hide such an RCB.
        if (activations is not null)
        {
            foreach (var activation in activations.Take(64))
            {
                if (string.IsNullOrWhiteSpace(activation.Rcb) ||
                    string.IsNullOrWhiteSpace(activation.DataSet))
                    continue;
                var rcb = IedBrowserRcbReportEvidence.Canonical(activation.Rcb);
                // A conflicting DataSet for a known RCB is not a second RCB.
                if (groups.Any(item => string.Equals(
                        IedBrowserRcbReportEvidence.Canonical(item.Group.RcbReference),
                        rcb, StringComparison.OrdinalIgnoreCase)))
                    continue;
                groups.Add((new StaticDataSetInitialImageDiagnostic.Group(
                    activation.DataSet, activation.Rcb, 0, 0, 0, 0), false));
            }
        }

        return groups
            .OrderBy(item => item.Group.DataSetReference, StringComparer.Ordinal)
            .ThenBy(item => item.Group.RcbReference, StringComparer.Ordinal)
            .Take(maxGroups)
            .Select(item =>
        {
            var group = item.Group;
            var stream = IedBrowserRcbReportEvidence.Select(
                continuity, group.RcbReference, group.DataSetReference, "");
            // Exact RCB AND DataSet match. An activation for another plan,
            // even one that shares selected process values, cannot be borrowed.
            // The small bounded snapshot is read only on operator diagnosis,
            // never on the MMS receive hot path.
            var exact = activations?.Where(a =>
                string.Equals(IedBrowserRcbReportEvidence.Canonical(a.Rcb),
                    IedBrowserRcbReportEvidence.Canonical(group.RcbReference),
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(IedBrowserRcbReportEvidence.Canonical(a.DataSet),
                    IedBrowserRcbReportEvidence.Canonical(group.DataSetReference),
                    StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            var activation = exact is { Length: 1 } ? exact[0] : null;
            return new Row(
                group.DataSetReference, group.RcbReference, group.Selected,
                group.ValueVisible, group.ValuePending, group.QualityNotSupplied,
                stream?.Frames ?? 0L, stream?.RptEnaWriteAccepted,
                stream?.GiWriteAccepted, stream?.Overflow ?? 0L)
            {
                SetupSucceeded = activation?.SetupSucceeded,
                SetupRptEnaWriteAccepted = activation?.RptEnaWriteAccepted,
                SetupGiWriteAccepted = activation?.GiWriteAccepted,
                SetupReturnedAtUtc = activation?.ActivationReturnedAtUtc,
                HasDisplayedPointAttribution = item.HasDisplayedAttribution
            };
        }).ToArray();
    }
}
