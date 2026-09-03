using System.Text;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

internal partial class DynamicReportQualificationResultWindow
{
    internal DynamicReportQualificationResultWindow(DynamicReportShadowVerificationCommissioningResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        InitializeComponent();

        Title = "G2.6 Physical Shadow Verification Evidence";
        HeaderText.Text = "G2.6 Report vs Independent MMS Shadow";
        SummaryText.Text = result.Summary;
        StateText.Text = result.IsBlocked
            ? "Blocked"
            : result.ShadowPassed
                ? "Shadow Passed / Production OFF"
                : result.PhysicalCollectionCompleted
                    ? "Collected / Shadow Not Passed"
                    : "Incomplete";
        EvidenceTextBox.Text = BuildG26ShadowEvidence(result);

        if (result.ShadowPassed)
            SetPassBadge();
    }

    private static string BuildG26ShadowEvidence(DynamicReportShadowVerificationCommissioningResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ARSAS G2.6 PHYSICAL SHADOW VERIFICATION EVIDENCE");
        builder.AppendLine(new string('=', 68));
        builder.AppendLine($"Result: {result.Summary}");
        builder.AppendLine($"Blocked: {result.IsBlocked}");
        builder.AppendLine($"Physical collection completed: {result.PhysicalCollectionCompleted}");
        builder.AppendLine($"Typed shadow passed: {result.ShadowPassed}");
        builder.AppendLine($"Cleanup succeeded: {result.CleanupSucceeded}");
        builder.AppendLine($"Deliberate reconnect proven: {result.ReconnectProven}");
        builder.AppendLine($"Exact RCB: {Dash(result.RcbReference)}");
        builder.AppendLine($"Exact member count: {result.MemberReferences.Count}");

        if (result.MemberReferences.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("EXACT INFORMATIONREPORT-PROVEN MEMBER ENVELOPE");
            for (var index = 0; index < result.MemberReferences.Count; index++)
                builder.AppendLine($"[{index}] {result.MemberReferences[index]}");
        }

        if (result.Evidence is not null)
        {
            var evidence = result.Evidence;
            builder.AppendLine();
            builder.AppendLine("PHYSICAL SHADOW COUNTERS");
            builder.AppendLine($"Evidence ID: {evidence.EvidenceId}");
            builder.AppendLine($"Report observations: {evidence.ReportObservations.Count}");
            builder.AppendLine($"Independent MMS polls: {evidence.PollObservations.Count}");
            builder.AppendLine($"Reconnects: {evidence.SuccessfulReconnects}/{evidence.ReconnectAttempts}");
            builder.AppendLine($"Report resubscriptions after reconnect: {evidence.ReportResubscriptionsAfterReconnect}");
            builder.AppendLine($"Poll reference recoveries after reconnect: {evidence.PollReferenceRecoveriesAfterReconnect}");
            builder.AppendLine($"Dynamic activation attempts: {evidence.DynamicActivationAttempts}");
            builder.AppendLine($"Report quality observations: {evidence.ReportObservations.Count(item => !string.IsNullOrWhiteSpace(item.Quality))}");
            builder.AppendLine($"Report device timestamp observations: {evidence.ReportObservations.Count(item => item.DeviceTimestampUtc.HasValue)}");
        }

        if (result.Acceptance?.Shadow is not null)
        {
            var shadow = result.Acceptance.Shadow;
            builder.AppendLine();
            builder.AppendLine("TYPED ARIEC SHADOW GATES");
            builder.AppendLine($"Exact member identity: {shadow.ExactMemberIdentityPassed}");
            builder.AppendLine($"Value parity: {shadow.ValueParityPassed}");
            builder.AppendLine($"Quality parity: {shadow.QualityParityPassed}");
            builder.AppendLine($"Device timestamp parity: {shadow.TimestampParityPassed}");
            builder.AppendLine($"Report order: {shadow.ReportOrderPassed}");
            builder.AppendLine($"No missing report edges: {shadow.NoMissingReportEdgesPassed}");
            builder.AppendLine($"No duplicate report edges: {shadow.NoDuplicateReportEdgesPassed}");
            builder.AppendLine($"Polling authority: {shadow.PollingAuthorityGuardPassed}");
            builder.AppendLine($"Reconnect regression: {shadow.ReconnectRegressionPassed}");
            builder.AppendLine($"No repeated mutation loop: {shadow.NoRepeatedMutationLoopPassed}");
            foreach (var failure in shadow.Failures)
                builder.AppendLine("FAIL: " + failure);
        }

        if (result.Acceptance?.ProductionAcceptanceCandidate is not null)
        {
            var candidate = result.Acceptance.ProductionAcceptanceCandidate;
            builder.AppendLine();
            builder.AppendLine("STRICT PRODUCTION-ACCEPTANCE CANDIDATE — NOT PERSISTED");
            builder.AppendLine($"Smart Control regression: {candidate.ControlRegressionPassed}");
            builder.AppendLine($"Static reporting regression: {candidate.StaticReportingRegressionPassed}");
            builder.AppendLine($"Dynamic InformationReport regression: {candidate.DynamicInformationReportRegressionPassed}");
            builder.AppendLine($"Polling authority: {candidate.PollingAuthorityGuardPassed}");
            builder.AppendLine($"Reconnect regression: {candidate.ReconnectRegressionPassed}");
            builder.AppendLine($"Observed q/t regression: {candidate.QualityRegressionPassed}");
            builder.AppendLine($"No mutation loop: {candidate.NoRepeatedMutationLoopPassed}");
            builder.AppendLine($"All passed: {candidate.AllPassed}");
        }

        builder.AppendLine();
        builder.AppendLine("DETAILED EVIDENCE");
        foreach (var line in result.EvidenceLines)
            builder.AppendLine(line);

        builder.AppendLine();
        builder.AppendLine("STATE BOUNDARY");
        builder.AppendLine("Shadow PASS != ProductionEligible.");
        builder.AppendLine("The persisted profile remains InformationReportProven and production automatic dynamic reporting remains OFF.");
        return builder.ToString();
    }

    private static string Dash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
