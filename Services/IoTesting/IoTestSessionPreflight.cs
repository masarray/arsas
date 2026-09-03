using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public static class IoTestSessionPreflight
{
    public static IoTestSessionActionResult Validate(IoTestIedPlan? ied)
    {
        if (ied == null)
            return IoTestSessionActionResult.Failure("Select an imported IED first.");

        var unsafeEnabled = ied.TestPoints
            .Where(point => point.WorkspaceSelected && point.IsIncludedInFat && point.TestEnabled && !point.ImportReady)
            .ToList();
        if (unsafeEnabled.Count > 0)
        {
            return IoTestSessionActionResult.Failure(
                $"{unsafeEnabled.Count} shared-workspace-selected FAT signal(s) still require import/binding review. Disable their TEST scope or repair their mapping before starting FAT.");
        }

        var enabledReady = ied.TestPoints
            .Where(point => point.WorkspaceSelected && point.IsIncludedInFat && point.TestEnabled && point.ImportReady)
            .ToList();
        if (enabledReady.Count == 0)
            return IoTestSessionActionResult.Failure("No shared-workspace-selected, included, import-ready FAT signal has TEST enabled for this IED.");

        var duplicateReferences = enabledReady
            .GroupBy(
                point => IoTestLiveBindingService.NormalizeReference(
                    string.IsNullOrWhiteSpace(point.LiveSignalReference)
                        ? point.ObjectReference
                        : point.LiveSignalReference),
                StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) &&
                            group.Count() > 1 &&
                            !IsDistinctSclDataSetMembershipFanOut(group))
            .ToList();
        if (duplicateReferences.Count > 0)
        {
            var first = duplicateReferences[0];
            var ids = string.Join(", ", first.Select(point => point.TestPointId));
            return IoTestSessionActionResult.Failure(
                $"One live IEC 61850 reference is assigned to multiple TEST-enabled points in the shared included FAT scope ({ids}). Resolve the duplicate mapping before FAT so one edge cannot produce multiple results.");
        }

        return IoTestSessionActionResult.Success(
            $"{enabledReady.Count} shared-workspace-selected FAT signal(s) passed session preflight.");
    }

    private static bool IsDistinctSclDataSetMembershipFanOut(IEnumerable<IoTestPointPlan> points)
    {
        var rows = points.ToList();
        if (rows.Count < 2 || rows.Any(point => !IoTestSignalSelectionService.IsSclDataSetAuthority(point)))
            return false;

        // Static membership identity is source + DataSet + member index/reference. Runtime
        // leaf identity is deliberately excluded: distinct FCDA/FCD memberships are allowed
        // to share one engine-proven primary leaf and must remain separate FAT rows.
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in rows)
        {
            if (string.IsNullOrWhiteSpace(point.SignalAddress) ||
                string.IsNullOrWhiteSpace(point.DataSetName) ||
                point.SourceRow <= 0 ||
                string.IsNullOrWhiteSpace(point.SourceIecReference))
            {
                return false;
            }

            var identity = $"{point.SignalAddress.Trim()}|{point.DataSetName.Trim()}|{point.SourceRow}|{point.SourceIecReference.Trim()}";
            if (!identities.Add(identity))
                return false;
        }

        return identities.Count == rows.Count;
    }
}
