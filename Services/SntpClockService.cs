using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ArIED61850Tester.Services;

public enum SntpClockServiceState
{
    Stopped,
    Starting,
    Serving,
    PortUnavailable,
    Faulted
}

public sealed record SntpClientObservation(
    IPAddress Address,
    DateTimeOffset LastRequestUtc,
    int RequestCount,
    byte Version);

public sealed record SntpClockServiceSnapshot(
    SntpClockServiceState State,
    string Detail,
    SntpNetworkBinding? Binding,
    DateTimeOffset? LastBroadcastUtc,
    int ObservedClientCount,
    bool ClockHealthy);

/// <summary>
/// Lightweight SNTPv4 commissioning clock for ARSAS.
///
/// Design goals:
/// - clean-room wire implementation from RFC semantics;
/// - one UDP/123 service bound only to the station-bus interface chosen by Windows routing;
/// - Mode 4 unicast replies plus Mode 5 directed broadcasts;
/// - no dependency on MMS/GOOSE/SV protocol code;
/// - fail-open for IEC 61850: any SNTP problem is diagnostic only and never breaks an IED association.
/// </summary>
public sealed class SntpClockService : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly ConcurrentDictionary<string, SntpClientObservation> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly SntpClockHealthMonitor _clockHealth = new();
    private readonly SntpServerProfile _profile;
    private readonly TimeSpan _broadcastInterval;
    private UdpClient? _udp;
    private CancellationTokenSource? _serviceCancellation;
    private Task? _receiveTask;
    private Task? _broadcastTask;
    private SntpNetworkBinding? _binding;
    private DateTimeOffset? _lastBroadcastUtc;
    private SntpClockServiceState _state = SntpClockServiceState.Stopped;
    private string _detail = "SNTP clock service is stopped.";
    private int _broadcastPulseRequested;

    public SntpClockService(
        SntpServerProfile? profile = null,
        TimeSpan? broadcastInterval = null)
    {
        _profile = profile ?? new SntpServerProfile();
        _broadcastInterval = NormalizeBroadcastInterval(broadcastInterval ?? TimeSpan.FromSeconds(16));
    }

    public event Action<SntpClockServiceSnapshot>? StatusChanged;
    public event Action<SntpClientObservation>? ClientRequestObserved;

    public SntpClockServiceSnapshot Snapshot
        => new(
            _state,
            _detail,
            _binding,
            _lastBroadcastUtc,
            _clients.Count,
            _clockHealth.Sample().IsHealthy);

    public IReadOnlyCollection<SntpClientObservation> ObservedClients
        => _clients.Values.OrderBy(item => item.Address.ToString(), StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task EnsureStartedAsync(IPAddress iedAddress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(iedAddress);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var requestedBinding = SntpNetworkRouteResolver.ResolveForRemote(iedAddress);
            if (_udp != null && _binding != null)
            {
                if (_binding.LocalAddress.Equals(requestedBinding.LocalAddress))
                {
                    RequestImmediateBroadcast();
                    return;
                }

                SetState(
                    SntpClockServiceState.Serving,
                    $"SNTP remains bound to {_binding.Summary}. IED {iedAddress} routes through {requestedBinding.LocalAddress}; multi-NIC serving is deferred to the raw/Npcap transport phase.");
                return;
            }

            await StartCoreAsync(requestedBinding, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void RequestImmediateBroadcast()
        => Interlocked.Exchange(ref _broadcastPulseRequested, 1);

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var cancellation = _serviceCancellation;
            _serviceCancellation = null;
            if (cancellation != null)
            {
                try { cancellation.Cancel(); } catch { }
            }

            try { _udp?.Dispose(); } catch { }
            _udp = null;

            var tasks = new[] { _receiveTask, _broadcastTask }.Where(task => task != null).Cast<Task>().ToArray();
            _receiveTask = null;
            _broadcastTask = null;
            if (tasks.Length > 0)
            {
                try { await Task.WhenAll(tasks).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch { }
            }

            cancellation?.Dispose();
            _binding = null;
            _lastBroadcastUtc = null;
            SetState(SntpClockServiceState.Stopped, "SNTP clock service is stopped.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    private Task StartCoreAsync(SntpNetworkBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetState(SntpClockServiceState.Starting, $"Preparing SNTP on {binding.Summary}.");

        var udp = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            // Do not co-bind UDP/123. If Windows Time or another server owns it, fail clearly
            // rather than risk nondeterministic packet delivery between two NTP listeners.
            udp.Client.ExclusiveAddressUse = true;
            udp.EnableBroadcast = true;
            udp.Client.Bind(new IPEndPoint(binding.LocalAddress, 123));
        }
        catch (SocketException ex)
        {
            udp.Dispose();
            SetState(
                SntpClockServiceState.PortUnavailable,
                $"UDP/123 is unavailable on {binding.LocalAddress} ({ex.SocketErrorCode}). Windows Time or another NTP service may already own the port.");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            udp.Dispose();
            SetState(SntpClockServiceState.Faulted, $"Could not start SNTP on {binding.LocalAddress}: {ex.Message}");
            return Task.CompletedTask;
        }

        _binding = binding;
        _udp = udp;
        _clients.Clear();
        _clockHealth.Reset();
        _serviceCancellation = new CancellationTokenSource();

        SetState(
            SntpClockServiceState.Serving,
            binding.DirectedBroadcast == null
                ? $"SNTP unicast server active on {binding.LocalAddress}:123. This subnet has no usable directed broadcast address."
                : $"SNTP server active on {binding.LocalAddress}:123; Mode 5 broadcast targets {binding.DirectedBroadcast}:123.");

        _receiveTask = ReceiveLoopAsync(udp, _serviceCancellation.Token);
        _broadcastTask = BroadcastLoopAsync(udp, binding, _serviceCancellation.Token);
        RequestImmediateBroadcast();
        return Task.CompletedTask;
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (SocketException ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                    SetState(SntpClockServiceState.Faulted, $"SNTP receive failed: {ex.SocketErrorCode}.");
                break;
            }

            var receiveUtc = DateTimeOffset.UtcNow;
            if (!SntpPacket.TryReadClientRequest(received.Buffer, out var request))
                continue;

            var health = _clockHealth.Sample(receiveUtc);
            var transmitUtc = DateTimeOffset.UtcNow;
            byte[] reply;
            try
            {
                reply = SntpPacket.BuildServerReply(
                    received.Buffer,
                    receiveUtc,
                    transmitUtc,
                    _profile with { ReferenceUtc = health.ReferenceUtc },
                    health.IsHealthy);
            }
            catch (ArgumentOutOfRangeException)
            {
                SetState(SntpClockServiceState.Serving,
                    "SNTP request was ignored because the Windows UTC value is outside the NTP timestamp range.");
                continue;
            }

            try
            {
                await udp.SendAsync(reply, reply.Length, received.RemoteEndPoint).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (SocketException ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                    SetState(SntpClockServiceState.Faulted, $"SNTP reply to {received.RemoteEndPoint.Address} failed: {ex.SocketErrorCode}.");
                continue;
            }

            var key = received.RemoteEndPoint.Address.ToString();
            var observation = _clients.AddOrUpdate(
                key,
                _ => new SntpClientObservation(received.RemoteEndPoint.Address, transmitUtc, 1, request.Version),
                (_, previous) => previous with
                {
                    LastRequestUtc = transmitUtc,
                    RequestCount = previous.RequestCount + 1,
                    Version = request.Version
                });
            ClientRequestObserved?.Invoke(observation);
        }
    }

    private async Task BroadcastLoopAsync(
        UdpClient udp,
        SntpNetworkBinding binding,
        CancellationToken cancellationToken)
    {
        if (binding.DirectedBroadcast == null)
            return;

        var destination = new IPEndPoint(binding.DirectedBroadcast, 123);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (Interlocked.Exchange(ref _broadcastPulseRequested, 0) == 1 ||
                    _lastBroadcastUtc == null ||
                    DateTimeOffset.UtcNow - _lastBroadcastUtc >= _broadcastInterval)
                {
                    var health = _clockHealth.Sample();
                    if (health.IsHealthy)
                    {
                        var now = DateTimeOffset.UtcNow;
                        var packet = SntpPacket.BuildBroadcast(
                            now,
                            _profile with { ReferenceUtc = health.ReferenceUtc },
                            synchronized: true);
                        await udp.SendAsync(packet, packet.Length, destination).ConfigureAwait(false);
                        _lastBroadcastUtc = now;
                        PublishStatus();
                    }
                    else
                    {
                        SetState(
                            SntpClockServiceState.Serving,
                            $"SNTP is listening on {binding.LocalAddress}:123, but this broadcast was suppressed because the Windows clock health check failed: {health.Detail}");
                    }
                }

                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                    break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (SocketException ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                    SetState(SntpClockServiceState.Faulted, $"SNTP broadcast failed: {ex.SocketErrorCode}.");
                break;
            }
        }
    }

    private void SetState(SntpClockServiceState state, string detail)
    {
        _state = state;
        _detail = detail;
        PublishStatus();
    }

    private void PublishStatus()
        => StatusChanged?.Invoke(Snapshot);

    private static TimeSpan NormalizeBroadcastInterval(TimeSpan interval)
        => interval < TimeSpan.FromSeconds(16) ? TimeSpan.FromSeconds(16) : interval;

    private sealed class SntpClockHealthMonitor
    {
        private readonly object _sync = new();
        private DateTimeOffset _baselineUtc;
        private long _baselineTimestamp;
        private DateTimeOffset _referenceUtc;

        public void Reset()
        {
            lock (_sync)
            {
                _baselineUtc = DateTimeOffset.UtcNow;
                _baselineTimestamp = Stopwatch.GetTimestamp();
                _referenceUtc = _baselineUtc;
            }
        }

        public ClockHealthSample Sample(DateTimeOffset? nowOverride = null)
        {
            lock (_sync)
            {
                var now = (nowOverride ?? DateTimeOffset.UtcNow).ToUniversalTime();
                if (_baselineTimestamp == 0)
                {
                    _baselineUtc = now;
                    _baselineTimestamp = Stopwatch.GetTimestamp();
                    _referenceUtc = now;
                }

                if (now.Year is < 2020 or > 2100)
                    return new ClockHealthSample(false, now, _referenceUtc, $"system UTC year {now.Year} is outside the commissioning safety window");

                var elapsed = Stopwatch.GetElapsedTime(_baselineTimestamp);
                var expected = _baselineUtc + elapsed;
                var jump = (now - expected).Duration();

                if (jump > TimeSpan.FromSeconds(2))
                {
                    // Reject one packet after a large wall-clock step, then re-baseline.
                    _baselineUtc = now;
                    _baselineTimestamp = Stopwatch.GetTimestamp();
                    _referenceUtc = now;
                    return new ClockHealthSample(false, now, _referenceUtc, $"system clock stepped by {jump.TotalMilliseconds:N0} ms");
                }

                return new ClockHealthSample(
                    true,
                    now,
                    _referenceUtc,
                    "Windows UTC is monotonic and sane; advertised as low-priority local reference (stratum 15), not GPS/PTP.");
            }
        }
    }

    private sealed record ClockHealthSample(
        bool IsHealthy,
        DateTimeOffset UtcNow,
        DateTimeOffset ReferenceUtc,
        string Detail);
}
