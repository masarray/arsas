using System.Collections.Concurrent;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// UI-facing facade for the IEC 61850 runtime.
///
/// Native/network calls must never execute their synchronous prefix on the WPF Dispatcher.
/// A Task-returning API is not sufficient protection because socket/vendor code may block
/// before the first asynchronous yield.
///
/// Per IED, normal lifecycle/control operations remain serialized. Stop/Disconnect is a
/// deliberately pre-emptive lane: it cancels the active operation first and invokes the
/// runtime stop without waiting behind a hung Connect/Start gate. A cancelled/stale operation
/// is never allowed to report success to its caller afterwards. Different IEDs stay fully
/// independent. This facade changes lifecycle scheduling only; it does not add MMS polling,
/// dynamic DataSet writes, or any acquisition fallback.
/// </summary>
public sealed class Iec61850MonitorRuntime : IAsyncDisposable
{
    private static readonly TimeSpan DisposeBudget = TimeSpan.FromSeconds(3);

    private sealed class DeviceOperationSlot
    {
        public object SyncRoot { get; } = new();
        public SemaphoreSlim OperationGate { get; } = new(1, 1);
        public SemaphoreSlim StopGate { get; } = new(1, 1);
        public CancellationTokenSource? ActiveOperationCancellation { get; set; }
        public long Generation { get; set; }
    }

    private readonly Services.Iec61850MonitorRuntime _inner = new();
    private readonly ConcurrentDictionary<string, DeviceOperationSlot> _deviceSlots =
        new(StringComparer.OrdinalIgnoreCase);
    private int _disposeStarted;

    public Iec61850MonitorRuntime()
    {
        _inner.Diagnostic += entry => Diagnostic?.Invoke(entry);
        _inner.PointUpdated += snapshot => PointUpdated?.Invoke(snapshot);
        _inner.EventRaised += entry => EventRaised?.Invoke(entry);
    }

    public event Action<DiagnosticEntry>? Diagnostic;
    public event Action<Iec61850PointSnapshot>? PointUpdated;
    public event Action<Iec61850EventEntry>? EventRaised;

    public int ConnectedDeviceCount => _inner.ConnectedDeviceCount;
    public int MonitoringDeviceCount => _inner.MonitoringDeviceCount;

