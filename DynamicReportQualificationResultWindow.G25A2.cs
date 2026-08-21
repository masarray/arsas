using System.Text;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

internal partial class DynamicReportQualificationResultWindow
{
    internal DynamicReportQualificationResultWindow(DynamicReportStimulusEligibilityDiscoveryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        InitializeComponent();

        Title = "G2.5-A2 Stimulus Eligibility Discovery Evidence";
        HeaderText.Text = "G2.5-A2 Stimulus Eligibility Discovery";
        SummaryText.Text = result.Summary;
        StateText.Text = result.IsSuccess
            ? "Stimulus Candidate Proven"
            : result.IsBlocked
                ? "Blocked"
                : "No Candidate Transition Proven";
        EvidenceTextBox.Text = BuildG25A2Evidence(result);

        if (result.IsSuccess)
            SetPassBadge();
    }

    private static string BuildG25A2Evidence(DynamicReportStimulusEligibilityDiscoveryResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ARSAS G2.5-A2 READ-ONLY STIMULUS ELIGIBILITY DISCOVERY EVIDENCE");
        builder.AppendLine(new string('=', 84));
        builder.AppendLine($"Result: {result.Summary}");
        builder.AppendLine($"Blocked: {result.IsBlocked}");
        builder.AppendLine($"G2.5-A2 success: {result.IsSuccess}");
        builder.AppendLine($"Stimulus eligibility proven: {result.StimulusEligibilityProven}");

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
        builder.AppendLine("READ-ONLY SAMPLING RESULT");
        builder.AppendLine($"Input profile state: {result.InputProfile?.State.ToString() ?? "-"}");
        builder.AppendLine($"Baseline captured: {result.BaselineCaptured}");
        builder.AppendLine($"Association healthy: {result.AssociationHealthy}");
        builder.AppendLine($"Candidate count: {result.CandidateCount}");
        builder.AppendLine($"Fast-lane count: {result.FastLaneCount}");
        builder.AppendLine($"Sample cycles: {result.SampleCycles}");
        builder.AppendLine($"Read failures: {result.ReadFailures}");

        builder.AppendLine();
        builder.AppendLine("ELIGIBLE CANDIDATES");
        if (result.EligibleCandidates.Count == 0)
        {
            builder.AppendLine("  none");
        }
        else
        {
            foreach (var candidate in result.EligibleCandidates)
            {
                builder.AppendLine($"  #{candidate.Rank} {candidate.Reference}");
                builder.AppendLine($"     MMS: {candidate.MmsReference}");
                builder.AppendLine($"     Kind: {candidate.Kind}");
                builder.AppendLine($"     Baseline -> final: {candidate.BaselineValue} -> {candidate.FinalValue}");
                builder.AppendLine($"     Transitions: {candidate.TransitionCount}");
                builder.AppendLine($"     Observed active/pulse duration ms: {FormatDuration(candidate.ObservedActiveMilliseconds)}");
                builder.AppendLine($"     Fast lane: {candidate.FastLane}");
                builder.AppendLine($"     Score: {candidate.Score}");
                builder.AppendLine($"     Selection reason: {candidate.SelectionReason}");
                foreach (var transition in candidate.Transitions)
                    builder.AppendLine($"       {transition.ObservedAtUtc:O} | {transition.BeforeValue} -> {transition.AfterValue}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("ALL OBSERVED CANDIDATES");
        foreach (var candidate in result.Observations)
        {
            builder.AppendLine($"  {(candidate.FastLane ? "FAST" : "SECONDARY"),-9} | score={candidate.Score,4} | transitions={candidate.TransitionCount,2} | {candidate.Kind,-20} | {candidate.Reference} | {candidate.BaselineValue} -> {candidate.FinalValue}");
        }

        builder.AppendLine();
        builder.AppendLine("WIRE / DIAGNOSTIC EVIDENCE");
        foreach (var line in result.EvidenceLines)
            builder.AppendLine(line);

        builder.AppendLine();
        builder.AppendLine("SAFETY STATE");
        builder.AppendLine("G2.5-A2 is read-only: it does not read/write RCB attributes, enable RptEna, send GI, or mutate any DataSet.");
        builder.AppendLine("G2.5-A2 does not alter the persisted InformationReportProven profile.");
        builder.AppendLine("G2.5-A2 PASS only identifies a physical stimulus-responsive MMS candidate; it does NOT prove dchg reporting.");
        builder.AppendLine("Production automatic dynamic reporting remains OFF.");
        return builder.ToString();
    }

    private static string FormatDuration(double? milliseconds)
        => milliseconds.HasValue
            ? milliseconds.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            : "-";
}
