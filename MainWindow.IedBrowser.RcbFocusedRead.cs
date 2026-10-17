using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private readonly IedBrowserFocusedAdmission _iedBrowserRcbFocusedAdmission = new();

    private void QueueIedBrowserRcbFocusedRead(IedBrowserTreeRow row)
    {
        if (row is not { Kind: "RCB", Payload: RcbExportRow rcb } ||
            SelectedDevice is not { IsConnected: true, IsDemo: false } device ||
            device.LiveDiscoveryModel is null)
            return;
        var targets = IedBrowserRcbFocusedReadPlan.Build(device.LiveDiscoveryModel, rcb);
        if (targets.Count == 0)
        {
            QueueIedBrowserReportUnitRead(row);
            return;
        }
        // Only the currently visible RCB and its three verified read-only
        // attribute names; never Read All or operate a device.
        var association = device.LiveValues.Association;
        var key = IedBrowserFocusedAdmission.PageKey(row.Key, null) + "|RCB";
        if (!_iedBrowserRcbFocusedAdmission.CanStart(
                device.DeviceId, association, key, 1))
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            if (!ReferenceEquals(SelectedDevice, device) ||
                !ReferenceEquals(_iedBrowserRenderedPage, row) ||
                device.LiveValues.Association != association ||
                !device.IsConnected || device.IsBusy || _iedBrowserReadOperation != null ||
                !_iedBrowserRcbFocusedAdmission.TryStart(
                    device.DeviceId, association, key, 1))
                return;
            var lease = new IedBrowserRcbCheckLease(device);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                _applicationCancellation.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(18));
            _iedBrowserReadOperation = cts;
            var values = new IedBrowserReadResultInbox(() => { });
            var progress = new Progress<IedBrowserReadProgress>(_ => { });
            try
            {
                if (device.IsMonitoring)
                    await IedBrowserIsolatedReadService.ReadAsync(
                        device, targets, values, progress, cts.Token);
                else
                    await _runtime.ReadBrowserValuesAsync(
                        device, targets, values, progress, cts.Token);
                if (!lease.IsCurrent(device) || !ReferenceEquals(SelectedDevice, device))
                    return;
                values.Drain(result =>
                {
                    if (result.Error.Length != 0 ||
                        !IedBrowserReadValuePolicy.TryAcceptDisplay(
                            result.Value, out var scalar, out _))
                        return;
                    device.LiveValues.PublishForAssociation(association,
                        result.Target.Reference, result.Target.FunctionalConstraint,
                        scalar, result.Quality, result.DeviceTimestamp, "MMS Read",
                        result.UtcTimeEvidence, result.QualityEvidence);
                });
                _iedBrowserLastLiveVersion = -1;
                RefreshIedBrowserVisibleValues();
            }
            catch (OperationCanceledException) { /* no stale admission */ }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (lease.IsCurrent(device) && ReferenceEquals(_iedBrowserRenderedPage, row))
                    IedBrowserDetailStatus.ToolTip =
                        "Selected RCB read-only MMS inspection failed: " + ex.Message;
            }
            finally
            {
                if (ReferenceEquals(_iedBrowserReadOperation, cts))
                    _iedBrowserReadOperation = null;
                if (lease.IsCurrent(device) &&
                    ReferenceEquals(_iedBrowserRenderedPage, row))
                    UpdateIedBrowserRcbReportDetails();
                RefreshIedBrowserReadButtons();
                if (lease.IsCurrent(device) &&
                    ReferenceEquals(_iedBrowserRenderedPage, row))
                    QueueIedBrowserReportUnitRead(row);
            }
        }));
    }

    private void QueueIedBrowserReportUnitRead(IedBrowserTreeRow row)
    {
        if (row.Kind != "RCB" ||
            SelectedDevice is not { IsConnected: true, IsDemo: false } device ||
            device.LiveDiscoveryModel is null) return;
        var association = device.LiveValues.Association;
        var key = IedBrowserFocusedAdmission.PageKey(row.Key, "|unit-CF");
        if (!_iedBrowserAutoReadAdmission.CanStart(
                device.DeviceId, association, key, 3)) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!ReferenceEquals(SelectedDevice, device) ||
                device.LiveValues.Association != association ||
                !ReferenceEquals(_iedBrowserNavigation.Current, row) ||
                !ReferenceEquals(_iedBrowserRenderedPage, row) ||
                device.IsBusy || _iedBrowserReadOperation is not null) return;
            if (IedBrowserMeasurementMetadataReadPlan.BuildForPage(
                    device.LiveDiscoveryModel, row, device.LiveValues).Count == 0)
            {
                _iedBrowserAutoReadAdmission.Complete(key);
                return;
            }
            IedBrowserReadValues_Click(new Button { Tag = "ReportUnitAuto" },
                new RoutedEventArgs());
        }));
    }
}
