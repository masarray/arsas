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

public enum SntpClockTransportMode
{
    None,
    UdpSocket,
    NpcapRaw
}

public sealed record SntpClientObservation(
    IPAddress Address,
    DateTimeOffset LastRequestUtc,
    int RequestCount,
    byte Version);

public sealed record SntpReplyObservation(
    IPAddress Address,
    DateTimeOffset SentUtc,
    long ReplyCount,
    byte Version,
    SntpClockTransportMode TransportMode);

public sealed record SntpClockServiceSnapshot(
    SntpClockServiceState State,
    string Detail,
    SntpNetworkBinding? Binding,
    DateTimeOffset? LastBroadcastUtc,
    int ObservedClientCount,
    bool ClockHealthy,
    SntpClockTransportMode TransportMode,
    long BroadcastCount,
    long ClientRequestCount,
    long ReplyCount,
    DateTimeOffset? LastRequestUtc,
    DateTimeOffset? LastReplyUtc);

/// <summary>
/// Lightweight SNTPv4 commissioning clock for ARSAS.
///
/// Preferred path is a normal UDP/123 socket bound to the station-bus interface. When
/// Windows Time or another service already owns UDP/123, ARSAS falls back to a raw Npcap
/// Ethernet transport on the same adapter. ARSAS never stops or reconfigures W32Time.
///
/// Evidence is deliberately split into broadcast sent, client request observed and
/// Mode-4 reply sent. None of those signals alone is presented as proof that the IED has
/// synchronized its internal clock.
/// </summary>
public sealed class SntpClockService : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly ConcurrentDictionary<string, SntpClientObservation> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly SntpClockHealthMonitor _clockHealth = new();
    private readonly SntpServerProfile _profile;
    private readonly TimeSpan _broadcastInterval;
    private UdpClient? _udp;
    private SntpRawNpcapTransport? _rawTransport;
    private CancellationTokenSource? _serviceCancellation;
    private Task? _receiveTask;
    private Task? _broadcastTask;
    private SntpNetworkBinding? _binding;
    private DateTimeOffset? _lastBroadcastUtc;
    private DateTimeOffset? _lastRequestUtc;
    private DateTimeOffset? _lastReplyUtc;
    private SntpClockServiceState _state = SntpClockServiceState.Stopped;
    private SntpClockTransportMode _transportMode = SntpClockTransportMode.None;
    private string _detail = "SNTP clock service is stopped.";
    private int _broadcastPulseRequested;
    private long _broadcastCount;
    private long _clientRequestCount;
    private long _replyCount;

    public SntpClockService(
        SntpServerProfile? profile = null,
        TimeSpan? broadcastInterval = null)
    {
        _profile = profile ?? new SntpServerProfile();
        _broadcastInterval = NormalizeBroadcastInterval(broadcastInterval ?? TimeSpan.FromSeconds(64));
    }

    public event Action<SntpClockServiceSnapshot>? StatusChanged;
    public event Action<SntpClientObservation>? ClientRequestObserved;
    public event Action<SntpReplyObservation>? ReplySent;

    public SntpClockServiceSnapshot Snapshot
        => new(
            _state,
            _detail,
            _binding,
            _lastBroadcastUtc,
            _clients.Count,
            _clockHealth.Sample().IsHealthy,
            _transportMode,
            Interlocked.Read(ref _broadcastCount),
            Interlocked.Read(ref _clientRequestCount),
            Interlocked.Read(ref _replyCount),
            _lastRequestUtc,
            _lastReplyUtc);

    public IReadOnlyCollection<SntpClientObservation> ObservedClients
        => _clients.Values.OrderBy(item => item.Address.ToString(), StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task EnsureStartedAsync(IPAddress iedAddress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(iedAddress);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var requestedBinding = SntpNetworkRouteResolver.ResolveForRemote(iedAddress);
            if ((_udp != null || _rawTransport != null) && _binding != null)
            {
                if (_binding.LocalAddress.Equals(requestedBinding.LocalAddress))
                {
                    RequestImmediateBroadcast();
                    return;
                }

                SetState(
                    SntpClockServiceState.Serving,
                    $"SNTP remains bound to {_binding.Summary}. IED {iedAddress} routes through {requestedBinding.LocalAddress}; one station-bus clock transport is active at a time.");
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

            if (_rawTransport != null)
            {
                try { await _rawTransport.DisposeAsync().ConfigureAwait(false); } catch { }
                _rawTransport = null;
            }

            cancellation?.Dispose();
            _binding = null;
            _lastBroadcastUtc = null;
            _lastRequestUtc = null;
            _lastReplyUtc = null;
            _transportMode = SntpClockTransportMode.None;
            ResetEvidenceCounters();
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

    private async Task StartCoreAsync(SntpNetworkBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetState(SntpClockServiceState.Starting, $"Preparing SNTP on {binding.Summary}.");

        ResetEvidenceCounters();
        _clients.Clear();
        _clockHealth.Reset();
        _binding = binding;

        var serviceCancellation = new CancellationTokenSource();
        _serviceCancellation = serviceCancellation;

        var udp = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            // Prefer the ordinary Windows socket path when it is actually available.
            // Never co-bind or stop W32Time: a bind conflict falls through to Npcap RAW.
            udp.Client.ExclusiveAddressUse = true;
            udp.EnableBroadcast = true;
            udp.Client.Bind(new IPEndPoint(binding.LocalAddress, 123));

            _udp = udp;
            _transportMode = SntpClockTransportMode.UdpSocket;
            SetState(
                SntpClockServiceState.Serving,
                binding.DirectedBroadcast == null
                    ? $"SNTP UDP server active on {binding.LocalAddress}:123 with SIPROTEC compatibility stratum {_profile.Stratum}. No usable directed broadcast is available."
                    : $"SNTP UDP server active on {binding.LocalAddress}:123 with SIPROTEC compatibility stratum {_profile.Stratum}; Mode 5 broadcast targets {binding.DirectedBroadcast}:123.");

            _receiveTask = ReceiveLoopAsync(udp, serviceCancellation.Token);
            _broadcastTask = BroadcastLoopAsync(binding, serviceCancellation.Token);
            RequestImmediateBroadcast();
            return;
        }
        catch (SocketException ex)
        {
            udp.Dispose();
            _udp = null;
            await StartRawFallbackAsync(binding, ex.SocketErrorCode.ToString(), serviceCancellation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            udp.Dispose();
            _udp = null;
            await StartRawFallbackAsync(binding, ex.Message, serviceCancellation, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StartRawFallbackAsync(
        SntpNetworkBinding binding,
        string udpFailure,
        CancellationTokenSource serviceCancellation,
        CancellationToken startCancellation)
    {
        startCancellation.ThrowIfCancellationRequested();
        try
        {
            var raw = new SntpRawNpcapTransport(binding);
            await raw.StartAsync(HandleRawClientRequestAsync, serviceCancellation.Token).ConfigureAwait(false);
            _rawTransport = raw;
            _transportMode = SntpClockTransportMode.NpcapRaw;

            SetState(
                SntpClockServiceState.Serving,
                binding.DirectedBroadcast == null
                    ? $"UDP/123 unavailable ({udpFailure}); Npcap RAW SNTP fallback active on {binding.InterfaceName} / {binding.LocalAddress}. Windows Time was left unchanged."
                    : $"UDP/123 unavailable ({udpFailure}); Npcap RAW SNTP fallback active on {binding.InterfaceName} / {binding.LocalAddress}. Mode 5 broadcast targets {binding.DirectedBroadcast}:123. Windows Time was left unchanged.");

            _broadcastTask = BroadcastLoopAsync(binding, serviceCancellation.Token);
            RequestImmediateBroadcast();
        }
        catch (Exception rawException)
        {
            try { serviceCancellation.Cancel(); } catch { }
            serviceCancellation.Dispose();
            if (ReferenceEquals(_serviceCancellation, serviceCancellation))
                _serviceCancellation = null;
            _binding = null;
            _transportMode = SntpClockTransportMode.None;
            SetState(
                SntpClockServiceState.PortUnavailable,
                $"UDP/123 unavailable ({udpFailure}) and Npcap RAW fallback could not start: {rawException.Message}. IEC 61850 remains unaffected.");
        }
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

            RecordClientRequest(received.RemoteEndPoint.Address, receiveUtc, request.Version);
            var reply = BuildReplyOrNull(received.Buffer, receiveUtc, out var transmitUtc);
            if (reply == null)
                continue;

            try
            {
                await udp.SendAsync(reply, reply.Length, received.RemoteEndPoint).ConfigureAwait(false);
                RecordReply(received.RemoteEndPoint.Address, transmitUtc, request.Version);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (SocketException ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                    SetState(SntpClockServiceState.Faulted, $"SNTP reply to {received.RemoteEndPoint.Address} failed: {ex.SocketErrorCode}.");
            }
        }
    }

    private async Task HandleRawClientRequestAsync(
        SntpRawClientFrame rawRequest,
        DateTimeOffset receiveUtc,
        CancellationToken cancellationToken)
    {
        if (!SntpPacket.TryReadClientRequest(rawRequest.Payload, out var request))
            return;

        RecordClientRequest(rawRequest.SourceAddress, receiveUtc, request.Version);
        var reply = BuildReplyOrNull(rawRequest.Payload, receiveUtc, out var transmitUtc);
        if (reply == null)
            return;

        var raw = _rawTransport;
        if (raw == null)
            return;

        try
        {
            await raw.SendReplyAsync(rawRequest, reply, cancellationToken).ConfigureAwait(false);
            RecordReply(rawRequest.SourceAddress, transmitUtc, request.Version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetState(SntpClockServiceState.Faulted, $"RAW SNTP reply to {rawRequest.SourceAddress} failed: {ex.Message}");
        }
    }

    private byte[]? BuildReplyOrNull(ReadOnlySpan<byte> requestPacket, DateTimeOffset receiveUtc, out DateTimeOffset transmitUtc)
    {
        var health = _clockHealth.Sample(receiveUtc);
        transmitUtc = DateTimeOffset.UtcNow;
        try
        {
            return SntpPacket.BuildServerReply(
                requestPacket,
                receiveUtc,
                transmitUtc,
                _profile with { ReferenceUtc = health.ReferenceUtc },
                health.IsHealthy);
        }
        catch (ArgumentOutOfRangeException)
        {
            SetState(
                SntpClockServiceState.Serving,
                "SNTP request was ignored because the Windows UTC value is outside the NTP timestamp range.");
            return null;
        }
    }

    private async Task BroadcastLoopAsync(SntpNetworkBinding binding, CancellationToken cancellationToken)
    {
        if (binding.DirectedBroadcast == null)
            return;

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

                        await SendBroadcastPacketAsync(binding.DirectedBroadcast, packet, cancellationToken).ConfigureAwait(false);
                        _lastBroadcastUtc = now;
                        Interlocked.Increment(ref _broadcastCount);
                        PublishStatus();
                    }
                    else
                    {
                        SetState(
                            SntpClockServiceState.Serving,
                            $"SNTP broadcast suppressed because the Windows clock health check failed: {health.Detail}");
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
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                    SetState(SntpClockServiceState.Faulted, $"SNTP broadcast failed: {ex.Message}");
                break;
            }
        }
    }

    private async Task SendBroadcastPacketAsync(
        IPAddress directedBroadcast,
        byte[] packet,
        CancellationToken cancellationToken)
    {
        if (_transportMode == SntpClockTransportMode.UdpSocket && _udp != null)
        {
            var destination = new IPEndPoint(directedBroadcast, 123);
            await _udp.SendAsync(packet, packet.Length, destination).ConfigureAwait(false);
            return;
        }

        if (_transportMode == SntpClockTransportMode.NpcapRaw && _rawTransport != null)
        {
            await _rawTransport.SendBroadcastAsync(directedBroadcast, packet, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("No active SNTP transport is available for broadcast.");
    }

    private void RecordClientRequest(IPAddress address, DateTimeOffset requestUtc, byte version)
    {
        _lastRequestUtc = requestUtc;
        Interlocked.Increment(ref _clientRequestCount);
        var key = address.ToString();
        var observation = _clients.AddOrUpdate(
            key,
            _ => new SntpClientObservation(address, requestUtc, 1, version),
            (_, previous) => previous with
            {
                LastRequestUtc = requestUtc,
                RequestCount = previous.RequestCount + 1,
                Version = version
            });
        ClientRequestObserved?.Invoke(observation);
        PublishStatus();
    }

    private void RecordReply(IPAddress address, DateTimeOffset sentUtc, byte version)
    {
        _lastReplyUtc = sentUtc;
        var replyCount = Interlocked.Increment(ref _replyCount);
        ReplySent?.Invoke(new SntpReplyObservation(address, sentUtc, replyCount, version, _transportMode));
        PublishStatus();
    }

    private void ResetEvidenceCounters()
    {
        Interlocked.Exchange(ref _broadcastCount, 0);
        Interlocked.Exchange(ref _clientRequestCount, 0);
        Interlocked.Exchange(ref _replyCount, 0);
        Interlocked.Exchange(ref _broadcastPulseRequested, 0);
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
        => interval < TimeSpan.FromSeconds(64) ? TimeSpan.FromSeconds(64) : interval;

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
                    _baselineUtc = now;
                    _baselineTimestamp = Stopwatch.GetTimestamp();
                    _referenceUtc = now;
                    return new ClockHealthSample(false, now, _referenceUtc, $"system clock stepped by {jump.TotalMilliseconds:N0} ms");
                }

                return new ClockHealthSample(
                    true,
                    now,
                    _referenceUtc,
                    "Windows UTC is monotonic and sane; synchronized SNTP is advertised as a local commissioning source, not as GPS/PTP traceability.");
            }
        }
    }

    private sealed record ClockHealthSample(
        bool IsHealthy,
        DateTimeOffset UtcNow,
        DateTimeOffset ReferenceUtc,
        string Detail);
}
