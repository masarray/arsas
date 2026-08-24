using System.Text;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

internal partial class DynamicReportQualificationResultWindow
{
    internal DynamicReportQualificationResultWindow(DynamicReportCommandBoundA3CommissioningResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        InitializeComponent();

        Title = "G2.6-P1 Deterministic A3 dchg Proof Evidence";
        HeaderText.Text = "G2.6-P1 Deterministic A3 — Command → dchg Report";
        SummaryText.Text = result.Summary;
        StateText.Text = result.IsSuccess
            ? "A3 Command-Bound dchg Proven"
            : result.IsBlocked
                ? "Blocked"
                : "A3 Not Proven";
        EvidenceTextBox.Text = BuildG26P1A3Evidence(result);

        if (result.IsSuccess)
            SetPassBadge();
    }

    private static string BuildG26P1A3Evidence(DynamicReportCommandBoundA3CommissioningResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ARSAS G2.6-P1 DETERMINISTIC A3 COMMAND-BOUND DCHG EVIDENCE");
        builder.AppendLine(new string('=', 76));
        builder.AppendLine($"Result: {result.Summary}");
        builder.AppendLine($"Blocked: {result.IsBlocked}");
        builder.AppendLine($"A3 success: {result.IsSuccess}");
        builder.AppendLine($"Command/report correlation: {result.CommandBoundReportCorrelationProven}");

        builder.AppendLine();
        builder.AppendLine("EXISTING ARSAS COMMAND");
        builder.AppendLine($"Captured: {result.Witness.CommandCaptured}");
        builder.AppendLine($"Object: {TextOrDash(result.Witness.CommandSignalReference)}");
        builder.AppendLine($"Control status: {TextOrDash(result.Witness.ControlStatusReference)}");
        builder.AppendLine($"Requested value: {TextOrDash(result.Witness.RequestedValue)}");
        builder.AppendLine($"Source: {TextOrDash(result.Witness.CommandSource)}");
        builder.AppendLine($"Observed at UTC: {result.Witness.CommandObservedAtUtc?.ToString("O") ?? "-"}");
        builder.AppendLine($"Command-bound transition proven: {result.Witness.CommandBoundTransitionProven}");
        builder.AppendLine($"Read-only witness association healthy: {result.Witness.AssociationHealthy}");
        builder.AppendLine($"Witness cycles/read failures: {result.Witness.SampleCycles}/{result.Witness.ReadFailures}");

        if (result.Witness.Transitions.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("COMMAND-BOUND QUALIFIED-MEMBER TRANSITIONS");
            foreach (var transition in result.Witness.Transitions)
            {
                builder.AppendLine(
                    $"[{transition.Index}] {transition.MemberReference} ({transition.PointReference}) {transition.BeforeValue} -> {transition.AfterValue} at {transition.ObservedAtUtc:O}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("DCHG INFORMATIONREPORT");
        builder.AppendLine($"Core success: {result.CoreResult.IsSuccess}");
        builder.AppendLine($"Activation proven: {result.CoreResult.ActivationProven}");
        builder.AppendLine($"Spontaneous dchg proven: {result.CoreResult.SpontaneousDataChangeProven}");
        builder.AppendLine($"URCB: {TextOrDash(result.CoreResult.RcbReference)}");
        builder.AppendLine($"Temporary DataSet: {TextOrDash(result.CoreResult.DataSetReference)}");
        builder.AppendLine($"RptID: {TextOrDash(result.CoreResult.ReportId)}");
        builder.AppendLine($"Report included indexes: [{string.Join(",", result.CoreResult.IncludedIndexes)}]");
        builder.AppendLine($"Report reasons: [{string.Join(",", result.CoreResult.Reasons)}]");
        builder.AppendLine($"Correlated command/report indexes: [{string.Join(",", result.CorrelatedIndexes)}]");
        foreach (var member in result.CorrelatedMemberReferences)
            builder.AppendLine("- correlated member: " + member);

        builder.AppendLine();
        builder.AppendLine("CLEANUP / RELEASE");
        builder.AppendLine($"Monitor cleanup: {result.CoreResult.MonitorCleanupSucceeded}");
        builder.AppendLine($"TrgOps/OptFlds restore: {result.CoreResult.ProofFieldRestoreSucceeded}");
        builder.AppendLine($"Fresh-association cleanup closure: {result.CoreResult.FreshCleanupClosureSucceeded}");
        builder.AppendLine($"Report association healthy after proof: {result.CoreResult.AssociationHealthyAfterReport}");

        builder.AppendLine();
        builder.AppendLine("FULL EVIDENCE");
        foreach (var line in result.EvidenceLines)
            builder.AppendLine(line);

        builder.AppendLine();
        builder.AppendLine("SAFETY STATE");
        builder.AppendLine("A3 command-bound dchg PASS != ProductionEligible.");
        builder.AppendLine("This commissioning action does not save or advance the persisted profile.");
        builder.AppendLine("Production automatic dynamic reporting remains OFF until later shadow verification and G2.6 regression acceptance explicitly mark the identity ProductionEligible.");
        return builder.ToString();
    }
}
