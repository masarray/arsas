using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Services;

/// <summary>
/// Session-owner bridge for ARIEC61850 connected reconciliation.
/// The raw MMS session never leaves NativeIec61850Client; ARSAS callers receive only
/// the engine reconciliation document and never construct probes or classify MMS failures.
/// </summary>
public sealed partial class NativeIec61850Client
{
    private static readonly object ReconciliationOwnerRegistryGate = new();
    private static readonly List<WeakReference<NativeIec61850Client>> ReconciliationOwners = new();
    private static long _nextReconciliationOwnerSequence;
    private readonly long _reconciliationOwnerSequence;

    public NativeIec61850Client()
    {
        _reconciliationOwnerSequence = Interlocked.Increment(ref _nextReconciliationOwnerSequence);
        lock (ReconciliationOwnerRegistryGate)
        {
            PruneReconciliationOwnersLocked();
            ReconciliationOwners.Add(new WeakReference<NativeIec61850Client>(this));
        }
    }

    /// <summary>
    /// Runs the engine-owned connected reconciliation pipeline against this client's
    /// already-owned MMS association. No second association is created.
    /// </summary>
    public Task<Iec61850DesignLiveReconciliationDocument> ReconcileDesignLiveAsync(
        LiveIedModelDiscoveryDocument designModel,
        LiveIedModelDiscoveryDocument liveModel,
        Iec61850DesignLiveReconciliationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(designModel);
        ArgumentNullException.ThrowIfNull(liveModel);

        var service = new Iec61850ConnectedReconciliationService(_session);

        // When the association is active, serialize reconciliation reads with the other
        // native MMS work owned by this client. When it is already down, call the engine
        // facade directly so ARIEC can return TransportFailure rather than an ARSAS-side
        // lifecycle guess or an ObjectDisposedException from the client's I/O gate.
        return _session.IsMmsInitiated
            ? RunMmsOperationAsync(
                () => service.ReconcileAsync(designModel, liveModel, options, cancellationToken),
                cancellationToken)
            : service.ReconcileAsync(designModel, liveModel, options, cancellationToken);
    }

    /// <summary>
    /// Resolves the NativeIec61850Client that already owns the requested endpoint and
    /// delegates to its ARIEC connected facade. Multiple simultaneously active owners are
    /// rejected instead of guessing which association belongs to the FAT workflow.
    /// </summary>
    public static Task<Iec61850DesignLiveReconciliationDocument> ReconcileConnectedAsync(
        string ipAddress,
        int port,
        LiveIedModelDiscoveryDocument designModel,
        LiveIedModelDiscoveryDocument liveModel,
        Iec61850DesignLiveReconciliationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);
        ArgumentNullException.ThrowIfNull(designModel);
        ArgumentNullException.ThrowIfNull(liveModel);

        var owner = ResolveReconciliationOwner(ipAddress, port);
        return owner.ReconcileDesignLiveAsync(
            designModel,
            liveModel,
            options,
            cancellationToken);
    }

    private static NativeIec61850Client ResolveReconciliationOwner(string ipAddress, int port)
    {
        var host = ipAddress.Trim();
        var normalizedPort = port <= 0 ? 102 : port;

        lock (ReconciliationOwnerRegistryGate)
        {
            PruneReconciliationOwnersLocked();
            var matches = ReconciliationOwners
                .Select(reference => reference.TryGetTarget(out var client) ? client : null)
                .Where(client => client != null &&
                                 client._host.Equals(host, StringComparison.OrdinalIgnoreCase) &&
                                 client._port == normalizedPort)
                .Cast<NativeIec61850Client>()
                .OrderByDescending(client => client._reconciliationOwnerSequence)
                .ToList();

            var active = matches.Where(client => client.IsConnected).ToList();
            if (active.Count == 1)
                return active[0];

            if (active.Count > 1)
            {
                throw new InvalidOperationException(
                    $"More than one active native MMS association owns {host}:{normalizedPort}; connected reconciliation was withheld rather than guessing the FAT session.");
            }

            if (matches.Count > 0)
            {
                // Preserve the newest session owner after a disconnect so the ARIEC
                // connected facade can classify the session state as TransportFailure.
                return matches[0];
            }
        }

        throw new InvalidOperationException(
            $"No native MMS session owner is registered for {host}:{normalizedPort}; connected reconciliation cannot run until the IED session exists.");
    }

    private static void PruneReconciliationOwnersLocked()
    {
        for (var index = ReconciliationOwners.Count - 1; index >= 0; index--)
        {
            if (!ReconciliationOwners[index].TryGetTarget(out _))
                ReconciliationOwners.RemoveAt(index);
        }
    }
}
