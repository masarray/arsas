using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Keeps a direct-SCL FAT plan and the Engineering signal catalog on one selection
/// authority.  The exact static DataSet membership row is the bridge identity; its
/// resolved ObjectReference remains the shared acquisition target.
/// </summary>
public static class IoFatEngineeringSelectionBridge
{
    public static int Initialize(
        IoTestIedPlan ied,
        Iec61850MonitorDevice device,
        bool preserveExistingEngineeringSelection)
    {
        ArgumentNullException.ThrowIfNull(ied);
        ArgumentNullException.ThrowIfNull(device);

        var changed = 0;
        foreach (var point in ied.TestPoints.Where(IoTestSignalSelectionService.IsSclDataSetAuthority))
        {
            var signal = FindSignal(point, device);
            if (signal is null)
                continue;

            if (preserveExistingEngineeringSelection)
            {
                var enabled = signal.IsSelected && point.IsIncludedInFat;
                if (point.TestEnabled != enabled)
                {
                    point.TestEnabled = enabled;
                    changed++;
                }
            }
            else
            {
                var selected = point.TestEnabled && point.IsIncludedInFat;
                if (signal.IsSelected != selected)
                {
                    signal.IsSelected = selected;
                    changed++;
                }
            }
        }

        device.RecountSelectedSignals();
        return changed;
    }

    public static bool ApplyFatPointSelection(
        IoTestPointPlan point,
        Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(device);
        if (!IoTestSignalSelectionService.IsSclDataSetAuthority(point))
            return false;

        var signal = FindSignal(point, device);
        if (signal is null)
            return false;

        var selected = point.TestEnabled && point.IsIncludedInFat;
        if (signal.IsSelected == selected)
            return false;

        signal.IsSelected = selected;
        device.RecountSelectedSignals();
        return true;
    }

    public static bool ApplyEngineeringSignalSelection(
        SignalDefinition signal,
        bool selected,
        IoTestIedPlan ied,
        Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(ied);
        ArgumentNullException.ThrowIfNull(device);

        var changed = false;
        foreach (var point in ied.TestPoints.Where(IoTestSignalSelectionService.IsSclDataSetAuthority))
        {
            if (!ReferenceEquals(FindSignal(point, device), signal))
                continue;

            if (selected && !point.IsIncludedInFat)
            {
                point.RestoreToFat();
                changed = true;
            }
            if (point.TestEnabled != selected)
            {
                point.TestEnabled = selected;
                changed = true;
            }
        }
        return changed;
    }

    public static SignalDefinition? FindSignal(
        IoTestPointPlan point,
        Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(device);

        var staticReferences = new[]
            {
                point.SourceIecReference,
                point.ReportDisplayReference,
                point.EventLogSearchReference
            }
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(IoTestLiveBindingService.NormalizeReference)
            .Where(reference => reference.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dataSet = IoTestLiveBindingService.NormalizeReference(point.DataSetName);

        var exactMembership = device.Signals
            .Where(signal => staticReferences.Contains(
                IoTestLiveBindingService.NormalizeReference(signal.DisplayReference)))
            .Where(signal => dataSet.Length == 0 ||
                             dataSet.Equals(
                                 IoTestLiveBindingService.NormalizeReference(signal.DataSetReference),
                                 StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactMembership.Length == 1)
            return exactMembership[0];

        var runtime = IoTestLiveBindingService.NormalizeReference(point.ObjectReference);
        if (runtime.Length == 0)
            return null;

        var runtimeMatches = device.Signals
            .Where(signal => runtime.Equals(
                IoTestLiveBindingService.NormalizeReference(signal.ObjectReference),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return runtimeMatches.Length == 1 ? runtimeMatches[0] : null;
    }
}
