using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    /// <summary>
    /// Resolves the Engineering monitor device that owns one FAT IED plan.
    /// FAT control must never create a second MMS/control stack: the exact same
    /// SignalDefinition instances, ctlModel inspection, control service, wire evidence,
    /// and process-feedback correlation used by Engineering remain authoritative.
    /// </summary>
    internal Iec61850MonitorDevice? ResolveIoFatCommandDevice(IoTestIedPlan? ied)
    {
        if (ied is null)
            return null;

        var device = ResolveIoTestDevice(ied.LiveDeviceId)
                     ?? ResolveIoTestDevice(ied.IpAddress)
                     ?? ResolveIoTestDevice(ied.IedName);
        if (device is null)
            return null;

        // CommandSignals contains only controls whose live ctlModel has proved that
        // operation is allowed and whose UI command semantics are supported. Keep an
        // explicit owner mapping so a FAT command cannot accidentally fall back to the
        // Engineering tab's currently selected IED in a multi-IED workspace.
        device.RefreshCommandSignalProjection();
        foreach (var signal in device.Signals.Where(signal => signal.IsControlSignal && signal.IsValidControlObject))
            _signalOwners[signal] = device;

        return device;
    }

    internal async Task RefreshIoFatCommandValuesAsync(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // Preload is the same serialized live ctlModel authority used by the Engineering
        // Command Panel. StatusOnly stays read-only and never enters CommandSignals.
        await PreloadControlModelsAsync();
        device.RefreshCommandSignalProjection();

        foreach (var signal in device.Signals.Where(signal => signal.IsControlSignal && signal.IsValidControlObject))
            _signalOwners[signal] = device;

        if (device.IsConnected && device.CommandSignals.Count > 0)
            await RefreshControlValuesAsync(device, force: true);

        device.RefreshCommandSignalProjection();
    }

    internal Task ExecuteIoFatControlClaimAsync(SignalDefinition signal, ControlCommandClaim claim)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(claim);
        return ExecuteClaimedControlAsync(signal, claim);
    }
}
