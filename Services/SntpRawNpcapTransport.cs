using System.Net;
using System.Net.NetworkInformation;
using AR.Iec61850.Transports;
using AR.Iec61850.Transports.Npcap;

namespace ArIED61850Tester.Services;

/// <summary>
/// Raw Ethernet SNTP fallback for Windows hosts where UDP/123 is already owned by W32Time
/// or another service. It never changes the Windows Time service and never claims that a
/// transmitted packet proves the IED synchronized its clock.
/// </summary>
public sealed class SntpRawNpcapTransport : IAsyncDisposable
{
    private readonly SntpNetworkBinding _binding;
    private readonly byte[] _localMac;
    private readonly string _adapterSelector;
    private NpcapProcessBusDuplexTransport? _transport;
    private CancellationTokenSource? _cancellation;
    private Task? _captureTask;
    private Func<SntpRawClientFrame, DateTimeOffset, CancellationToken, Task>? _requestHandler;
    private int _identification;

    public SntpRawNpcapTransport(SntpNetworkBinding binding)
    {
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        (_adapterSelector, _localMac) = ResolveNpcapAdapter(binding);
    }

    public event Action<Exception>? Faulted;
    public string AdapterSelector => _adapterSelector;

    public Task StartAsync(
        Func<SntpRawClientFrame, DateTimeOffset, CancellationToken, Task> requestHandler,
        CancellationToken applicationCancellation = default)
    {
        ArgumentNullException.ThrowIfNull(requestHandler);
        if (_transport != null)
            throw new InvalidOperationException("Raw SNTP Npcap transport is already running.");

        _requestHandler = requestHandler;
        _transport = new NpcapProcessBusDuplexTransport(_adapterSelector);
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(applicationCancellation);
        _captureTask = CaptureLoopAsync(_transport, _cancellation.Token);
        return Task.CompletedTask;
    }

    public async Task SendReplyAsync(
        SntpRawClientFrame request,
        ReadOnlyMemory<byte> sntpPayload,
        CancellationToken cancellationToken = default)
    {
        var transport = _transport ?? throw new InvalidOperationException("Raw SNTP transport is not running.");
        var frame = SntpEthernetFrameCodec.BuildServerReply(
            request,
            _localMac,
            _binding.LocalAddress,
            sntpPayload.Span,
            NextIdentification());
        await transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendBroadcastAsync(
        IPAddress directedBroadcast,
        ReadOnlyMemory<byte> sntpPayload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directedBroadcast);
        var transport = _transport ?? throw new InvalidOperationException("Raw SNTP transport is not running.");
        var frame = SntpEthernetFrameCodec.BuildBroadcast(
            _localMac,
            _binding.LocalAddress,
            directedBroadcast,
            sntpPayload.Span,
            NextIdentification());
        await transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private async Task CaptureLoopAsync(
        NpcapProcessBusDuplexTransport transport,
        CancellationToken cancellationToken)
    {
        var options = new ProcessBusCaptureOptions
        {
            Filter = "udp dst port 123",
            ReadTimeoutMilliseconds = 250,
            BufferCapacity = 1024
        };

        try
        {
            await foreach (var captured in transport.CaptureAsync(options, cancellationToken).ConfigureAwait(false))
            {
                if (!SntpEthernetFrameCodec.TryParseClientRequest(captured.Frame, _binding.LocalAddress, out var request))
                    continue;

                var handler = _requestHandler;
                if (handler != null)
                    await handler(request, captured.Timestamp.ToUniversalTime(), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex);
        }
    }

    private ushort NextIdentification()
        => unchecked((ushort)Interlocked.Increment(ref _identification));

    private static (string Selector, byte[] LocalMac) ResolveNpcapAdapter(SntpNetworkBinding binding)
    {
        var windowsAdapter = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(adapter => adapter.Id.Equals(binding.InterfaceId, StringComparison.OrdinalIgnoreCase));
        if (windowsAdapter == null)
            throw new InvalidOperationException($"Windows network adapter '{binding.InterfaceName}' ({binding.InterfaceId}) is no longer available.");

        var localMac = windowsAdapter.GetPhysicalAddress().GetAddressBytes();
        if (localMac.Length != 6)
            throw new InvalidOperationException($"Station-bus adapter '{binding.InterfaceName}' does not expose a six-byte Ethernet MAC address.");

        var adapters = NpcapAdapterCatalog.ListAdapters();
        if (adapters.Count == 0)
            throw new InvalidOperationException("Npcap is not installed or no capture adapters are available.");

        var normalizedId = Normalize(binding.InterfaceId);
        if (!string.IsNullOrEmpty(normalizedId))
        {
            var byId = adapters.FirstOrDefault(adapter =>
                Normalize(adapter.Name).Contains(normalizedId, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return (byId.Name, localMac);
        }

        var normalizedMac = Convert.ToHexString(localMac);
        var byMac = adapters
            .Where(adapter => Normalize(adapter.MacAddress?.ToString()).Equals(normalizedMac, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (byMac.Length == 1)
            return (byMac[0].Name, localMac);

        throw new InvalidOperationException(
            $"Npcap could not map the Windows station-bus adapter '{binding.InterfaceName}' ({binding.LocalAddress}) to a capture device.");
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var character in value)
        {
            if (char.IsAsciiHexDigit(character))
                buffer[length++] = char.ToUpperInvariant(character);
        }

        return new string(buffer[..length]);
    }

    public async ValueTask DisposeAsync()
    {
        var cancellation = _cancellation;
        _cancellation = null;
        if (cancellation != null)
        {
            try { cancellation.Cancel(); } catch { }
        }

        var captureTask = _captureTask;
        _captureTask = null;
        if (captureTask != null)
        {
            try { await captureTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        try { _transport?.Dispose(); } catch { }
        _transport = null;
        _requestHandler = null;
        cancellation?.Dispose();
    }
}
