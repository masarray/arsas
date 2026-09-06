using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// P0 responsiveness guard for the FAT workspace lifecycle.
///
/// Confirmation and evidence-session state transitions remain on the WPF Dispatcher, but
/// the high-rate FAT live projection is quiesced before sealing/saving and durable workspace
/// persistence runs on a worker thread. This prevents a closing window from competing with
/// incoming report traffic while preserving UI-bound session state safety.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool P0LifecycleRegistered = RegisterP0Lifecycle();

    private bool _p0LifecycleInstalled;
    private bool _p0CloseInProgress;
    private bool _p0AllowClose;

    private static bool RegisterP0Lifecycle()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0Lifecycle_Loaded));
        return true;
    }

    private static void P0Lifecycle_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is IoListTestingWindow window)
            window.InstallP0Lifecycle();
    }

    private void InstallP0Lifecycle()
    {
        if (_p0LifecycleInstalled)
            return;

        _p0LifecycleInstalled = true;

        // XAML wires Window_Closing during InitializeComponent. Replace only that lifecycle
        // edge; all existing OnClosed cleanup remains intact.
        Closing -= Window_Closing;
        Closing += P0Window_Closing;
    }

    private async void P0Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_p0AllowClose)
            return;

        e.Cancel = true;
        if (_p0CloseInProgress)
            return;

        if (IsPreparingIed)
        {
            var activeNames = string.Join(", ", Project.Ieds
                .Where(ied => ied.IsPreparing)
                .Select(ied => ied.IedName));
            MessageBox.Show(
                this,
                $"ARSAS is still preparing {activeNames}. You can inspect or connect other IEDs while these independent workflows run, but finish preparation before closing this workspace.",
                "IED preparation in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Session.HasActiveSessions)
        {
            var activeCount = Session.ActiveSessionCount;
            var answer = MessageBox.Show(
                this,
                $"{activeCount} FAT evidence session(s) are active. Returning to Engineering will stop every active IED session, seal each independent evidence journal, and save the current project progress.\n\nStop all sessions and return?",
                "Stop active FAT sessions",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;
        }

        _p0CloseInProgress = true;
        if (Owner is MainWindow engineeringWindow)
            engineeringWindow.SuspendIoFatRuntimeProjection(this);

        IsEnabled = false;
        // Let the disabled/closing visual state paint before journal sealing. The actual
        // session mutation remains on Dispatcher; only durable workspace I/O is offloaded.
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            if (Session.HasActiveSessions)
            {
                // Session.StopAll mutates controller/project state and raises UI-bound
                // PropertyChanged notifications. Keep that state transition on Dispatcher;
                // offloading the whole coordinator would create a cross-thread WPF defect.
                var stopAll = Session.StopAll(
                    "Workspace closed by operator; per-IED evidence journal sealed.");
                if (!stopAll.Succeeded)
                {
                    MessageBox.Show(
                        this,
                        stopAll.Message,
                        "Evidence journals could not be sealed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }

            // Persistence is pure durable I/O after the session state is sealed and is the
            // portion that must not occupy the WPF Dispatcher.
            if (Storage != null)
                await Task.Run(Storage.SaveNow);

            _p0AllowClose = true;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var answer = MessageBox.Show(
                this,
                $"ARSAS could not save the latest IO FAT progress.\n\n{ex.Message}\n\nClose the workspace anyway?",
                "Progress save failed",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error,
                MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes)
            {
                _p0AllowClose = true;
                Close();
            }
        }
        finally
        {
            if (!_p0AllowClose)
            {
                _p0CloseInProgress = false;
                IsEnabled = true;
                if (Owner is MainWindow engineeringWindow)
                    engineeringWindow.ResumeIoFatRuntimeProjection(this);
            }
        }
    }
}
