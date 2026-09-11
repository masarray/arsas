using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Canonicalizes the automatic Engineering -> FAT static DataSet workspace.
/// Historical scl-manual-* rows are evidence/provenance input only: they must never
/// survive as a second active FAT row beside the authoritative static DataSet member.
/// </summary>
public static class IoFatCanonicalEvidenceMigrationService
{
    private const string EngineeringProjectionStaticDataSetAuthority = "ENGINEERING_SCL_DATASET_AUTHORITY";

    public sealed record Result(
        int RemovedManualRows,
        int MigratedEvidenceRows,
        int AmbiguousEvidenceRows);

    public static Result MigrateAndRemoveLegacyManualRows(IoTestProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var removed = 0;
        var migrated = 0;
        var ambiguous = 0;

        foreach (var ied in project.Ieds)
        {
            // Engineering projection deliberately carries a provenance-specific binding
            // status before bootstrap. Normalize that status to the shared static DataSet
            // authority contract before any legacy migration or runtime matching. Without
            // this step, field projects can contain 58 canonical Engineering rows plus
            // restored scl-manual-* history, while the migration incorrectly concludes
            // there is no static authority and skips the IED entirely.
            var canonicalRows = ied.TestPoints
                .Where(IsCanonicalStaticDataSetAuthority)
                .ToArray();
            if (canonicalRows.Length == 0)
                continue;

            foreach (var canonical in canonicalRows)
            {
                if (IsEngineeringProjectionStaticDataSetAuthority(canonical))
                {
                    canonical.BindingStatus = IoTestSignalSelectionService.SclDataSetAuthorityBindingStatus;
                }
            }

            var manualRows = ied.TestPoints
                .Where(IsLegacyManualWorkspaceRow)
                .ToArray();

            foreach (var manual in manualRows)
            {
                var runtimeReference = FirstNonEmpty(
                    manual.LiveSignalReference,
                    manual.ObjectReference,
                    manual.EventLogSearchReference,
                    manual.SourceIecReference,
                    manual.ReportDisplayReference);

                var canonicalMatches = IoFatEngineeringSelectionBridge
                    .FindStaticDataSetRuntimeCoverage(
                        ied,
                        runtimeReference,
                        manual.FunctionalConstraint)
                    .ToArray();

                if (canonicalMatches.Length == 1)
                {
                    if (MigrateEvidenceOnly(manual, canonicalMatches[0]))
                        migrated++;
                }
                else if (canonicalMatches.Length > 1 && HasEvidence(manual))
                {
                    // A legacy scalar can cover more than one distinct static membership.
                    // Never guess which canonical row owns old evidence. Keep the source
                    // snapshot as audit history and require fresh evidence for those rows.
                    ambiguous++;
                }

                // Removal is intentional even when evidence cannot be mapped uniquely.
                // The automatic Engineering FAT path is a static DataSet projection; a
                // historical manual alias is not a second IEC/SCL row authority.
                if (ied.TestPoints.Remove(manual))
                    removed++;
            }
        }

        return new Result(removed, migrated, ambiguous);
    }

    public static bool IsLegacyManualWorkspaceRow(IoTestPointPlan point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return point.TestPointId.StartsWith("scl-manual-", StringComparison.OrdinalIgnoreCase) ||
               IoTestSignalSelectionService.IsSclWorkspaceAuthority(point);
    }

    private static bool IsCanonicalStaticDataSetAuthority(IoTestPointPlan point)
        => IoTestSignalSelectionService.IsSclDataSetAuthority(point) ||
           IsEngineeringProjectionStaticDataSetAuthority(point);

    private static bool IsEngineeringProjectionStaticDataSetAuthority(IoTestPointPlan point)
        => string.Equals(
            point.BindingStatus,
            EngineeringProjectionStaticDataSetAuthority,
            StringComparison.OrdinalIgnoreCase);

    private static bool MigrateEvidenceOnly(IoTestPointPlan source, IoTestPointPlan target)
    {
        var changed = false;

        if (target.Runtime.Value1Evidence is null && source.Runtime.Value1Evidence is not null)
        {
            target.Runtime.Value1Evidence = source.Runtime.Value1Evidence;
            changed = true;
        }

        if (target.Runtime.Value2Evidence is null && source.Runtime.Value2Evidence is not null)
        {
            target.Runtime.Value2Evidence = source.Runtime.Value2Evidence;
            changed = true;
        }

        if (target.Runtime.OnEvidence is null && source.Runtime.OnEvidence is not null)
        {
            target.Runtime.OnEvidence = source.Runtime.OnEvidence;
            changed = true;
        }

        if (target.Runtime.OffEvidence is null && source.Runtime.OffEvidence is not null)
        {
            target.Runtime.OffEvidence = source.Runtime.OffEvidence;
            changed = true;
        }

        if (!target.Runtime.IsComplete && source.Runtime.IsComplete)
        {
            target.Runtime.State = source.Runtime.State;
            target.Runtime.StatusReason = source.Runtime.StatusReason;
            changed = true;
        }

        if (source.Runtime.Attempt > target.Runtime.Attempt)
        {
            target.Runtime.Attempt = source.Runtime.Attempt;
            changed = true;
        }

        return changed;
    }

    private static bool HasEvidence(IoTestPointPlan point)
        => point.Runtime.Value1Evidence is not null ||
           point.Runtime.Value2Evidence is not null ||
           point.Runtime.OnEvidence is not null ||
           point.Runtime.OffEvidence is not null ||
           point.Runtime.IsComplete;

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
