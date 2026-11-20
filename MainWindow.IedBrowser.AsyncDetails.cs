using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    // One CPU-heavy details projection at a time; rapid IED/LN switching
    // never creates an unbounded pool of concurrent schema traversal workers.
    private readonly SemaphoreSlim _iedBrowserDetailBuildGate = new(1, 1);
    private int _iedBrowserDetailBuildGeneration;

    private async Task BuildIedBrowserDetailsAsync(
        IedBrowserTreeRow page, LiveIedModelDiscoveryDocument? model,
        LiveIedModelDiscoveryDocument? designModel,
        Iec61850MonitorDevice? owner, bool connected,
        string detailStatus, int generation, long association)
    {
        try
        {
            await _iedBrowserDetailBuildGate.WaitAsync(_applicationCancellation.Token);
            IReadOnlyList<IedBrowserDetailRow> rows;
            try
            {
                // Before entering the worker, abandon superseded navigation.
                if (generation != _iedBrowserDetailBuildGeneration ||
                    !ReferenceEquals(SelectedDevice, owner) ||
                    _applicationCancellation.IsCancellationRequested)
                    return;
                rows = await Task.Run(() =>
                {
                    using var phase = IedBrowserPerformanceProbe.Measure(
                        "Browser.DetailsBuild", owner?.Name);
                    return IedBrowserPresentation.Details(page, model, connected, designModel);
                }, _applicationCancellation.Token);
            }
            finally
            {
                _iedBrowserDetailBuildGate.Release();
            }

            if (generation != _iedBrowserDetailBuildGeneration ||
                _applicationCancellation.IsCancellationRequested ||
                MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
                !ReferenceEquals(SelectedDevice, owner) ||
                !ReferenceEquals(_iedBrowserRenderedPage, page) ||
                owner?.LiveValues.Association != association ||
                owner.IsConnected != connected ||
                !ReferenceEquals(owner.LiveDiscoveryModel ?? owner.SclWorkspace?.DesignModel, model))
                return;

            IedBrowserDetailStatus.Text = detailStatus;
            ReplaceIedBrowserDetails(rows);
            _iedBrowserLastLiveVersion = -1;
            RefreshIedBrowserVisibleValues();
            RefreshIedBrowserReadButtons();
            QueueIedBrowserFocusedRead(page);
            QueueIedBrowserRcbFocusedRead(page);
            if (_iedBrowserDetails.Rows.Count > 0)
                IedBrowserDetailList.ScrollIntoView(_iedBrowserDetails.Rows[0]);
        }
        catch (OperationCanceledException)
        {
            // Closing/replacement is normal; no stale page may be published.
        }
        catch (Exception ex)
        {
            if (generation == _iedBrowserDetailBuildGeneration &&
                ReferenceEquals(SelectedDevice, owner))
                IedBrowserDetailStatus.Text = "Detail projection unavailable · " + ex.Message;
        }
    }
}
