using System.IO;
using System.Windows;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private enum SclSignalSelectionMode
    {
        StaticDataSet,
        BrowseIed,
        Manual
    }

    // This records that the operator has made an explicit selection decision for the
    // shared SCL device. It is intentionally separate from "any selected signal" so an
    // intentionally empty manual selection is preserved when Engineering opens FAT.
    private readonly HashSet<string> _sharedSclSelectionAuthorityDeviceIds =
        new(StringComparer.OrdinalIgnoreCase);

    // Keep the operator's acquisition intent independently of transient runtime teardown.
    // FAT and Engineering share one device/workspace, so entering FAT must never demote an
    // explicitly selected Static DataSet report-only workspace into generic Hybrid/MMS.
    private readonly HashSet<string> _sharedSclStaticDataSetAuthorityDeviceIds =
        new(StringComparer.OrdinalIgnoreCase);

    // Prompted Static DataSet selection is consumed by the same number of IEDs that were
    // presented in that decision. This closes the golden #1888 hole where the FAT import
    // path called MarkSharedSelectionAuthority and accidentally downgraded an explicit
    // Static DataSet choice back to Hybrid before the window was shown.
    private int _pendingSharedStaticSelectionAssignments;

    private bool IsSharedStaticDataSetAuthority(Iec61850MonitorDevice device)
        => _sharedSclStaticDataSetAuthorityDeviceIds.Contains(device.DeviceId) ||
           Iec61850MonitoringModeRegistry.IsStaticDataSetReportOnly(device);

    private SclSignalSelectionMode? PromptSclSignalSelectionMode(Window owner, int iedCount)
    {
        var dialog = new SclSignalSelectionModeWindow(iedCount)
        {
            Owner = owner
        };
        if (dialog.ShowDialog() != true)
        {
            _pendingSharedStaticSelectionAssignments = 0;
            return null;
        }

        SclSignalSelectionMode? mode = dialog.Selection switch
        {
            IedWorkspaceTaskChoice.StaticDataSet => SclSignalSelectionMode.StaticDataSet,
            IedWorkspaceTaskChoice.BrowseIed => SclSignalSelectionMode.BrowseIed,
            IedWorkspaceTaskChoice.ManualSignals => SclSignalSelectionMode.Manual,
            _ => null
        };
        _pendingSharedStaticSelectionAssignments = mode == SclSignalSelectionMode.StaticDataSet
            ? Math.Max(1, iedCount)
            : 0;
        return mode;
    }

    private async Task ApplyStaticDataSetSelection(Iec61850MonitorDevice device)
    {
        // WPF-owned SignalDefinition collections stay on their Dispatcher.
        // Cooperative slices keep input/render live; matching is indexed by
        // exact ARIEC DataSet owner, not a background thread over live bindings.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        device.UpdateReportingProgress(4, "Preparing ARIEC Static DataSet members…");
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        var merge = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);
        RegisterRecoveredDataSetSignals(device, merge);
        var inventoryMs = clock.Elapsed.TotalMilliseconds;
        device.UpdateReportingProgress(20, "Matching exact DataSet authority…");
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);

        var authoritativeSignals = Iec61850StaticDataSetAuthoritySelection.Build(device);
        var authorityMs = clock.Elapsed.TotalMilliseconds;
        Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device);
        device.UpdateReportingProgress(30, "Selecting verified DataSet member rows…");
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);

        // Snapshot object identities before yielding: even a pre-emptive Stop
        // cannot invalidate a live WPF collection enumerator mid-operation.
        var signalRows = device.Signals.ToArray();
        device.BeginBulkSignalSelection();
        try
        {
            var index = 0;
            var total = signalRows.Length;
            foreach (var signal in signalRows)
            {
                signal.IsSelected = authoritativeSignals.Contains(signal);
                index++;
                if ((index & 127) == 0 && index < total)
                {
                    device.UpdateReportingProgress(30d + 30d * index / Math.Max(1, total),
                        $"Selecting Static DataSet members · {index:N0}/{total:N0}…");
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }
        finally
        {
            device.EndBulkSignalSelection();
        }
        var selectionMs = clock.Elapsed.TotalMilliseconds;
        device.UpdateReportingProgress(63, "Synchronizing FAT signal selection…");
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);

        if (_ioFatSelectionBridgeProject is not null)
        {
            var index = 0;
            foreach (var signal in signalRows)
            {
                SynchronizeEngineeringSelectionToFat(signal, device);
                if ((++index & 127) == 0)
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        _sharedSclSelectionAuthorityDeviceIds.Add(device.DeviceId);
        _sharedSclStaticDataSetAuthorityDeviceIds.Add(device.DeviceId);
        SaveSignalSelectionMemory(device);
        device.RefreshComputed();
        if (_pendingSharedStaticSelectionAssignments > 0)
            _pendingSharedStaticSelectionAssignments--;

        AddLog("INFO", device.Name,
            $"Static DataSet report-only authority selected: {device.SelectedLiveSignalCount} exact runtime member row(s) from {merge.MandatoryCatalogCount} ARIEC static membership descriptor(s); cyclic MMS process polling and dynamic DataSet writes remain disabled.");
        AddLog("INFO", device.Name,
            $"P0_STATIC_UI_WORK inventoryMs={inventoryMs:0.###} exactAuthorityMs={authorityMs - inventoryMs:0.###} selectionMs={selectionMs - authorityMs:0.###} totalMs={clock.Elapsed.TotalMilliseconds:0.###} signalRows={device.Signals.Count} selected={authoritativeSignals.Count}");

        device.UpdateReportingProgress(75, "Checking configured RCB reporting coverage…");
        LogStaticDataSetReportFeasibility(device);
        _ = ObserveInitialStaticReportEvidenceAsync(device);
        SynchronizeProductionFatSelectedIed();
    }

    private void ClearSharedSignalSelection(Iec61850MonitorDevice device)
    {
        device.BeginBulkSignalSelection();
        try
        {
            foreach (var signal in device.Signals)
                signal.IsSelected = false;
        }
        finally
        {
            device.EndBulkSignalSelection();
        }
    }

    private void MarkSharedSelectionAuthority(Iec61850MonitorDevice device)
    {
        // The initial FAT import historically reached this helper for both branches. If the
        // immediately preceding operator decision was Static DataSet, preserve that explicit
        // report-only authority instead of silently demoting it to Hybrid.
        if (_pendingSharedStaticSelectionAssignments > 0)
        {
            // Legacy synchronous FAT hook retains exact membership semantics.
            // Card/SCL actions use the cooperative responsive path above.
            var merge = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);
            RegisterRecoveredDataSetSignals(device, merge);
            var selected = Iec61850StaticDataSetAuthoritySelection.Build(device);
            Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device);
            device.BeginBulkSignalSelection();
            try
            {
                foreach (var signal in device.Signals)
                    signal.IsSelected = selected.Contains(signal);
            }
            finally
            {
                device.EndBulkSignalSelection();
            }
            SynchronizeAllEngineeringSelectionsToFat(device);
            _sharedSclSelectionAuthorityDeviceIds.Add(device.DeviceId);
            _sharedSclStaticDataSetAuthorityDeviceIds.Add(device.DeviceId);
            SaveSignalSelectionMemory(device);
            device.RefreshComputed();
            _pendingSharedStaticSelectionAssignments--;
            LogStaticDataSetReportFeasibility(device);
            _ = ObserveInitialStaticReportEvidenceAsync(device);
            SynchronizeProductionFatSelectedIed();
            return;
        }

        // Manual selection restores the normal Smart/Hybrid acquisition contract.
        _sharedSclStaticDataSetAuthorityDeviceIds.Remove(device.DeviceId);
        Iec61850MonitoringModeRegistry.UseHybrid(device);
        _sharedSclSelectionAuthorityDeviceIds.Add(device.DeviceId);
        SaveSignalSelectionMemory(device);
    }

    private string[] CurrentEngineeringSclSourcePaths()
        => Devices
            .Select(device => device.SclSourcePath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void RegisterSharedSclSourcePaths(
        IoTestProject project,
        IEnumerable<IoTestIedPlan> ieds,
        IReadOnlyCollection<IoFatSourceInput> sourceInputs)
    {
        var uniquePathByFileName = sourceInputs
            .GroupBy(input => Path.GetFileName(input.FilePath), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => Path.GetFullPath(group.Single().FilePath),
                StringComparer.OrdinalIgnoreCase);
        var sourceById = project.Sources.ToDictionary(source => source.SourceId, StringComparer.OrdinalIgnoreCase);

        foreach (var ied in ieds)
        {
            var device = ResolveIoTestDevice(ied.LiveDeviceId)
                         ?? ResolveIoTestDevice(ied.IpAddress)
                         ?? ResolveIoTestDevice(ied.IedName);
            if (device is null)
                continue;

            IoFatSourceDescriptor? source = null;
            var sourceId = ied.TestPoints
                .Select(point => point.SignalAddress)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && sourceById.ContainsKey(value));
            if (!string.IsNullOrWhiteSpace(sourceId))
                source = sourceById[sourceId];

            // Manual-only SCL workspaces can legitimately have zero static DataSet rows,
            // so source identity must not depend on finding a FAT point first.
            if (source is null && !string.IsNullOrWhiteSpace(device.SclSourceSha256))
            {
                source = project.Sources.FirstOrDefault(candidate => candidate.Sha256.Equals(
                    device.SclSourceSha256,
                    StringComparison.OrdinalIgnoreCase));
            }
            if (source is null && project.Sources.Count == 1)
                source = project.Sources[0];
            if (source is null && !string.IsNullOrWhiteSpace(device.SclSourcePath))
            {
                var currentName = Path.GetFileName(device.SclSourcePath);
                source = project.Sources.FirstOrDefault(candidate => candidate.FileName.Equals(
                    currentName,
                    StringComparison.OrdinalIgnoreCase));
            }
            if (source is null || !uniquePathByFileName.TryGetValue(source.FileName, out var sourcePath))
                continue;

            device.SclSourcePath = sourcePath;
            device.SclSourceSha256 = source.Sha256;
        }
    }

    private async Task ApplyManualSelectionToFatProjectAsync(
        IoTestProject project,
        IEnumerable<IoTestIedPlan> ieds,
        Window owner,
        bool resetSelection)
    {
        foreach (var ied in ieds)
        {
            var device = ResolveIoTestDevice(ied.LiveDeviceId)
                         ?? ResolveIoTestDevice(ied.IpAddress)
                         ?? ResolveIoTestDevice(ied.IedName);
            if (device is null)
                continue;

            if (resetSelection)
            {
                ClearSharedSignalSelection(device);
                foreach (var point in ied.TestPoints)
                    point.WorkspaceSelected = false;
            }

            await OpenSignalSelectionWizardAsync(
                device,
                autoStartAfterSave: false,
                ownerOverride: owner);

            // The FAT window is not yet attached during an initial FAT import, so perform
            // the same bridge operation explicitly. Selected non-DataSet SCL signals are
            // materialized here as persistent FAT rows; existing FAT TEST/disposition state
            // is never rewritten by Engineering selection.
            foreach (var signal in device.Signals)
            {
                IoFatEngineeringSelectionBridge.ApplyEngineeringSignalSelection(
                    signal,
                    signal.IsSelected,
                    ied,
                    device);
            }

            MarkSharedSelectionAuthority(device);
        }
    }
}
