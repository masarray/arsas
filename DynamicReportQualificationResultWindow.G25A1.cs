using System.Text;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

internal partial class DynamicReportQualificationResultWindow
{
    internal DynamicReportQualificationResultWindow(DynamicReportStimulusWitnessCommissioningResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        InitializeComponent();

        Title = "G2.5-A1 Stimulus Witness Evidence";
        HeaderText.Text = "G2.5-A1 Stimulus Witness + dchg Correlation";
        SummaryText.Text = result.Summary;
        StateText.Text = result.IsSuccess
            ? "Stimulus/report correlation Proven"
            : result.StimulusWitnessProven
                ? "Stimulus proven; report correlation not proven"
                : "Stimulus witness not proven";
        EvidenceTextBox.Text = BuildG25A1Evidence(result);

        if (result.IsSuccess)
            SetPassBadge();
    }

    private static string BuildG25A1Evidence(DynamicReportStimulusWitnessCommissioningResult result)
    {
        var core = result.CoreResult;
        var witness = result.Witness;
        var builder = new StringBuilder();
        builder.AppendLine("ARSAS G2.5-A1 STIMULUS WITNESS + SPONTANEOUS DCHG CORRELATION EVIDENCE");
        builder.AppendLine(new string('=', 88));
        builder.AppendLine($"Result: {result.Summary}");
        builder.AppendLine($"G2.5-A1 success: {result.IsSuccess}");
        builder.AppendLine($"Stimulus witness proven: {result.StimulusWitnessProven}");
        builder.AppendLine($"Report correlation proven: {result.ReportCorrelationProven}");
        builder.AppendLine($"Correlated indexes: [{string.Join(",", result.CorrelatedIndexes)}]");

        builder.AppendLine();
        builder.AppendLine("CORE G2.5-A REPORT PATH");
        builder.AppendLine($"Activation proven: {core.ActivationProven}");
        builder.AppendLine($"Spontaneous data-change proven: {core.SpontaneousDataChangeProven}");
        builder.AppendLine($"Report association healthy: {core.AssociationHealthyAfterReport}");
        builder.AppendLine($"Report included indexes: [{string.Join(",", core.IncludedIndexes)}]");
        builder.AppendLine($"Report reasons: [{string.Join(",", core.Reasons)}]");
        builder.AppendLine($"Monitor cleanup: {core.MonitorCleanupSucceeded}");
        builder.AppendLine($"Proof-field restore: {core.ProofFieldRestoreSucceeded}");
        builder.AppendLine($"Fresh cleanup closure: {core.FreshCleanupClosureSucceeded}");

        builder.AppendLine();
        builder.AppendLine("INDEPENDENT READ-ONLY STIMULUS WITNESS");
        builder.AppendLine($"ARMED observed: {witness.ArmedObserved}");
        builder.AppendLine($"Baseline captured: {witness.BaselineCaptured}");
        builder.AppendLine($"Qualified-member change observed: {witness.ChangeObserved}");
        builder.AppendLine($"Witness association healthy: {witness.AssociationHealthy}");
        builder.AppendLine($"Sample cycles: {witness.SampleCycles}");
        builder.AppendLine($"Read failures: {witness.ReadFailures}");
        if (witness.BaselineValues.Count > 0)
        {
            builder.AppendLine("Baseline values:");
            for (var index = 0; index < witness.BaselineValues.Count && index < core.MemberReferences.Count; index++)
                builder.AppendLine($"  [{index}] {core.MemberReferences[index]} = {witness.BaselineValues[index]}");
        }
        builder.AppendLine("Witnessed transitions:");
        foreach (var transition in witness.Transitions)
            builder.AppendLine($"  [{transition.Index}] {transition.MemberReference}: {transition.BeforeValue} -> {transition.AfterValue} @ {transition.ObservedAtUtc:O}");

        builder.AppendLine();
        builder.AppendLine("WIRE / DIAGNOSTIC EVIDENCE");
        foreach (var line in result.EvidenceLines)
            builder.AppendLine(line);

        builder.AppendLine();
        builder.AppendLine("SAFETY STATE");
        builder.AppendLine("The G2.5-A1 witness association is read-only and does not read/write RCB attributes or mutate DataSets.");
        builder.AppendLine("The core G2.5-A path still sends NO GI and remains one-URCB commissioning only.");
        builder.AppendLine("The persisted InformationReportProven profile is not advanced by this gate.");
        builder.AppendLine("Production automatic dynamic reporting remains OFF.");
        return builder.ToString();
    }
}
