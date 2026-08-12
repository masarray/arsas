using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ArIED61850Tester;

public partial class App : Application
{
    private static readonly object UiErrorSync = new();
    private static string _lastUiErrorSignature = string.Empty;
    private static DateTime _lastUiErrorUtc = DateTime.MinValue;
    private static int _uiErrorHandlerActive;
    private CancellationTokenSource? _updateCancellation;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Keep every IED card on one reusable template key while replacing the
        // legacy bitmap fallback with the compact ARVREL-derived vector fascia.
        // Installing this before StartupUri materializes also makes the portable
        // smoke test validate that the vector ResourceDictionary is packaged.
        InstallArvrelMiniIedFascia();

        if (Array.Exists(e.Args, argument =>
                string.Equals(argument, "--portable-smoke-test", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(RunPortableSmokeTest());
            return;
        }

        GridUxBehavior.Install();
        FaultRecordUxBehavior.Install();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();

        _updateCancellation = new CancellationTokenSource();
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => _ = AppUpdateCoordinator.RunLazyAsync(_updateCancellation.Token)));
    }

    private void InstallArvrelMiniIedFascia()
    {
        var fasciaResources = new ResourceDictionary
        {
            Source = new Uri(
                "/ARSAS;component/Resources/ArvrelMiniIedFascia.xaml",
                UriKind.Relative)
        };

        if (fasciaResources["ArvrelMiniIedRelayFrontPanelTemplate"] is not ControlTemplate template)
        {
            throw new InvalidOperationException(
                "The packaged ARVREL mini IED fascia resource is missing its ControlTemplate.");
        }

        Resources["IedRelayFrontPanelTemplate"] = template;
    }

    private static int RunPortableSmokeTest()
    {
        try
        {
            var lockPath = Path.Combine(AppContext.BaseDirectory, "engines", "ARIEC61850.lock.json");
            if (!File.Exists(lockPath))
            {
                WritePortableSmokeDiagnostic($"Engine lock was not extracted. BaseDirectory={AppContext.BaseDirectory}; expected={lockPath}");
                return 21;
            }

            foreach (var assemblyName in new[]
                     {
                         "AR.Iec61850",
                         "AR.Iec61850.Transports.Npcap",
                         "SharpPcap",
                         "PacketDotNet"
                     })
            {
                _ = Assembly.Load(new AssemblyName(assemblyName));
            }

            var probePath = Path.Combine(Path.GetTempPath(), $"ARSAS-portable-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "portable-smoke-test");
            File.Delete(probePath);
            return 0;
        }
        catch (Exception ex)
        {
            WritePortableSmokeDiagnostic(ex.ToString());
            return 22;
        }
    }

    private static void WritePortableSmokeDiagnostic(string message)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "ARSAS-portable-smoke-error.txt"),
                message);
        }
        catch
        {
            // Diagnostics must never replace the original smoke-test result.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updateCancellation?.Cancel();
        _updateCancellation?.Dispose();
        _updateCancellation = null;
        base.OnExit(e);
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
                Debug.WriteLine($"Suppressed repeated ARSAS UI error: {signature}");
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
            Debug.WriteLine($"Failed to route ARSAS UI error to Diagnostics: {reportingError}");
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
