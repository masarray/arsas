using System.Text;
using System.Windows;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class DynamicReportQualificationResultWindow : Window
{
    public DynamicReportQualificationResultWindow(DynamicReportQualificationCommissioningResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        InitializeComponent();

        SummaryText.Text = result.Summary;
        StateText.Text = result.SavedProfile?.State.ToString()
                         ?? result.Coordinator?.Assessment.State.ToString()
                         ?? (result.IsBlocked ? "Blocked" : "Not qualified");
        EvidenceTextBox.Text = BuildEvidence(result);

        if (result.IsSuccess && result.SavedProfile is not null)
        {
            StateBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(236, 253, 243));
            StateBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(171, 225, 193));
            StateText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 118, 71));
        }
    }

    private static string BuildEvidence(DynamicReportQualificationCommissioningResult result)
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
