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
            .Where(item => expectedReferences.Contains(NormalizeReference(item.IecReference)))
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
            .SelectMany(reference => NormalizeTelegramForms(reference, point.IedName))
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var livePointCandidates = device.Points
            .Where(item => MatchesAnyTelegram(item.IecReference, device, expectedTelegrams))
            .ToList();
        if (livePointCandidates.Count == 1)
        {
            return new PointBinding(
                IoTestLiveBindingState.LivePointReady,
                "Live point matched uniquely after normalizing the IED/Application and functional-group hierarchy.",
                livePointCandidates[0].IecReference,
                livePointCandidates[0]);
        }

        var signalCandidates = device.Signals
            .Where(item => !item.IsControlSignal &&
                           MatchesAnyTelegram(item.ObjectReference, device, expectedTelegrams))
            .ToList();
        if (signalCandidates.Count == 1)
        {
            return new PointBinding(
                IoTestLiveBindingState.BoundNormalized,
                "Discovered signal matched uniquely after normalizing the IED/Application and functional-group hierarchy.",
                signalCandidates[0].ObjectReference,
                null);
        }

        var reason = exactLivePoints.Count > 1 || exactSignals.Count > 1 ||
                     signalCandidates.Count > 1 || livePointCandidates.Count > 1
            ? "More than one live candidate matched the imported telegram; automatic binding was withheld."
            : device.Signals.Count == 0
                ? "The IED is loaded but its signal model has not been discovered yet."
                : "None of the imported IEC 61850/event-log references was found in the loaded IED model.";
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

    private static bool MatchesAnyTelegram(
        string? observedReference,
        Iec61850MonitorDevice device,
        IReadOnlySet<string> expectedTelegrams)
    {
        if (expectedTelegrams.Count == 0)
            return false;

        if (NormalizeTelegramForms(observedReference, device.Name).Any(expectedTelegrams.Contains))
            return true;

        return !string.IsNullOrWhiteSpace(device.SclIedName) &&
               NormalizeTelegramForms(observedReference, device.SclIedName).Any(expectedTelegrams.Contains);
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
        var slash = normalized.IndexOf('/');
        var name = (iedName ?? string.Empty).Trim().ToLowerInvariant();
        if (slash <= 0 || string.IsNullOrWhiteSpace(name))
            return normalized;

        var domain = normalized[..slash];
        if (!domain.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            return normalized;

        var domainSuffix = domain[name.Length..];
        var path = normalized[(slash + 1)..].TrimStart('/');

        // FAT source/report traceability can use IEDNameApplication/FunctionGroup/LN.DO.DA,
        // while the live MMS model exposes the same function group as an LN prefix.
        // "Application" is a display wrapper, not part of the live telegram identity.
        if (domainSuffix.Equals("application", StringComparison.OrdinalIgnoreCase))
            return path;

        return domainSuffix.Length == 0 ? path : domainSuffix + "/" + path;
    }

    internal static IReadOnlySet<string> NormalizeTelegramForms(string? reference, string? iedName)
    {
        var forms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = NormalizeTelegram(reference, iedName);
        if (string.IsNullOrWhiteSpace(normalized))
            return forms;

        forms.Add(normalized);
        var collapsed = CollapseDisplayHierarchy(normalized);
        if (!string.IsNullOrWhiteSpace(collapsed))
            forms.Add(collapsed);
        return forms;
    }

    private static string CollapseDisplayHierarchy(string normalizedTelegram)
    {
        var value = (normalizedTelegram ?? string.Empty).Trim();
        var firstDot = value.IndexOf('.');
        if (firstDot <= 0)
            return value;

        var logicalNodePath = value[..firstDot];
        if (!logicalNodePath.Contains('/'))
            return value;

        // Siemens/DIGSI source exports can render LN prefixes as folders, e.g.
        // ADD/GGIO1 or VI3p1_OperationalValues/RPRE_MMXU1. MMS/SCL identifies the
        // same LN as ADDGGIO1 or VI3p1_OperationalValuesRPRE_MMXU1. Collapse only
        // the pre-DO hierarchy; DO/DA separators remain untouched. Candidate
        // uniqueness is still enforced by the caller, so this is not fuzzy matching.
        var collapsedLogicalNode = logicalNodePath.Replace("/", string.Empty, StringComparison.Ordinal);
        return collapsedLogicalNode + value[firstDot..];
    }

    private sealed record PointBinding(
        IoTestLiveBindingState State,
        string Reason,
        string Reference,
        Iec61850MonitorPoint? LivePoint);
}
