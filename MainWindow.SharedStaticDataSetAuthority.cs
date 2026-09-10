using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    /// <summary>
    /// Reasserts the acquisition contract already chosen in Engineering when the FAT
    /// projection is opened. FAT is a consumer of the shared static DataSet workspace;
    /// it must never demote that workspace to Hybrid/MMS merely because a FAT session
    /// was materialized.
    /// </summary>
    private void PreserveSharedStaticDataSetAuthority(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device);
        _sharedSclSelectionAuthorityDeviceIds.Add(device.DeviceId);
        _sharedSclStaticDataSetAuthorityDeviceIds.Add(device.DeviceId);
        SaveSignalSelectionMemory(device);
        device.RefreshComputed();

        AddLog(
            "INFO",
            "FAT",
            $"Preserved Engineering Static DataSet report-only authority for {device.Name}; FAT did not enable Hybrid or cyclic MMS polling.");
    }
}
