using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// IEC 61850 command identity is the full DataObject reference within one
/// IED, not a DataSet membership, RCB or WPF SignalDefinition instance.
/// </summary>
public static class Iec61850ControlIdentity
{
    public static string Normalize(string? value)
        => (value ?? string.Empty).Trim().Replace('\\', '/').Replace('$', '.');

    public static int CountSelected(IEnumerable<SignalDefinition> signals)
        => signals.Where(s => s.IsSelected && s.IsValidControlObject)
            .Select(s => Normalize(s.ObjectReference))
            .Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    // Catalog membership and operational authorization are separate concerns.
    public static SignalDefinition[] DistinctSelected(IEnumerable<SignalDefinition> signals)
        => signals.Where(s => s.IsSelected && s.IsValidControlObject)
            .GroupBy(s => Normalize(s.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(s => s.ControlCommandBusy)
                .ThenByDescending(s => s.ControlConfirmationPending)
                .ThenByDescending(s => s.ControlSupportsOperate)
                .ThenByDescending(s => s.ControlModelResolved).First())
            .ToArray();

    public static SignalDefinition[] DistinctOperable(IEnumerable<SignalDefinition> signals)
        => signals.Where(s => s.IsSelected && s.IsValidControlObject &&
                              s.ControlModelResolved && s.ControlSupportsOperate && !s.IsGenericControl)
            .GroupBy(s => Normalize(s.ObjectReference), StringComparer.OrdinalIgnoreCase)
            // Prefer an in-flight owner in legacy cached duplicate models.
            .Select(group => group.OrderByDescending(s => s.ControlCommandBusy)
                .ThenByDescending(s => s.ControlConfirmationPending).First()).ToArray();

    // Reopening a view is not a request to invalidate verified live metadata.
    // Status-only objects legitimately have no operable current-value projection.
    public static bool NeedsInspection(SignalDefinition signal, bool force)
        => !signal.ControlInspectionBusy && !signal.ControlCommandBusy &&
           (force || !signal.ControlModelResolved ||
            (signal.ControlSupportsOperate && signal.ControlCurrentValue == "-"));

    /// <summary>
    /// A control is NOT a DataSet member merely because its feedback Pos.stVal
    /// is reported. Yet a verified live CSWI.Pos may exist in another LD and
    /// must remain discoverable without changing the operator's signal selection.
    /// Candidate admission deliberately does not require a pre-proved CDC:
    /// the purpose of the bounded native inspection is to verify that CDC.
    /// No control is enabled until
    /// the exact live ctlModel and Oper structure pass native inspection.
    /// </summary>
    public static bool IsIndependentCswiCandidate(SignalDefinition signal)
        => signal.IsValidControlObject &&
           string.Equals(SignalDefinition.DetectLogicalNodeClass(signal.LogicalNode),
               "CSWI", StringComparison.OrdinalIgnoreCase) &&
           Normalize(signal.ObjectReference).EndsWith(".Pos",
               StringComparison.OrdinalIgnoreCase);

    public static SignalDefinition[] IndependentCswiInspectionCandidates(
        IEnumerable<SignalDefinition> signals, int budget = 24)
    {
        if (budget <= 0) return [];
        return signals.Where(s => !s.IsSelected && IsIndependentCswiCandidate(s))
            .GroupBy(s => Normalize(s.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(s => s.ControlModelResolved)
                .ThenByDescending(s => s.ControlSupportsOperate).First())
            .OrderBy(s => Normalize(s.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .Take(budget).ToArray();
    }

    public static SignalDefinition[] VerifiedIndependentCswiControls(
        IEnumerable<SignalDefinition> signals)
        => IndependentCswiInspectionCandidates(signals)
            .Where(s => s.ControlModelResolved && s.ControlSupportsOperate)
            .ToArray();

    /// <summary>
    /// Operator inventory != authority to Operate. An independently declared
    /// CSWI.Pos may be shown in Command Dock before native CDC verification,
    /// but must never enter VerifiedCommandTargets unless type-safe and live.
    /// CO-service evidence (or a completed live ctlModel inspection) prevents
    /// name-only and status-only aliases from masquerading as control objects.
    /// No first-24 display truncation; budget applies to read-only PROBES only.
    /// </summary>
    public static SignalDefinition[] DiscoveredIndependentCswiControls(
        IEnumerable<SignalDefinition> signals)
        => signals.Where(s => !s.IsSelected && IsIndependentCswiCandidate(s) &&
                              s.HasControlServiceEvidence)
            .GroupBy(s => Normalize(s.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(s => s.ControlSupportsOperate)
                .ThenByDescending(s => s.ControlModelResolved).First())
            .OrderBy(s => Normalize(s.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .ToArray();


    /// <summary>
    /// Read-only probe priority. These are IEC function hints, NOT commands
    /// or eligibility decisions. A switch position can be behind any vendor
    /// prefix or logical device; prefer its typed ctlModel discovery over
    /// generic channel banks so operator equipment appears promptly.
    /// Unrecognized LN classes remain in the queue.
    /// </summary>
    public static int ControlInspectionPriority(SignalDefinition signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var reference = Normalize(signal.ObjectReference);
        var dot = reference.LastIndexOf('.');
        var doName = dot < 0 ? string.Empty : reference[(dot + 1)..];
        var ln = signal.LogicalNodeClass.ToUpperInvariant();
        if (doName.Equals("Pos", StringComparison.OrdinalIgnoreCase))
            return ln switch { "CSWI" => 0, "XCBR" => 1, "XSWI" => 2, _ => 8 };
        if (doName.Equals("TapChg", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (ln == "ATCC") return 12;
        if (ln == "GGIO") return 90;
        if (ln == "CILO") return 95;
        return 40;
    }

    /// <summary>
    /// Discover control-service candidates independently of LN class, display
    /// name, DataSet membership and operator selection. CO/CF service evidence
    /// only schedules a read-only ctlModel probe; typed live Oper/SBOw status
    /// proof is still mandatory before a command is admitted. The default
    /// scan does not silently truncate very large vendor control inventories.
    /// </summary>
    public static SignalDefinition[] DiscoveredControlInspectionCandidates(
        IEnumerable<SignalDefinition> signals, int budget = int.MaxValue)
    {
        if (budget <= 0) return [];
        return signals.Where(signal => signal.IsValidControlObject &&
                                       signal.HasControlServiceEvidence &&
                                       !signal.ControlModelResolved)
            .GroupBy(signal => Normalize(signal.ObjectReference),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(signal => signal.IsSelected)
                .ThenByDescending(signal => signal.ControlSupportsOperate).First())
            .OrderByDescending(signal => signal.IsSelected)
            .ThenBy(ControlInspectionPriority)
            .ThenBy(signal => Normalize(signal.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .Take(budget).ToArray();
    }

    public static SignalDefinition[] VerifiedCommandTargets(
        IEnumerable<SignalDefinition> signals, bool admitDiscovered)
        => signals.Where(signal => signal.IsValidControlObject &&
                                   signal.ControlSupportsOperate &&
                                   !signal.IsGenericControl &&
                                   (signal.IsSelected ||
                                    (admitDiscovered && signal.HasControlServiceEvidence)))
            .GroupBy(signal => Normalize(signal.ObjectReference),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(signal => signal.ControlCommandBusy)
                .ThenByDescending(signal => signal.ControlConfirmationPending)
                .ThenByDescending(signal => signal.IsSelected).First())
            .ToArray();


    /// <summary>
    /// Presentation classification only, never Operate authority or asserted
    /// physical wiring. IEC LN class and typed CDC sort verified command
    /// objects; MMS ctlModel and Oper/SBOw proof alone grant authorization.
    /// A GGIO DPC does not become a breaker merely because it has ctlVal.
    /// </summary>
    public static (int Rank, string Role) DescribeCommandRole(SignalDefinition signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var cdc = (signal.VerifiedLiveControlCdc.Length > 0
            ? signal.VerifiedLiveControlCdc : signal.ControlCdc).Trim().ToUpperInvariant();
        var reference = Normalize(signal.ObjectReference);
        var lastDot = reference.LastIndexOf('.');
        var dataObject = lastDot < 0 ? string.Empty : reference[(lastDot + 1)..];
        var ln = signal.LogicalNodeClass.ToUpperInvariant();

        // Discovery visibility must not depend on a native live CDC result.
        // A CSWI.Pos with verified CO service declarations can legitimately
        // have an unresolved/contradictory typed CDC (e.g. SG7 DPC DO with
        // Boolean Oper.ctlVal). Rank it as an *inspection candidate* first,
        // while SignalDefinition.ControlSupportsOperate and native admission
        // remain independent and fail closed. Do NOT infer operability here.
        if (ln == "CSWI" &&
            dataObject.Equals("Pos", StringComparison.OrdinalIgnoreCase) &&
            signal.HasControlServiceEvidence)
            return (0, signal.ControlSupportsOperate
                ? "Switch controller · CSWI"
                : "Switch controller · CSWI · verification required");

        if (cdc == "DPC" && dataObject.Equals("Pos", StringComparison.OrdinalIgnoreCase))
            return ln switch
            {
                "CSWI" => (0, "Switch controller · CSWI"),
                "XCBR" => (10, "Circuit breaker · XCBR"),
                "XSWI" => (20, "Disconnector · XSWI"),
                _ => (85, "Position control · equipment unlinked")
            };

        if (cdc == "BSC" && dataObject.Equals("TapChg", StringComparison.OrdinalIgnoreCase))
            return (40, ln == "ATCC" ? "Tap changer · ATCC" : "Tap changer · equipment unlinked");

        if (cdc == "APC" && (ln == "AVCO" || ln == "AVC"))
            return (50, "Voltage control");

        if (cdc == "BSC")
            return (90, "Step control · equipment unlinked");

        // Maintenance and block commands remain available but are never
        // confused with switch position Open/Close.
        if (new[] { "MemClr", "RsStat", "EvtRcdClr", "LEDRs", "BlkOpn",
                    "BlkCls", "TagOn", "Mod", "LocSta", "CmdBlk" }
            .Contains(dataObject, StringComparer.OrdinalIgnoreCase))
            return (350, "Auxiliary / maintenance");

        if (ln == "GGIO")
            return (250, "Generic I/O · equipment unlinked");

        return (150, "Other IEC control · equipment unlinked");
    }

    public static int CommandDockPriority(SignalDefinition signal)
        => DescribeCommandRole(signal).Rank;

    public static string CommandDockRole(SignalDefinition signal)
        => DescribeCommandRole(signal).Role;

    public static SignalDefinition[] InspectionRepresentatives(IEnumerable<SignalDefinition> signals)
        => DistinctSelected(signals).Where(s => !s.ControlModelResolved).ToArray();
}
