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
        Manual
    }

    // This records that the operator has made an explicit selection decision for the
    // shared SCL device. It is intentionally separate from "any selected signal" so an
    // intentionally empty manual selection is preserved when Engineering opens FAT.
    private readonly HashSet<string> _sharedSclSelectionAuthorityDeviceIds =
        new(StringComparer.OrdinalIgnoreCase);

    private SclSignalSelectionMode? PromptSclSignalSelectionMode(Window owner, int iedCount)
    {
        var dialog = new SclSignalSelectionModeWindow(iedCount)
        {
            Owner = owner
        };
        if (dialog.ShowDialog() != true)
            return null;

        return dialog.UseStaticDataSet
            ? SclSignalSelectionMode.StaticDataSet
            : SclSignalSelectionMode.Manual;
    }

    private void ApplyStaticDataSetSelection(Iec61850MonitorDevice device)
    {
        // Static DataSet remains the source of truth for WHAT is monitored. Acquisition
        // prefers the IED's configured BRCB/URCB, while bounded MMS reads remain available
        // for the initial image and for DataSet members whose report mapping/projection is
        // incomplete. Dynamic DataSet writes stay disabled so monitoring never rewrites the
        // IED configuration merely to obtain IEDScout-equivalent readable values.
        var merge = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);
        RegisterRecoveredDataSetSignals(device, merge);
        Iec61850MonitoringModeRegistry.UseStaticDataSetWithMmsFallback(device);

        device.BeginBulkSignalSelection();
        try
        {
            foreach (var signal in device.Signals)
                signal.IsSelected = Iec61850StaticDataSetSelectionPolicy.IsEligible(signal);
        }
        finally
        {
            device.EndBulkSignalSelection();
        }

        SynchronizeAllEngineeringSelectionsToFat(device);
        _sharedSclSelectionAuthorityDeviceIds.Add(device.DeviceId);
        SaveSignalSelectionMemory(device);
        device.RefreshComputed();

        AddLog(
            "INFO",
            device.Name,
            $"Static DataSet authority selected: {device.SelectedLiveSignalCount} runtime DataSet signal(s); configured RCB reporting is preferred, bounded MMS read fallback remains available for initial/uncovered values, and dynamic DataSet writes are disabled.");
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
        // Manual selection restores the normal Smart/Hybrid acquisition contract.
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
