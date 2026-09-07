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

        // Initialize safety defaults from the complete discovered signal collection before
        // CommandSignals is rebuilt. This closes the first-frame FAT race where Sync could
        // otherwise be rendered from the model's old false value before the projection event.
        EnsureP0CommandDefaultsForDevice(device);
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

        EnsureP0CommandDefaultsForDevice(device);

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
            foreach (var reference in ExactCommandFeedbackCandidates(signal))
            {
                var key = NormalizeReference(reference);
                if (!latestByReference.TryGetValue(key, out var point))
                    continue;

                var value = point.Value?.Trim() ?? string.Empty;
                if (value.Length == 0 || value == "-")
                    continue;

                signal.ControlCurrentValue = value;
                projected++;
                break;
            }
        }

        return projected;
    }

    private static IEnumerable<string> ExactCommandFeedbackCandidates(SignalDefinition signal)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new[]
        {
            signal.ControlStatusReference,
            signal.ObjectReference,
            signal.DisplayReference
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            var value = candidate.Trim();
            if (seen.Add(value))
                yield return value;
        }

        // Some static DataSets report a composite CDC object such as CSWI1.Pos while the
        // control inspector records the status leaf CSWI1.Pos.stVal. Only that exact parent
        // is accepted; deliberately do not use prefix/Contains matching because XCBR1.Pos
        // and CSWI1.Pos can coexist in the same IED and must never cross-feed each other.
        var statusReference = signal.ControlStatusReference?.Trim();
        if (!string.IsNullOrWhiteSpace(statusReference) &&
            statusReference.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase))
        {
            var parent = statusReference[..^".stVal".Length];
            if (seen.Add(parent))
                yield return parent;
        }
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
