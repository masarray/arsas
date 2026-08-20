using System.Text;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

internal partial class DynamicReportQualificationResultWindow
{
    internal DynamicReportQualificationResultWindow(DynamicReportCleanupClosureCommissioningResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        InitializeComponent();

        Title = "G2.4-C Fresh Association Cleanup Closure Evidence";
        HeaderText.Text = "G2.4-C Fresh Association Cleanup Closure";
        SummaryText.Text = result.Summary;
        StateText.Text = result.IsSuccess
            ? "G2.4 Cleanup Closed"
            : result.IsBlocked
                ? "Blocked"
                : "Cleanup closure not proven";
        EvidenceTextBox.Text = BuildG24CEvidence(result);

        if (result.IsSuccess)
            SetPassBadge();
    }

    private static string BuildG24CEvidence(DynamicReportCleanupClosureCommissioningResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ARSAS G2.4-C FRESH ASSOCIATION CLEANUP CLOSURE EVIDENCE");
        builder.AppendLine(new string('=', 72));
        builder.AppendLine($"Result: {result.Summary}");
        builder.AppendLine($"Blocked: {result.IsBlocked}");
        builder.AppendLine($"G2.4-C success: {result.IsSuccess}");

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
        builder.AppendLine("G2.4 PROVEN TARGET");
        builder.AppendLine($"Input profile state: {result.InputProfile?.State.ToString() ?? "-"}");
        builder.AppendLine($"URCB: {G24CTextOrDash(result.RcbReference)}");
        builder.AppendLine($"Previous temporary DataSet: {G24CTextOrDash(result.TemporaryDataSetReference)}");

        if (result.FreshRcbSnapshot is not null)
        {
            var snapshot = result.FreshRcbSnapshot;
            builder.AppendLine();
            builder.AppendLine("FRESH-ASSOCIATION RCB CLOSURE");
            builder.AppendLine($"Availability: {snapshot.Availability}");
            builder.AppendLine($"DatSet probe: {snapshot.DataSetProbeState}");
            builder.AppendLine($"DatSet: {G24CTextOrDash(snapshot.DataSetReference)}");
            builder.AppendLine($"RptEna: {G24CTextOrDash(snapshot.EnabledState)}");
            builder.AppendLine($"Resv: {G24CTextOrDash(snapshot.ReservationState)}");
            builder.AppendLine($"Owner: {G24CTextOrDash(snapshot.Owner)}");
            builder.AppendLine($"Reservation time: {G24CTextOrDash(snapshot.ReservationTimeSeconds)}");
            builder.AppendLine($"TrgOps read-only: {G24CTextOrDash(snapshot.TriggerOptions)}");
            builder.AppendLine($"OptFlds read-only: {G24CTextOrDash(snapshot.OptionalFields)}");
            builder.AppendLine($"RptID: {G24CTextOrDash(snapshot.ReportId)}");
        }

        builder.AppendLine();
        builder.AppendLine("TEMPORARY DATASET CLOSURE");
        builder.AppendLine($"Absent from fresh NamedVariableList: {result.TemporaryDataSetAbsentFromNameList}");
        builder.AppendLine($"Direct DataSet directory absent: {result.TemporaryDataSetDirectoryAbsent}");
        builder.AppendLine($"Fresh association healthy: {result.AssociationHealthy}");

        builder.AppendLine();
        builder.AppendLine("WIRE / SAFETY EVIDENCE");
        foreach (var line in result.EvidenceLines)
            builder.AppendLine(line);

        builder.AppendLine();
        builder.AppendLine("SAFETY STATE");
        builder.AppendLine("G2.4-C is read-only and performs zero MMS writes or DataSet mutations.");
        builder.AppendLine("G2.4-C does not alter the persisted InformationReportProven profile.");
        builder.AppendLine("G2.4 Cleanup Closed != ProductionEligible.");
        builder.AppendLine("Production automatic dynamic reporting remains OFF until G2.5/G2.6 acceptance gates pass.");
        return builder.ToString();
    }

    private static string G24CTextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
