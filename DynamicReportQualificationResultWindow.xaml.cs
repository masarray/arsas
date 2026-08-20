using System.Text;
using System.Windows;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

internal partial class DynamicReportQualificationResultWindow : Window
{
    internal DynamicReportQualificationResultWindow(DynamicReportQualificationCommissioningResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        InitializeComponent();

        Title = "G2.3 Dynamic Reporting Qualification Evidence";
        HeaderText.Text = "G2.3 Dynamic Reporting Qualification";
        SummaryText.Text = result.Summary;
        StateText.Text = result.SavedProfile?.State.ToString()
                         ?? result.Coordinator?.Assessment.State.ToString()
                         ?? (result.IsBlocked ? "Blocked" : "Not qualified");
        EvidenceTextBox.Text = BuildG23Evidence(result);

        if (result.IsSuccess && result.SavedProfile is not null)
            SetPassBadge();
    }

    internal DynamicReportQualificationResultWindow(DynamicReportActivationCommissioningResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        InitializeComponent();

        Title = "G2.4 One-URCB InformationReport Proof Evidence";
        HeaderText.Text = "G2.4 One-URCB InformationReport Proof";
        SummaryText.Text = result.Summary;
        StateText.Text = result.SavedProfile?.State.ToString()
                         ?? result.InputProfile?.State.ToString()
                         ?? (result.IsBlocked ? "Blocked" : "Not proven");
        EvidenceTextBox.Text = BuildG24Evidence(result);

        if (result.IsSuccess && result.SavedProfile is not null)
            SetPassBadge();
    }

