using System.Globalization;
using System.Text;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

internal partial class DynamicReportQualificationResultWindow
{
    internal DynamicReportQualificationResultWindow(DynamicReportCommandBoundStimulusWitnessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        InitializeComponent();

        Title = "G2.5-A2.1 Command-Bound Stimulus Witness Evidence";
        HeaderText.Text = "G2.5-A2.1 Command-Bound High-Speed Witness";
        SummaryText.Text = result.Summary;
        StateText.Text = result.IsSuccess
            ? "Command-Bound Transition Proven"
            : result.IsBlocked
                ? "Blocked"
                : "Command-Bound Transition Not Proven";
        EvidenceTextBox.Text = BuildG25A21Evidence(result);

        if (result.IsSuccess)
            SetPassBadge();
    }

    private static string BuildG25A21Evidence(DynamicReportCommandBoundStimulusWitnessResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ARSAS G2.5-A2.1 COMMAND-BOUND HIGH-SPEED STIMULUS WITNESS EVIDENCE");
        builder.AppendLine(new string('=', 92));
        builder.AppendLine($"Result: {result.Summary}");
        builder.AppendLine($"Blocked: {result.IsBlocked}");
        builder.AppendLine($"G2.5-A2.1 success: {result.IsSuccess}");
        builder.AppendLine($"Stimulus witness proven: {result.StimulusWitnessProven}");

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
        builder.AppendLine("COMMAND BINDING");
        builder.AppendLine($"Input profile state: {result.InputProfile?.State.ToString() ?? "-"}");
        builder.AppendLine($"Pre-command baseline captured: {result.BaselineCaptured}");
        builder.AppendLine($"Command captured: {result.CommandCaptured}");
        builder.AppendLine($"Command signal: {TextOrDash(result.CommandSignalReference)}");
        builder.AppendLine($"ControlStatusReference: {TextOrDash(result.ControlStatusReference)}");
        builder.AppendLine($"Control model: {TextOrDash(result.ControlModelText)}");
        builder.AppendLine($"Pre-command baseline points: {result.PreCommandBaselineCount}");
        builder.AppendLine($"Focused candidates: {result.FocusCandidateCount}");
        builder.AppendLine($"Sample cycles: {result.SampleCycles}");
        builder.AppendLine($"Read failures: {result.ReadFailures}");
        builder.AppendLine($"Association healthy: {result.AssociationHealthy}");

        builder.AppendLine();
        builder.AppendLine("ELIGIBLE COMMAND-BOUND CANDIDATES");
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
                builder.AppendLine($"     Exact ControlStatusReference: {candidate.ExactControlStatus}");
                builder.AppendLine($"     Kind: {candidate.Kind}");
                builder.AppendLine($"     Baseline -> final: {candidate.BaselineValue} -> {candidate.FinalValue}");
                builder.AppendLine($"     Transitions: {candidate.TransitionCount}");
                builder.AppendLine($"     Observed active/pulse duration ms: {FormatDuration(candidate.ObservedActiveMilliseconds)}");
                foreach (var transition in candidate.Transitions)
                    builder.AppendLine($"       {transition.ObservedAtUtc:O} | {transition.BeforeValue} -> {transition.AfterValue}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("ALL FOCUSED OBSERVATIONS");
        foreach (var candidate in result.Observations)
        {
            builder.AppendLine($"  exact={candidate.ExactControlStatus,-5} | transitions={candidate.TransitionCount,2} | {candidate.Kind,-20} | {candidate.Reference} | {candidate.BaselineValue} -> {candidate.FinalValue}");
        }

        builder.AppendLine();
        builder.AppendLine("WIRE / DIAGNOSTIC EVIDENCE");
        foreach (var line in result.EvidenceLines)
            builder.AppendLine(line);

        builder.AppendLine();
        builder.AppendLine("SAFETY STATE");
        builder.AppendLine("G2.5-A2.1 witness is read-only and does not alter, delay, wrap or re-issue the existing ARSAS control transaction.");
        builder.AppendLine("The one OPEN/CLOSE command is the operator-requested existing ARSAS control action; the witness itself performs no control write.");
        builder.AppendLine("G2.5-A2.1 does not access/mutate RCB/DataSet state, send GI, save the InformationReportProven profile, or enable production dynamic reporting.");
        builder.AppendLine("G2.5-A2.1 PASS identifies a command-bound physical MMS candidate only; it does NOT prove spontaneous dchg reporting.");
        builder.AppendLine("Production automatic dynamic reporting remains OFF.");
        return builder.ToString();
    }

    private static string FormatDuration(double? milliseconds)
        => milliseconds.HasValue
            ? milliseconds.Value.ToString("0.0", CultureInfo.InvariantCulture)
            : "-";

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
