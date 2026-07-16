using System.Collections.Concurrent;
using System.Diagnostics;
using AR.Iec61850.Goose;
using AR.Iec61850.Monitoring;
using AR.Iec61850.Scl;
using AR.Iec61850.Transports;
using AR.Iec61850.Transports.Npcap;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed class GooseSubscriberFrameSnapshot
{
    public required string StreamKey { get; init; }
    public required DateTimeOffset CaptureTimestamp { get; init; }
    public required GooseFrame Frame { get; init; }
    public required ProcessBusStreamEvent StreamEvent { get; init; }
    public required long PacketCount { get; init; }
}

public sealed class GooseSubscriberStatusSnapshot
{
    public bool IsRunning { get; init; }
    public long CapturedFrames { get; init; }
    public long GooseFrames { get; init; }
    public long OtherFrames { get; init; }
    public int StreamCount { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Level { get; init; } = "INFO";
}

/// <summary>
/// Read-only raw-Ethernet GOOSE capture runtime. Decoding and sequence/TAL supervision are
/// delegated to the ARIEC61850 engine; this class only owns Npcap lifetime and UI-safe snapshots.
/// </summary>
public sealed class GooseSubscriberRuntime : IAsyncDisposable
{
    public const string DefaultCaptureFilter = "ether proto 0x88b8 or (vlan and ether proto 0x88b8)";

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, long> _streamPacketCounts = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _captureCancellation;
    private Task? _captureTask;
    private long _capturedFrames;
    private long _gooseFrames;
    private long _otherFrames;

    public event Action<GooseSubscriberFrameSnapshot>? FrameReceived;
    public event Action<GooseSubscriberStatusSnapshot>? StatusChanged;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _captureTask is { IsCompleted: false };
        }
    }

    public IReadOnlyList<GooseAdapterOption> ListAdapters()
        => NpcapAdapterCatalog.ListAdapters()
            .Select(adapter => new GooseAdapterOption
            {
                Index = adapter.Index,
                Name = adapter.Name,
                Description = adapter.Description,
                MacAddress = adapter.MacAddress?.ToString() ?? string.Empty
            })
            .ToArray();

    public Task StartAsync(
        string adapterSelector,
        SclDocument? sclDocument,
        string? captureFilter,
        CancellationToken applicationCancellation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterSelector);

        lock (_gate)
        {
            if (_captureTask is { IsCompleted: false })
                throw new InvalidOperationException("GOOSE subscriber is already running.");

            _capturedFrames = 0;
            _gooseFrames = 0;
            _otherFrames = 0;
            _streamPacketCounts.Clear();
            _captureCancellation?.Dispose();
            _captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(applicationCancellation);
            _captureTask = CaptureLoopAsync(
                adapterSelector,
                sclDocument,
                string.IsNullOrWhiteSpace(captureFilter) ? DefaultCaptureFilter : captureFilter.Trim(),
                _captureCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? captureTask;
        lock (_gate)
        {
            cancellation = _captureCancellation;
            captureTask = _captureTask;
        }

        cancellation?.Cancel();
        if (captureTask is null)
            return;

        try
        {
            var completed = await Task.WhenAny(captureTask, Task.Delay(TimeSpan.FromSeconds(4))).ConfigureAwait(false);
            if (completed == captureTask)
                await captureTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CaptureLoopAsync(
        string adapterSelector,
        SclDocument? sclDocument,
        string captureFilter,
        CancellationToken cancellationToken)
    {
        var monitor = sclDocument is null
            ? new ProcessBusStreamMonitor()
            : new ProcessBusStreamMonitor(sclDocument);
        var stopwatch = Stopwatch.StartNew();
        var nextStatus = TimeSpan.Zero;
        var finalMessage = "GOOSE subscriber stopped.";
        var finalLevel = "INFO";

        PublishStatus(true, "GOOSE subscriber started. Waiting for IEC 61850-8-1 frames…");

        try
        {
            using var source = new NpcapProcessBusFrameSource(adapterSelector);
            var options = new ProcessBusCaptureOptions
            {
                Filter = captureFilter,
                ReadTimeoutMilliseconds = 500,
                BufferCapacity = 8192
            };

            await foreach (var captured in source.CaptureAsync(options, cancellationToken).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _capturedFrames);
                var streamEvent = monitor.Observe(captured.Timestamp, captured.Frame);
                if (streamEvent.Kind != ProcessBusEventKind.Goose ||
                    !GooseFrameParser.TryParseEthernetFrame(captured.Frame, out var frame))
                {
                    Interlocked.Increment(ref _otherFrames);
                    continue;
                }

                Interlocked.Increment(ref _gooseFrames);
                var streamKey = BuildStreamKey(frame);
                var streamPacketCount = _streamPacketCounts.AddOrUpdate(streamKey, 1, static (_, current) => current + 1);
                FrameReceived?.Invoke(new GooseSubscriberFrameSnapshot
                {
                    StreamKey = streamKey,
                    CaptureTimestamp = captured.Timestamp,
                    Frame = frame,
                    StreamEvent = streamEvent,
                    PacketCount = streamPacketCount
                });

                if (stopwatch.Elapsed >= nextStatus)
                {
                    PublishStatus(true, $"Receiving GOOSE • {Interlocked.Read(ref _gooseFrames):N0} frame(s) • {_streamPacketCounts.Count:N0} stream(s)");
                    nextStatus = stopwatch.Elapsed.Add(TimeSpan.FromSeconds(1));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            finalMessage = "GOOSE subscriber stopped.";
        }
        catch (Exception ex)
        {
            finalMessage = $"GOOSE capture failed: {ex.Message}";
            finalLevel = "ERROR";
        }
        finally
        {
            var suffix = $"{Interlocked.Read(ref _gooseFrames):N0} GOOSE frame(s) • {_streamPacketCounts.Count:N0} stream(s)";
            PublishStatus(false, finalLevel == "ERROR" ? $"{finalMessage} • {suffix}" : $"GOOSE subscriber stopped • {suffix}", finalLevel);
        }
    }

    private void PublishStatus(bool running, string message, string level = "INFO")
        => StatusChanged?.Invoke(new GooseSubscriberStatusSnapshot
        {
            IsRunning = running,
            CapturedFrames = Interlocked.Read(ref _capturedFrames),
            GooseFrames = Interlocked.Read(ref _gooseFrames),
            OtherFrames = Interlocked.Read(ref _otherFrames),
            StreamCount = _streamPacketCounts.Count,
            Message = message,
            Level = level
        });

    private static string BuildStreamKey(GooseFrame frame)
        => string.Join("|",
            frame.AppId.ToString("X4", System.Globalization.CultureInfo.InvariantCulture),
            frame.Source.ToString(),
            frame.Destination.ToString(),
            frame.Vlan?.VlanId.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            frame.Pdu.GoCbRef,
            frame.Pdu.DataSetReference);

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _captureCancellation?.Dispose();
            _captureCancellation = null;
            _captureTask = null;
        }
    }
}
