using System.ComponentModel;
using System.IO;
using System.Windows;

namespace ArIED61850Tester;

/// <summary>
/// P0 responsiveness guard for the FAT workspace lifecycle.
///
/// The legacy Closing handler sealed evidence journals and flushed persistence synchronously
/// on the WPF Dispatcher. Both operations can touch disk and, during a live session, cascade
/// into runtime teardown. That made Close look like an application hang. Keep confirmation on
/// the UI thread, then perform journal sealing and durable save on a worker thread.
/// </summary>
public partial class IoListTestingWindow
{
    private bool _p0CloseInProgress;
    private bool _p0AllowClose;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

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
        IsEnabled = false;

        try
        {
            if (Session.HasActiveSessions)
            {
                var stopAll = await Task.Run(() =>
                    Session.StopAll("Workspace closed by operator; per-IED evidence journal sealed."));
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
            }
        }
    }
}
