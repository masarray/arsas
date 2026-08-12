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

    public bool IsConnected => IsSessionHealthy();
    public string ConnectionState =>
        $"{_session.State}; transport={_session.IsTransportConnected}; pump={_session.IsReceivePumpRunning}";

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
            await ConnectCoreAsync(normalizedHost, normalizedPort, cancellationToken).ConfigureAwait(false);
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
            var first = await DownloadCoreAsync(
                record,
                destinationRoot,
                progress,
                cancellationToken).ConfigureAwait(false);

            if (first.IsSuccess)
                return first;

            if (FaultRecordRemotePathFallbackPolicy.ShouldTryCompatibilityPaths(first) && IsSessionHealthy())
            {
                var attempted = new List<string> { "discovered path" };
                var last = first;
                foreach (var candidate in FaultRecordRemotePathFallbackPolicy.BuildCandidates(record))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    attempted.Add(candidate.Label);
                    last = await DownloadCoreAsync(
                        candidate.Record,
                        destinationRoot,
                        progress,
                        cancellationToken).ConfigureAwait(false);
                    if (last.IsSuccess)
                    {
                        return CloneResult(
                            last,
                            $"{last.Message} SIPROTEC compatibility path succeeded via {candidate.Label}; " +
                            $"the discovered lowercase/segmented path returned MMS file-non-existent.");
                    }

                    if (!FaultRecordRemotePathFallbackPolicy.ShouldTryCompatibilityPaths(last) || !IsSessionHealthy())
                        break;
                }

                first = CloneResult(
                    last,
                    $"SIPROTEC FileOpen compatibility paths were exhausted ({string.Join("; ", attempted)}). " +
                    BuildFailureMessage(last));
            }

            // A transport or receive-pump fault invalidates the MMS association. The
            // downloader cleans its temporary directory, so one complete reconnect and
            // bounded retry is safe and avoids turning a transient connection loss into
            // an immediate user-visible failure.
            if (!IsSessionHealthy() &&
                !cancellationToken.IsCancellationRequested &&
                !string.IsNullOrWhiteSpace(_host))
            {
                var firstFailure = first.Message;
                await ConnectCoreAsync(_host, _port, cancellationToken).ConfigureAwait(false);
                var recovered = await DownloadCoreAsync(
                    record,
                    destinationRoot,
                    progress,
                    cancellationToken).ConfigureAwait(false);

                if (recovered.IsSuccess)
                {
                    return CloneResult(
                        recovered,
                        $"{recovered.Message} Automatic reconnect recovered the interrupted MMS file-transfer session.");
                }

                return CloneResult(
                    recovered,
                    $"Initial transfer failed and the dedicated session became unhealthy. " +
                    $"Automatic reconnect/retry also failed. First failure: {firstFailure}\n\n" +
                    $"Retry failure: {BuildFailureMessage(recovered)}");
            }

            return CloneResult(first, BuildFailureMessage(first));
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

    private async Task ConnectCoreAsync(
        string normalizedHost,
        int normalizedPort,
        CancellationToken cancellationToken)
    {
        var sameEndpoint =
            _host.Equals(normalizedHost, StringComparison.OrdinalIgnoreCase) &&
            _port == normalizedPort;
        if (sameEndpoint && IsSessionHealthy())
            return;

        // The connection operation token must not own the lifetime of a reusable MMS
        // receive pump. A completed scan token is replaced before download; without this
        // rebind the old cancellation would stop confirmed-service response routing while
        // the association still appeared to be MmsInitiated.
        if (_session.IsTransportConnected ||
            _session.IsMmsInitiated ||
            _session.IsReceivePumpRunning)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
        }

        _service = null;
        await _session.ConnectAsync(
            normalizedHost,
            normalizedPort,
            TimeSpan.FromSeconds(8),
            cancellationToken).ConfigureAwait(false);
        await _session.RebindReceivePumpToSessionLifetimeAsync(cancellationToken).ConfigureAwait(false);

        if (!IsSessionHealthy())
        {
            throw new InvalidOperationException(
                $"The dedicated fault-record association is not operational after connect. {ConnectionState}.");
        }

        _host = normalizedHost;
        _port = normalizedPort;
        _service = new Iec61850FaultRecordService(_session);
    }

    private async Task<Iec61850FaultRecordDownloadResult> DownloadCoreAsync(
        Iec61850FaultRecordSet record,
        string destinationRoot,
        IProgress<Iec61850FaultRecordDownloadProgress>? progress,
        CancellationToken cancellationToken)
        => await Iec61850FaultRecordInteroperableDownloader.DownloadAsync(
            _session,
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

    private string BuildFailureMessage(Iec61850FaultRecordDownloadResult result)
        => $"{result.Message} Dedicated session: {ConnectionState}. " +
           $"Receive routing: {ValueOrDash(_session.LastReceiveRoutingSummary)}";

    private static Iec61850FaultRecordDownloadResult CloneResult(
        Iec61850FaultRecordDownloadResult source,
        string message)
        => new()
        {
            IsSuccess = source.IsSuccess,
            RecordId = source.RecordId,
            DestinationDirectory = source.DestinationDirectory,
            Files = source.Files,
            BytesTransferred = source.BytesTransferred,
            Message = message
        };

    private bool IsSessionHealthy()
        => _session.IsMmsInitiated &&
           _session.IsTransportConnected &&
           _session.IsReceivePumpRunning;

    private void EnsureReady()
    {
        if (!IsSessionHealthy() || _service == null)
        {
            throw new InvalidOperationException(
                $"The dedicated fault-record association is not ready. {ConnectionState}.");
        }
    }

    private static string ValueOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