    private void SetPassBadge()
    {
        StateBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(236, 253, 243));
        StateBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(171, 225, 193));
        StateText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 118, 71));
    }

    private static string BuildG23Evidence(DynamicReportQualificationCommissioningResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ARSAS G2 DYNAMIC REPORTING QUALIFICATION EVIDENCE");
        builder.AppendLine(new string('=', 62));
        builder.AppendLine($"Result: {result.Summary}");
        builder.AppendLine($"Blocked: {result.IsBlocked}");
        builder.AppendLine($"Qualification success: {result.IsSuccess}");

        if (result.Identity is not null)
        {
            builder.AppendLine();
            builder.AppendLine("IED IDENTITY");
            builder.AppendLine($"Stable identity: {result.Identity.StableIdentityKey}");
            builder.AppendLine($"Model fingerprint: {result.Identity.ModelFingerprint}");
            builder.AppendLine($"Model: {result.Identity.Model}");
            builder.AppendLine($"Firmware: {result.Identity.FirmwareRevision}");
            builder.AppendLine($"Profile revision: {result.Identity.ProfileRevision}");
        }

        builder.AppendLine();
        builder.AppendLine("CLASS-A CANDIDATES");
        builder.AppendLine($"Selected signals: {result.Candidates.SelectedSignalCount}");
        builder.AppendLine($"Scalar ST/MX candidates: {result.Candidates.ScalarClassACount}");
        builder.AppendLine($"Exact live mappings: {result.Candidates.ExactResolvedCount}");
        builder.AppendLine($"Direct MMS reads passed: {result.Candidates.DirectReadValidatedCount}");

        if (result.Coordinator is not null)
        {
            builder.AppendLine();
            builder.AppendLine("QUALIFICATION LADDER");
            builder.AppendLine(result.Coordinator.Summary);
            builder.AppendLine($"Successful milestones: {string.Join(", ", result.Coordinator.SuccessfulMilestoneMemberCounts)}");
            builder.AppendLine($"Failed milestone: {result.Coordinator.FailedMilestoneMemberCount}");
            builder.AppendLine($"Failure localization: {result.Coordinator.FailureLocalizationAttempted}");
            builder.AppendLine($"Fresh association required: {result.Coordinator.RequiresFreshAssociation}");
            builder.AppendLine($"Envelope candidate: {result.Coordinator.EnvelopeCandidateAttemptId}");
        }

        if (result.SavedProfile is not null)
        {
            builder.AppendLine();
            builder.AppendLine("PERSISTED PROFILE");
            builder.AppendLine($"State: {result.SavedProfile.State}");
            builder.AppendLine($"Safe exact envelope: {result.SavedProfile.ProvenSafeMemberCount} member(s)");
            builder.AppendLine($"Proven Define request: {result.SavedProfile.ProvenSafeDefineRequestByteCount} byte(s)");
            builder.AppendLine($"Negotiated max MMS PDU: {result.SavedProfile.NegotiatedMaxMmsPduSize?.ToString() ?? "unknown"}");
            builder.AppendLine($"File: {result.ProfilePath}");
        }

        if (result.Candidates.Rejections.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("CANDIDATE REJECTIONS");
            foreach (var rejection in result.Candidates.Rejections)
                builder.AppendLine("- " + rejection);
        }

        builder.AppendLine();
        builder.AppendLine("WIRE / SAFETY EVIDENCE");
        foreach (var line in result.EvidenceLines)
            builder.AppendLine(line);

        builder.AppendLine();
        builder.AppendLine("SAFETY STATE");
        builder.AppendLine("EnvelopeQualified != RcbActivationProven != InformationReportProven != ProductionEligible");
        builder.AppendLine("No automatic production dynamic RCB/URCB activation is enabled by this commissioning action.");
        return builder.ToString();
    }

    private static string BuildG24Evidence(DynamicReportActivationCommissioningResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ARSAS G2.4 ONE-URCB INFORMATIONREPORT PROOF EVIDENCE");
        builder.AppendLine(new string('=', 68));
        builder.AppendLine($"Result: {result.Summary}");
        builder.AppendLine($"Blocked: {result.IsBlocked}");
        builder.AppendLine($"G2.4 success: {result.IsSuccess}");
        builder.AppendLine($"Cleanup proven: {result.CleanupSucceeded}");

        if (result.Identity is not null)
        {
            builder.AppendLine();
            builder.AppendLine("IED IDENTITY");
            builder.AppendLine($"Stable identity: {result.Identity.StableIdentityKey}");
            builder.AppendLine($"Model fingerprint: {result.Identity.ModelFingerprint}");
            builder.AppendLine($"Model: {result.Identity.Model}");
            builder.AppendLine($"Firmware: {result.Identity.FirmwareRevision}");
            builder.AppendLine($"Profile revision: {result.Identity.ProfileRevision}");
        }

        builder.AppendLine();
        builder.AppendLine("G2.3 INPUT ENVELOPE");
        builder.AppendLine($"Input profile state: {result.InputProfile?.State.ToString() ?? "-"}");
        builder.AppendLine($"Proven safe envelope: {result.InputProfile?.ProvenSafeMemberCount.ToString() ?? "-"} member(s)");
        builder.AppendLine($"G2.4 exact members: {result.MemberReferences.Count}");
        foreach (var member in result.MemberReferences)
            builder.AppendLine("- " + member);

        builder.AppendLine();
        builder.AppendLine("ONE-URCB PROOF TARGET");
        builder.AppendLine($"URCB: {TextOrDash(result.RcbReference)}");
        builder.AppendLine($"Temporary DataSet: {TextOrDash(result.DataSetReference)}");

        if (result.ActivationProof is not null)
        {
            builder.AppendLine();
            builder.AppendLine("RCB ACTIVATION PROOF");
            builder.AppendLine($"Success: {result.ActivationProof.IsSuccess}");
            builder.AppendLine($"Fresh RCB availability: {result.ActivationProof.FreshRcbAvailabilityVerified}");
            builder.AppendLine($"Exact DataSet readback: {result.ActivationProof.DataSetReadbackVerified}");
            builder.AppendLine($"DatSet binding accepted: {result.ActivationProof.RcbDataSetBindingAccepted}");
            builder.AppendLine($"RptEna accepted: {result.ActivationProof.RptEnaAccepted}");
            builder.AppendLine($"Association healthy after activation: {result.ActivationProof.AssociationHealthyAfterActivation}");
            builder.AppendLine($"Evidence ID: {result.ActivationProof.EvidenceId}");
        }

        if (result.InformationReportProof is not null)
        {
            builder.AppendLine();
            builder.AppendLine("ACTUAL INFORMATIONREPORT PROOF");
            builder.AppendLine($"Success: {result.InformationReportProof.IsSuccess}");
            builder.AppendLine($"Kind: {result.InformationReportProof.Kind}");
            builder.AppendLine($"Actual InformationReport received: {result.InformationReportProof.ActualInformationReportReceived}");
            builder.AppendLine($"Report identity verified: {result.InformationReportProof.ReportIdentityVerified}");
            builder.AppendLine($"Exact member mapping verified: {result.InformationReportProof.ExactMemberMappingVerified}");
            builder.AppendLine($"Association healthy after report: {result.InformationReportProof.AssociationHealthyAfterReport}");
            builder.AppendLine($"Report-authoritative proof points: {result.InformationReportProof.ReportAuthoritativePointCount}");
            builder.AppendLine($"Evidence ID: {result.InformationReportProof.EvidenceId}");
        }

        if (result.SavedProfile is not null)
        {
            builder.AppendLine();
            builder.AppendLine("PERSISTED PROFILE AFTER G2.4");
            builder.AppendLine($"State: {result.SavedProfile.State}");
            builder.AppendLine($"File: {result.ProfilePath}");
        }

        builder.AppendLine();
        builder.AppendLine("WIRE / SAFETY EVIDENCE");
        foreach (var line in result.EvidenceLines)
            builder.AppendLine(line);

        builder.AppendLine();
        builder.AppendLine("SAFETY STATE");
        builder.AppendLine("RptEna accepted != InformationReportProven");
        builder.AppendLine("GI accepted != InformationReportProven");
        builder.AppendLine("InformationReportProven != ProductionEligible");
        builder.AppendLine("G2.4 uses one auxiliary URCB only and does not make production monitoring report-authoritative.");
        builder.AppendLine("Production automatic dynamic reporting remains OFF until controlled G2.5 scale-out and all G2.6 physical regressions pass.");
        return builder.ToString();
    }

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private void CopyEvidence_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(EvidenceTextBox.Text ?? string.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Copy Evidence", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();
}
