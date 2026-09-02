using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    /// <summary>
    /// Starts one IED's FAT monitor from an explicit acquisition scope without changing
    /// the operator's Engineering/TEST selection. Iec61850MonitorRuntime snapshots the
    /// selected signal set when StartMonitoringAsync is called, so P1 temporarily arms
    /// only the acquisition signals under the device's bulk-selection guard and restores
    /// the operator selection immediately after the runtime point definitions are built.
    /// </summary>
    private async Task<bool> StartIoFatDeviceMonitorAsync(
        Iec61850MonitorDevice device,
        IReadOnlyCollection<SignalDefinition> acquisitionSignals)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(acquisitionSignals);

        var acquisition = acquisitionSignals
            .Where(signal => signal.CanPublishToRuntime)
            .GroupBy(
                signal => IoTestLiveBindingService.NormalizeReference(signal.ObjectReference),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (acquisition.Length == 0)
        {
            SetStatus($"{device.Name}: no readable FAT acquisition point is available.");
            return false;
        }

        var acquisitionSet = acquisition.ToHashSet();
        var originalSelection = device.Signals
            .ToDictionary(signal => signal, signal => signal.IsSelected);

        device.BeginBulkSignalSelection();
        try
        {
            // Runtime acquisition and operator selection are intentionally different
            // authorities in P1. The bulk guard prevents Signal_PropertyChanged from
            // reflecting these temporary acquisition flags back into TEST checkboxes.
            foreach (var signal in device.Signals)
                signal.IsSelected = acquisitionSet.Contains(signal);

            device.HasReportStream = false;
            device.ReportPulseActive = false;
            _reportPulseUntil.Remove(device.DeviceId);
            RemoveDeviceHighlights(device.DeviceId);
            RemoveDevicePoints(device.DeviceId);
            device.Points.Clear();

            SetStatus($"{device.Name}: starting independent FAT live acquisition…");
            var points = await _runtime.StartMonitoringAsync(
                device,
                acquisition,
                PollingIntervalMs,
                _applicationCancellation.Token);

            device.Points.AddRange(points);
            GlobalPoints.AddRange(points);
            RebuildControlFeedbackIndex(device);
            foreach (var point in points)
                _pointIndex[point.PointKey] = point;
            device.RefreshComputed();
            RaiseWorkspaceCounts();
            SetStatus($"{device.Name}: FAT monitoring {points.Count} point(s). {device.AcquisitionMode}");
            return true;
        }
        catch (OperationCanceledException)
        {
            SetStatus($"{device.Name}: FAT monitor start cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            AddLog("ERROR", device.Name, $"FAT monitor start failed: {ex.Message}");
            SetStatus($"{device.Name}: FAT monitor start failed. Diagnostics is marked with !.");
            MarkDiagnosticAlert();
            return false;
        }
        finally
        {
            foreach (var (signal, wasSelected) in originalSelection)
                signal.IsSelected = wasSelected;
            device.EndBulkSignalSelection();
            device.RefreshComputed();
            RaiseWorkspaceCounts();
        }
    }
}
