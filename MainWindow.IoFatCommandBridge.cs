using System.Diagnostics;
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
        var stopwatch = Stopwatch.StartNew();
        var fallbackRead = false;

        // Preload is the same serialized live ctlModel authority used by the Engineering
        // Command Panel. StatusOnly stays read-only and never enters CommandSignals.
        await PreloadControlModelsAsync();
        device.RefreshCommandSignalProjection();

        foreach (var signal in device.Signals.Where(signal => signal.IsControlSignal && signal.IsValidControlObject))
            _signalOwners[signal] = device;

        // FAT is only a projection of the already-running Engineering session. Index the
        // shared process image first and immediately seed command rows from its current
        // report/poll values. Do not issue a second forced MMS read for values Engineering
        // already owns; the normal refresh path is retained only as a fail-safe for rows
        // whose status value is still unavailable.
        RebuildControlFeedbackIndex(device);
        ProjectIoFatCommandValuesFromSharedProcessImage(device);

        if (device.IsConnected && device.CommandSignals.Any(signal =>
                signal.ControlCurrentValue == "-" ||
                string.IsNullOrWhiteSpace(signal.ControlCurrentValue) ||
                signal.ControlModelText == "Auto-detect"))
        {
            fallbackRead = true;
            await RefreshControlValuesAsync(device, force: false);
        }

        device.RefreshCommandSignalProjection();
        RebuildControlFeedbackIndex(device);

        // A report may have advanced the process image while the fallback inspection was
        // running. Re-apply the shared image last so LIVE VALUE always reflects the same
        // report-backed state that Engineering presents, not an older inspection sample.
        var projected = ProjectIoFatCommandValuesFromSharedProcessImage(device);
        Trace.WriteLine(
            $"[IO FAT P0] Command values refresh completed in {stopwatch.ElapsedMilliseconds} ms; " +
            $"device={device.Name}; projected={projected}; fallbackRead={fallbackRead}.");
    }

    private int ProjectIoFatCommandValuesFromSharedProcessImage(Iec61850MonitorDevice device)
    {
        if (device.CommandSignals.Count == 0 || device.Points.Count == 0)
            return 0;

        var latestByReference = device.Points
            .Where(point => !string.IsNullOrWhiteSpace(point.IecReference))
            .GroupBy(point => NormalizeReference(point.IecReference), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(point => point.Sequence).First(),
                StringComparer.OrdinalIgnoreCase);

        var projected = 0;
        foreach (var signal in device.CommandSignals)
        {
            if (string.IsNullOrWhiteSpace(signal.ControlStatusReference))
                continue;

            var key = NormalizeReference(signal.ControlStatusReference);
            if (!latestByReference.TryGetValue(key, out var point))
                continue;

            var value = point.Value?.Trim() ?? string.Empty;
            if (value.Length == 0 || value == "-")
                continue;

            signal.ControlCurrentValue = value;
            projected++;
        }

        return projected;
    }

    internal async Task ExecuteIoFatControlClaimAsync(SignalDefinition signal, ControlCommandClaim claim)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(claim);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await ExecuteClaimedControlAsync(signal, claim);
            Trace.WriteLine(
                $"[IO FAT P0] Command completed in {stopwatch.ElapsedMilliseconds} ms; " +
                $"signal={signal.DisplayReference}; current={signal.ControlCurrentValue}; model={signal.ControlModelText}.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[IO FAT P0] Command failed after {stopwatch.ElapsedMilliseconds} ms; " +
                $"signal={signal.DisplayReference}; error={ex.Message}.");
            throw;
        }
    }
}
