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
        return BindPlans(project.Ieds, devices.ToList(), project.SignalCount);
    }

    /// <summary>
    /// Rebinds one FAT IED only. Connection preparation can run for several IEDs at the
    /// same time; a slow/offline IED must never clear another IED's already-proven live
    /// state just because a full-project refresh happened during an await boundary.
    /// </summary>
    public IoTestLiveBindingSummary BindIed(
        IoTestIedPlan ied,
        IEnumerable<Iec61850MonitorDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(ied);
        ArgumentNullException.ThrowIfNull(devices);
        return BindPlans(new[] { ied }, devices.ToList(), ied.TestPoints.Count);
    }

    private static IoTestLiveBindingSummary BindPlans(
        IReadOnlyCollection<IoTestIedPlan> plans,
        IReadOnlyCollection<Iec61850MonitorDevice> deviceList,
        int signalCount)
    {
        var deviceBoundCount = 0;
        var signalBoundCount = 0;
        var livePointCount = 0;
        var missingSignalCount = 0;

        foreach (var iedPlan in plans)
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
            plans.Count,
            deviceBoundCount,
            signalCount,
            signalBoundCount,
            livePointCount,
            missingSignalCount);
    }

    private static PointBinding BindPoint(IoTestPointPlan point, Iec61850MonitorDevice device)
    {
        var importedReferences = ImportedReferences(point);
        if (!point.ImportReady || importedReferences.Count == 0)
        {
            return new PointBinding(
                IoTestLiveBindingState.SignalNotFound,
                "The imported row is not ready for automatic live binding.",
                string.Empty,
                null);
        }

        var expectedReferences = importedReferences
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
                "Exact imported or prepared IEC 61850 reference is already active in the live monitor.",
                exactLivePoints[0].IecReference,
                exactLivePoints[0]);
        }

        var exactSignals = device.Signals
            .Where(item => IsSignalEligible(item, point) &&
                           expectedReferences.Contains(NormalizeReference(item.ObjectReference)))
            .ToList();
        if (exactSignals.Count == 1)
        {
            return new PointBinding(
                IoTestLiveBindingState.BoundExact,
                "Exact imported or prepared IEC 61850 reference is present in the discovered IED model.",
                exactSignals[0].ObjectReference,
                null);
        }

        var livePointCandidates = BestCandidates(
            device.Points,
            item => item.IecReference,
            importedReferences,
            point,
            device);
        if (livePointCandidates.Count == 1)
        {
            return new PointBinding(
                IoTestLiveBindingState.LivePointReady,
                "Live point matched uniquely using canonical IEC 61850 spelling, including IED/Application, MMS FC tokens and verified functional-group/LN boundary rules.",
                livePointCandidates[0].IecReference,
                livePointCandidates[0]);
        }

        var signalCandidates = BestCandidates(
            device.Signals.Where(item => IsSignalEligible(item, point)).ToList(),
            item => item.ObjectReference,
            importedReferences,
            point,
            device);
        if (signalCandidates.Count == 1)
        {
            return new PointBinding(
                IoTestLiveBindingState.BoundNormalized,
                "Discovered signal matched uniquely using canonical IEC 61850 spelling, including IED/Application, MMS FC tokens and verified functional-group/LN boundary rules.",
                signalCandidates[0].ObjectReference,
                null);
        }

        var reason = exactLivePoints.Count > 1 || exactSignals.Count > 1 ||
                     signalCandidates.Count > 1 || livePointCandidates.Count > 1
            ? "More than one equally strong IEC 61850 candidate matched the imported telegram; automatic binding was withheld."
            : device.Signals.Count == 0
                ? "The IED is loaded but its signal model has not been discovered yet."
                : "None of the imported IEC 61850/event-log references was found in the loaded IED model after conservative canonical matching.";
        return new PointBinding(IoTestLiveBindingState.SignalNotFound, reason, string.Empty, null);
    }

    private static bool IsSignalEligible(SignalDefinition signal, IoTestPointPlan point)
    {
        if (signal.IsControlSignal || string.IsNullOrWhiteSpace(signal.ObjectReference))
            return false;

        return string.IsNullOrWhiteSpace(point.FunctionalConstraint) ||
               string.IsNullOrWhiteSpace(signal.FunctionalConstraint) ||
               signal.FunctionalConstraint.Equals(point.FunctionalConstraint, StringComparison.OrdinalIgnoreCase);
    }

    private static List<T> BestCandidates<T>(
        IReadOnlyCollection<T> candidates,
        Func<T, string?> referenceSelector,
        IReadOnlyCollection<string> importedReferences,
        IoTestPointPlan point,
        Iec61850MonitorDevice device)
    {
        var scored = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = importedReferences.Max(reference => IoTestReferenceMatcher.Score(
                    reference,
                    referenceSelector(candidate),
                    point.IedName,
                    device.Name,
                    device.SclIedName,
                    point.LogicalNode))
            })
            .Where(item => item.Score > 0)
            .ToList();

        if (scored.Count == 0)
            return new List<T>();

        var best = scored.Max(item => item.Score);
        return scored.Where(item => item.Score == best).Select(item => item.Candidate).ToList();
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

        // During FAT preparation the signal-selection pass may prove one unique live
        // model reference from otherwise incomplete source metadata (for example a
        // legacy 7SX80 ANSI-27 row). Keep that exact prepared reference authoritative
        // for subsequent model/live-point binding. It is transient runtime state and is
        // cleared automatically whenever ApplyLiveBinding reports a non-bound result.
        if (point.IsLiveBound)
            Add(point.LiveSignalReference);

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
        => IoTestReferenceMatcher.NormalizeRaw(reference);

    internal static string NormalizeTelegram(string? reference, string? iedName)
        => IoTestReferenceMatcher.NormalizeTelegram(reference, iedName);

    internal static IReadOnlySet<string> NormalizeImportedTelegramForms(
        string? reference,
        string? iedName,
        string? logicalNode)
        => IoTestReferenceMatcher.ImportedForms(reference, iedName, logicalNode);

    private sealed record PointBinding(
        IoTestLiveBindingState State,
        string Reason,
        string Reference,
        Iec61850MonitorPoint? LivePoint);
}
