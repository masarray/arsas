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

            // SCL identity and operator selection have one authority shared by Engineering
            // and FAT. When Engineering has already completed the import choice, its exact
            // checkbox state (including an intentionally empty selection) initializes FAT.
            // A brand-new FAT entry point instead starts from the imported static DataSet.
            // FatDisposition remains orthogonal and is never changed here.
            if (preserveExistingEngineeringSelection)
            {
                if (point.TestEnabled != signal.IsSelected)
                {
                    point.TestEnabled = signal.IsSelected;
                    changed++;
                }
            }
            else
            {
                if (!point.TestEnabled)
                {
                    point.TestEnabled = true;
                    changed++;
                }

                if (!signal.IsSelected)
                {
                    signal.IsSelected = true;
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

        // P0.2: TestEnabled is the shared selection state. FAT disposition is deliberately
        // orthogonal: Remove from FAT must not silently unselect the Engineering signal, and
        // Restore must not silently reselect it. This preserves the checkbox exactly across
        // Removed Signals while the dedicated restore action remains the only disposition
        // authority.
        var selected = point.TestEnabled;
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

        // P0.2: Engineering selection shares the operator checkbox state, not FAT
        // disposition authority. A row explicitly removed from FAT must stay in Removed
        // Signals when Engineering is toggled off/on. Only the dedicated Removed Signals
        // restore action may change ExcludedByOperator back to Included. This also means a
        // later restore preserves the latest operator checkbox state without resurrecting
        // evidence scope as a side effect of workspace navigation.
        var changed = false;
        foreach (var point in ied.TestPoints.Where(IoTestSignalSelectionService.IsSclDataSetAuthority))
        {
            if (!ReferenceEquals(FindSignal(point, device), signal))
                continue;

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
