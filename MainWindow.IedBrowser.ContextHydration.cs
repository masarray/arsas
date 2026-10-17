using System.Windows.Threading;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// Operator-context read-on-open: one finite bounded read-only snapshot, never
/// a data-model-wide automatic scan. The current LIVE MMS model is the only
/// wire target authority. Values go into the existing association-bound ledger;
/// no secondary process-value cache or duplicated decoder is introduced.
/// </summary>
public partial class MainWindow
{
    private const int IedBrowserContextMaxTargets = 12;
    private const int IedBrowserContextMaxPagesPerAssociation = 24;
    private static readonly TimeSpan IedBrowserContextDeadline =
        TimeSpan.FromSeconds(12);
    private CancellationTokenSource? _iedBrowserContextReadOperation;
    private Iec61850MonitorDevice? _iedBrowserContextReadDevice;
    private long _iedBrowserContextReadAssociation = -1;
    private long _iedBrowserContextReadEpoch = -1;
    private bool _iedBrowserContextReadPending;
    private readonly HashSet<string> _iedBrowserContextReadPages = new(StringComparer.Ordinal);
    private IedBrowserReadFailureEvidence? _iedBrowserContextReadFailures;

    private void CancelIedBrowserContextRead()
    {
        _iedBrowserContextReadPending = false;
        _iedBrowserContextReadOperation?.Cancel();
    }

    private void ScheduleIedBrowserContextHydration()
    {
        if (MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
            SelectedDevice is not { IsConnected: true, IsBusy: false,
                IsDemo: false, LiveDiscoveryModel: { } model } device ||
            !ReferenceEquals(_iedBrowserObservedDevice, device) ||
            device.LiveValues.IsOffline || _iedBrowserReadOperation is not null)
            return;
        var page = _iedBrowserRenderedPage;
        if (page is null || !ReferenceEquals(page, _iedBrowserNavigation.Current))
            return;

        // Browser projection epoch and association are O(1) generation fences.
        // A replacement schema may have identical object paths, but its old
        // attempted-page identities and failure evidence are never reused.
        var association = device.LiveValues.Association;
        var epoch = device.BrowserProjectionEpoch;
        if (!ReferenceEquals(_iedBrowserContextReadDevice, device) ||
            _iedBrowserContextReadAssociation != association ||
            _iedBrowserContextReadEpoch != epoch)
        {
            CancelIedBrowserContextRead();
            _iedBrowserContextReadDevice = device;
            _iedBrowserContextReadAssociation = association;
            _iedBrowserContextReadEpoch = epoch;
            _iedBrowserContextReadPages.Clear();
            _iedBrowserContextReadFailures = new IedBrowserReadFailureEvidence(
                device, bindToLiveModel: true);
        }

        // Never open simultaneous secondary MMS readers on one IED.
        if (_iedBrowserContextReadOperation is not null)
        {
            _iedBrowserContextReadPending = true;
            return;
        }

        var focus = page.Payload is LiveIedLogicalNodeModel
            ? _iedBrowserSelectedDoReference : null;
        var key = page.Key + "|" + (focus ?? "");
        if (_iedBrowserContextReadPages.Contains(key) ||
            _iedBrowserContextReadPages.Count >= IedBrowserContextMaxPagesPerAssociation)
            return; // cap extra associations and memory on enormous IED models
        var targets = IedBrowserContextHydrationPlan.Build(
            model, page, device.LiveValues, IedBrowserContextMaxTargets, focus);
        _iedBrowserContextReadPages.Add(key); // one automatic attempt per page/association
        if (targets.Count == 0) return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(
            _applicationCancellation.Token);
        cts.CancelAfter(IedBrowserContextDeadline);
        _iedBrowserContextReadOperation = cts;
        RefreshIedBrowserReadButtons(); // disable competing manual read; enable Cancel
        _ = RunIedBrowserContextHydrationAsync(device, model, association,
            epoch, targets, cts);
    }

    private async Task RunIedBrowserContextHydrationAsync(
        Iec61850MonitorDevice device, LiveIedModelDiscoveryDocument model,
        long association, long epoch, IReadOnlyList<IedBrowserReadTarget> targets,
        CancellationTokenSource cts)
    {
        var inbox = new IedBrowserReadResultInbox(() =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(Drain)));
        bool IsCurrent() =>
            ReferenceEquals(SelectedDevice, device) &&
            ReferenceEquals(device.LiveDiscoveryModel, model) &&
            device.IsConnected && !device.LiveValues.IsOffline &&
            device.LiveValues.Association == association &&
            device.BrowserProjectionEpoch == epoch;

        void Drain()
        {
            var changed = false;
            inbox.Drain(result =>
            {
                if (!IsCurrent()) return;
                if (result.Error.Length != 0 ||
                    !IedBrowserReadValuePolicy.TryAcceptDisplay(
                        result.Value, out var safe, out var reason))
                {
                    _iedBrowserContextReadFailures?.RecordFailure(
                        result.Target, result.Error.Length == 0 ? reason : result.Error);
                    changed = true;
                    return;
                }

                if (device.LiveValues.PublishForAssociation(association,
                        result.Target.Reference, result.Target.FunctionalConstraint,
                        safe, result.Quality, result.DeviceTimestamp, "MMS Read",
                        result.UtcTimeEvidence, result.QualityEvidence))
                {
                    _iedBrowserContextReadFailures?.ClearFailure(result.Target);
                    changed = true;
                }
            });
            if (changed && IsCurrent() &&
                MainTabs.SelectedIndex == IedBrowserWorkspaceIndex)
            {
                _iedBrowserLastLiveVersion = -1;
                RefreshIedBrowserVisibleValues(); // update visible rows in place
            }
        }

        try
        {
            // The monitoring association remains authoritative for Reports;
            // the EXISTING isolated reader is reused, with verified schema.
            // Without monitoring use the already-connected runtime session.
            if (device.IsMonitoring)
                await IedBrowserIsolatedReadService.ReadAsync(device, targets,
                    inbox, new Progress<IedBrowserReadProgress>(static _ => { }),
                    cts.Token);
            else
                await _runtime.ReadBrowserValuesAsync(device, targets,
                    inbox, new Progress<IedBrowserReadProgress>(static _ => { }),
                    cts.Token);
            Drain(); // preserve the final producer batch, without UI race
        }
        catch (OperationCanceledException)
        {
            Drain(); // a partial completed snapshot remains valid evidence
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Drain();
            if (IsCurrent() && MainTabs.SelectedIndex == IedBrowserWorkspaceIndex)
                IedBrowserDetailStatus.Text =
                    "Automatic read-only snapshot unavailable; use Read for this page. " +
                    ex.Message;
        }
        finally
        {
            Drain();
            if (ReferenceEquals(_iedBrowserContextReadOperation, cts))
            {
                _iedBrowserContextReadOperation = null;
                var pending = _iedBrowserContextReadPending;
                _iedBrowserContextReadPending = false;
                if (pending && IsCurrent())
                    Dispatcher.BeginInvoke(DispatcherPriority.Background,
                        new Action(ScheduleIedBrowserContextHydration));
            }
            cts.Dispose();
            RefreshIedBrowserReadButtons();
        }
    }
}
