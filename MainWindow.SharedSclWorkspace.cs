using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private readonly HashSet<string> _sharedSclSelectionAuthorityDeviceIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sharedSclStaticDataSetAuthorityDeviceIds = new(StringComparer.OrdinalIgnoreCase);
    private int _pendingSharedStaticSelectionAssignments;

    private bool UsesSharedSclSelectionAuthority(Iec61850MonitorDevice? device)
        => device != null && _sharedSclSelectionAuthorityDeviceIds.Contains(device.DeviceId);

    private bool UsesSharedStaticDataSetAuthority(Iec61850MonitorDevice? device)
        => device != null && _sharedSclStaticDataSetAuthorityDeviceIds.Contains(device.DeviceId);

    private void SynchronizeAllEngineeringSelectionsToFat(Iec61850MonitorDevice? device)
    {
        if (device == null)
            return;

        foreach (var signal in device.Signals)
            signal.IsSelectedForFat = signal.IsSelected;
    }

    private void SetSharedSclSelectionAuthority(Iec61850MonitorDevice device, bool enabled)
    {
        if (enabled)
            _sharedSclSelectionAuthorityDeviceIds.Add(device.DeviceId);
        else
            _sharedSclSelectionAuthorityDeviceIds.Remove(device.DeviceId);
    }

    private void SetSharedStaticDataSetAuthority(Iec61850MonitorDevice device, bool enabled)
    {
        if (enabled)
            _sharedSclStaticDataSetAuthorityDeviceIds.Add(device.DeviceId);
        else
            _sharedSclStaticDataSetAuthorityDeviceIds.Remove(device.DeviceId);
    }

    private void PreserveSharedStaticDataSetAuthority(Iec61850MonitorDevice device)
    {
        SetSharedSclSelectionAuthority(device, true);
        SetSharedStaticDataSetAuthority(device, true);
        Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device);
    }

    private void ApplySharedStaticDataSetSelectionAuthority(
        Iec61850MonitorDevice device,
        SclWorkspaceStaticDataSetMergeResult merge)
    {
        if (device == null)
            return;

        var authoritativeSignals = merge.AuthoritativeSignals
            .Where(signal => signal != null)
            .ToHashSet();

        // Static DataSet mode is report-only by design. Selection here identifies exact
        // report membership and must never fall through to cyclic process polling.
        Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device);

        device.BeginBulkSignalSelection();
        try
        {
            foreach (var signal in device.Signals)
                signal.IsSelected = authoritativeSignals.Contains(signal);
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
        if (_pendingSharedStaticSelectionAssignments > 0)
            _pendingSharedStaticSelectionAssignments--;

        AddLog(
            "INFO",
            device.Name,
            $"Static DataSet report-only authority selected: {device.SelectedLiveSignalCount} exact runtime member row(s) from {merge.MandatoryCatalogCount} ARIEC static membership descriptor(s); cyclic MMS process polling and dynamic DataSet writes remain disabled.");

        // Make feasibility and first-report proof visible from the initial Engineering
        // workflow rather than waiting until FAT is opened. The observer waits for the
        // shared monitor to start and never changes acquisition method.
        LogStaticDataSetReportFeasibility(device);
        _ = ObserveInitialStaticReportEvidenceAsync(device);

        // P5 native FAT is a thin view over SelectedDevice.Points. Re-synchronize the
        // canonical view after static DataSet authority refresh so a same-IED SCL refresh
        // is visible immediately without reviving the retired Engineering -> IoTest bootstrap.
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
        // report-only authority instead of silently downgrading it to shared polling mode.
        if (UsesSharedStaticDataSetAuthority(device) || _pendingSharedStaticSelectionAssignments > 0)
        {
            PreserveSharedStaticDataSetAuthority(device);
            if (_pendingSharedStaticSelectionAssignments > 0)
                _pendingSharedStaticSelectionAssignments--;
            return;
        }

        SetSharedSclSelectionAuthority(device, true);
        SetSharedStaticDataSetAuthority(device, false);
        Iec61850MonitoringModeRegistry.UseSharedSelection(device);
    }

    private void ClearSharedSelectionAuthority(Iec61850MonitorDevice device)
    {
        SetSharedSclSelectionAuthority(device, false);
        SetSharedStaticDataSetAuthority(device, false);
        Iec61850MonitoringModeRegistry.UseHybrid(device);
    }

    private void ApplySharedSclSelectionAuthority(Iec61850MonitorDevice device)
    {
        SynchronizeAllEngineeringSelectionsToFat(device);
        MarkSharedSelectionAuthority(device);
        SaveSignalSelectionMemory(device);
        device.RefreshComputed();
    }

    private void ClearSharedSclSelectionAuthority(Iec61850MonitorDevice device)
    {
        ClearSharedSignalSelection(device);
        ClearSharedSelectionAuthority(device);
        SaveSignalSelectionMemory(device);
        device.RefreshComputed();
    }
}
