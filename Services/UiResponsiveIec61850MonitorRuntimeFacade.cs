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
/// independent. This facade also owns one bounded command-feedback freshness fence at the
/// runtime/UI boundary; it does not add MMS polling, dynamic DataSet writes, or acquisition
/// fallback.
/// </summary>
public sealed class Iec61850MonitorRuntime : IAsyncDisposable
{
    private static readonly TimeSpan DisposeBudget = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CommandFeedbackFreshnessWindow = TimeSpan.FromSeconds(2);

    private sealed class DeviceOperationSlot
    {
        public object SyncRoot { get; } = new();
        public SemaphoreSlim OperationGate { get; } = new(1, 1);
        public SemaphoreSlim StopGate { get; } = new(1, 1);
        public CancellationTokenSource? ActiveOperationCancellation { get; set; }
        public long Generation { get; set; }
    }

    private sealed record CommandFeedbackFence(
        string ExpectedValue,
        DateTime ExpiresUtc);

    private readonly Services.Iec61850MonitorRuntime _inner = new();
    private readonly ConcurrentDictionary<string, DeviceOperationSlot> _deviceSlots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CommandFeedbackFence> _commandFeedbackFences =
        new(StringComparer.OrdinalIgnoreCase);
    private int _disposeStarted;

    public Iec61850MonitorRuntime()
    {
        _inner.Diagnostic += entry => Diagnostic?.Invoke(entry);
        _inner.PointUpdated += ForwardPointUpdate;
        _inner.EventRaised += ForwardEventRaised;
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
    {
        ClearCommandFeedbackFences(deviceId);
        return RunPreemptiveStopAsync(deviceId, () => _inner.StopMonitoringAsync(deviceId));
    }

    public Task StopDeviceAsync(string deviceId)
    {
        ClearCommandFeedbackFences(deviceId);
        return RunPreemptiveStopAsync(deviceId, () => _inner.StopDeviceAsync(deviceId));
    }

    /// <summary>
    /// P0 command-feedback freshness fence.
    ///
    /// The inner runtime publishes command-confirmed process feedback immediately, then its
    /// report/poll monitor resumes. Some relays can return one pre-command MMS verification
    /// sample before their status cache/report stream catches up, producing a visible
    /// Closed → Open → Closed flash even though the command was accepted and the matching
    /// dchg arrives moments later.
    ///
    /// This is deliberately not a WPF debounce and it does not manufacture state. The
    /// confirmed value itself opens a short per-point fence. Contradictory non-report
    /// snapshots are withheld during that bounded window. Report traffic remains process
    /// authority; a contradictory report is forwarded immediately. A matching report is
    /// forwarded as confirmation while the short fence remains alive so a stale poll that
    /// was already in flight cannot flash the process value or manufacture a duplicate SOE.
    /// </summary>
    private void ForwardPointUpdate(Iec61850PointSnapshot snapshot)
    {
        var key = CommandFeedbackFenceKey(snapshot.Point.DeviceId, snapshot.Point.IecReference);
        var nowUtc = DateTime.UtcNow;

        if (IsConfirmedCommandFeedback(snapshot))
        {
            _commandFeedbackFences[key] = new CommandFeedbackFence(
                snapshot.Value?.Trim() ?? string.Empty,
                nowUtc.Add(CommandFeedbackFreshnessWindow));
            PointUpdated?.Invoke(snapshot);
            return;
        }

        if (!_commandFeedbackFences.TryGetValue(key, out var fence))
        {
            PointUpdated?.Invoke(snapshot);
            return;
        }

        if (nowUtc > fence.ExpiresUtc)
        {
            _commandFeedbackFences.TryRemove(key, out _);
            PointUpdated?.Invoke(snapshot);
            return;
        }

        var matchesConfirmed = CommandFeedbackValuesEquivalent(fence.ExpectedValue, snapshot.Value);
        if (snapshot.IsReportTraffic)
        {
            // A contradictory report is a real process transition and must immediately
            // release the fence. A matching report confirms the command; keep the fence
            // until its short expiry so an already in-flight stale poll cannot flash back.
            if (!matchesConfirmed)
                _commandFeedbackFences.TryRemove(key, out _);
            PointUpdated?.Invoke(snapshot);
            return;
        }

        if (matchesConfirmed)
        {
            PointUpdated?.Invoke(snapshot);
            return;
        }

        EmitFreshnessDiagnostic(
            snapshot.Point.DeviceName,
            $"withheld stale MMS verification {snapshot.Point.IecReference}={snapshot.Value} inside the command-confirmed {fence.ExpectedValue} freshness window. Report traffic remains authoritative.");
    }

    /// <summary>
    /// The same freshness rule must cover SOE, not only the visible live value. Otherwise a
    /// stale polling sample suppressed from the grid could still create a phantom Open/Close
    /// event and a second synthetic return-to-command event when the matching report arrives.
    /// </summary>
    private void ForwardEventRaised(Iec61850EventEntry entry)
    {
        var key = CommandFeedbackFenceKey(entry.DeviceId, entry.IecReference);
        if (!_commandFeedbackFences.TryGetValue(key, out var fence))
        {
            EventRaised?.Invoke(entry);
            return;
        }

        var nowUtc = DateTime.UtcNow;
        if (nowUtc > fence.ExpiresUtc)
        {
            _commandFeedbackFences.TryRemove(key, out _);
            EventRaised?.Invoke(entry);
            return;
        }

        // The initial command-confirmed transition is legitimate process evidence and is
        // the event that opened the fence through ForwardPointUpdate just before this call.
        if (IsConfirmedCommandFeedback(entry.Reason))
        {
            EventRaised?.Invoke(entry);
            return;
        }

        var matchesConfirmed = CommandFeedbackValuesEquivalent(fence.ExpectedValue, entry.NewValue);
        if (IsReportEvent(entry))
        {
            if (!matchesConfirmed)
            {
                // A report-proven change away from the commanded state is real. Release the
                // fence so all following report/SOE transitions flow normally.
                _commandFeedbackFences.TryRemove(key, out _);
                EventRaised?.Invoke(entry);
                return;
            }

            EmitFreshnessDiagnostic(
                entry.DeviceName,
                $"suppressed duplicate report SOE {entry.IecReference}={entry.NewValue}; it only confirms the already-published command state {fence.ExpectedValue}.");
            return;
        }

        if (matchesConfirmed)
        {
            EmitFreshnessDiagnostic(
                entry.DeviceName,
                $"suppressed duplicate MMS verification SOE {entry.IecReference}={entry.NewValue}; command-confirmed state is already {fence.ExpectedValue}.");
            return;
        }

        EmitFreshnessDiagnostic(
            entry.DeviceName,
            $"withheld phantom MMS verification SOE {entry.IecReference}: {entry.OldValue} → {entry.NewValue} inside the command-confirmed {fence.ExpectedValue} freshness window.");
    }

    private void EmitFreshnessDiagnostic(string source, string message)
        => Diagnostic?.Invoke(new DiagnosticEntry
        {
            Time = DateTime.Now,
            Level = "INFO",
            Source = source,
            Message = "P0_COMMAND_FRESHNESS: " + message
        });

    private static bool IsConfirmedCommandFeedback(Iec61850PointSnapshot snapshot)
        => IsConfirmedCommandFeedback(snapshot.Reason);

    private static bool IsConfirmedCommandFeedback(string? reason)
        => (reason ?? string.Empty).Contains(
            "confirmed command feedback",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsReportEvent(Iec61850EventEntry entry)
    {
        var reason = (entry.Reason ?? string.Empty).Trim();
        var source = (entry.SourceMode ?? string.Empty).Trim();
        return reason.Contains("dchg", StringComparison.OrdinalIgnoreCase) ||
               reason.Contains("data-change", StringComparison.OrdinalIgnoreCase) ||
               reason.Contains("qchg", StringComparison.OrdinalIgnoreCase) ||
               reason.Contains("quality-change", StringComparison.OrdinalIgnoreCase) ||
               reason.Contains("dupd", StringComparison.OrdinalIgnoreCase) ||
               reason.Contains("data-update", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("report", StringComparison.OrdinalIgnoreCase);
    }

    private static string CommandFeedbackFenceKey(string? deviceId, string? reference)
        => $"{NormalizeDeviceKey(deviceId)}|{NormalizeReference(reference)}";

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty)
            .Trim()
            .Replace('$', '.')
            .Replace("..", ".", StringComparison.Ordinal)
            .ToLowerInvariant();

    private static bool CommandFeedbackValuesEquivalent(string? left, string? right)
    {
        var leftText = (left ?? string.Empty).Trim();
        var rightText = (right ?? string.Empty).Trim();
        if (leftText.Equals(rightText, StringComparison.OrdinalIgnoreCase))
            return true;

        if (bool.TryParse(leftText, out var leftBool) &&
            bool.TryParse(rightText, out var rightBool))
        {
            return leftBool == rightBool;
        }

        var leftState = ExtractStateCode(leftText);
        var rightState = ExtractStateCode(rightText);
        return leftState.Length > 0 && rightState.Length > 0 &&
               leftState.Equals(rightState, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractStateCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var open = value.LastIndexOf('[');
        var close = value.LastIndexOf(']');
        return open >= 0 && close > open ? value[(open + 1)..close].Trim() : string.Empty;
    }

    private void ClearCommandFeedbackFences(string? deviceId)
    {
        var prefix = NormalizeDeviceKey(deviceId) + "|";
        foreach (var key in _commandFeedbackFences.Keys
                     .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            _commandFeedbackFences.TryRemove(key, out _);
        }
    }

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
        _commandFeedbackFences.Clear();
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
