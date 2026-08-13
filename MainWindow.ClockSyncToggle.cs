using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private bool _clockSyncEnabled = true;

    internal bool IsClockSyncEnabled => _clockSyncEnabled;

    internal async Task SetClockSyncEnabledAsync(bool enabled)
    {
        if (_clockSyncEnabled == enabled)
            return;

        _clockSyncEnabled = enabled;

        if (!enabled)
        {
            Devices.CollectionChanged -= ClockSyncDevices_CollectionChanged;
            foreach (var device in Devices)
                device.PropertyChanged -= ClockSyncDevice_PropertyChanged;

            await _clockSyncIntegrationGate.WaitAsync();
            try
            {
                await _sntpClockService.StopAsync();
                _clockSyncObservedClients.Clear();
                AddLog("INFO", "Clock Sync",
                    "Clock Sync disabled from the FAT workspace. IEC 61850 monitoring remains active.");
            }
            catch (Exception ex)
            {
                AddLog("WARN", "Clock Sync",
                    $"Clock Sync disable requested, but the SNTP service reported: {ex.Message}");
            }
            finally
            {
                _clockSyncIntegrationGate.Release();
            }

            return;
        }

        Devices.CollectionChanged -= ClockSyncDevices_CollectionChanged;
        Devices.CollectionChanged += ClockSyncDevices_CollectionChanged;
        foreach (var device in Devices)
            AttachClockSyncDevice(device);

        AddLog("INFO", "Clock Sync",
            "Clock Sync enabled from the FAT workspace. ARSAS will advertise laptop time to connected IPv4 IEDs using the SIPROTEC-compatible SNTP profile.");
    }
}
