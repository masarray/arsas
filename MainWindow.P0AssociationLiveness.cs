using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Supplements the report-only monitor path with endpoint liveness without turning a process
/// value into an MMS heartbeat. While an Engineering association is active we use ICMP only
/// after that endpoint has first proven it supports ICMP. Two consecutive proven endpoint
/// losses transition the shared Engineering device OFFLINE. Once offline, a bounded TCP/102
/// reachability check detects the endpoint returning and the normal saved-model connection +
/// monitor pipeline is used to create the new authoritative MMS association.
/// </summary>
public partial class MainWindow
{
    private static readonly bool AssociationLivenessClassHandlerRegistered = RegisterAssociationLivenessClassHandler();
    private readonly Dictionary<string, int> _associationLivenessFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _associationPingProven = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _associationReconnectWanted = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _associationResumeMonitoring = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _associationLivenessTimer;
    private CancellationTokenSource? _associationLivenessCancellation;
    private bool _associationLivenessTickRunning;

    private static bool RegisterAssociationLivenessClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(AssociationLiveness_Loaded));
        return true;
    }

    private static void AssociationLiveness_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.EnsureAssociationLivenessWatchdog();
    }

    private void EnsureAssociationLivenessWatchdog()
    {
        if (_associationLivenessTimer != null)
            return;

        _associationLivenessCancellation = new CancellationTokenSource();
        _associationLivenessTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _associationLivenessTimer.Tick += AssociationLivenessTimer_Tick;
        _associationLivenessTimer.Start();
        Closed += AssociationLiveness_Closed;
    }

    private async void AssociationLivenessTimer_Tick(object? sender, EventArgs e)
    {
        if (_associationLivenessTickRunning || _associationLivenessCancellation?.IsCancellationRequested != false)
            return;

        _associationLivenessTickRunning = true;
        try
        {
            await RunAssociationLivenessPassAsync(_associationLivenessCancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _associationLivenessTickRunning = false;
        }
    }

    private async Task RunAssociationLivenessPassAsync(CancellationToken cancellationToken)
    {
        foreach (var device in Devices.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (device.IsDemo || string.IsNullOrWhiteSpace(device.IpAddress) || device.IsBusy)
                continue;

            if (_associationReconnectWanted.Contains(device.DeviceId))
            {
                if (device.IsConnected)
                {
                    CompleteAssociationReconnect(device);
                    continue;
                }

                if (!await IsMmsEndpointReachableAsync(device, cancellationToken).ConfigureAwait(true))
                    continue;

                await TryAssociationReconnectAsync(device, cancellationToken).ConfigureAwait(true);
                continue;
            }

            if (!device.IsConnected)
            {
                _associationLivenessFailures.Remove(device.DeviceId);
                continue;
            }

            var pingAlive = await TryPingEndpointAsync(device.IpAddress, cancellationToken).ConfigureAwait(true);

            // A user may intentionally disconnect while the asynchronous probe is in flight.
            // Never convert that explicit Stop into an automatic reconnect request.
            if (!device.IsConnected || _associationReconnectWanted.Contains(device.DeviceId))
            {
                _associationLivenessFailures.Remove(device.DeviceId);
                continue;
            }

            if (pingAlive == true)
            {
                _associationPingProven.Add(device.DeviceId);
                _associationLivenessFailures[device.DeviceId] = 0;
                continue;
            }

            // Before the first successful ping, null means ICMP may simply be blocked by the
            // site and must never declare the IED dead. After ICMP has been proven for this
            // endpoint, however, both an explicit non-success reply and a PingException/general
            // transport failure are real liveness failures. Physical cable/power loss on Windows
            // commonly surfaces as PingException rather than a TimedOut reply.
            if (!_associationPingProven.Contains(device.DeviceId))
                continue;

            var failures = _associationLivenessFailures.TryGetValue(device.DeviceId, out var current)
                ? current + 1
                : 1;
            _associationLivenessFailures[device.DeviceId] = failures;
            if (failures < 2)
                continue;

            await MarkAssociationOfflineAsync(device).ConfigureAwait(true);
        }
    }

    private async Task MarkAssociationOfflineAsync(Iec61850MonitorDevice device)
    {
        var resumeMonitoring = device.IsMonitoring;
        _associationReconnectWanted.Add(device.DeviceId);
        if (resumeMonitoring)
            _associationResumeMonitoring.Add(device.DeviceId);

        device.IsConnected = false;
        device.IsMonitoring = false;
        device.Status = "Offline";
        device.Detail = $"{device.EndpointText} stopped responding. Smart reconnect will resume automatically when the IEC 61850 endpoint returns.";
        device.AcquisitionMode = "Connection lost • smart reconnect";
        device.RefreshComputed();
        AddLog("WARN", device.Name,
            $"Endpoint liveness lost after two consecutive probes; {device.EndpointText} marked OFFLINE and automatic reconnect armed.");

        try
        {
            await StopDeviceConnectionAsync(device).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AddLog("WARN", device.Name,
                $"Offline cleanup reported {ex.GetType().Name}: {ex.Message}. Smart reconnect remains armed.");
        }

        // StopDeviceConnectionAsync owns normal manual-disconnect text; restore the
        // automatic-recovery state so Engineering and FAT cards expose the real condition.
        device.IsConnected = false;
        device.IsMonitoring = false;
        device.Status = "Offline";
        device.Detail = $"{device.EndpointText} is offline. Waiting for the IEC 61850 endpoint to return…";
        device.AcquisitionMode = "Connection lost • smart reconnect";
        device.RefreshComputed();
    }

    private async Task TryAssociationReconnectAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        device.Status = "Reconnecting";
        device.Detail = $"{device.EndpointText} is reachable again. Opening a fresh IEC 61850 association…";
        device.RefreshComputed();

        var connected = false;
        try
        {
            connected = device.HasDiscoveryCache && device.Signals.Count > 0
                ? await ConnectUsingSavedModelAsync(device, selectDevice: false).ConfigureAwait(true)
                : await ConnectAndConfigureDeviceAsync(device, openWizard: false, selectDevice: false).ConfigureAwait(true);

            if (connected &&
                _associationResumeMonitoring.Contains(device.DeviceId) &&
                !device.IsMonitoring)
            {
                await StartDeviceMonitorAsync(device).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AddLog("WARN", device.Name,
                $"Smart reconnect attempt failed: {ex.GetType().Name}: {ex.Message}");
        }

        if (connected && device.IsConnected)
        {
            CompleteAssociationReconnect(device);
            return;
        }

        device.Status = "Reconnect pending";
        device.Detail = $"{device.EndpointText} answered the reachability probe but an IEC 61850 association is not ready yet. ARSAS will retry automatically.";
        device.RefreshComputed();
    }

    private void CompleteAssociationReconnect(Iec61850MonitorDevice device)
    {
        _associationReconnectWanted.Remove(device.DeviceId);
        _associationResumeMonitoring.Remove(device.DeviceId);
        _associationLivenessFailures[device.DeviceId] = 0;
        AddLog("INFO", device.Name,
            $"Smart reconnect completed for {device.EndpointText}; shared Engineering/FAT connection state is live again.");
    }

    private static async Task<bool?> TryPingEndpointAsync(
        string host,
        CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, TimeSpan.FromMilliseconds(650), Array.Empty<byte>(), null, cancellationToken)
                .ConfigureAwait(false);
            return reply.Status == IPStatus.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PingException)
        {
            // null is only "unknown" until this endpoint has first proven ICMP support.
            // RunAssociationLivenessPassAsync turns it into a failure after that proof.
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<bool> IsMmsEndpointReachableAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(850));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(device.IpAddress, device.Port, timeout.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void AssociationLiveness_Closed(object? sender, EventArgs e)
    {
        Closed -= AssociationLiveness_Closed;
        if (_associationLivenessTimer != null)
        {
            _associationLivenessTimer.Stop();
            _associationLivenessTimer.Tick -= AssociationLivenessTimer_Tick;
            _associationLivenessTimer = null;
        }

        _associationLivenessCancellation?.Cancel();
        _associationLivenessCancellation?.Dispose();
        _associationLivenessCancellation = null;
        _associationLivenessFailures.Clear();
        _associationPingProven.Clear();
        _associationReconnectWanted.Clear();
        _associationResumeMonitoring.Clear();
    }
}
