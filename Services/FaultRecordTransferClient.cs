using AR.Iec61850.FaultRecords;
using AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// Owns a short-lived IEC 61850 association dedicated to remote fault-record browsing and download.
/// Keeping this session separate prevents large file transfers from blocking live monitoring traffic.
/// </summary>
public sealed class FaultRecordTransferClient : IAsyncDisposable
{
    private readonly MmsClientSession _session = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private Iec61850FaultRecordService? _service;
    private string _host = string.Empty;
    private int _port = 102;

    public bool IsConnected => _session.IsMmsInitiated;
    public string ConnectionState => _session.State.ToString();

    public async Task ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var normalizedHost = host.Trim();
        var normalizedPort = port <= 0 ? 102 : port;

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session.IsMmsInitiated &&
                _host.Equals(normalizedHost, StringComparison.OrdinalIgnoreCase) &&
                _port == normalizedPort)
            {
                return;
            }

            if (_session.IsTransportConnected)
                await _session.DisposeAsync().ConfigureAwait(false);

            await _session.ConnectAsync(
                normalizedHost,
                normalizedPort,
                TimeSpan.FromSeconds(8),
                cancellationToken).ConfigureAwait(false);

            _host = normalizedHost;
            _port = normalizedPort;
            _service = new Iec61850FaultRecordService(_session);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<Iec61850FaultRecordCatalog> DiscoverAsync(
        string? remoteDirectory,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady();
            return await _service!.DiscoverAsync(
                remoteDirectory,
                new Iec61850FaultRecordDiscoveryOptions
                {
                    TraverseSubdirectories = true,
                    MaximumDirectoryDepth = 4,
                    MaximumDirectoryCount = 128,
                    MaximumEntries = 20_000,
                    MaximumPagesPerDirectory = 32
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<Iec61850FaultRecordDownloadResult> DownloadAsync(
        Iec61850FaultRecordSet record,
        string destinationRoot,
        IProgress<Iec61850FaultRecordDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady();
            return await _service!.DownloadAsync(
                record,
                destinationRoot,
                new Iec61850FaultRecordDownloadOptions
                {
                    MaximumTotalBytes = 1024L * 1024L * 1024L,
                    MaximumFileBytes = 512L * 1024L * 1024L,
                    MaximumReadOperationsPerFile = 100_000,
                    // Completeness describes COMTRADE companion coverage; it must not block
                    // MMS FileOpen/FileRead of files that the IED actually exposes.
                    RequireCompleteRecord = false,
                    RequireDeclaredSizeMatch = false
                },
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _service = null;
            await _session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }

    private void EnsureReady()
    {
        if (!_session.IsMmsInitiated || _service == null)
        {
            throw new InvalidOperationException(
                $"The dedicated fault-record association is not ready. Current MMS state: {_session.State}.");
        }
    }
}
