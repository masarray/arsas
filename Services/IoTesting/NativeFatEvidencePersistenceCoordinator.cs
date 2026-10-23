using System.Diagnostics;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Immutable sparse FAT evidence captured at the evidence-change boundary. It contains only
/// stable IED identity and detached evidence; it never retains Engineering Points or WPF rows.
/// </summary>
internal sealed class NativeFatEvidenceDurabilitySnapshot
{
    private NativeFatEvidenceDurabilitySnapshot(
        string deviceId,
        string iedName,
        string ipAddress,
        IReadOnlyDictionary<string, NativeFatEvidenceSlotState> evidenceByRow)
    {
        DeviceId = deviceId;
        IedName = iedName;
        IpAddress = ipAddress;
        StableIedName = NativeFatCanonicalEvidenceOverlay.NormalizeIedName(iedName);
        EvidenceByRow = evidenceByRow;
    }

    internal string DeviceId { get; }
    internal string IedName { get; }
    internal string IpAddress { get; }
    internal string StableIedName { get; }
    internal IReadOnlyDictionary<string, NativeFatEvidenceSlotState> EvidenceByRow { get; }

    internal static NativeFatEvidenceDurabilitySnapshot Capture(
        Iec61850MonitorDevice source,
        NativeFatIedSessionCacheState sourceCache)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Capture(source.DeviceId, source.Name, source.IpAddress, sourceCache);
    }

    internal static NativeFatEvidenceDurabilitySnapshot Capture(
        string deviceId,
        string iedName,
        string ipAddress,
        NativeFatIedSessionCacheState sourceCache)
    {
        ArgumentNullException.ThrowIfNull(sourceCache);

        var detached = NativeFatCanonicalEvidenceOverlay.Snapshot(sourceCache)
            .ToDictionary(
                pair => pair.Key,
                pair => Clone(pair.Value),
                StringComparer.OrdinalIgnoreCase);

        return new NativeFatEvidenceDurabilitySnapshot(
            deviceId?.Trim() ?? string.Empty,
            iedName?.Trim() ?? string.Empty,
            ipAddress?.Trim() ?? string.Empty,
            detached);
    }

    private static NativeFatEvidenceSlotState Clone(NativeFatEvidenceSlotState source)
        => new()
        {
            Value1 = source.Value1Evidence?.RawValue ?? source.Value1,
            Value2 = source.Value2Evidence?.RawValue ?? source.Value2,
            Value1Evidence = source.Value1Evidence,
            Value2Evidence = source.Value2Evidence,
            Result = source.Result
        };
}

/// <summary>
/// Lightweight per-IED persistence worker. No permanent thread exists: a worker is created
/// only while one stable IEDName has dirty evidence, coalesces short bursts, and serializes
/// writes so the newest generation is always the final JSON on disk.
/// </summary>
internal sealed class NativeFatEvidencePersistenceCoordinator
{
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(120);

    private readonly NativeFatEvidenceStore _store;
    private readonly object _gate = new();
    private readonly Dictionary<string, IedWriteState> _stateByIed =
        new(StringComparer.OrdinalIgnoreCase);

    internal NativeFatEvidencePersistenceCoordinator(NativeFatEvidenceStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

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

    internal async Task DrainAllAsync()
    {
        while (true)
        {
            Task[] workers;
            lock (_gate)
            {
                workers = _stateByIed.Values
                    .Select(state => state.Worker)
                    .Where(worker => worker != null)
                    .Cast<Task>()
                    .Distinct()
                    .ToArray();
            }

            if (workers.Length == 0)
                return;

            await Task.WhenAll(workers).ConfigureAwait(false);
        }
    }

    private async Task RunWorkerAsync(IedWriteState state)
    {
        while (true)
        {
            await Task.Delay(CoalesceWindow).ConfigureAwait(false);

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
                await _store.SaveAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
                Trace.WriteLine(
                    $"[FAT evidence store] persisted {snapshot.EvidenceByRow.Count} sparse row(s); " +
                    $"ied={snapshot.IedName}; generation={generation}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Trace.WriteLine(
                    $"[FAT evidence store] persistence failed for {snapshot.IedName}: {ex.Message}");
            }

            lock (_gate)
            {
                if (generation == state.Generation)
                {
                    state.Worker = null;
                    return;
                }
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
