using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    internal bool RestoreP0SharedStaticDataSetMembership(IoTestIedPlan ied)
    {
        ArgumentNullException.ThrowIfNull(ied);
        var device = ResolveIoTestDevice(ied.LiveDeviceId)
                     ?? ResolveIoTestDevice(ied.IpAddress)
                     ?? ResolveIoTestDevice(ied.IedName);
        if (device is null || !IsSharedStaticDataSetAuthority(device))
            return false;

        var missing = ied.TestPoints
            .Where(point => point.ImportReady && !point.WorkspaceSelected)
            .ToArray();
        if (missing.Length == 0)
            return true;

        // Static DataSet is the current explicit shared workspace authority. A stale local
        // snapshot must not filter those exact imported members out of FAT. Guard the batch
        // so 50+ rows do not fan out into 50+ Engineering bridge/save/refresh operations.
        _ioFatSelectionBridgeActive = true;
        try
        {
            foreach (var point in missing)
                point.WorkspaceSelected = true;
        }
        finally
        {
            _ioFatSelectionBridgeActive = false;
        }

        ScheduleIoFatSelectionSave(device);
        _loadedIoFatWindow?.Storage?.ScheduleSave();
        RaiseWorkspaceCounts();
        return true;
    }
}
