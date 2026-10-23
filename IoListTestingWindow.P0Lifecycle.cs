using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// P0 responsiveness guard for the FAT workspace lifecycle.
///
/// Confirmation and evidence-session state transitions remain on the WPF Dispatcher, but
/// the high-rate FAT live projection is quiesced before sealing/saving. Close-only durable
/// journal flush/read-back verification and workspace persistence run on worker threads so
/// a closing window never blocks the Dispatcher on disk work.
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
        // Let the disabled/closing visual state paint before journal sealing. Session state
        // remains Dispatcher-owned; only the physical seal/read-back work is deferred.
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            if (Session.HasActiveSessions)
            {
                var stopSucceeded = false;
                var stopMessage = string.Empty;
                // StopAll still performs controller/project state mutation and UI-bound
                // PropertyChanged notifications synchronously on this Dispatcher. The scope
                // changes only production journal Dispose(): its durable flush + read-back
                // verification are queued to a worker and awaited immediately afterwards.
                using (IoTestEvidenceJournal.BeginDeferredSealScope())
                {
                    var stopAll = Session.StopAll(
                        "Workspace closed by operator; per-IED evidence journal sealed.");
                    stopSucceeded = stopAll.Succeeded;
                    stopMessage = stopAll.Message;
                }

                // Do not allow project save or window close until every queued journal has
                // completed its physical disk barrier and full hash-chain read-back.
                await IoTestEvidenceJournal.AwaitDeferredSealsAsync();

                if (!stopSucceeded)
                {
                    MessageBox.Show(
                        this,
                        stopMessage,
                        "Evidence journals could not be sealed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }

            // Persistence is pure durable I/O after every session journal is actually sealed
            // and verified, so it also stays away from the WPF Dispatcher.
            if (Storage != null)
                await Task.Run(Storage.SaveNow);

            _p0AllowClose = true;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var answer = MessageBox.Show(
                this,
                $"ARSAS could not seal the FAT evidence or save the latest IO FAT progress.\n\n{ex.Message}\n\nClose the workspace anyway?",
                "FAT close failed",
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
                if (Owner is MainWindow resumeOwner)
                    resumeOwner.ResumeIoFatRuntimeProjection(this);
            }
        }
    }
}
