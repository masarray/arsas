using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record IoTestLiveBindingSummary(
    int IedCount,
    int DeviceBoundCount,
    int SignalCount,
    int SignalBoundCount,
    int LivePointCount,
    int MissingSignalCount);

public sealed class IoTestLiveBindingService
{
    public IoTestLiveBindingSummary Bind(
        IoTestProject project,
        IEnumerable<Iec61850MonitorDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(devices);

        var deviceList = devices.ToList();
        var deviceBoundCount = 0;
        var signalBoundCount = 0;
        var livePointCount = 0;
        var missingSignalCount = 0;

        foreach (var iedPlan in project.Ieds)
        {
            var device = FindDevice(iedPlan, deviceList);
            if (device == null)
            {
                iedPlan.ApplyLiveDeviceBinding(null, "Not connected");
                foreach (var point in iedPlan.TestPoints)
                {
                    point.ApplyLiveBinding(
                        IoTestLiveBindingState.DeviceNotLoaded,
                        "Load or connect the imported IED before starting its FAT session.");
                }
                continue;
            }

            deviceBoundCount++;
            iedPlan.ApplyLiveDeviceBinding(
                device.DeviceId,
                device.IsMonitoring
                    ? $"Monitoring · {device.AcquisitionMode}"
                    : device.IsConnected
                        ? "Connected · preparing live acquisition"
                        : "Workspace model ready",
                device.IsConnected,
                device.IsMonitoring);

            foreach (var point in iedPlan.TestPoints)
            {
                var binding = BindPoint(point, device);
                point.ApplyLiveBinding(binding.State, binding.Reason, device.DeviceId, binding.Reference);
                if (point.IsLiveBound)
                    signalBoundCount++;
                if (binding.State == IoTestLiveBindingState.LivePointReady)
                {
                    livePointCount++;
                    if (binding.LivePoint != null)
                    {
                        point.Runtime.CurrentValue = binding.LivePoint.Value;
                        point.Runtime.CurrentQuality = binding.LivePoint.Quality;
                        point.Runtime.CurrentSource = binding.LivePoint.SourceMode;
                        point.Runtime.CurrentIedTimestamp = string.IsNullOrWhiteSpace(binding.LivePoint.DeviceTimestamp) || binding.LivePoint.DeviceTimestamp == "-"
                            ? "—"
                            : binding.LivePoint.DeviceTimestamp;
                    }
                }
                else if (binding.State == IoTestLiveBindingState.SignalNotFound)
                {
                    missingSignalCount++;
                }
            }
        }

        return new IoTestLiveBindingSummary(
            project.Ieds.Count,
            deviceBoundCount,
            project.SignalCount,
            signalBoundCount,
            livePointCount,
            missingSignalCount);
    }

    private static PointBinding BindPoint(IoTestPointPlan point, Iec61850MonitorDevice device)
    {
        if (!point.ImportReady || string.IsNullOrWhiteSpace(point.ObjectReference))
        {
            return new PointBinding(
                IoTestLiveBindingState.SignalNotFound,
                "The imported row is not ready for automatic live binding.",
                string.Empty,
                null);
        }

        var expected = NormalizeReference(point.ObjectReference);
        var exactLivePoint = device.Points.FirstOrDefault(item =>
            NormalizeReference(item.IecReference).Equals(expected, StringComparison.OrdinalIgnoreCase));
        if (exactLivePoint != null)
        {
            return new PointBinding(
                IoTestLiveBindingState.LivePointReady,
                "Exact imported object reference is already active in the live monitor.",
                exactLivePoint.IecReference,
                exactLivePoint);
        }

        var exactSignal = device.Signals.FirstOrDefault(item =>
            !item.IsControlSignal &&
            NormalizeReference(item.ObjectReference).Equals(expected, StringComparison.OrdinalIgnoreCase));
        if (exactSignal != null)
        {
            return new PointBinding(
                IoTestLiveBindingState.BoundExact,
                "Exact imported object reference is present in the discovered IED model.",
                exactSignal.ObjectReference,
                null);
        }

        var expectedTelegram = NormalizeTelegram(point.ObjectReference, point.IedName);
        var livePointCandidates = device.Points
            .Where(item => NormalizeTelegram(item.IecReference, device.Name)
                .Equals(expectedTelegram, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (livePointCandidates.Count == 1)
        {
            return new PointBinding(
                IoTestLiveBindingState.LivePointReady,
                "Live point matched after normalizing the IED-name prefix.",
                livePointCandidates[0].IecReference,
                livePointCandidates[0]);
        }

        var signalCandidates = device.Signals
            .Where(item => !item.IsControlSignal &&
                NormalizeTelegram(item.ObjectReference, device.Name)
                    .Equals(expectedTelegram, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (signalCandidates.Count == 1)
        {
            return new PointBinding(
                IoTestLiveBindingState.BoundNormalized,
                "Discovered signal matched after normalizing the IED-name prefix.",
                signalCandidates[0].ObjectReference,
                null);
        }

        var reason = signalCandidates.Count > 1 || livePointCandidates.Count > 1
            ? "More than one live candidate matched the imported telegram; automatic binding was withheld."
            : device.Signals.Count == 0
                ? "The IED is loaded but its signal model has not been discovered yet."
                : "The imported object reference was not found in the loaded IED model.";
        return new PointBinding(IoTestLiveBindingState.SignalNotFound, reason, string.Empty, null);
    }

    private static Iec61850MonitorDevice? FindDevice(
        IoTestIedPlan plan,
        IReadOnlyCollection<Iec61850MonitorDevice> devices)
    {
        var exact = devices.FirstOrDefault(device =>
            DeviceNameMatches(device, plan.IedName) &&
            device.IpAddress.Equals(plan.IpAddress, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        var nameMatches = devices.Where(device => DeviceNameMatches(device, plan.IedName)).ToList();
        if (nameMatches.Count == 1)
            return nameMatches[0];

        var ipMatches = devices.Where(device =>
            device.IpAddress.Equals(plan.IpAddress, StringComparison.OrdinalIgnoreCase)).ToList();
        return ipMatches.Count == 1 ? ipMatches[0] : null;
    }

    private static bool DeviceNameMatches(Iec61850MonitorDevice device, string iedName)
        => device.Name.Equals(iedName, StringComparison.OrdinalIgnoreCase) ||
           device.SclIedName.Equals(iedName, StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeReference(string? reference)
        => (reference ?? string.Empty)
            .Trim()
            .Replace('$', '.')
            .Replace("..", ".")
            .TrimEnd('.')
            .ToLowerInvariant();

    internal static string NormalizeTelegram(string? reference, string? iedName)
    {
        var normalized = NormalizeReference(reference);
        var slash = normalized.IndexOf('/');
        var name = (iedName ?? string.Empty).Trim().ToLowerInvariant();
        if (slash <= 0 || string.IsNullOrWhiteSpace(name))
            return normalized;

        var domain = normalized[..slash];
        if (domain.StartsWith(name, StringComparison.OrdinalIgnoreCase) && domain.Length > name.Length)
            return domain[name.Length..] + normalized[slash..];
        return normalized;
    }

    private sealed record PointBinding(
        IoTestLiveBindingState State,
        string Reason,
        string Reference,
        Iec61850MonitorPoint? LivePoint);
}
