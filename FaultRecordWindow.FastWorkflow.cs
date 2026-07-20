using System.Windows.Threading;

namespace ArIED61850Tester;

public partial class FaultRecordWindow
{
    private bool _initialFastWorkflowObserved;

    /// <summary>
    /// The Loaded handler starts discovery automatically. The first transient failure is
    /// kept quiet while one bounded reconnect/rescan is attempted; only a final failure is
    /// surfaced to the user. This also installs centered toast and safe re-download UX.
    /// </summary>
    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        InstallRedownloadUx();

        if (_initialFastWorkflowObserved)
            return;

        _initialFastWorkflowObserved = true;
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);

        while (IsBusy && IsVisible)
        {
            if (StatusText.StartsWith("Fault-record scan failed", StringComparison.OrdinalIgnoreCase))
                HideStartupScanToast();
            await Task.Delay(50).ConfigureAwait(true);
        }

        if (!IsVisible)
            return;

        if (Records.Count > 0)
        {
            HideStartupScanToast();
            return;
        }

        if (!StatusText.StartsWith("Fault-record scan failed", StringComparison.OrdinalIgnoreCase))
            return;

        HideStartupScanToast();
        StatusText = "Automatic file discovery is reconnecting and retrying once…";
        await Task.Delay(250).ConfigureAwait(true);

        if (!IsVisible || IsBusy)
            return;

        await ScanAsync().ConfigureAwait(true);
        if (Records.Count > 0 ||
            !StatusText.StartsWith("Fault-record scan failed", StringComparison.OrdinalIgnoreCase))
        {
            HideStartupScanToast();
        }
    }
}