using AR.Iec61850.Discovery;
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

            // Binding is intentionally cache-only. Reconciliation production is async and
            // happens in the FAT/session lifecycle; this path must never perform MMS reads
            // or block the UI while an IED is slow.
            var reconciliation = BuildEngineReconciliation(device);
            foreach (var point in iedPlan.TestPoints)
            {
                var binding = BindPoint(point, device, reconciliation);
                point.ApplyLiveBinding(binding.State, binding.Reason, device.DeviceId, binding.Reference);
                if (point.IsLiveBound)
                    signalBoundCount++;
                if (binding.State == IoTestLiveBindingState.LivePointReady)
                {
                    livePointCount++;
                    if (binding.LivePoint != null)
                    {
                        point.Runtime.CurrentValue = CanonicalFatPresentationValue(binding.LivePoint.Value);
                        point.Runtime.CurrentQuality = binding.LivePoint.Quality;
                        point.Runtime.CurrentSource = binding.LivePoint.SourceMode;
                        point.Runtime.CurrentIedTimestamp = string.IsNullOrWhiteSpace(binding.LivePoint.DeviceTimestamp) || binding.LivePoint.DeviceTimestamp == "-"
                            ? "—"
                            : binding.LivePoint.DeviceTimestamp;
                    }
                }
                else if (binding.State == IoTestLiveBindingState.SignalNotFound)
                {
                    // SignalNotFound is reserved for ARIEC61850's confirmed Absent verdict.
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

    private static EngineReconciliationContext BuildEngineReconciliation(Iec61850MonitorDevice device)
    {
        var cached = IoTestReconciliationCache.Get(device);
        return new EngineReconciliationContext(cached.Document, cached.FailureReason);
    }

    private static PointBinding BindPoint(
        IoTestPointPlan point,
        Iec61850MonitorDevice device,
        EngineReconciliationContext reconciliation)
    {
        var importedReferences = ImportedReferences(point);
        if (!point.ImportReady || importedReferences.Count == 0)
        {
            return new PointBinding(
                IoTestLiveBindingState.NotEvaluated,
                "The imported row is not ready for automatic live binding; no absence conclusion was made.",
                string.Empty,
                null);
        }

        var enginePoint = FindEngineReconciliationPoint(
            point,
            device,
            importedReferences,
            reconciliation.Document,
            out var engineAmbiguous);
        var enginePresentation = enginePoint == null
            ? null
            : IoTestReconciliationPresentation.FromEnginePoint(enginePoint);

        // Only ARIEC61850 may prove true absence. Never let cached/discovered application
        // rows override a confirmed engine Absent verdict.
        if (enginePresentation?.IsConfirmedAbsent == true)
        {
            return new PointBinding(
                IoTestLiveBindingState.SignalNotFound,
                enginePresentation.Reason,
                enginePresentation.Reference,
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
                WithEngineEvidence(
                    "Exact imported or prepared IEC 61850 reference is already active in the live monitor.",
                    enginePresentation),
                exactLivePoints[0].IecReference,
                exactLivePoints[0]);
        }

        // Static DataSet inventory has two deliberately different identities for structured
        // members: DisplayReference is the exact FCDA/FCD membership presented to the user,
        // while ObjectReference is ARIEC's resolved scalar runtime leaf. Match either exact
        // identity, but bind/capture only through the unique runtime ObjectReference.
        var exactSignals = device.Signals
            .Where(item => IsSignalEligible(item, point) &&
                           ExactSignalIdentityMatches(item, expectedReferences))
            .ToList();
        if (exactSignals.Count == 1)
        {
            var runtimeReference = NormalizeReference(exactSignals[0].ObjectReference);
            if (runtimeReference.Length > 0)
            {
                var signalLivePoints = device.Points
                    .Where(item => NormalizeReference(item.IecReference)
                        .Equals(runtimeReference, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (signalLivePoints.Count == 1)
                {
                    return new PointBinding(
                        IoTestLiveBindingState.LivePointReady,
                        WithEngineEvidence(
                            "Exact static DataSet member resolved to one active scalar runtime point.",
                            enginePresentation),
                        signalLivePoints[0].IecReference,
                        signalLivePoints[0]);
                }
            }

            return new PointBinding(
                IoTestLiveBindingState.BoundExact,
                WithEngineEvidence(
                    "Exact imported/static DataSet identity is present in the ARSAS signal workspace; its resolved runtime leaf is retained for monitor preparation.",
                    enginePresentation),
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
                WithEngineEvidence(
                    "One existing ARSAS live row matched the imported FAT row; protocol absence status remains owned by ARIEC reconciliation.",
                    enginePresentation),
                livePointCandidates[0].IecReference,
                livePointCandidates[0]);
        }

        var signalCandidates = BestSignalCandidates(
            device.Signals.Where(item => IsSignalEligible(item, point)).ToList(),
            importedReferences,
            point,
            device);
        if (signalCandidates.Count == 1)
        {
            var runtimeReference = NormalizeReference(signalCandidates[0].ObjectReference);
            if (runtimeReference.Length > 0)
            {
                var signalLivePoints = device.Points
                    .Where(item => NormalizeReference(item.IecReference)
                        .Equals(runtimeReference, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (signalLivePoints.Count == 1)
                {
                    return new PointBinding(
                        IoTestLiveBindingState.LivePointReady,
                        WithEngineEvidence(
                            "One uniquely matched static/runtime signal identity has one active scalar live point.",
                            enginePresentation),
                        signalLivePoints[0].IecReference,
                        signalLivePoints[0]);
                }
            }

            return new PointBinding(
                IoTestLiveBindingState.BoundNormalized,
                WithEngineEvidence(
                    "One existing ARSAS signal row matched the imported FAT row; protocol absence status remains owned by ARIEC reconciliation.",
                    enginePresentation),
                signalCandidates[0].ObjectReference,
                null);
        }

        if (enginePresentation != null)
        {
            return new PointBinding(
                enginePresentation.State,
                enginePresentation.Reason,
                enginePresentation.Reference,
                null);
        }

        var localReason = exactLivePoints.Count > 1 || exactSignals.Count > 1 ||
                          signalCandidates.Count > 1 || livePointCandidates.Count > 1
            ? "More than one equally strong ARSAS row matched the imported FAT point; automatic live-row binding was withheld."
            : device.Signals.Count == 0
                ? "The IED is loaded but its ARSAS signal workspace has not been populated yet."
                : "No unique ARSAS live/signal row matched this imported FAT point.";

        var engineReason = engineAmbiguous
            ? "More than one ARIEC reconciliation point matched the imported row; no absence conclusion was made."
            : string.IsNullOrWhiteSpace(reconciliation.FailureReason)
                ? "No unique ARIEC reconciliation point was associated with this imported row; no absence conclusion was made."
                : reconciliation.FailureReason + " No absence conclusion was made.";

        // A local lookup miss is explicitly NotEvaluated. It is never SignalNotFound.
        return new PointBinding(
            IoTestLiveBindingState.NotEvaluated,
            $"{localReason} {engineReason}",
            string.Empty,
            null);
    }

    private static bool ExactSignalIdentityMatches(
        SignalDefinition signal,
        IReadOnlySet<string> expectedReferences)
    {
        var runtime = NormalizeReference(signal.ObjectReference);
        if (runtime.Length > 0 && expectedReferences.Contains(runtime))
            return true;

        var display = NormalizeReference(signal.DisplayReference);
        return display.Length > 0 && expectedReferences.Contains(display);
    }

    private static Iec61850DesignLivePointReconciliation? FindEngineReconciliationPoint(
        IoTestPointPlan point,
        Iec61850MonitorDevice device,
        IReadOnlyCollection<string> importedReferences,
        Iec61850DesignLiveReconciliationDocument? document,
        out bool ambiguous)
    {
        ambiguous = false;
        if (document == null || document.Points.Count == 0)
            return null;

        var scored = document.Points
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = EngineReferences(candidate)
                    .Select(engineReference => importedReferences.Max(importedReference =>
                        IoTestReferenceMatcher.Score(
                            importedReference,
                            engineReference,
                            point.IedName,
                            device.Name,
                            device.SclIedName,
                            point.LogicalNode)))
                    .DefaultIfEmpty(0)
                    .Max()
            })
            .Where(item => item.Score > 0)
            .ToList();

        if (scored.Count == 0)
            return null;

        var bestScore = scored.Max(item => item.Score);
        var best = scored.Where(item => item.Score == bestScore).Select(item => item.Candidate).ToList();
        ambiguous = best.Count > 1;
        return best.Count == 1 ? best[0] : null;
    }

    private static IEnumerable<string> EngineReferences(Iec61850DesignLivePointReconciliation point)
    {
        if (!string.IsNullOrWhiteSpace(point.Reference))
            yield return point.Reference;
        if (!string.IsNullOrWhiteSpace(point.MmsReference))
            yield return point.MmsReference;
        if (!string.IsNullOrWhiteSpace(point.CanonicalMmsReference))
            yield return point.CanonicalMmsReference;
        if (!string.IsNullOrWhiteSpace(point.EffectiveMmsReference))
            yield return point.EffectiveMmsReference;
        if (!string.IsNullOrWhiteSpace(point.ObservedReference))
            yield return point.ObservedReference;
        if (!string.IsNullOrWhiteSpace(point.ObservedMmsReference))
            yield return point.ObservedMmsReference;
    }

    private static string WithEngineEvidence(
        string localReason,
        IoTestReconciliationPresentationResult? enginePresentation)
        => enginePresentation == null
            ? localReason
            : $"{localReason} {enginePresentation.Reason}";

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

    private static List<SignalDefinition> BestSignalCandidates(
        IReadOnlyCollection<SignalDefinition> candidates,
        IReadOnlyCollection<string> importedReferences,
        IoTestPointPlan point,
        Iec61850MonitorDevice device)
    {
        var scored = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = importedReferences.Max(reference => Math.Max(
                    IoTestReferenceMatcher.Score(
                        reference,
                        candidate.ObjectReference,
                        point.IedName,
                        device.Name,
                        device.SclIedName,
                        point.LogicalNode),
                    IoTestReferenceMatcher.Score(
                        reference,
                        candidate.DisplayReference,
                        point.IedName,
                        device.Name,
                        device.SclIedName,
                        point.LogicalNode)))
            })
            .Where(item => item.Score > 0)
            .ToList();

        if (scored.Count == 0)
            return new List<SignalDefinition>();

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
        // model reference from otherwise incomplete source metadata. Keep that exact
        // prepared row available for subsequent UI/live-point lookup only; it is not
        // evidence that an IEC 61850 design point is present or absent.
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

    private static string CanonicalFatPresentationValue(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        return bool.TryParse(text, out var boolean)
            ? boolean ? "True" : "False"
            : text;
    }

    private sealed record EngineReconciliationContext(
        Iec61850DesignLiveReconciliationDocument? Document,
        string FailureReason);

    private sealed record PointBinding(
        IoTestLiveBindingState State,
        string Reason,
        string Reference,
        Iec61850MonitorPoint? LivePoint);
}
