using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public static class IoTestSessionPreflight
{
    public static IoTestSessionActionResult Validate(IoTestIedPlan? ied)
    {
        if (ied == null)
            return IoTestSessionActionResult.Failure("Select an imported IED first.");

        // First retire aliases that can already be proven redundant from persisted/source
        // identity. This remains the cheapest normal path.
        IoFatEngineeringSelectionBridge.RetireRedundantManualWorkspaceRows(ied);

        var unsafeEnabled = ied.TestPoints
            .Where(point => point.WorkspaceSelected && point.IsIncludedInFat && point.TestEnabled && !point.ImportReady)
            .ToList();
        if (unsafeEnabled.Count > 0)
        {
            return IoTestSessionActionResult.Failure(
                $"{unsafeEnabled.Count} shared-workspace-selected FAT signal(s) still require import/binding review. Disable their TEST scope or repair their mapping before starting FAT.");
        }

        var enabledReady = EnabledReadyPoints(ied);
        if (enabledReady.Count == 0)
            return IoTestSessionActionResult.Failure("No shared-workspace-selected, included, import-ready FAT signal has TEST enabled for this IED.");

        // Field P0: same-source continuation can restore an old scl-manual-* row whose
        // historical BindingStatus/source spelling no longer identifies it as a workspace
        // alias. Once live binding proves that this row and an authoritative Static DataSet
        // member resolve to the exact same primary live IEC 61850 reference, keeping both
        // enabled would make one physical edge capable of producing two FAT results.
        //
        // Repair only that exact, proven shadow case. We deliberately do NOT relax the
        // duplicate guard: unknown/legacy non-manual duplicates still fail closed, and
        // duplicate static rows must still satisfy the distinct-membership fan-out rule.
        var retiredLiveAliases = RetireProvenStaticDataSetShadowAliases(enabledReady);
        if (retiredLiveAliases > 0)
            enabledReady = EnabledReadyPoints(ied);

        var duplicateReferences = DuplicateReferenceGroups(enabledReady);
        if (duplicateReferences.Count > 0)
        {
            var first = duplicateReferences[0];
            var ids = string.Join(", ", first.Select(point => point.TestPointId));
            return IoTestSessionActionResult.Failure(
                $"One live IEC 61850 reference '{first.Key}' is assigned to multiple enabled test points in the shared included FAT scope ({ids}). Resolve the duplicate mapping before FAT so one edge cannot produce multiple results.");
        }

        var repairText = retiredLiveAliases == 0
            ? string.Empty
            : $" Retired {retiredLiveAliases} stale manual live-reference alias(es) in favor of Static DataSet authority.";
        return IoTestSessionActionResult.Success(
            $"{enabledReady.Count} shared-workspace-selected FAT signal(s) passed session preflight.{repairText}");
    }

    private static List<IoTestPointPlan> EnabledReadyPoints(IoTestIedPlan ied)
        => ied.TestPoints
            .Where(point => point.WorkspaceSelected && point.IsIncludedInFat && point.TestEnabled && point.ImportReady)
            .ToList();

    private static List<IGrouping<string, IoTestPointPlan>> DuplicateReferenceGroups(
        IReadOnlyCollection<IoTestPointPlan> points)
        => points
            .GroupBy(EffectiveLiveReference, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) &&
                            group.Count() > 1 &&
                            !IsDistinctSclDataSetMembershipFanOut(group))
            .ToList();

    private static int RetireProvenStaticDataSetShadowAliases(
        IReadOnlyCollection<IoTestPointPlan> enabledReady)
    {
        var changed = 0;
        foreach (var group in enabledReady
                     .GroupBy(EffectiveLiveReference, StringComparer.OrdinalIgnoreCase)
                     .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1))
        {
            var rows = group.ToList();
            var staticRows = rows
                .Where(IoTestSignalSelectionService.IsSclDataSetAuthority)
                .ToList();
            if (staticRows.Count == 0)
                continue;

            var nonStaticRows = rows
                .Where(point => !IoTestSignalSelectionService.IsSclDataSetAuthority(point))
                .ToList();
            if (nonStaticRows.Count == 0 || nonStaticRows.Any(point => !IsGeneratedManualWorkspaceAlias(point)))
                continue;

            // Exact runtime identity is already proven by the grouping key. Preserve the
            // manual row, TEST preference, FAT disposition and evidence for audit/history;
            // only remove its shared workspace ownership so the Static DataSet member is
            // the sole evidence authority for this live leaf.
            foreach (var alias in nonStaticRows)
            {
                if (!alias.WorkspaceSelected)
                    continue;
                alias.WorkspaceSelected = false;
                changed++;
            }
        }

        return changed;
    }

    private static bool IsGeneratedManualWorkspaceAlias(IoTestPointPlan point)
        => IoTestSignalSelectionService.IsSclWorkspaceAuthority(point) ||
           point.TestPointId.StartsWith("scl-manual-", StringComparison.OrdinalIgnoreCase);

    private static string EffectiveLiveReference(IoTestPointPlan point)
        => IoTestLiveBindingService.NormalizeReference(
            string.IsNullOrWhiteSpace(point.LiveSignalReference)
                ? point.ObjectReference
                : point.LiveSignalReference);

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
