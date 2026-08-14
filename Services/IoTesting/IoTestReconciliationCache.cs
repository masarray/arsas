using System.Collections.Concurrent;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Owns the ARSAS-side lifecycle of engine reconciliation documents.
///
/// Reconciliation production is asynchronous and cancellable; synchronous FAT/UI binding
/// only reads the latest document for the exact design/live model object pair. This keeps
/// network-capable reconciliation out of UI binding paths when ARIEC later exposes the
/// connected P1.3 facade.
///
/// The current P1.2 integration deliberately passes probe:null. ARIEC therefore owns all
/// reconciliation semantics while discovery misses remain DesignOnly instead of being
/// promoted to protocol absence by ARSAS.
/// </summary>
public static class IoTestReconciliationCache
{
    private static readonly ConcurrentDictionary<Iec61850MonitorDevice, CacheEntry> Entries = new();

    public static async Task RefreshAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var designModel = device.SclWorkspace?.DesignModel;
        var liveModel = device.LiveDiscoveryModel;
        if (designModel == null || liveModel == null)
        {
            Entries.TryRemove(device, out _);
            return;
        }

        try
        {
            var document = await Iec61850DesignLiveReconciler.ReconcileAsync(
                    designModel,
                    liveModel,
                    probe: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

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
    /// Refreshes IEDs sequentially by design. P1.2 does not perform network reads here,
    /// and the sequential contract also prevents a future connected reconciler from turning
    /// a project-level refresh into an uncontrolled parallel probe storm.
    /// </summary>
    public static async Task RefreshAsync(
        IEnumerable<Iec61850MonitorDevice> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);

        foreach (var device in devices.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RefreshAsync(device, cancellationToken).ConfigureAwait(false);
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
