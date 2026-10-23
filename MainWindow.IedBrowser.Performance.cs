using System.Runtime.CompilerServices;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Services;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private readonly IedBrowserUnitProjectionCache _iedBrowserUnitCache = new();
    // Schema tasks are shared with Read bindings through SharedIedSchemaIndex.
    private Task<IedBrowserSchemaIndex>? _iedBrowserAwaitedSchema;
    private Iec61850MonitorDevice? _iedBrowserAwaitedSchemaDevice;
    private Iec61850MonitorDevice? _iedBrowserPreparingDevice;
    private LiveIedModelDiscoveryDocument? _iedBrowserPreparingModel;

    // Keep a small bounded pool of completed/in-flight SCL inventories across
    // IED switching: BCUGE -> SG7 -> BCUGE must not reparse unchanged SCL.
    private readonly BoundedFifoCache<string, Task<SclReportControlInventoryResult>>
        _iedBrowserInventoryFlights = new(8, StringComparer.Ordinal);
    private Task<SclReportControlInventoryResult>? _iedBrowserInventoryTask;
    private Task<SclReportControlInventoryResult>? _iedBrowserAwaitedInventory;
    private Iec61850MonitorDevice? _iedBrowserAwaitedInventoryDevice;

    private Task<IedBrowserAcceptanceMetrics>? _iedBrowserMetricsFlight;
    private Iec61850MonitorDevice? _iedBrowserMetricsDevice;
    private LiveIedModelDiscoveryDocument? _iedBrowserMetricsModel;
    private long _iedBrowserMetricsAssociation;
    private DateTime _iedBrowserMetricsStartedUtc;

    private void ShowIedBrowserPreparing(Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument model,
        string stage = "Indexing model on background worker…")
    {
        if (ReferenceEquals(_iedBrowserPreparingDevice, device) &&
            ReferenceEquals(_iedBrowserPreparingModel, model))
        {
            IedBrowserDetailStatus.Text = stage;
            return;
        }
        _iedBrowserPreparingDevice = device;
        _iedBrowserPreparingModel = model;
        // Never leave the previous IED's tree clickable during model preparation.
        _iedBrowserNavigationRenderTicket++;
        _iedBrowserRenderedPage = null;
        _iedBrowserNavigation.SelectFromTree(null);
        _iedBrowserSyncingSelection = true;
        try
        {
            IedBrowserTreeGrid.SelectedItem = null;
            _iedBrowserTree.Reset([]);
            IedBrowserTreeGrid.ItemsSource = _iedBrowserTree.Rows;
            ReplaceIedBrowserDetails([]);
        }
        finally { _iedBrowserSyncingSelection = false; }
        IedBrowserObjectPath.Text = device.Name + " · preparing engineering model";
        IedBrowserDetailStatus.Text = stage;
    }

    private bool TryActivateIedBrowserSchema(
        Iec61850MonitorDevice device, LiveIedModelDiscoveryDocument? model)
    {
        if (ReferenceEquals(_iedBrowserValueSchemaIndex?.SourceModel, model))
            return true;
        if (model is null)
        {
            _iedBrowserValueSchemaIndex = null;
            return true;
        }
        var task = SharedIedSchemaIndex.PrepareAsync(model);
        if (task.IsCompletedSuccessfully)
        {
            _iedBrowserValueSchemaIndex = task.Result;
            _iedBrowserPreparingDevice = null;
            _iedBrowserPreparingModel = null;
            return true;
        }
        ShowIedBrowserPreparing(device, model);
        if (task.IsFaulted || task.IsCanceled)
        {
            IedBrowserDetailStatus.Text = "Model indexing unavailable · " +
                (task.Exception?.GetBaseException().Message ?? "worker canceled");
            return false;
        }
        if (!ReferenceEquals(_iedBrowserAwaitedSchema, task) ||
            !ReferenceEquals(_iedBrowserAwaitedSchemaDevice, device))
        {
            _iedBrowserAwaitedSchema = task;
            _iedBrowserAwaitedSchemaDevice = device;
            _ = CompleteIedBrowserSchemaAsync(task, device, model,
                device.LiveValues.Association);
        }
        return false;
    }

    private async Task CompleteIedBrowserSchemaAsync(
        Task<IedBrowserSchemaIndex> task, Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument model, long association)
    {
        try { await task; }
        catch (Exception) { /* The faulted state is shown by the guarded caller. */ }
        if (_applicationCancellation.IsCancellationRequested ||
            MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
            !ReferenceEquals(SelectedDevice, device) ||
            !ReferenceEquals(device.LiveDiscoveryModel ?? device.SclWorkspace?.DesignModel, model) ||
            device.LiveValues.Association != association)
            return;
        RefreshIedBrowser();
    }

    private bool TryResolveIedBrowserSclInventory(Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument? model, string sourcePath, DateTime sourceStamp,
        out SclReportControlInventoryResult? inventory)
    {
        inventory = null;
        if (sourceStamp == DateTime.MinValue) return true;
        var iedName = EffectiveSclIedName(device);
        var accessPoint = device.SclAccessPointName;
        var key = string.Join("|", sourcePath, sourceStamp.Ticks,
            iedName, accessPoint);
        if (!_iedBrowserInventoryFlights.TryGetValue(key, out var task))
        {
            // Cache failed tasks too: bad/unavailable XML must not launch an
            // endless retry/Refresh loop. Source stamp change or explicit
            // operator Refresh is the only retry trigger for the same key.
            // A completed task is reused when the operator returns to the IED.
            task = Task.Run(() =>
            {
                using var measure = IedBrowserPerformanceProbe.Measure(
                    "SCL.Inventory", device.Name);
                return SclReportControlFilter.InspectFile(sourcePath, iedName, accessPoint);
            });
            _iedBrowserInventoryFlights.Set(key, task);
        }
        _iedBrowserInventoryTask = task;
        if (!task.IsCompleted)
        {
            if (model is not null)
                ShowIedBrowserPreparing(device, model,
                    "Reading SCL inventory on background worker…");
            else IedBrowserDetailStatus.Text = "Reading SCL inventory on background worker…";
            if (!ReferenceEquals(_iedBrowserAwaitedInventory, task) ||
                !ReferenceEquals(_iedBrowserAwaitedInventoryDevice, device))
            {
                _iedBrowserAwaitedInventory = task;
                _iedBrowserAwaitedInventoryDevice = device;
                _ = CompleteIedBrowserInventoryAsync(task, device, model,
                    device.LiveValues.Association);
            }
            return false;
        }
        if (!task.IsCompletedSuccessfully)
        {
            IedBrowserDetailStatus.Text = "Source inventory unavailable · " +
                (task.Exception?.GetBaseException().Message ?? "worker canceled");
            return true; // Same nonfatal behavior as the previous source-file inspector.
        }
        inventory = task.Result;
        return true;
    }

    private async Task CompleteIedBrowserInventoryAsync(
        Task<SclReportControlInventoryResult> task,
        Iec61850MonitorDevice device, LiveIedModelDiscoveryDocument? model, long association)
    {
        try { await task; }
        catch (Exception) { /* Nonfatal inventory failure is displayed on next refresh. */ }
        if (_applicationCancellation.IsCancellationRequested ||
            MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
            !ReferenceEquals(SelectedDevice, device) ||
            !ReferenceEquals(device.LiveDiscoveryModel ?? device.SclWorkspace?.DesignModel, model) ||
            device.LiveValues.Association != association)
            return;
        RefreshIedBrowser();
    }

    // SSOT coverage is not a five-Hz UI task. Model declarations are indexed
    // separately; the observation ledger itself is never copied or modified.
    private void QueueIedBrowserAcceptanceMetrics(
        Iec61850MonitorDevice device, LiveIedModelDiscoveryDocument model)
    {
        var association = device.LiveValues.Association;
        var sameScope = ReferenceEquals(_iedBrowserMetricsDevice, device) &&
                        ReferenceEquals(_iedBrowserMetricsModel, model) &&
                        _iedBrowserMetricsAssociation == association;
        if (!sameScope)
        {
            _iedBrowserMetricsDevice = device;
            _iedBrowserMetricsModel = model;
            _iedBrowserMetricsAssociation = association;
            // A scope switch must not inherit the previous IED's five-second
            // throttle; an older in-flight calculation may finish first.
            _iedBrowserMetricsStartedUtc = DateTime.MinValue;
            IedBrowserSubtitle.ToolTip = "IEC SSOT · calculating read-only coverage…";
        }
        if (_iedBrowserMetricsFlight is { IsCompleted: false } ||
            (sameScope && DateTime.UtcNow - _iedBrowserMetricsStartedUtc <
                TimeSpan.FromSeconds(5)))
            return;
        var failures = _iedBrowserReadFailureEvidence?.SnapshotCurrent(device, 32);
        var enumEvidence = _iedBrowserSclEnumEvidence;
        _iedBrowserMetricsStartedUtc = DateTime.UtcNow;
        var flight = Task.Run(() =>
        {
            using var measure = IedBrowserPerformanceProbe.Measure(
                "Metrics.Capture", device.Name);
            return IedBrowserAcceptanceMetrics.Capture(model, device.LiveValues,
                enumEvidence, failures);
        });
        _iedBrowserMetricsFlight = flight;
        _ = PublishIedBrowserAcceptanceMetricsAsync(
            flight, device, model, association);
    }

    // If selection changes while an old full-model coverage flight is running,
    // schedule exactly the *latest* requested IED once that flight completes.
    // Without this handoff a quiet IED can stay on "calculating" indefinitely.
    private void QueuePendingIedBrowserMetricsAfter(
        Task<IedBrowserAcceptanceMetrics> completed,
        Iec61850MonitorDevice completedDevice,
        LiveIedModelDiscoveryDocument completedModel, long completedAssociation)
    {
        if (!ReferenceEquals(_iedBrowserMetricsFlight, completed) ||
            MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
            _applicationCancellation.IsCancellationRequested ||
            SelectedDevice is not { } current)
            return;
        var currentModel = current.LiveDiscoveryModel;
        if (currentModel is null ||
            !ReferenceEquals(current, _iedBrowserMetricsDevice) ||
            !ReferenceEquals(currentModel, _iedBrowserMetricsModel) ||
            current.LiveValues.Association != _iedBrowserMetricsAssociation)
            return;
        if (ReferenceEquals(completedDevice, current) &&
            ReferenceEquals(completedModel, currentModel) &&
            completedAssociation == current.LiveValues.Association)
            return;
        QueueIedBrowserAcceptanceMetrics(current, currentModel);
    }

    private async Task PublishIedBrowserAcceptanceMetricsAsync(
        Task<IedBrowserAcceptanceMetrics> flight,
        Iec61850MonitorDevice device, LiveIedModelDiscoveryDocument model, long association)
    {
        IedBrowserAcceptanceMetrics metrics;
        try { metrics = await flight; }
        catch (Exception ex)
        {
            if (ReferenceEquals(SelectedDevice, device) &&
                ReferenceEquals(_iedBrowserMetricsFlight, flight) &&
                _iedBrowserMetricsAssociation == association)
                IedBrowserSubtitle.ToolTip = "IEC SSOT coverage unavailable · " + ex.Message;
            QueuePendingIedBrowserMetricsAfter(flight, device, model, association);
            return;
        }
        QueuePendingIedBrowserMetricsAfter(flight, device, model, association);
        if (_applicationCancellation.IsCancellationRequested ||
            MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
            !ReferenceEquals(SelectedDevice, device) ||
            !ReferenceEquals(_iedBrowserMetricsFlight, flight) ||
            !ReferenceEquals(device.LiveDiscoveryModel, model) ||
            device.LiveValues.Association != association)
            return;
        IedBrowserSubtitle.ToolTip = metrics.Summary +
            " · sampled " + DateTime.Now.ToString("HH:mm:ss");
    }
}
