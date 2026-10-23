using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Re-projects existing RCB display objects from the live monitor's current
/// caller-owned set. This is NOT an RCB read, subscription, process-value
/// store, or occupancy inference from received Report frames.
/// </summary>
public static class IedBrowserRcbLiveOwnershipPresentation
{
    private const string CallerEvidence =
        "Successfully activated by the current ARSAS monitor association.";

    public static int Reconcile(IReadOnlyList<RcbExportRow> rows,
        IReadOnlySet<string> activeReferences, bool connected)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(activeReferences);
        var owned = connected
            ? activeReferences.Select(Normalize)
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = 0;
        foreach (var row in rows)
        {
            var own = owned.Contains(Normalize(row.Reference));
            if (own)
            {
                // Association's successful activation, not the mere existence
                // of an RCB or a past Report, is the ownership authority.
                if (row.Availability != MmsRcbOperationalAvailability.UsedByCaller ||
                    !string.Equals(row.Reason, CallerEvidence,
                        StringComparison.Ordinal))
                {
                    row.Availability = MmsRcbOperationalAvailability.UsedByCaller;
                    row.StatusText = "ARSAS active";
                    row.Reason = CallerEvidence;
                    changed++;
                }
            }
            else if (row.Availability == MmsRcbOperationalAvailability.UsedByCaller &&
                string.Equals(row.Reason, CallerEvidence, StringComparison.Ordinal))
            {
                // Stop/reconnect invalidates ONLY our injected ownership
                // evidence. An independent read-only RCB snapshot is not
                // rewritten, and a remote client's InUse state is preserved.
                row.Availability = MmsRcbOperationalAvailability.Unknown;
                row.StatusText = "Unknown";
                row.Reason = "Current ARSAS monitor ownership ended; live RCB occupancy not checked.";
                changed++;
            }
        }
        return changed;
    }

    private static string Normalize(string? reference)
        => (reference ?? "").Trim().Replace('$', '.');
}
