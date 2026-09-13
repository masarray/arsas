using System.Diagnostics;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Immutable FAT evidence write input captured while the Engineering IED and its canonical
/// rows are still alive. Persistence must never enumerate a live WPF-bound collection after
/// an IED has started closing/removing from the Engineering workspace.
/// </summary>
internal sealed class NativeFatEvidenceDurabilitySnapshot
{
    private NativeFatEvidenceDurabilitySnapshot(
        Iec61850MonitorDevice device,
        NativeFatIedSessionCacheState cache)
    {
        Device = device;
        Cache = cache;
        StableIedName = NativeFatCanonicalEvidenceOverlay.NormalizeIedName(device.Name);
    }

    internal Iec61850MonitorDevice Device { get; }
    internal NativeFatIedSessionCacheState Cache { get; }
    internal string StableIedName { get; }

    internal static NativeFatEvidenceDurabilitySnapshot Capture(
        Iec61850MonitorDevice source,
        NativeFatIedSessionCacheState sourceCache)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceCache);

        var detachedDevice = new Iec61850MonitorDevice
        {
            DeviceId = source.DeviceId,
            Name = source.Name,
            IpAddress = source.IpAddress,
            Port = source.Port
        };

        // SaveAsync needs only canonical row identity. Copy that identity now so a later
        // RemoveDevicePoints/Points.Clear cannot turn a valid evidence snapshot into an
        // empty snapshot while a debounced/background write is still pending.
        foreach (var point in source.Points)
        {
            detachedDevice.Points.Add(new Iec61850MonitorPoint
            {
                DeviceId = point.DeviceId,
                DeviceName = point.DeviceName,
                IpAddress = point.IpAddress,
                SignalName = point.SignalName,
                IecReference = point.IecReference
            });
        }

        var detachedCache = new NativeFatIedSessionCacheState();
        NativeFatCanonicalEvidenceOverlay.MergeMissing(
            detachedCache,
            NativeFatCanonicalEvidenceOverlay.Snapshot(sourceCache));

        return new NativeFatEvidenceDurabilitySnapshot(detachedDevice, detachedCache);
    }
}

/// <summary>
/// Lightweight per-IED persistence worker. There is no permanent thread: a worker exists
/// only while an IED has dirty evidence. Writes for the same stable IEDName are serialized
/// and the newest generation is always persisted last.
/// </summary>
internal sealed class NativeFatEvidencePersistenceCoordinator
{
    private readonly NativeFatEvidenceHydrationService _service;
    private readonly object _gate = new();
    private readonly Dictionary<string, IedWriteState> _stateByIed =
        new(StringComparer.OrdinalIgnoreCase);

    internal NativeFatEvidencePersistenceCoordinator(NativeFatEvidenceHydrationService service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

    internal void Queue(NativeFatEvidenceDurabilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.StableIedName))
            return;

        lock (_gate)
        {
            if (!_stateByIed.TryGetValue(snapshot.StableIedName, out var state))
            {
                state = new IedWriteState();
                _stateByIed[snapshot.StableIedName] = state;
            }

            state.Latest = snapshot;
            state.Generation++;
            if (state.Worker == null || state.Worker.IsCompleted)
                state.Worker = Task.Run(() => RunWorkerAsync(state));
        }
    }

    internal async Task DrainAsync(string iedName)
    {
        var key = NativeFatCanonicalEvidenceOverlay.NormalizeIedName(iedName);
        while (true)
        {
            Task? worker;
            lock (_gate)
            {
                if (!_stateByIed.TryGetValue(key, out var state) || state.Worker == null)
                    return;
                worker = state.Worker;
            }

            await worker.ConfigureAwait(false);

            lock (_gate)
            {
                if (!_stateByIed.TryGetValue(key, out var state) ||
                    state.Worker == null ||
                    ReferenceEquals(state.Worker, worker))
                {
                    return;
                }
            }
        }
    }

    private async Task RunWorkerAsync(IedWriteState state)
    {
        while (true)
        {
            NativeFatEvidenceDurabilitySnapshot snapshot;
            long generation;
            lock (_gate)
            {
                snapshot = state.Latest
                    ?? throw new InvalidOperationException("FAT evidence worker has no pending snapshot.");
                generation = state.Generation;
            }

            try
            {
                await _service
                    .SaveAsync(snapshot.Device, snapshot.Cache, CancellationToken.None)
                    .ConfigureAwait(false);
                Trace.WriteLine(
                    $"[FAT durability] persisted frozen evidence; ied={snapshot.Device.Name}; generation={generation}; rows={NativeFatCanonicalEvidenceOverlay.Snapshot(snapshot.Cache).Count}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Trace.WriteLine(
                    $"[FAT durability] evidence persistence failed for {snapshot.Device.Name}: {ex.Message}");
            }

            lock (_gate)
            {
                if (generation == state.Generation)
                {
                    state.Worker = null;
                    return;
                }

                // Evidence changed while this write was in flight. Loop immediately and
                // persist only the newest frozen generation after the older write completes.
            }
        }
    }

    private sealed class IedWriteState
    {
        internal NativeFatEvidenceDurabilitySnapshot? Latest { get; set; }
        internal long Generation { get; set; }
        internal Task? Worker { get; set; }
    }
}
