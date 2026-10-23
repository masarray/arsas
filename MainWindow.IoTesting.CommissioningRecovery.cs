using System.Collections.Concurrent;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private static readonly TimeSpan FatAutoResumeSettleDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan FatAutoResumeRetryDelay = TimeSpan.FromMilliseconds(500);
    private bool _fatCommissioningRecoveryAttached;

    // Every FAT IED owns one isolated controller. Keep an explicit registry here so
    // reconnect recovery and SOE fallback work for the primary IED and every sibling,
    // without changing the selected IED or sharing evidence state between devices.
    private readonly ConcurrentDictionary<IoTestSessionController, byte> _fatCommissioningControllers = new();
    private readonly Dictionary<IoTestSessionController, DateTime> _fatReconnectOnlineSinceUtc = new();
    private readonly Dictionary<IoTestSessionController, DateTime> _fatReconnectLastResumeAttemptUtc = new();

    /// <summary>
    /// Installs commissioning recovery once for the application lifetime. PointUpdated is
    /// still the normal FAT observation authority. Runtime SOE is a delayed fail-safe only:
    /// it is replayed when the direct FAT route has not reflected the process edge by the
    /// time the WPF background queue gets a turn. This preserves rapid edges without
    /// double-journaling the normal PointUpdated + SOE pair.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_fatCommissioningRecoveryAttached)
            return;

        _fatCommissioningRecoveryAttached = true;
        _runtime.EventRaised += CommissioningRuntime_EventRaised;
        _uiFlushTimer.Tick += CommissioningRecovery_Tick;
    }

    internal void RegisterFatCommissioningController(IoTestSessionController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _fatCommissioningControllers.TryAdd(controller, 0);
    }

    internal void ClearFatCommissioningControllers()
    {
        _fatCommissioningControllers.Clear();
        _fatReconnectOnlineSinceUtc.Clear();
        _fatReconnectLastResumeAttemptUtc.Clear();
    }

    private void CommissioningRuntime_EventRaised(Iec61850EventEntry entry)
    {
        if (entry == null || _fatCommissioningControllers.IsEmpty)
            return;

        // Runtime raises PointUpdated immediately before EventRaised. The normal FAT route
        // therefore gets first chance to drain its lossless edge queue. Queue this SOE at
        // the same background priority and replay it only if the point runtime still shows
        // the old condition. In normal operation this becomes a no-op, not a duplicate.
        Dispatcher.BeginInvoke(
            new Action(() => DeliverFatSoeFallback(entry)),
            DispatcherPriority.Background);
    }

    private void DeliverFatSoeFallback(Iec61850EventEntry entry)
    {
        foreach (var controller in _fatCommissioningControllers.Keys.ToArray())
        {
            if (controller.State != IoTestSessionState.Running || controller.ActiveIed == null)
                continue;
            if (!FatControllerNeedsSoeFallback(controller, entry))
                continue;

            controller.Enqueue(entry);
        }
    }

    private static bool FatControllerNeedsSoeFallback(
        IoTestSessionController controller,
        Iec61850EventEntry entry)
    {
        var activeIed = controller.ActiveIed;
        if (activeIed == null)
            return false;

        var deviceMatches = activeIed.LiveDeviceId.Equals(entry.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                            activeIed.IpAddress.Equals(entry.IpAddress, StringComparison.OrdinalIgnoreCase) ||
                            activeIed.IedName.Equals(entry.DeviceName, StringComparison.OrdinalIgnoreCase);
        if (!deviceMatches)
            return false;

        var eventReference = NormalizeFatRuntimeReference(entry.IecReference);
        foreach (var point in activeIed.TestPoints)
        {
            if (point.CaptureMode != FatCaptureMode.AutomaticTransition ||
                !point.WorkspaceSelected || !point.IsIncludedInFat || !point.TestEnabled || !point.ImportReady)
            {
                continue;
            }

            var liveReference = NormalizeFatRuntimeReference(
                string.IsNullOrWhiteSpace(point.LiveSignalReference)
                    ? point.ObjectReference
                    : point.LiveSignalReference);
            if (!liveReference.Equals(eventReference, StringComparison.OrdinalIgnoreCase))
                continue;

            // CurrentValue is updated by IoTestSessionController.ProcessSnapshot. If it
            // already equals the SOE value, the direct PointUpdated route consumed this
            // transition and replaying it would only duplicate audit history.
            if (!Iec61850MonitorPoint.AreSemanticallyEquivalent(point.Runtime.CurrentValue, entry.NewValue))
                return true;
        }

        return false;
    }

    private void CommissioningRecovery_Tick(object? sender, EventArgs e)
    {
        if (_fatCommissioningControllers.IsEmpty)
        {
            _fatReconnectOnlineSinceUtc.Clear();
            _fatReconnectLastResumeAttemptUtc.Clear();
            return;
        }

        var controllers = _fatCommissioningControllers.Keys.ToArray();
        foreach (var controller in controllers)
        {
            if (controller.State != IoTestSessionState.Interrupted || controller.ActiveIed == null)
            {
                ResetFatReconnectSettleWindow(controller);
                continue;
            }

            var device = FindFatRecoveryDevice(controller.ActiveIed);
            if (device == null || !device.IsConnected || !device.IsMonitoring)
            {
                ResetFatReconnectSettleWindow(controller);
                continue;
            }

            var nowUtc = DateTime.UtcNow;
            if (!_fatReconnectOnlineSinceUtc.TryGetValue(controller, out var onlineSinceUtc))
            {
                _fatReconnectOnlineSinceUtc[controller] = nowUtc;
                _fatReconnectLastResumeAttemptUtc.Remove(controller);
                continue;
            }

            // Give the runtime time to publish a fresh post-association image before
            // Resume() creates the new connection-generation baseline. No transition is
            // ever inferred across the communication gap.
            if (nowUtc - onlineSinceUtc < FatAutoResumeSettleDelay)
                continue;
            if (_fatReconnectLastResumeAttemptUtc.TryGetValue(controller, out var lastAttemptUtc) &&
                nowUtc - lastAttemptUtc < FatAutoResumeRetryDelay)
            {
                continue;
            }

            _fatReconnectLastResumeAttemptUtc[controller] = nowUtc;
            var resume = controller.Resume();
            if (!resume.Succeeded || controller.State != IoTestSessionState.Running)
                continue;

            SetStatus($"{controller.ActiveIed?.IedName}: FAT auto-resumed after IEC 61850 reconnect; a fresh connection-generation baseline is active.");
            ResetFatReconnectSettleWindow(controller);
        }
    }

    private Iec61850MonitorDevice? FindFatRecoveryDevice(IoTestIedPlan ied)
    {
        if (!string.IsNullOrWhiteSpace(ied.LiveDeviceId))
        {
            var byId = Devices.FirstOrDefault(device =>
                device.DeviceId.Equals(ied.LiveDeviceId, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return byId;
        }

        return Devices.FirstOrDefault(device =>
                   device.IpAddress.Equals(ied.IpAddress, StringComparison.OrdinalIgnoreCase) &&
                   (device.Name.Equals(ied.IedName, StringComparison.OrdinalIgnoreCase) ||
                    device.SclIedName.Equals(ied.IedName, StringComparison.OrdinalIgnoreCase)))
               ?? Devices.FirstOrDefault(device =>
                   device.IpAddress.Equals(ied.IpAddress, StringComparison.OrdinalIgnoreCase));
    }

    private void ResetFatReconnectSettleWindow(IoTestSessionController controller)
    {
        _fatReconnectOnlineSinceUtc.Remove(controller);
        _fatReconnectLastResumeAttemptUtc.Remove(controller);
    }

    private static string NormalizeFatRuntimeReference(string? reference)
        => (reference ?? string.Empty)
            .Trim()
            .Replace('$', '.')
            .Replace("..", ".")
            .ToLowerInvariant();
}
