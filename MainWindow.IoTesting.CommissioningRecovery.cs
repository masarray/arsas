using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private static readonly TimeSpan FatAutoResumeSettleDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan FatAutoResumeRetryDelay = TimeSpan.FromMilliseconds(500);
    private bool _fatCommissioningRecoveryAttached;
    private DateTime _fatReconnectOnlineSinceUtc = DateTime.MinValue;
    private DateTime _fatReconnectLastResumeAttemptUtc = DateTime.MinValue;

    /// <summary>
    /// Installs the commissioning-only bridge after the WPF source is ready. The normal
    /// Engineering event projection remains untouched; FAT receives the same lossless
    /// runtime SOE stream in parallel instead of relying on the 200 ms UI projection.
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

    private void CommissioningRuntime_EventRaised(Iec61850EventEntry entry)
    {
        var controller = _activeIoTestSessionController;
        if (controller == null || controller.State != IoTestSessionState.Running)
            return;

        // IoTestSessionController owns its own concurrent edge queue and dispatcher drain.
        // Feed the authoritative runtime SOE directly so rapid OFF→ON→OFF transitions are
        // never lost by the Engineering UI's intentionally coalesced 200 ms projection.
        controller.Enqueue(entry);
    }

    private void CommissioningRecovery_Tick(object? sender, EventArgs e)
    {
        var controller = _activeIoTestSessionController;
        if (controller == null ||
            controller.State != IoTestSessionState.Interrupted ||
            controller.ActiveIed == null)
        {
            ResetFatReconnectSettleWindow();
            return;
        }

        var device = FindFatRecoveryDevice(controller.ActiveIed);
        if (device == null || !device.IsConnected || !device.IsMonitoring)
        {
            ResetFatReconnectSettleWindow();
            return;
        }

        var nowUtc = DateTime.UtcNow;
        if (_fatReconnectOnlineSinceUtc == DateTime.MinValue)
        {
            _fatReconnectOnlineSinceUtc = nowUtc;
            _fatReconnectLastResumeAttemptUtc = DateTime.MinValue;
            return;
        }

        // Give the runtime time to publish a fresh post-association image before Resume()
        // establishes the new FAT baseline. This deliberately prevents an edge spanning
        // the communication gap from becoming evidence.
        if (nowUtc - _fatReconnectOnlineSinceUtc < FatAutoResumeSettleDelay)
            return;
        if (_fatReconnectLastResumeAttemptUtc != DateTime.MinValue &&
            nowUtc - _fatReconnectLastResumeAttemptUtc < FatAutoResumeRetryDelay)
            return;

        _fatReconnectLastResumeAttemptUtc = nowUtc;
        controller.Resume();
        if (controller.State != IoTestSessionState.Running)
            return;

        SetStatus($"{controller.ActiveIed?.IedName}: FAT auto-resumed after IEC 61850 reconnect; a fresh connection-generation baseline is active.");
        ResetFatReconnectSettleWindow();
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

    private void ResetFatReconnectSettleWindow()
    {
        _fatReconnectOnlineSinceUtc = DateTime.MinValue;
        _fatReconnectLastResumeAttemptUtc = DateTime.MinValue;
    }
}
