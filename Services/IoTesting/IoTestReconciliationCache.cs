using System.Collections.Concurrent;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Owns the ARSAS-side lifecycle of engine reconciliation documents.
///
/// Reconciliation production is asynchronous and cancellable; synchronous FAT/UI binding
/// only reads the latest document for the exact design/live model object pair. Production
/// refreshes delegate to the native session owner, which in turn calls the ARIEC connected
/// facade. FAT/UI code never owns an MMS session, an exact-read probe, or protocol failure
/// classification.
/// </summary>
public static class IoTestReconciliationCache
{
    private static readonly ConcurrentDictionary<Iec61850MonitorDevice, CacheEntry> Entries = new();

    /// <summary>
    /// Production refresh: when a native session owner exists for this endpoint, use its
    /// already-active association and let ARIEC61850 own exact reads, alternate strategies,
    /// probe budgets, and failure verdicts. A workspace that has never created a session may
    /// still build a model-only document; that path can only remain DesignOnly, never Absent.
    ///
    /// Direct SCL FAT is intentionally different. Its complete static DataSet scope is already
    /// authoritative before association and local signal selection does not require design/live
    /// reconciliation. Even model-only reconciliation can be CPU-heavy enough on a large saved
    /// model to hold the operator before StartMonitoringAsync. Therefore direct SCL FAT has no
    /// reconciliation gate at all: invalidate any stale cache and return immediately. Live
    /// acquisition starts from the SCL authority; Engineering/diagnostic reconciliation remains
    /// an explicit, non-critical workflow and is never allowed to delay physical FAT readiness.
    /// </summary>
    public static Task RefreshAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (UsesDirectSclFatAuthority(device))
        {
            Entries.TryRemove(device, out _);
            return Task.CompletedTask;
        }

        if (!NativeIec61850Client.HasReconciliationOwner(device.IpAddress, device.Port))
            return RefreshModelOnlyAsync(device, cancellationToken);

        return RefreshAsync(
            device,
            (design, live, token) => NativeIec61850Client.ReconcileConnectedAsync(
                device.IpAddress,
                device.Port,
                design,
                live,
                options: null,
                cancellationToken: token),
            cancellationToken);
    }

    /// <summary>
    /// Explicit model-only refresh for deterministic tests and offline model inspection.
    /// It never produces protocol absence because no exact read probe is supplied.
    /// </summary>
    public static Task RefreshModelOnlyAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken = default)
        => RefreshAsync(
            device,
            static (design, live, token) => Iec61850DesignLiveReconciler.ReconcileAsync(
                design,
                live,
                probe: null,
                cancellationToken: token),
            cancellationToken);

    public static async Task RefreshAsync(
        Iec61850MonitorDevice device,
        Func<LiveIedModelDiscoveryDocument,
            LiveIedModelDiscoveryDocument,
            CancellationToken,
            Task<Iec61850DesignLiveReconciliationDocument>> producer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(producer);

        var designModel = device.SclWorkspace?.DesignModel;
        var liveModel = device.LiveDiscoveryModel;
        if (designModel == null || liveModel == null)
        {
            Entries.TryRemove(device, out _);
            return;
        }

        try
        {
            var document = await producer(designModel, liveModel, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // Do not publish a document for a model generation that changed while the
            // reconciliation task was running. The next refresh will reconcile the new pair.
            if (!ReferenceEquals(device.SclWorkspace?.DesignModel, designModel) ||
                !ReferenceEquals(device.LiveDiscoveryModel, liveModel))
            {
                return;
            }

            Entries[device] = new CacheEntry(
                designModel,
                liveModel,
                document,
                string.Empty,
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(device.SclWorkspace?.DesignModel, designModel) &&
                ReferenceEquals(device.LiveDiscoveryModel, liveModel))
            {
                Entries[device] = new CacheEntry(
                    designModel,
                    liveModel,
                    null,
                    $"ARIEC reconciliation could not be produced asynchronously: {ex.GetType().Name}: {ex.Message}",
                    DateTimeOffset.UtcNow);
            }
        }
    }

    /// <summary>
    /// Refreshes IEDs sequentially by design. The sequential contract prevents a project
    /// refresh from turning bounded relay verification into an uncontrolled multi-IED probe storm.
    /// </summary>
    public static async Task RefreshAsync(
        IEnumerable<Iec61850MonitorDevice> devices,
        Func<Iec61850MonitorDevice,
            LiveIedModelDiscoveryDocument,
            LiveIedModelDiscoveryDocument,
            CancellationToken,
            Task<Iec61850DesignLiveReconciliationDocument>> producer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(producer);

        foreach (var device in devices.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RefreshAsync(
                    device,
                    (design, live, token) => producer(device, design, live, token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public static IoTestReconciliationCacheSnapshot Get(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var designModel = device.SclWorkspace?.DesignModel;
        if (designModel == null)
        {
            return new IoTestReconciliationCacheSnapshot(
                null,
                "No SCL design model is attached to this ARSAS IED workspace.",
                null,
                false);
        }

        var liveModel = device.LiveDiscoveryModel;
        if (liveModel == null)
        {
            return new IoTestReconciliationCacheSnapshot(
                null,
                "No authoritative ARIEC live discovery model is available yet.",
                null,
                false);
        }

        if (!Entries.TryGetValue(device, out var entry) ||
            !ReferenceEquals(entry.DesignModel, designModel) ||
            !ReferenceEquals(entry.LiveModel, liveModel))
        {
            return new IoTestReconciliationCacheSnapshot(
                null,
                "ARIEC reconciliation cache is not ready for the current design/live model generation.",
                null,
                false);
        }

        return new IoTestReconciliationCacheSnapshot(
            entry.Document,
            entry.FailureReason,
            entry.ProducedAtUtc,
            true);
    }

    public static void Invalidate(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        Entries.TryRemove(device, out _);
    }

    private static bool UsesDirectSclFatAuthority(Iec61850MonitorDevice device)
        => device.SclWorkspace?.DesignModel is not null &&
           device.IdentitySource.Contains("FAT SCL", StringComparison.OrdinalIgnoreCase);

    private sealed record CacheEntry(
        object DesignModel,
        object LiveModel,
        Iec61850DesignLiveReconciliationDocument? Document,
        string FailureReason,
        DateTimeOffset ProducedAtUtc);
}

public sealed record IoTestReconciliationCacheSnapshot(
    Iec61850DesignLiveReconciliationDocument? Document,
    string FailureReason,
    DateTimeOffset? ProducedAtUtc,
    bool IsCurrent);
