using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private bool _controlDiagnosticNormalizerInstalled;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_controlDiagnosticNormalizerInstalled)
            return;

        _controlDiagnosticNormalizerInstalled = true;

        // The native Smart Control service deliberately rejects ctlModel=StatusOnly
        // when asked to open an executable command session. For the explorer this is
        // valid live-model information, not a communication failure. Replace the
        // original diagnostic subscriber after the window is ready so status-only
        // objects do not raise a red application error.
        _runtime.Diagnostic -= Runtime_Diagnostic;
        _runtime.Diagnostic += Runtime_DiagnosticWithControlModelClassification;
    }

    private void Runtime_DiagnosticWithControlModelClassification(DiagnosticEntry entry)
    {
        if (IsStatusOnlyControlInspection(entry.Message))
        {
            Runtime_Diagnostic(new DiagnosticEntry
            {
                Time = entry.Time,
                Level = "INFO",
                Source = entry.Source,
                Message = $"{ExtractControlReference(entry.Message)}: ctlModel=StatusOnly; this is a read-only status object and command actions are disabled."
            });
            return;
        }

        if (IsUnknownControlModelInspection(entry.Message))
        {
            Runtime_Diagnostic(new DiagnosticEntry
            {
                Time = entry.Time,
                Level = "WARN",
                Source = entry.Source,
                Message = $"{ExtractControlReference(entry.Message)}: ctlModel could not be resolved; command actions remain disabled until the live model is known."
            });
            return;
        }

        Runtime_Diagnostic(entry);
    }

    private static bool IsStatusOnlyControlInspection(string? message)
        => !string.IsNullOrWhiteSpace(message) &&
           message.Contains("Control inspection failed", StringComparison.OrdinalIgnoreCase) &&
           (message.Contains("ctlModel=StatusOnly", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("ctlModel=Status only", StringComparison.OrdinalIgnoreCase));

    private static bool IsUnknownControlModelInspection(string? message)
        => !string.IsNullOrWhiteSpace(message) &&
           message.Contains("Control inspection failed", StringComparison.OrdinalIgnoreCase) &&
           message.Contains("ctlModel=Unknown", StringComparison.OrdinalIgnoreCase);

    private static string ExtractControlReference(string message)
    {
        const string marker = "Control inspection failed for ";
        var start = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return "IEC 61850 control object";

        start += marker.Length;
        var end = message.IndexOf(':', start);
        var reference = end > start ? message[start..end] : message[start..];
        return string.IsNullOrWhiteSpace(reference) ? "IEC 61850 control object" : reference.Trim();
    }
}
