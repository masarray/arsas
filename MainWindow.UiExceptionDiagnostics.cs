using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    internal void ReportUnexpectedUiErrorWithStackTrace(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        _pendingDiagnostics.Enqueue(new DiagnosticEntry
        {
            Time = DateTime.Now,
            Level = "ERROR",
            Source = "UI",
            Message = exception.ToString()
        });

        MarkDiagnosticAlert();
        SetStatus("Unexpected UI error captured with stack trace. Diagnostics is marked with !.");
    }
}
