using System.IO;
using System.Windows;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private enum SclSignalSelectionMode
    {
        StaticDataSet,
        Manual
    }

    // This records that the operator has made an explicit selection decision for the
    // shared SCL device. It is intentionally separate from "any selected signal" so an
    // intentionally empty manual selection is preserved when Engineering opens FAT.
    private readonly HashSet<string> _sharedSclSelectionAuthorityDeviceIds =
        new(StringComparer.OrdinalIgnoreCase);

    private SclSignalSelectionMode? PromptSclSignalSelectionMode(Window owner, int iedCount)
    {
        // Engineering SCL import is offline-first. The legacy selection mode remains the
        // authority returned to OpenScl_Click only when the operator explicitly chooses a
        // monitoring workflow; RCB/file-transfer actions are independent and never imply
        // Live Monitor startup.
        if (SelectedDevice is not { } selectedDevice)
        {
            var fallbackDialog = new SclSignalSelectionModeWindow(iedCount)
            {
                Owner = owner
            };
            if (fallbackDialog.ShowDialog() != true)
                return null;

            return fallbackDialog.UseStaticDataSet
                ? SclSignalSelectionMode.StaticDataSet
                : SclSignalSelectionMode.Manual;
        }

        var canDownloadComtrade = !string.IsNullOrWhiteSpace(selectedDevice.IpAddress);
        var quickStart = new SclWorkspaceQuickStartWindow(
            iedCount,
            selectedDevice.Name,
            canDownloadComtrade ? selectedDevice.EndpointText : "No MMS endpoint in SCL",
            canMonitor: true,
            canDownloadComtrade)
        {
            Owner = owner
        };

        if (quickStart.ShowDialog() != true)
        {
            ScheduleSclOfflineReadyStatus(selectedDevice);
            return null;
        }

        switch (quickStart.SelectedAction)
        {
            case SclWorkspaceAction.MonitorStaticDataSet:
                return SclSignalSelectionMode.StaticDataSet;

            case SclWorkspaceAction.MonitorSelectedSignals:
                return SclSignalSelectionMode.Manual;

            case SclWorkspaceAction.RcbEngineering:
                Dispatcher.BeginInvoke(
                    new Action(() => OpenRcbEngineeringQuickStart(selectedDevice)),
                    DispatcherPriority.ContextIdle);
                return null;

            case SclWorkspaceAction.DownloadComtrade:
                Dispatcher.BeginInvoke(
                    new Action(() => OpenComtradeQuickStart(selectedDevice)),
                    DispatcherPriority.ContextIdle);
                return null;

            case SclWorkspaceAction.BrowseOffline:
            case null:
            default:
                ScheduleSclOfflineReadyStatus(selectedDevice);
                return null;
        }
    }

    private void ScheduleSclOfflineReadyStatus(Iec61850MonitorDevice device)
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                AddLog("INFO", "SCL", $"{device.Name}: Engineering workspace kept offline; no MMS connection or monitoring session was started.");
                SetStatus($"{device.Name}: SCL workspace ready offline · choose an IED action when needed.");
            }),
            DispatcherPriority.ContextIdle);
    }

    private void ApplyStaticDataSetSelection(Iec61850MonitorDevice device)
    {
        device.BeginBulkSignalSelection();
        try
        {
            foreach (var signal in device.Signals)
                signal.IsSelected = !string.IsNullOrWhiteSpace(signal.DataSetReference);
        }
        finally
        {
            device.EndBulkSignalSelection();
        }

        SynchronizeAllEngineeringSelectionsToFat(device);
        _sharedSclSelectionAuthorityDeviceIds.Add(device.DeviceId);
        SaveSignalSelectionMemory(device);
        device.RefreshComputed();
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
