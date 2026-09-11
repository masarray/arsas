using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    /// <summary>
    /// Reasserts the acquisition contract already chosen in Engineering when the FAT
    /// projection is opened. FAT is a passive consumer of the shared static DataSet
    /// workspace; this method deliberately performs no signal reselection, persistence,
    /// reconnect, discovery, or RefreshComputed work.
    /// </summary>
    private void PreserveSharedStaticDataSetAuthority(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device);
        _sharedSclSelectionAuthorityDeviceIds.Add(device.DeviceId);
        _sharedSclStaticDataSetAuthorityDeviceIds.Add(device.DeviceId);

        AddLog(
            "INFO",
            "FAT",
            $"Preserved Engineering Static DataSet report-only authority for {device.Name}; FAT did not reconnect, enable Hybrid, or start cyclic MMS polling.");
    }
}