    public Task<IReadOnlyList<SignalDefinition>> ConnectAndDiscoverAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken,
        IProgress<IedDiscoveryProgress>? progress = null)
        => RunDeviceOperationAsync(
            device?.DeviceId,
            cancellationToken,
            token => _inner.ConnectAndDiscoverAsync(device, token, progress));

    public Task ConnectUsingCachedModelAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken,
        IProgress<IedDiscoveryProgress>? progress = null)
        => RunDeviceOperationAsync(
            device?.DeviceId,
            cancellationToken,
            token => _inner.ConnectUsingCachedModelAsync(device, token, progress));

    public Task<IReadOnlyList<Iec61850MonitorPoint>> StartMonitoringAsync(
        Iec61850MonitorDevice device,
        IEnumerable<SignalDefinition> selectedSignals,
        int pollingIntervalMs,
        CancellationToken cancellationToken)
        => RunDeviceOperationAsync(
            device?.DeviceId,
            cancellationToken,
            token => _inner.StartMonitoringAsync(device, selectedSignals, pollingIntervalMs, token));

    public Task<Iec61850ControlCapabilities> InspectControlAsync(
        string deviceId,
        SignalDefinition signal,
        CancellationToken cancellationToken)
        => RunDeviceOperationAsync(
            deviceId,
            cancellationToken,
            token => _inner.InspectControlAsync(deviceId, signal, token));

    public Task<Iec61850ControlCommandResult> ExecuteControlAsync(
        string deviceId,
        Iec61850ControlCommandRequest request,
        CancellationToken cancellationToken)
        => RunDeviceOperationAsync(
            deviceId,
            cancellationToken,
            token => _inner.ExecuteControlAsync(deviceId, request, token));

    public HybridReportPhysicalValidationSnapshot CaptureHybridReportPhysicalValidation(string deviceId)
        => _inner.CaptureHybridReportPhysicalValidation(deviceId);

    public Task StopMonitoringAsync(string deviceId)
        => RunPreemptiveStopAsync(deviceId, () => _inner.StopMonitoringAsync(deviceId));

    public Task StopDeviceAsync(string deviceId)
        => RunPreemptiveStopAsync(deviceId, () => _inner.StopDeviceAsync(deviceId));

    private async Task RunDeviceOperationAsync(
        string? deviceId,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task> operation)
    {
        ThrowIfDisposing();
        ArgumentNullException.ThrowIfNull(operation);

        var slot = GetSlot(deviceId);
        await slot.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        CancellationTokenSource? linkedCancellation = null;
        long generation = 0;
        try
        {
            // A Stop that is already in progress owns StopGate. Waiting here prevents a new
            // Connect/Start from racing the teardown. We hold StopGate only while publishing
            // the new active-operation identity; never for the network operation itself.
            await slot.StopGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                lock (slot.SyncRoot)
                {
                    slot.ActiveOperationCancellation = linkedCancellation;
                    generation = ++slot.Generation;
                }
            }
            finally
            {
                slot.StopGate.Release();
            }

            await Task.Run(
                    async () => await operation(linkedCancellation.Token).ConfigureAwait(false),
                    CancellationToken.None)
                .ConfigureAwait(false);

            // Stop/Disconnect can pre-empt this lane. Even if a native call ignored
            // cancellation and eventually returned success, a pre-empted generation must
            // not re-enter the UI as a successful stale Connect/Start operation.
            linkedCancellation.Token.ThrowIfCancellationRequested();
            lock (slot.SyncRoot)
            {
                if (slot.Generation != generation)
                    throw new OperationCanceledException("IEC 61850 lifecycle operation was superseded.");
            }
        }
        finally
        {
            if (linkedCancellation != null)
            {
                lock (slot.SyncRoot)
                {
                    if (ReferenceEquals(slot.ActiveOperationCancellation, linkedCancellation))
                        slot.ActiveOperationCancellation = null;
                }
                linkedCancellation.Dispose();
            }
            slot.OperationGate.Release();
        }
    }

    private async Task<T> RunDeviceOperationAsync<T>(
        string? deviceId,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<T>> operation)
    {
        ThrowIfDisposing();
        ArgumentNullException.ThrowIfNull(operation);

        var slot = GetSlot(deviceId);
        await slot.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        CancellationTokenSource? linkedCancellation = null;
        long generation = 0;
        try
        {
            await slot.StopGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                lock (slot.SyncRoot)
                {
                    slot.ActiveOperationCancellation = linkedCancellation;
                    generation = ++slot.Generation;
                }
            }
            finally
            {
                slot.StopGate.Release();
            }

            var result = await Task.Run(
                    async () => await operation(linkedCancellation.Token).ConfigureAwait(false),
                    CancellationToken.None)
                .ConfigureAwait(false);

            linkedCancellation.Token.ThrowIfCancellationRequested();
            lock (slot.SyncRoot)
            {
                if (slot.Generation != generation)
                    throw new OperationCanceledException("IEC 61850 lifecycle operation was superseded.");
            }
            return result;
        }
        finally
        {
            if (linkedCancellation != null)
            {
                lock (slot.SyncRoot)
                {
                    if (ReferenceEquals(slot.ActiveOperationCancellation, linkedCancellation))
                        slot.ActiveOperationCancellation = null;
                }
                linkedCancellation.Dispose();
            }
            slot.OperationGate.Release();
        }
    }

    private async Task RunPreemptiveStopAsync(string? deviceId, Func<Task> stopOperation)
    {
        ThrowIfDisposing();
        ArgumentNullException.ThrowIfNull(stopOperation);

        var slot = GetSlot(deviceId);
        CancellationTokenSource? activeCancellation;
        lock (slot.SyncRoot)
        {
            activeCancellation = slot.ActiveOperationCancellation;
            ++slot.Generation;
        }

        // Cancellation is intentionally issued before StopGate and, critically, without
        // waiting for OperationGate. This is the escape path when Connect/Start is hung.
        try
        {
            activeCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The active operation completed between snapshot and cancellation.
        }

        await slot.StopGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(
                    async () => await stopOperation().ConfigureAwait(false),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            slot.StopGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        foreach (var slot in _deviceSlots.Values)
        {
            CancellationTokenSource? activeCancellation;
            lock (slot.SyncRoot)
            {
                activeCancellation = slot.ActiveOperationCancellation;
                ++slot.Generation;
            }
            try
            {
                activeCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Benign completion race during process teardown.
            }
        }

        Task disposeTask;
        try
        {
            disposeTask = Task.Run(async () => await _inner.DisposeAsync().ConfigureAwait(false));
        }
        catch
        {
            return;
        }

        var completed = await Task.WhenAny(disposeTask, Task.Delay(DisposeBudget)).ConfigureAwait(false);
        if (completed == disposeTask)
        {
            try
            {
                await disposeTask.ConfigureAwait(false);
            }
            catch
            {
                // Shutdown is best-effort. Native teardown failure must not freeze WPF.
            }
        }
        else
        {
            _ = disposeTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        // Do not Dispose the per-device semaphores here. An uncooperative native operation
        // can still unwind after the bounded shutdown budget and its finally block must be
        // able to Release() safely. They become process-lifetime garbage with this facade.
        _deviceSlots.Clear();
    }

    private DeviceOperationSlot GetSlot(string? deviceId)
        => _deviceSlots.GetOrAdd(NormalizeDeviceKey(deviceId), static _ => new DeviceOperationSlot());

    private void ThrowIfDisposing()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
            throw new ObjectDisposedException(nameof(Iec61850MonitorRuntime));
    }

    private static string NormalizeDeviceKey(string? deviceId)
        => string.IsNullOrWhiteSpace(deviceId) ? "__unbound__" : deviceId.Trim();
}
