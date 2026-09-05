using System.Collections.Concurrent;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// UI-facing facade for the IEC 61850 runtime.
///
/// The protocol engine intentionally owns MMS serialization, report ownership and per-IED
/// session state. WPF must not, however, execute the synchronous prefix of a native/network
/// operation on the Dispatcher thread. Some vendor/OS socket and teardown paths can block
/// before their first asynchronous yield; when invoked directly from a Click/Closing handler
/// that makes the whole application appear hung even though the API returns Task.
///
/// This facade is resolved by MainWindow from its own namespace (and wraps the Services
/// runtime explicitly). Every potentially blocking lifecycle/control operation starts on the
/// ThreadPool, while a per-device gate prevents Connect/Start/Stop/Disconnect races for the
/// same IED. Different IEDs remain independent. No polling/read fallback is introduced here.
/// </summary>
public sealed class Iec61850MonitorRuntime : IAsyncDisposable
{
    private static readonly TimeSpan DisposeBudget = TimeSpan.FromSeconds(3);

    private readonly Services.Iec61850MonitorRuntime _inner = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _deviceLifecycleGates =
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
        => RunDeviceLifecycleAsync(
            device?.DeviceId,
            cancellationToken,
            () => _inner.ConnectAndDiscoverAsync(device, cancellationToken, progress));

    public Task ConnectUsingCachedModelAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken,
        IProgress<IedDiscoveryProgress>? progress = null)
        => RunDeviceLifecycleAsync(
            device?.DeviceId,
            cancellationToken,
            () => _inner.ConnectUsingCachedModelAsync(device, cancellationToken, progress));

    public Task<IReadOnlyList<Iec61850MonitorPoint>> StartMonitoringAsync(
        Iec61850MonitorDevice device,
        IEnumerable<SignalDefinition> selectedSignals,
        int pollingIntervalMs,
        CancellationToken cancellationToken)
        => RunDeviceLifecycleAsync(
            device?.DeviceId,
            cancellationToken,
            () => _inner.StartMonitoringAsync(device, selectedSignals, pollingIntervalMs, cancellationToken));

    public Task<Iec61850ControlCapabilities> InspectControlAsync(
        string deviceId,
        SignalDefinition signal,
        CancellationToken cancellationToken)
        => RunDeviceLifecycleAsync(
            deviceId,
            cancellationToken,
            () => _inner.InspectControlAsync(deviceId, signal, cancellationToken));

    public Task<Iec61850ControlCommandResult> ExecuteControlAsync(
        string deviceId,
        Iec61850ControlCommandRequest request,
        CancellationToken cancellationToken)
        => RunDeviceLifecycleAsync(
            deviceId,
            cancellationToken,
            () => _inner.ExecuteControlAsync(deviceId, request, cancellationToken));

    public HybridReportPhysicalValidationSnapshot CaptureHybridReportPhysicalValidation(string deviceId)
        => _inner.CaptureHybridReportPhysicalValidation(deviceId);

    public Task StopMonitoringAsync(string deviceId)
        => RunDeviceLifecycleAsync(
            deviceId,
            CancellationToken.None,
            () => _inner.StopMonitoringAsync(deviceId));

    public Task StopDeviceAsync(string deviceId)
        => RunDeviceLifecycleAsync(
            deviceId,
            CancellationToken.None,
            () => _inner.StopDeviceAsync(deviceId));

    private async Task RunDeviceLifecycleAsync(
        string? deviceId,
        CancellationToken cancellationToken,
        Func<Task> operation)
    {
        ThrowIfDisposing();
        ArgumentNullException.ThrowIfNull(operation);

        var key = NormalizeDeviceKey(deviceId);
        var gate = _deviceLifecycleGates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Task-returning APIs are not automatically non-blocking. Invoke the complete
            // operation from the ThreadPool so a blocking native prefix can never seize the
            // WPF Dispatcher. Awaiting callers still receive the real completion/exception.
            await Task.Run(operation, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<T> RunDeviceLifecycleAsync<T>(
        string? deviceId,
        CancellationToken cancellationToken,
        Func<Task<T>> operation)
    {
        ThrowIfDisposing();
        ArgumentNullException.ThrowIfNull(operation);

        var key = NormalizeDeviceKey(deviceId);
        var gate = _deviceLifecycleGates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(operation, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        // Dispose on a worker thread as well: a vendor/native close path is allowed to be
        // slow, but it is never allowed to freeze the operator's window. MainWindow also has
        // its own shutdown budget; this inner bound keeps the facade independently safe.
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
                // Application shutdown is best-effort. The process must remain closable even
                // when a native session reports a teardown failure.
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

        foreach (var gate in _deviceLifecycleGates.Values)
            gate.Dispose();
        _deviceLifecycleGates.Clear();
    }

    private void ThrowIfDisposing()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
            throw new ObjectDisposedException(nameof(Iec61850MonitorRuntime));
    }

    private static string NormalizeDeviceKey(string? deviceId)
        => string.IsNullOrWhiteSpace(deviceId) ? "__unbound__" : deviceId.Trim();
}
