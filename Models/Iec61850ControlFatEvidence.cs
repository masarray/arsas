namespace ArIED61850Tester.Models;

/// <summary>
/// Copy-only field FAT snapshot. Never polls the relay, sends MMS requests,
/// changes a control lease, or claims physical feedback from a service reply.
/// </summary>
public sealed record Iec61850ControlFatEvidence(
    DateTimeOffset CapturedAtUtc,
    string Ied,
    string ControlObject,
    string Cdc,
    string ControlModel,
    string CurrentValueAtInspection,
    string RequestedValue,
    string OriginCategory,
    string OriginatorId,
    bool InterlockCheck,
    bool SynchroCheck,
    bool Test,
    string ResultStage,
    string ResultSummary,
    string ControlNumber,
    string CommandTermination,
    string WireTimeline,
    string DiagnosticEvidence)
{
    public string ToClipboardText()
    {
        static string Line(string? value) =>
            (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

        return string.Join(Environment.NewLine, new[]
        {
            "ARSAS IEC 61850 CONTROL FAT EVIDENCE",
            "Captured UTC: " + CapturedAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            "IED: " + Line(Ied),
            "Object: " + Line(ControlObject),
            "CDC / ctlModel: " + Line(Cdc) + " / " + Line(ControlModel),
            "IED value at last inspection: " + Line(CurrentValueAtInspection),
            "Requested value: " + Line(RequestedValue),
            "Origin: " + Line(OriginCategory) + " / " + Line(OriginatorId),
            "InterlockCheck: " + InterlockCheck.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "SynchroCheck: " + SynchroCheck.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Test: " + Test.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Result: " + Line(ResultStage),
            "Summary: " + Line(ResultSummary),
            "ctlNum: " + Line(ControlNumber),
            "CommandTermination: " + Line(CommandTermination),
            "MMS steps (evidence only): " + Line(WireTimeline),
            "",
            "IEC 61850 DIAGNOSTICS",
            DiagnosticEvidence ?? "",
            "",
            "NOTICE: MMS service acceptance does not prove primary equipment movement. Verify independent process feedback and Wireshark capture."
        });
    }
}
