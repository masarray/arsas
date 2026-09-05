using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private IoListTestingWindow? _ioFatLiveValueMirrorWindow;

    /// <summary>
    /// Keeps FAT presentation on the same already-filtered Engineering live image.
    /// This deliberately reuses the existing WPF UI flush instead of subscribing every
    /// FAT cell/row to live-point notifications or issuing any IEC 61850 read.
    /// </summary>
    internal void AttachIoFatLiveValueMirror(IoListTestingWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _uiFlushTimer.Tick -= IoFatLiveValueMirrorUiFlush_Tick;
        _ioFatLiveValueMirrorWindow = window;
        _uiFlushTimer.Tick += IoFatLiveValueMirrorUiFlush_Tick;

        // Populate immediately from the current Engineering image; subsequent refreshes
        // happen after UiFlushTimer_Tick because this handler is registered later.
        window.RefreshEngineeringLiveMirror(Devices);
    }

    internal void DetachIoFatLiveValueMirror(IoListTestingWindow window)
    {
        if (!ReferenceEquals(_ioFatLiveValueMirrorWindow, window))
            return;

        _uiFlushTimer.Tick -= IoFatLiveValueMirrorUiFlush_Tick;
        _ioFatLiveValueMirrorWindow = null;
    }

    private void IoFatLiveValueMirrorUiFlush_Tick(object? sender, EventArgs e)
    {
        var window = _ioFatLiveValueMirrorWindow;
        if (window is not { IsLoaded: true })
            return;

        window.RefreshEngineeringLiveMirror(Devices);
    }
}
