using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public static class IoTestSessionPreflight
{
    public static IoTestSessionActionResult Validate(IoTestIedPlan? ied)
    {
        if (ied == null)
            return IoTestSessionActionResult.Failure("Select an imported IED first.");

        var unsafeEnabled = ied.TestPoints
            .Where(point => point.IsIncludedInFat && point.TestEnabled && !point.ImportReady)
            .ToList();
        if (unsafeEnabled.Count > 0)
        {
            return IoTestSessionActionResult.Failure(
                $"{unsafeEnabled.Count} included, enabled signal(s) still require import/binding review. Disable them or repair their mapping before starting FAT.");
        }

        var enabledReady = ied.TestPoints
            .Where(point => point.IsIncludedInFat && point.TestEnabled && point.ImportReady)
            .ToList();
        if (enabledReady.Count == 0)
            return IoTestSessionActionResult.Failure("No included, import-ready FAT signal is enabled for this IED.");

        var duplicateReferences = enabledReady
            .GroupBy(
                point => IoTestLiveBindingService.NormalizeReference(
                    string.IsNullOrWhiteSpace(point.LiveSignalReference)
                        ? point.ObjectReference
                        : point.LiveSignalReference),
                StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .ToList();
        if (duplicateReferences.Count > 0)
        {
            var first = duplicateReferences[0];
            var ids = string.Join(", ", first.Select(point => point.TestPointId));
            return IoTestSessionActionResult.Failure(
                $"One live IEC 61850 reference is assigned to multiple included test points ({ids}). Resolve the duplicate mapping before FAT so one edge cannot produce multiple results.");
        }

        return IoTestSessionActionResult.Success(
            $"{enabledReady.Count} included signal(s) passed FAT session preflight.");
    }
}
