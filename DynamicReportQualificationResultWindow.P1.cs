using System.Text;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

internal partial class DynamicReportQualificationResultWindow
{
    internal DynamicReportQualificationResultWindow(DynamicReportOptionalFieldsProbeCommissioningResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        InitializeComponent();

        Title = "P1 Isolated OptFlds Micro-Probe Evidence";
        HeaderText.Text = "P1 Isolated OptFlds Micro-Probe";
        SummaryText.Text = result.Summary;
        StateText.Text = result.IsSuccess
            ? "P1 OptFlds Proven"
            : result.IsBlocked
                ? "Blocked"
                : result.CleanupSucceeded
                    ? "Not proven / restored"
                    : "Restore unproven";
        EvidenceTextBox.Text = BuildP1Evidence(result);

        if (result.IsSuccess)
            SetPassBadge();
    }

    private static string BuildP1Evidence(DynamicReportOptionalFieldsProbeCommissioningResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ARSAS P1 ISOLATED OPTFLDS MICRO-PROBE EVIDENCE");
        builder.AppendLine(new string('=', 62));
        builder.AppendLine($"Result: {result.Summary}");
        builder.AppendLine($"Blocked: {result.IsBlocked}");
        builder.AppendLine($"P1 success: {result.IsSuccess}");
        builder.AppendLine($"Restore proven: {result.CleanupSucceeded}");

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
        builder.AppendLine("P1 TARGET");
        builder.AppendLine($"Input profile state: {result.InputProfile?.State.ToString() ?? "-"}");
        builder.AppendLine($"URCB: {P1TextOrDash(result.RcbReference)}");
        builder.AppendLine("Requested optional fields: reason-for-inclusion + data-set-name");
        builder.AppendLine("Correct canonical OptFlds raw target: 061800");

        if (result.Probe is not null)
        {
            builder.AppendLine();
            builder.AppendLine("OPTFLDS READ / WRITE / RESTORE");
            builder.AppendLine($"Original raw: {P1TextOrDash(result.Probe.OriginalRaw)}");
            builder.AppendLine($"Requested raw: {P1TextOrDash(result.Probe.RequestedRaw)}");
            builder.AppendLine($"Readback raw: {P1TextOrDash(result.Probe.ReadbackRaw)}");
            builder.AppendLine($"Restore readback raw: {P1TextOrDash(result.Probe.RestoreReadbackRaw)}");
            builder.AppendLine($"Requested semantic match: {result.Probe.RequestedComparison?.IsSemanticMatch.ToString() ?? "-"}");
            builder.AppendLine($"Requested raw exact: {result.Probe.RequestedComparison?.IsRawExact.ToString() ?? "-"}");
            builder.AppendLine($"Requested padding-only diff: {result.Probe.RequestedComparison?.PaddingOnlyDifference.ToString() ?? "-"}");
            builder.AppendLine($"Restore semantic match: {result.Probe.RestoreComparison?.IsSemanticMatch.ToString() ?? "-"}");
            builder.AppendLine($"Restore raw exact: {result.Probe.RestoreComparison?.IsRawExact.ToString() ?? "-"}");
            builder.AppendLine($"Restore padding-only diff: {result.Probe.RestoreComparison?.PaddingOnlyDifference.ToString() ?? "-"}");
        }

        builder.AppendLine();
        builder.AppendLine("WIRE / SAFETY EVIDENCE");
        foreach (var line in result.EvidenceLines)
            builder.AppendLine(line);

        builder.AppendLine();
        builder.AppendLine("SAFETY STATE");
        builder.AppendLine("P1 writes OptFlds only on one forced-live proven-free URCB and immediately restores it.");
        builder.AppendLine("No TrgOps, DatSet, Resv, RptEna, GI or dynamic DataSet service is used by P1.");
        builder.AppendLine("P1 does not advance the G2 profile and production automatic dynamic reporting remains OFF.");
        return builder.ToString();
    }

    private static string P1TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
