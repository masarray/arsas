using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    /// <summary>
    /// Presentation-only lifecycle seed. FAT receives the current Engineering process image
    /// immediately, including scalar runtime leaves behind structured/static SCL identities.
    /// This method never enqueues Value1/Value2 evidence and never performs MMS reads.
    /// </summary>
    private void P0SeedFatFromEngineeringImageWithAliases(IoListTestingWindow fat)
    {
        FlushPendingPointUpdates();
        var bindingChanged = false;

        foreach (var ied in fat.Project.Ieds)
        {
            var device = ResolveP0FatDevice(ied);
            if (device == null)
                continue;

            foreach (var plan in ied.TestPoints)
            {
                var point = ResolveP0EngineeringImagePoint(plan, device);
                if (point == null || !P0EngineeringPointHasImage(point))
                    continue;

                ApplyP0FatLivePoint(plan.Runtime, point);

                if (plan.LiveBindingState != IoTestLiveBindingState.LivePointReady ||
                    !string.Equals(plan.LiveDeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        IoTestLiveBindingService.NormalizeReference(plan.LiveSignalReference),
                        IoTestLiveBindingService.NormalizeReference(point.IecReference),
                        StringComparison.OrdinalIgnoreCase))
                {
                    plan.ApplyLiveBinding(
                        IoTestLiveBindingState.LivePointReady,
                        "Bound to the unique shared Engineering process-image point.",
                        device.DeviceId,
                        point.IecReference);
                    bindingChanged = true;
                }
            }
        }

        if (bindingChanged)
            GetP0FatPointIndex(fat.Project, forceRebuild: true);

        fat.RefreshSharedCommandLiveValues();
    }

    private static Iec61850MonitorPoint? ResolveP0EngineeringImagePoint(
        IoTestPointPlan plan,
        Iec61850MonitorDevice device)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddP0ReferenceFamily(aliases, plan.LiveSignalReference);
        foreach (var reference in IoTestLiveBindingService.ImportedReferences(plan))
            AddP0ReferenceFamily(aliases, reference);

        if (aliases.Count == 0)
            return null;

        // First prefer the actual runtime point reference. This covers parent/static SCL
        // identities such as A.phsA -> A.phsA.cVal.mag.f and DO -> DO.stVal without fuzzy
        // prefix/Contains matching.
        var direct = device.Points
            .Where(point => P0ReferenceFamilyIntersects(aliases, point.IecReference))
            .Distinct()
            .ToArray();
        if (direct.Length == 1)
            return direct[0];

        // If the static member is a display identity, use the Engineering SignalDefinition
        // as the authoritative bridge to its exact runtime read/object reference.
        var runtimeAliases = new HashSet<string>(aliases, StringComparer.OrdinalIgnoreCase);
        foreach (var signal in device.Signals.Where(signal => !signal.IsControlSignal))
        {
            var signalAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddP0ReferenceFamily(signalAliases, signal.DisplayReference);
            AddP0ReferenceFamily(signalAliases, signal.RuntimeReadReference);
            AddP0ReferenceFamily(signalAliases, signal.ObjectReference);
            if (!signalAliases.Overlaps(aliases))
                continue;

            AddP0ReferenceFamily(runtimeAliases, signal.RuntimeReadReference);
            AddP0ReferenceFamily(runtimeAliases, signal.ObjectReference);
            AddP0ReferenceFamily(runtimeAliases, signal.DisplayReference);
        }

        var bridged = device.Points
            .Where(point => P0ReferenceFamilyIntersects(runtimeAliases, point.IecReference))
            .Distinct()
            .ToArray();
        return bridged.Length == 1 ? bridged[0] : null;
    }

    private static bool P0EngineeringPointHasImage(Iec61850MonitorPoint point)
        => point.LastUpdated != default ||
           !string.IsNullOrWhiteSpace(point.Value) ||
           !string.IsNullOrWhiteSpace(point.Quality);

    private static bool P0ReferenceFamilyIntersects(ISet<string> expected, string? reference)
    {
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddP0ReferenceFamily(actual, reference);
        return actual.Overlaps(expected);
    }

    private static void AddP0ReferenceFamily(ISet<string> aliases, string? reference)
    {
        var normalized = IoTestLiveBindingService.NormalizeReference(reference);
        if (normalized.Length == 0)
            return;

        aliases.Add(normalized);

        var suffixes = new[] { ".stVal", ".mag.f", ".instMag.f", ".cVal.mag.f" };
        foreach (var suffix in suffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                aliases.Add(normalized[..^suffix.Length]);
            else
                aliases.Add(normalized + suffix);
        }
    }
}
