using AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// Central evidence policy for RCB export presentation. The policy deliberately keeps
/// unknown live evidence unknown instead of converting a missing string into NoDataSet.
/// </summary>
public static class RcbExportEvidencePolicy
{
    public static string EffectiveDataSetReference(
        string? liveReference,
        string? fallbackReference,
        MmsRcbDataSetProbeState liveProbeState = MmsRcbDataSetProbeState.NotAttempted)
    {
        // A successful live DatSet read is authoritative even when the returned value is
        // empty. Falling back in that case would resurrect a stale discovery/SCL binding
        // and contradict positive live evidence.
        if (liveProbeState == MmsRcbDataSetProbeState.ReadSucceeded)
            return (liveReference ?? string.Empty).Trim();

        return !string.IsNullOrWhiteSpace(liveReference)
            ? liveReference.Trim()
            : (fallbackReference ?? string.Empty).Trim();
    }

    public static int EffectiveMemberCount(int liveMemberCount, int fallbackMemberCount)
        => liveMemberCount > 0 ? liveMemberCount : Math.Max(0, fallbackMemberCount);

    /// <summary>
    /// True when the source SCL intentionally/legitimately leaves the ReportControl
    /// datSet association open and a successful live read shows a runtime DataSet binding.
    /// This is normal dynamic-RCB evidence and must never be presented as configuration
    /// mismatch merely because the source side is blank.
    /// </summary>
    public static bool IsDynamicRuntimeBinding(
        string? sourceReference,
        string? liveReference,
        MmsRcbDataSetProbeState liveProbeState)
        => liveProbeState == MmsRcbDataSetProbeState.ReadSucceeded &&
           string.IsNullOrWhiteSpace(NormalizeReference(sourceReference)) &&
           !string.IsNullOrWhiteSpace(NormalizeReference(liveReference));

    public static bool HasSourceLiveBindingConflict(
        string? sourceReference,
        string? liveReference,
        MmsRcbDataSetProbeState liveProbeState)
    {
        // Only positive live binding evidence may contradict a fixed source binding.
        // A failed or unattempted live read is unresolved evidence, not a mismatch.
        if (liveProbeState != MmsRcbDataSetProbeState.ReadSucceeded)
            return false;

        var source = NormalizeReference(sourceReference);
        var live = NormalizeReference(liveReference);

        // An unbound source ReportControl is not a promise that the live DatSet must stay
        // empty. Dynamic RCB workflows are allowed to bind a DataSet at runtime, so blank
        // source + populated live binding is valid evidence rather than a conflict.
        if (source.Length == 0)
            return false;

        // A fixed source binding *is* a contract. A successful live read proving no binding,
        // or proving a different binding, is therefore a real configuration mismatch.
        if (live.Length == 0)
            return true;

        return !source.Equals(live, StringComparison.OrdinalIgnoreCase);
    }

    public static string DisplayBinding(string? reference)
        => string.IsNullOrWhiteSpace(reference) ? "<none>" : reference.Trim();

    public static MmsRcbOperationalAvailability SourceAvailability(
        MmsRcbOperationalAvailability? liveAvailability,
        string? configuredDataSetName,
        bool dataSetResolved,
        int configuredMemberCount)
    {
        if (liveAvailability.HasValue)
            return liveAvailability.Value;

        var hasConfiguredBinding = !string.IsNullOrWhiteSpace(configuredDataSetName);
        if (!hasConfiguredBinding)
        {
            // A blank source datSet is valid for a dynamic RCB. Before a live DatSet read
            // proves the current runtime association, keep the state unknown/informational
            // instead of painting the row as an operational NoDataSet failure.
            return MmsRcbOperationalAvailability.Unknown;
        }

        if (!dataSetResolved)
            return MmsRcbOperationalAvailability.Unknown;

        return configuredMemberCount == 0
            ? MmsRcbOperationalAvailability.DataSetEmpty
            : MmsRcbOperationalAvailability.Unknown;
    }

    public static MmsRcbOperationalAvailability LiveModelAvailability(
        MmsRcbOperationalAvailability? liveAvailability,
        string? effectiveDataSetReference,
        bool dataSetResolved,
        int memberCount)
    {
        if (liveAvailability.HasValue)
            return liveAvailability.Value;

        if (string.IsNullOrWhiteSpace(effectiveDataSetReference))
            return MmsRcbOperationalAvailability.Unknown;

        if (dataSetResolved && memberCount == 0)
            return MmsRcbOperationalAvailability.DataSetEmpty;

        return MmsRcbOperationalAvailability.Unknown;
    }

    public static string SourceReason(
        string? configuredDataSetName,
        bool dataSetResolved,
        int configuredMemberCount,
        bool connected)
    {
        if (string.IsNullOrWhiteSpace(configuredDataSetName))
        {
            return connected
                ? "The source SCL leaves the ReportControl datSet unbound. This is valid for a dynamic RCB; use Check Availability to read the current live binding."
                : "The source SCL leaves the ReportControl datSet unbound. This can be a valid dynamic RCB; connect the IED to read the current live binding.";
        }

        if (!dataSetResolved)
            return "The source SCL ReportControl names a DataSet, but that reference does not resolve in the same Logical Node.";

        if (configuredMemberCount == 0)
            return "The source SCL ReportControl references a DataSet that is explicitly empty.";

        return connected
            ? "Press Check Availability to verify the live DatSet binding, RptEna, reservation, Owner, and DataSet directory."
            : "Offline SCL inventory only. The configured DataSet is populated; connect the IED to verify live RCB state.";
    }

    public static string LiveModelReason(
        string? effectiveDataSetReference,
        bool dataSetResolved,
        int memberCount)
    {
        if (string.IsNullOrWhiteSpace(effectiveDataSetReference))
            return "The live discovery model has not proven the RCB DatSet binding. Run Check Availability before concluding that no DataSet exists.";

        if (!dataSetResolved)
            return "A DataSet reference is known for this RCB, but the current live model has not resolved its directory.";

        if (memberCount == 0)
            return "The resolved live DataSet contains no FCDA members.";

        return "Press Check Availability to prove the current live DatSet binding and RCB ownership state.";
    }

    public static string ScopeFromReference(string? reference)
    {
        var text = (reference ?? string.Empty).Trim();
        if (text.Length == 0)
            return "Scope unknown";

        var slash = text.IndexOf('/');
        if (slash <= 0 || slash >= text.Length - 1)
            return text;

        var domain = text[..slash];
        var remainder = text[(slash + 1)..];
        var separator = remainder.IndexOfAny(['.', '$']);
        var logicalNode = separator > 0 ? remainder[..separator] : remainder;
        return string.IsNullOrWhiteSpace(logicalNode)
            ? domain
            : $"{domain} / {logicalNode}";
    }

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');
}
