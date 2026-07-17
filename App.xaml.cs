using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ArIED61850Tester;

public partial class App : Application
{
    private static readonly object UiErrorSync = new();
    private static string _lastUiErrorSignature = string.Empty;
    private static DateTime _lastUiErrorUtc = DateTime.MinValue;
    private static int _uiErrorHandlerActive;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        GridUxBehavior.Install();
        FaultRecordUxBehavior.Install();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Mark the exception handled first. A modal MessageBox here can recursively trigger
        // the same layout/binding exception and create an endless stack of dialogs.
        e.Handled = true;

        var exception = e.Exception;
        var signature = $"{exception.GetType().FullName}|{exception.Message}";
        var nowUtc = DateTime.UtcNow;

        lock (UiErrorSync)
        {
            if (signature.Equals(_lastUiErrorSignature, StringComparison.Ordinal) &&
                nowUtc - _lastUiErrorUtc < TimeSpan.FromSeconds(10))
            {
                Debug.WriteLine($"Suppressed repeated ArIED UI error: {signature}");
                return;
            }

            _lastUiErrorSignature = signature;
            _lastUiErrorUtc = nowUtc;
        }

        if (Interlocked.Exchange(ref _uiErrorHandlerActive, 1) != 0)
            return;

        try
        {
            Debug.WriteLine(exception);
            if (Current?.MainWindow is MainWindow mainWindow)
                mainWindow.ReportUnexpectedUiError(exception);
        }
        catch (Exception reportingError)
        {
            Debug.WriteLine($"Failed to route ArIED UI error to Diagnostics: {reportingError}");
        }
        finally
        {
            var dispatcher = Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                Interlocked.Exchange(ref _uiErrorHandlerActive, 0);
            }
            else
            {
                dispatcher.BeginInvoke(
                    DispatcherPriority.ContextIdle,
                    new Action(() => Interlocked.Exchange(ref _uiErrorHandlerActive, 0)));
            }
        }
    }
}
