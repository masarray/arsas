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
    private CancellationTokenSource? _associationLifetimeCancellation;
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
            await ResetAssociationCoreAsync().ConfigureAwait(false);
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

        if (_session.IsTransportConnected ||
            _session.IsMmsInitiated ||
            _session.IsReceivePumpRunning ||
            _associationLifetimeCancellation is not null)
        {
            await ResetAssociationCoreAsync().ConfigureAwait(false);
        }

        // The receive pump must be owned by the MMS association, not by a single Scan/Download
        // UI operation. Stopping and restarting an active receive on the same COTP association
        // can invalidate some real IED transports. Instead, create the association-owned token
        // before ConnectAsync so the pump is born with the correct lifetime and never needs a
        // post-connect rebind. The caller token is linked only for the connect handshake itself.
        var associationLifetime = new CancellationTokenSource();
        _associationLifetimeCancellation = associationLifetime;
        using var connectCancellation = cancellationToken.Register(
            static state => ((CancellationTokenSource)state!).Cancel(),
            associationLifetime);

        try
        {
            await _session.ConnectAsync(
                normalizedHost,
                normalizedPort,
                TimeSpan.FromSeconds(8),
                associationLifetime.Token).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (!IsSessionHealthy())
            {
                throw new InvalidOperationException(
                    $"The dedicated fault-record association is not operational after connect. {ConnectionState}.");
            }

            _host = normalizedHost;
            _port = normalizedPort;
            _service = new Iec61850FaultRecordService(_session);
        }
        catch
        {
            await ResetAssociationCoreAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task ResetAssociationCoreAsync()
    {
        _service = null;

        var lifetime = _associationLifetimeCancellation;
        _associationLifetimeCancellation = null;
        if (lifetime is not null)
        {
            try
            {
                if (!lifetime.IsCancellationRequested)
                    lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Defensive only. This client is the single owner, so a disposed lifetime should
                // never normally be observed here.
            }
        }

        try
        {
            await _session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            lifetime?.Dispose();
        }
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