using System.Text;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

internal partial class DynamicReportQualificationResultWindow
{
    internal DynamicReportQualificationResultWindow(DynamicReportSpontaneousDataChangeCommissioningResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        InitializeComponent();

        Title = "G2.5-A Spontaneous dchg Proof Evidence";
        HeaderText.Text = "G2.5-A Spontaneous dchg Proof";
        SummaryText.Text = result.Summary;
        StateText.Text = result.IsSuccess
            ? "Spontaneous dchg Proven"
            : result.IsBlocked
                ? "Blocked"
                : "dchg proof not proven";
        EvidenceTextBox.Text = BuildG25AEvidence(result);

        if (result.IsSuccess)
            SetPassBadge();
    }

    private static string BuildG25AEvidence(DynamicReportSpontaneousDataChangeCommissioningResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ARSAS G2.5-A SPONTANEOUS DCHG INFORMATIONREPORT EVIDENCE");
        builder.AppendLine(new string('=', 76));
        builder.AppendLine($"Result: {result.Summary}");
        builder.AppendLine($"Blocked: {result.IsBlocked}");
        builder.AppendLine($"G2.5-A success: {result.IsSuccess}");

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
        builder.AppendLine("G2.5-A TARGET");
        builder.AppendLine($"Input profile state: {result.InputProfile?.State.ToString() ?? "-"}");
        builder.AppendLine($"URCB: {G25ATextOrDash(result.RcbReference)}");
        builder.AppendLine($"Temporary DataSet: {G25ATextOrDash(result.DataSetReference)}");
        builder.AppendLine($"RptID: {G25ATextOrDash(result.ReportId)}");
        builder.AppendLine($"Qualified members: {result.MemberReferences.Count}");
        foreach (var member in result.MemberReferences)
            builder.AppendLine("  " + member);

        builder.AppendLine();
        builder.AppendLine("TRIGGER CONTRACT");
        builder.AppendLine("TrgOps temporary: dchg ONLY");
        builder.AppendLine("Canonical TrgOps raw target: 0240");
        builder.AppendLine("OptFlds temporary: reason-for-inclusion + data-set-name");
        builder.AppendLine("Canonical OptFlds raw target: 061800");
        builder.AppendLine("GI requested: False");
        builder.AppendLine("Integrity/qchg/dupd requested: False");

        builder.AppendLine();
        builder.AppendLine("PROOF RESULT");
        builder.AppendLine($"Activation proven: {result.ActivationProven}");
        builder.AppendLine($"Spontaneous data-change proven: {result.SpontaneousDataChangeProven}");
        builder.AppendLine($"Association healthy after report: {result.AssociationHealthyAfterReport}");
        builder.AppendLine($"Included indexes: [{string.Join(",", result.IncludedIndexes)}]");
        builder.AppendLine($"Reasons: [{string.Join(",", result.Reasons)}]");
        builder.AppendLine("Included members:");
        foreach (var member in result.IncludedMemberReferences)
            builder.AppendLine("  " + member);

        builder.AppendLine();
        builder.AppendLine("CLEANUP CLOSURE");
        builder.AppendLine($"Monitor cleanup: {result.MonitorCleanupSucceeded}");
        builder.AppendLine($"Proof-field restore: {result.ProofFieldRestoreSucceeded}");
        builder.AppendLine($"Fresh-association cleanup closure: {result.FreshCleanupClosureSucceeded}");

        builder.AppendLine();
        builder.AppendLine("WIRE / SAFETY EVIDENCE");
        foreach (var line in result.EvidenceLines)
            builder.AppendLine(line);

        builder.AppendLine();
        builder.AppendLine("SAFETY STATE");
        builder.AppendLine("G2.5-A sends NO GI and accepts only an actual spontaneous data-change report as proof.");
        builder.AppendLine("G2.5-A does not alter the persisted InformationReportProven profile.");
        builder.AppendLine("G2.5-A PASS != ProductionEligible.");
        builder.AppendLine("Production automatic dynamic reporting remains OFF until later G2.5/G2.6 gates pass.");
        return builder.ToString();
    }

    private static string G25ATextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
