using System.Collections.Specialized;
using System.ComponentModel;
using System.Net;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private readonly SntpClockService _sntpClockService = new();
    private readonly SemaphoreSlim _clockSyncIntegrationGate = new(1, 1);
    private readonly HashSet<string> _clockSyncObservedClients = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _clockSyncRepliedClients = new(StringComparer.OrdinalIgnoreCase);
    private string _lastClockSyncStatus = string.Empty;
    private bool _clockSyncLifecycleAttached;

    internal event Action<SntpClockServiceSnapshot>? ClockSyncSnapshotChanged;
    internal SntpClockServiceSnapshot ClockSyncSnapshot => _sntpClockService.Snapshot;

    private void InitializeClockSyncLifecycle()
    {
        if (_clockSyncLifecycleAttached)
            return;

        _clockSyncLifecycleAttached = true;
        Devices.CollectionChanged += ClockSyncDevices_CollectionChanged;
        foreach (var device in Devices)
            AttachClockSyncDevice(device);

        _sntpClockService.StatusChanged += ClockSyncService_StatusChanged;
        _sntpClockService.ClientRequestObserved += ClockSyncService_ClientRequestObserved;
        _sntpClockService.ReplySent += ClockSyncService_ReplySent;
        Closed += ClockSyncMainWindow_Closed;
    }

    private void ClockSyncDevices_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<Iec61850MonitorDevice>())
                item.PropertyChanged -= ClockSyncDevice_PropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<Iec61850MonitorDevice>())
                AttachClockSyncDevice(item);
        }
    }

    private void AttachClockSyncDevice(Iec61850MonitorDevice device)
    {
        device.PropertyChanged -= ClockSyncDevice_PropertyChanged;
        device.PropertyChanged += ClockSyncDevice_PropertyChanged;

        if (device.IsConnected)
            ScheduleClockSyncReconcile(device);
    }

    private void ClockSyncDevice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Iec61850MonitorDevice.IsConnected) ||
            sender is not Iec61850MonitorDevice device ||
            !device.IsConnected)
            return;

        ScheduleClockSyncReconcile(device);
    }

    private void ScheduleClockSyncReconcile(Iec61850MonitorDevice device)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => ScheduleClockSyncReconcile(device)));
            return;
        }

        _ = EnsureClockSyncForDeviceAsync(device);
    }

    private async Task EnsureClockSyncForDeviceAsync(Iec61850MonitorDevice device)
    {
        if (!IPAddress.TryParse(device.IpAddress, out var iedAddress) ||
            iedAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return;

        await _clockSyncIntegrationGate.WaitAsync();
        try
        {
            await _sntpClockService.EnsureStartedAsync(iedAddress, _applicationCancellation.Token);
            _sntpClockService.RequestImmediateBroadcast();
        }
        catch (OperationCanceledException) when (_applicationCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AddLog("WARN", "Clock Sync",
                $"{device.Name}: IEC 61850 remains connected, but ARSAS SNTP could not start: {ex.Message}");
        }
        finally
        {
            _clockSyncIntegrationGate.Release();
        }
    }

    private void ClockSyncService_StatusChanged(SntpClockServiceSnapshot snapshot)
    {
        void Publish()
        {
            // FAT telemetry must receive every evidence-counter change even when the textual
            // service detail did not change. The live log remains deduplicated separately.
            ClockSyncSnapshotChanged?.Invoke(snapshot);

            var status = $"{snapshot.State}|{snapshot.TransportMode}|{snapshot.Detail}";
            if (status.Equals(_lastClockSyncStatus, StringComparison.Ordinal))
                return;

            _lastClockSyncStatus = status;
            var level = snapshot.State switch
            {
                SntpClockServiceState.Serving => "INFO",
                SntpClockServiceState.Starting => "INFO",
                SntpClockServiceState.Stopped => "INFO",
                _ => "WARN"
            };
            AddLog(level, "Clock Sync", snapshot.Detail);
        }

        if (Dispatcher.CheckAccess())
            Publish();
        else
            Dispatcher.BeginInvoke(new Action(Publish));
    }

    private void ClockSyncService_ClientRequestObserved(SntpClientObservation observation)
    {
        var key = observation.Address.ToString();

        void Publish()
        {
            if (!_clockSyncObservedClients.Add(key))
                return;

            var device = Devices.FirstOrDefault(item =>
                item.IpAddress.Equals(key, StringComparison.OrdinalIgnoreCase));
            var name = device?.Name ?? key;
            AddLog("INFO", "Clock Sync",
                $"{name} ({key}) sent an SNTPv{observation.Version} client request to ARSAS. Request observed; synchronization is not yet proven.");
        }

        if (Dispatcher.CheckAccess())
            Publish();
        else
            Dispatcher.BeginInvoke(new Action(Publish));
    }

    private void ClockSyncService_ReplySent(SntpReplyObservation observation)
    {
        var key = observation.Address.ToString();

        void Publish()
        {
            if (!_clockSyncRepliedClients.Add(key))
                return;

            var device = Devices.FirstOrDefault(item =>
                item.IpAddress.Equals(key, StringComparison.OrdinalIgnoreCase));
            var name = device?.Name ?? key;
            var transport = observation.TransportMode == SntpClockTransportMode.NpcapRaw ? "Npcap RAW" : "UDP";
            AddLog("INFO", "Clock Sync",
                $"{name} ({key}) received an ARSAS SNTP Mode 4 reply via {transport}. Reply sent; relay clock synchronization remains unproven until device evidence confirms it.");
        }

        if (Dispatcher.CheckAccess())
            Publish();
        else
            Dispatcher.BeginInvoke(new Action(Publish));
    }

    private async void ClockSyncMainWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            Devices.CollectionChanged -= ClockSyncDevices_CollectionChanged;
            foreach (var device in Devices)
                device.PropertyChanged -= ClockSyncDevice_PropertyChanged;

            _sntpClockService.StatusChanged -= ClockSyncService_StatusChanged;
            _sntpClockService.ClientRequestObserved -= ClockSyncService_ClientRequestObserved;
            _sntpClockService.ReplySent -= ClockSyncService_ReplySent;
            await _sntpClockService.DisposeAsync();
        }
        catch
        {
            // Application shutdown must never be blocked by a commissioning helper service.
        }
    }
}
