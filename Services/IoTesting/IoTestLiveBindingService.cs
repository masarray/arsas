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
        if (!point.ImportReady || ImportedReferences(point).Count == 0)
        {
            return new PointBinding(
                IoTestLiveBindingState.SignalNotFound,
                "The imported row is not ready for automatic live binding.",
                string.Empty,
                null);
        }

        var expectedReferences = ImportedReferences(point)
            .Select(NormalizeReference)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var exactLivePoints = device.Points
            .Where(item => FunctionalConstraintMatches(point.FunctionalConstraint, item.FunctionalConstraint) &&
                           expectedReferences.Contains(NormalizeReference(item.IecReference)))
            .ToList();
        if (exactLivePoints.Count == 1)
        {
            return new PointBinding(
                IoTestLiveBindingState.LivePointReady,
                "Exact imported IEC 61850 reference is already active in the live monitor.",
                exactLivePoints[0].IecReference,
                exactLivePoints[0]);
        }

        var exactSignals = device.Signals
            .Where(item => !item.IsControlSignal &&
                           FunctionalConstraintMatches(point.FunctionalConstraint, item.FunctionalConstraint) &&
                           expectedReferences.Contains(NormalizeReference(item.ObjectReference)))
            .ToList();
        if (exactSignals.Count == 1)
        {
            return new PointBinding(
                IoTestLiveBindingState.BoundExact,
                "Exact imported IEC 61850 reference is present in the discovered IED model.",
                exactSignals[0].ObjectReference,
                null);
        }

        var expectedTelegrams = ImportedReferences(point)
            .Select(reference => NormalizeTelegram(reference, point.IedName))
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var livePointCandidates = device.Points
            .Where(item => FunctionalConstraintMatches(point.FunctionalConstraint, item.FunctionalConstraint) &&
                           MatchesAnyTelegram(item.IecReference, device, expectedTelegrams))
            .ToList();
        if (livePointCandidates.Count == 1)
        {
            return new PointBinding(
                IoTestLiveBindingState.LivePointReady,
                "Live point matched uniquely after normalizing IEC 61850 logical-device/display wrappers.",
                livePointCandidates[0].IecReference,
                livePointCandidates[0]);
        }

        var signalCandidates = device.Signals
            .Where(item => !item.IsControlSignal &&
                           FunctionalConstraintMatches(point.FunctionalConstraint, item.FunctionalConstraint) &&
                           MatchesAnyTelegram(item.ObjectReference, device, expectedTelegrams))
            .ToList();
        if (signalCandidates.Count == 1)
        {
            return new PointBinding(
                IoTestLiveBindingState.BoundNormalized,
                "Discovered signal matched uniquely after normalizing IEC 61850 logical-device/display wrappers.",
                signalCandidates[0].ObjectReference,
                null);
        }

        var reason = exactLivePoints.Count > 1 || exactSignals.Count > 1 ||
                     signalCandidates.Count > 1 || livePointCandidates.Count > 1
            ? "More than one live candidate matched the imported telegram; automatic binding was withheld."
            : device.Signals.Count == 0
                ? "The IED is loaded but its signal model has not been discovered yet."
                : "None of the imported IEC 61850/event-log references was found in the loaded IED model with the required functional constraint.";
        return new PointBinding(IoTestLiveBindingState.SignalNotFound, reason, string.Empty, null);
    }

    internal static IReadOnlyList<string> ImportedReferences(IoTestPointPlan point)
    {
        ArgumentNullException.ThrowIfNull(point);
        var references = new List<string>();

        void Add(string? value)
        {
            var clean = RemoveFunctionalConstraintSuffix(value);
            if (clean.Length > 0 && !references.Contains(clean, StringComparer.OrdinalIgnoreCase))
                references.Add(clean);
        }

        Add(point.ObjectReference);
        Add(point.EventLogSearchReference);
        Add(point.SourceIecReference);
        Add(point.ReportDisplayReference);

        var eventReference = !string.IsNullOrWhiteSpace(point.EventLogSearchReference)
            ? point.EventLogSearchReference.Trim()
            : point.SourceIecReference?.Trim() ?? string.Empty;
        if (eventReference.Length > 0)
        {
            var bindable = eventReference;
            if (!string.IsNullOrWhiteSpace(point.DataAttribute) &&
                !bindable.EndsWith("." + point.DataAttribute.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                bindable += "." + point.DataAttribute.Trim();
            }

            if (!bindable.Contains('/') && !string.IsNullOrWhiteSpace(point.LogicalDevice))
                bindable = point.LogicalDevice.Trim() + "/" + bindable.TrimStart('/');

            Add(bindable);
            if (!string.IsNullOrWhiteSpace(point.IedName) &&
                bindable.Contains('/') &&
                !bindable.StartsWith(point.IedName, StringComparison.OrdinalIgnoreCase))
            {
                Add(point.IedName.Trim() + bindable);
            }
        }

        return references;
    }

    private static string RemoveFunctionalConstraintSuffix(string? reference)
    {
        var value = (reference ?? string.Empty).Trim();
        var marker = value.LastIndexOf(" [", StringComparison.Ordinal);
        if (marker > 0 && value.EndsWith(']'))
            value = value[..marker].TrimEnd();
        return value;
    }

    private static bool FunctionalConstraintMatches(string? expected, string? observed)
        => string.IsNullOrWhiteSpace(expected) ||
           string.IsNullOrWhiteSpace(observed) ||
           expected.Trim().Equals(observed.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAnyTelegram(
        string? observedReference,
        Iec61850MonitorDevice device,
        IReadOnlySet<string> expectedTelegrams)
    {
        if (expectedTelegrams.Count == 0)
            return false;

        if (expectedTelegrams.Contains(NormalizeTelegram(observedReference, device.Name)))
            return true;

        return !string.IsNullOrWhiteSpace(device.SclIedName) &&
               expectedTelegrams.Contains(NormalizeTelegram(observedReference, device.SclIedName));
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
        var normalized = NormalizeReference(RemoveFunctionalConstraintSuffix(reference));
        if (normalized.Length == 0)
            return string.Empty;

        // Exact-reference matching runs before this fallback and therefore preserves the
        // logical-device identity whenever both sides expose it consistently. For FAT
        // imports, however, vendor/report paths can contain one or more display/domain
        // wrappers (for example IEDNameApplication/ADD/GGIO1...) while native MMS
        // discovery can surface the same leaf as IEDNameApplication/GGIO1... or
        // ADD/GGIO1.... Compare the LN.DO.DA tail only in this secondary path.
        // Both callers require exactly one candidate; if two LDs expose the same LN/DO/DA
        // tail, automatic binding remains ambiguous and is deliberately blocked.
        var lastSlash = normalized.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < normalized.Length - 1)
            return normalized[(lastSlash + 1)..].TrimStart('/');

        return normalized;
    }

    private sealed record PointBinding(
        IoTestLiveBindingState State,
        string Reason,
        string Reference,
        Iec61850MonitorPoint? LivePoint);
}
