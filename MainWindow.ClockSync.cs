using System.Net;
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
    private long _clockSyncDesiredVersion;

    internal event Action<SntpClockServiceSnapshot>? ClockSyncSnapshotChanged;
    internal SntpClockServiceSnapshot ClockSyncSnapshot => _sntpClockService.Snapshot;

    private void InitializeClockSyncLifecycle()
    {
        if (_clockSyncLifecycleAttached)
            return;
        _clockSyncLifecycleAttached = true;

        _sntpClockService.StatusChanged += ClockSyncService_StatusChanged;
        _sntpClockService.ClientRequestObserved += ClockSyncService_ClientRequestObserved;
        _sntpClockService.ReplySent += ClockSyncService_ReplySent;
        Closed += ClockSyncMainWindow_Closed;
        InstallGlobalSntpToggle();
        PublishGlobalSntpUiState();
        // Deliberately no IED subscriptions: SNTP is an operator-controlled,
        // standalone commissioning clock listening only after toggle ON.
    }

    private async Task ReconcileStandaloneClockAsync()
    {
        var version = Interlocked.Increment(ref _clockSyncDesiredVersion);
        try
        {
            await _clockSyncIntegrationGate.WaitAsync(_applicationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (version != Volatile.Read(ref _clockSyncDesiredVersion))
                return;

            if (!_clockSyncEnabled)
            {
                await _sntpClockService.StopAsync();
                _clockSyncObservedClients.Clear();
                _clockSyncRepliedClients.Clear();
                return;
            }

            var selected = _selectedSntpBinding;
            if (selected is null)
            {
                _clockSyncEnabled = false;
                await _sntpClockService.StopAsync();
                SetStatus("SNTP: select a PC IPv4 address.");
                return;
            }

            // Re-resolve after the async gate: a removed NIC or changed IP must
            // never be served under an obsolete adapter identity.
            var binding = SntpNetworkRouteResolver.ResolveForLocal(
                selected.LocalAddress, selected.InterfaceId);
            var active = _sntpClockService.Snapshot;
            if (active.State == SntpClockServiceState.Serving &&
                active.Binding?.LocalAddress.Equals(binding.LocalAddress) == true &&
                active.Binding.InterfaceId.Equals(binding.InterfaceId, StringComparison.OrdinalIgnoreCase))
                return;

            if (active.State != SntpClockServiceState.Stopped)
                await _sntpClockService.StopAsync();

            if (version != Volatile.Read(ref _clockSyncDesiredVersion) || !_clockSyncEnabled)
                return;

            await _sntpClockService.StartOnLocalAddressAsync(
                binding.LocalAddress, binding.InterfaceId, _applicationCancellation.Token);
        }
        catch (OperationCanceledException) when (_applicationCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AddLog("WARN", "SNTP Server", $"Cannot serve the selected PC IP: {ex.Message}");
            SetStatus("SNTP Server: check selected PC IP or UDP/123 availability.");
        }
        finally
        {
            _clockSyncIntegrationGate.Release();
            PublishGlobalSntpUiState();
        }
    }

    private void ClockSyncService_StatusChanged(SntpClockServiceSnapshot snapshot)
    {
        void Publish()
        {
            RefreshGlobalSntpToggle(snapshot);
            ClockSyncSnapshotChanged?.Invoke(snapshot);
            var status = $"{snapshot.State}|{snapshot.TransportMode}|{snapshot.Detail}";
            if (status.Equals(_lastClockSyncStatus, StringComparison.Ordinal))
                return;
            _lastClockSyncStatus = status;
            AddLog(snapshot.State is SntpClockServiceState.Faulted or SntpClockServiceState.PortUnavailable
                ? "WARN" : "INFO", "SNTP Server", snapshot.Detail);
        }
        if (Dispatcher.CheckAccess()) Publish();
        else if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(new Action(Publish));
    }

    private void ClockSyncService_ClientRequestObserved(SntpClientObservation observation)
    {
        var key = observation.Address.ToString();
        void Publish()
        {
            if (!_clockSyncObservedClients.Add(key)) return;
            var device = Devices.FirstOrDefault(item =>
                item.IpAddress.Equals(key, StringComparison.OrdinalIgnoreCase));
            AddLog("INFO", "SNTP Server",
                $"{device?.Name ?? key} ({key}) sent an SNTPv{observation.Version} request. Clock synchronization is not yet proven.");
        }
        if (Dispatcher.CheckAccess()) Publish();
        else if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(Publish));
    }

    private void ClockSyncService_ReplySent(SntpReplyObservation observation)
    {
        var key = observation.Address.ToString();
        void Publish()
        {
            if (!_clockSyncRepliedClients.Add(key)) return;
            var device = Devices.FirstOrDefault(item =>
                item.IpAddress.Equals(key, StringComparison.OrdinalIgnoreCase));
            AddLog("INFO", "SNTP Server",
                $"{device?.Name ?? key} ({key}): SNTP Mode 4 reply sent. Device-side clock synchronization remains unproven.");
        }
        if (Dispatcher.CheckAccess()) Publish();
        else if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(Publish));
    }

    private async void ClockSyncMainWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            _clockSyncEnabled = false;
            Interlocked.Increment(ref _clockSyncDesiredVersion);
            _sntpClockService.StatusChanged -= ClockSyncService_StatusChanged;
            _sntpClockService.ClientRequestObserved -= ClockSyncService_ClientRequestObserved;
            _sntpClockService.ReplySent -= ClockSyncService_ReplySent;
            await _clockSyncIntegrationGate.WaitAsync();
            try { await _sntpClockService.DisposeAsync(); }
            finally { _clockSyncIntegrationGate.Release(); }
        }
        catch (Exception)
        {
            // Do not block application shutdown on a commissioning utility.
        }
    }
}
