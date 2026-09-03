using System.IO;
using System.Windows;
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

    private SclSignalSelectionMode PromptSclSignalSelectionMode(Window owner, int iedCount)
    {
        var result = MessageBox.Show(
            owner,
            $"Choose the signal authority for this SCL import ({iedCount} item(s)). The result is used by both Engineering and FAT.\n\n" +
            "YES — Use Static DataSet\n" +
            "Select every authoritative static DataSet member.\n\n" +
            "NO — Choose Signals Manually\n" +
            "Open Signal Selection and use the same checkboxes in Engineering and FAT.",
            "SCL Signal Selection",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        return result == MessageBoxResult.Yes
            ? SclSignalSelectionMode.StaticDataSet
            : SclSignalSelectionMode.Manual;
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
            var sourceId = ied.TestPoints
                .Select(point => point.SignalAddress)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(sourceId) ||
                !sourceById.TryGetValue(sourceId, out var source) ||
                !uniquePathByFileName.TryGetValue(source.FileName, out var sourcePath))
            {
                continue;
            }

            var device = ResolveIoTestDevice(ied.LiveDeviceId)
                         ?? ResolveIoTestDevice(ied.IpAddress)
                         ?? ResolveIoTestDevice(ied.IedName);
            if (device is null)
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
                    point.TestEnabled = false;
            }

            await OpenSignalSelectionWizardAsync(
                device,
                autoStartAfterSave: false,
                ownerOverride: owner);

            // The FAT window is not yet attached during an initial FAT import, so perform
            // the same bridge operation explicitly. Once loaded, property changes remain
            // synchronized bidirectionally by the normal bridge handlers.
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
