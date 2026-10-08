using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Association-local, exact-RCB report-stream evidence. Observational only:
/// the decoder and acquisition pipeline remain authoritative for process values.
/// SqNum/SubSqNum are 16-bit unsigned counters per IEC 61850-7-2, §17.2.3.2;
/// EntryID is an opaque BRCB resume cursor, NOT a numeric monotonic counter.
/// </summary>
internal sealed class Iec61850ReportContinuityState
{
    public ulong? LastSequenceNumber { get; set; }
    public ulong? SegmentedSequenceNumber { get; set; }
    public ulong? LastSubSequenceNumber { get; set; }
    public bool AwaitingMoreSegments { get; set; }
    public ulong? ConfigurationRevision { get; set; }
    public string LastEntryIdHex { get; set; } = string.Empty;
}

internal static class Iec61850ReportContinuityInspector
{
    private const ulong MaxSequenceNumber = ushort.MaxValue;

    /// <summary>
    /// A gap/backward move is an OBSERVED discontinuity, not proof of a specific
    /// lost event: resubscription, replay, server restart and report queueing may
    /// be relevant. Do not mistake a GI or a new EntryID for a validated reset.
    /// </summary>
    internal static IReadOnlyList<string> Observe(
        Iec61850ReportContinuityState state,
        NativeReportFrameMetadata frame,
        bool buffered)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(frame);

        List<string>? findings = null;
        void Warn(string message) => (findings ??= new List<string>(2)).Add(message);

        var priorEntryIdPresent = !string.IsNullOrEmpty(state.LastEntryIdHex);
        var entryIdPresent = !string.IsNullOrEmpty(frame.EntryIdHex);
        var entryIdChanged = priorEntryIdPresent && entryIdPresent &&
            !string.Equals(state.LastEntryIdHex, frame.EntryIdHex, StringComparison.OrdinalIgnoreCase);
        var entryContext = buffered
            ? $"; BRCB EntryID previousPresent={priorEntryIdPresent}, currentPresent={entryIdPresent}, changed={entryIdChanged}"
            : string.Empty;

        if (frame.BufferOverflow == true)
        {
            Warn(buffered
                ? "BRCB BufOvfl=true: possible loss of buffered entries; continuity cannot be certified from SqNum alone"
                : "URCB carried unexpected BufOvfl=true; inspect report OptionFields/decoder attribution");
        }

        if (frame.ConfRev.HasValue)
        {
            if (state.ConfigurationRevision.HasValue &&
                state.ConfigurationRevision.Value != frame.ConfRev.Value)
            {
                Warn($"Report ConfRev changed: previous={state.ConfigurationRevision.Value}, " +
                     $"current={frame.ConfRev.Value}; DataSet schema and member bindings require revalidation");
            }
            state.ConfigurationRevision = frame.ConfRev;
        }

        if (frame.SequenceNumber is { } current)
        {
            if (current > MaxSequenceNumber)
            {
                Warn($"Report SqNum={current} exceeds IEC 61850 INT16U range; " +
                     "decoder/metadata requires inspection; continuity unverified");
                // Keep prior baseline, since accepting an out-of-range counter would
                // make subsequent legitimate 16-bit values spuriously comparable.
            }
            else if (frame.SubSequenceNumber.HasValue)
            {
                var sub = frame.SubSequenceNumber.Value;
                if (sub > MaxSequenceNumber)
                    Warn($"Report SubSqNum={sub} exceeds IEC 61850 INT16U range; continuity unverified");

                if (state.AwaitingMoreSegments)
                {
                    var previousSub = state.LastSubSequenceNumber.GetValueOrDefault();
                    if (state.SegmentedSequenceNumber != current ||
                        previousSub == MaxSequenceNumber ||
                        sub != previousSub + 1)
                        Warn($"Segmented report discontinuity: expected sqNum={state.SegmentedSequenceNumber}, " +
                             $"subSqNum={previousSub + 1}; received sqNum={current}, subSqNum={sub}" + entryContext);
                }
                else
                {
                    if (sub != 0)
                        Warn($"Segmented report starts at SubSqNum={sub}, expected=0; " +
                             "prior segment evidence is absent" + entryContext);
                    CheckSequence(state.LastSequenceNumber, current, Warn, entryContext);
                }

                if (!frame.MoreSegmentsFollow.HasValue)
                    Warn("Segmented report has SubSqNum but MoreSegmentsFollow is absent; continuity unverified");

                state.SegmentedSequenceNumber = current;
                state.LastSubSequenceNumber = sub;
                state.AwaitingMoreSegments = frame.MoreSegmentsFollow == true;
                if (!state.AwaitingMoreSegments)
                {
                    state.LastSequenceNumber = current;
                    state.SegmentedSequenceNumber = null;
                    state.LastSubSequenceNumber = null;
                }
            }
            else
            {
                if (state.AwaitingMoreSegments)
                    Warn($"Segmented report was interrupted before continuation of " +
                         $"sqNum={state.SegmentedSequenceNumber}, subSqNum={state.LastSubSequenceNumber}" +
                         entryContext);
                state.AwaitingMoreSegments = false;
                state.SegmentedSequenceNumber = null;
                state.LastSubSequenceNumber = null;
                CheckSequence(state.LastSequenceNumber, current, Warn, entryContext);
                state.LastSequenceNumber = current;
            }
        }
        else if (frame.SubSequenceNumber.HasValue || frame.MoreSegmentsFollow == true)
        {
            Warn("Segmented report metadata has no SqNum; segment continuity cannot be checked");
        }

        if (entryIdPresent)
            state.LastEntryIdHex = frame.EntryIdHex;

        return findings ?? Array.Empty<string>();
    }

    private static void CheckSequence(
        ulong? previous,
        ulong current,
        Action<string> warn,
        string entryContext)
    {
        if (!previous.HasValue)
            return; // First observed report is not necessarily the first server report.

        var prior = previous.Value;
        if ((prior < MaxSequenceNumber && current == prior + 1) ||
            (prior == MaxSequenceNumber && current == 0))
            return;

        var classification = current == prior ? "duplicate/replay" :
            current < prior ? "backward/reset/replay" : "forward gap";
        warn($"Report sequence discontinuity ({classification}): previous={prior}, " +
             $"current={current}; RptEna/reconnect/GI context is not yet verified" +
             entryContext + "; no event-loss conclusion");
    }
}
