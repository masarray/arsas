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
    // A decoded value without raw OptionFields provenance cannot certify that
    // the prior wire report advertised an EntryID. Do not treat it as comparable.
    public bool LastEntryIdProvenByOptFlds { get; set; }

    // All counts belong to this exact report stream within the current
    // association. No static state, no process/control-plane side effects.
    public string ReportControlReference { get; set; } = string.Empty;
    public string DataSetReference { get; set; } = string.Empty;
    public string ReportId { get; set; } = string.Empty;
    public bool Buffered { get; set; }
    public long FramesSeen { get; set; }
    public long SequencedFrames { get; set; }
    public long MissingSequenceFrames { get; set; }
    public long SegmentedFrames { get; set; }
    public long AnomalyFindings { get; set; }
    // Frame counts stay independent of findings: one frame may carry multiple anomalies.
    public long AnomalousFrames { get; set; }
    // Bounded per-association receive-order evidence. No raw event history or timers.
    public long FramesAfterLastFinding { get; set; }
    public DateTimeOffset? FirstFrameReceivedAtUtc { get; set; }
    public DateTimeOffset? LastFrameReceivedAtUtc { get; set; }
    public DateTimeOffset? LastFindingReceivedAtUtc { get; set; }
    // Head + tail chronology: fixed-size per BRCB and allocated only on demand.
    public Iec61850BrcbChronologyFrame[]? InitialChronology { get; set; }
    public int InitialChronologyCount { get; set; }
    public Iec61850BrcbChronologyFrame[]? RecentChronology { get; set; }
    public int RecentChronologyCount { get; set; }
    public int RecentChronologyNext { get; set; }
    // Retain the last eight anomalous BRCB frames even if they fall in the
    // omitted middle of a long healthy stream. O(1) memory per buffered RCB.
    public Iec61850BrcbChronologyFrame[]? AnomalyChronology { get; set; }
    public int AnomalyChronologyCount { get; set; }
    public int AnomalyChronologyNext { get; set; }
    public long SequenceDiscontinuities { get; set; }
    public long SequenceWithChangedEntryId { get; set; }
    public long SequenceWithRepeatedEntryId { get; set; }
    public long SequenceWithoutComparableEntryId { get; set; }
    public long SequenceWithGiReason { get; set; }
    public long SequenceWithOverflow { get; set; }
    public long BufferOverflows { get; set; }
    public long ConfRevChanges { get; set; }
    public long EntryIdPresentFrames { get; set; }
    public ulong? FirstSequenceNumber { get; set; }
    public long OptFldsDecodedFrames { get; set; }
    public long OptFldsUnknownFrames { get; set; }
    public long SqNumAdvertisedFrames { get; set; }
    public long SqNumOmittedFrames { get; set; }
    public long SqNumAdvertisedMissingFrames { get; set; }
    public long EntryIdAdvertisedFrames { get; set; }
    public long EntryIdOmittedFrames { get; set; }
    public long EntryIdAdvertisedEmptyFrames { get; set; }
    public string LastOptFldsHex { get; set; } = string.Empty;
    public bool? RptEnaWriteAccepted { get; set; }
    public bool? GiWriteAccepted { get; set; }
    public DateTimeOffset? ActivationReturnedAtUtc { get; set; }
    public long GiReasonFrames { get; set; }
    public long IntegrityReasonFrames { get; set; }
    public long ReasonUnavailableFrames { get; set; }
    public ulong? LastAnomalyPriorSqNum { get; set; }
    public ulong? LastAnomalyCurrentSqNum { get; set; }
    public bool? LastAnomalyGiReason { get; set; }
    public DateTimeOffset? LastAnomalyReceivedAtUtc { get; set; }
}

/// <summary>
/// Decoder-metadata-only evidence: no raw EntryID, process values, or packet bodies.
/// FrameOrdinal is receive order, not a server event sequence or TCP packet index.
/// </summary>
public readonly record struct Iec61850BrcbChronologyFrame
{
    public long FrameOrdinal { get; init; }
    public DateTimeOffset? ReceivedAtUtc { get; init; }
    public ulong? SequenceNumber { get; init; }
    public bool? GiReason { get; init; }
    public bool? BufferOverflow { get; init; }
    public string? EntryIdRelation { get; init; }
    public int FindingCount { get; init; }
}

/// <summary>
/// Immutable copyable diagnostic evidence. Counts describe decoded metadata,
/// NOT completeness of SOE delivery or absence of lost events.
/// </summary>
public sealed record Iec61850ReportContinuityStreamSnapshot
{
    public string Rcb { get; init; } = string.Empty;
    public string DataSet { get; init; } = string.Empty;
    public string ReportId { get; init; } = string.Empty;
    public bool Buffered { get; init; }
    public long Frames { get; init; }
    public long Sequenced { get; init; }
    public long SequenceMissing { get; init; }
    public long Segmented { get; init; }
    public long Findings { get; init; }
    public long AnomalousFrames { get; init; }
    public long FramesAfterLastFinding { get; init; }
    public DateTimeOffset? FirstFrameReceivedAtUtc { get; init; }
    public DateTimeOffset? LastFrameReceivedAtUtc { get; init; }
    public DateTimeOffset? LastFindingReceivedAtUtc { get; init; }
    public IReadOnlyList<Iec61850BrcbChronologyFrame> Chronology { get; init; } =
        Array.Empty<Iec61850BrcbChronologyFrame>();
    public int ChronologyInitialCount { get; init; }
    public int ChronologyRecentCount { get; init; }
    public long ChronologyOmittedFrames { get; init; }
    public IReadOnlyList<Iec61850BrcbChronologyFrame> RecentAnomalies { get; init; } =
        Array.Empty<Iec61850BrcbChronologyFrame>();
    public long SequenceDiscontinuities { get; init; }
    public long SequenceWithChangedEntryId { get; init; }
    public long SequenceWithRepeatedEntryId { get; init; }
    public long SequenceWithoutComparableEntryId { get; init; }
    public long SequenceWithGiReason { get; init; }
    public long SequenceWithOverflow { get; init; }
    public long Overflow { get; init; }
    public long ConfRevChanges { get; init; }
    public long EntryIdPresent { get; init; }
    public ulong? FirstSqNum { get; init; }
    public ulong? LastSqNum { get; init; }
    public long OptFldsDecoded { get; init; }
    public long OptFldsUnknown { get; init; }
    public long SqNumAdvertised { get; init; }
    public long SqNumOmitted { get; init; }
    public long SqNumAdvertisedMissing { get; init; }
    public long EntryIdAdvertised { get; init; }
    public long EntryIdOmitted { get; init; }
    // An empty decoded EntryID could also represent an empty OCTET STRING; this
    // counter alone never attributes the cause to wire loss or decoder failure.
    public long EntryIdEmptyOrUnprojected { get; init; }
    public string LastOptFldsHex { get; init; } = string.Empty;
    public bool? RptEnaWriteAccepted { get; init; }
    public bool? GiWriteAccepted { get; init; }
    public DateTimeOffset? ActivationReturnedAtUtc { get; init; }
    public long GiReasonFrames { get; init; }
    public long IntegrityReasonFrames { get; init; }
    public long ReasonUnavailableFrames { get; init; }
    public ulong? LastAnomalyPriorSqNum { get; init; }
    public ulong? LastAnomalyCurrentSqNum { get; init; }
    public bool? LastAnomalyGiReason { get; init; }
    public DateTimeOffset? LastAnomalyReceivedAtUtc { get; init; }
}

public sealed record Iec61850ReportContinuitySnapshot
{
    public long ProcessUpdatesSeen { get; init; }
    public int StreamCount { get; init; }
    public int UntrackedStreamCount { get; init; }
    public long Frames { get; init; }
    public long Sequenced { get; init; }
    public long Findings { get; init; }
    public long AnomalousFrames { get; init; }
    public long SequenceDiscontinuities { get; init; }
    public long Overflows { get; init; }
    public long OptFldsDecoded { get; init; }
    public long OptFldsUnknown { get; init; }
    public IReadOnlyList<Iec61850ReportContinuityStreamSnapshot> Streams { get; init; } =
        Array.Empty<Iec61850ReportContinuityStreamSnapshot>();
}


internal static class Iec61850ReportContinuityInspector
{
    private const ulong MaxSequenceNumber = ushort.MaxValue;
    private const int InitialChronologyLimit = 32;
    private const int RecentChronologyLimit = 8;
    private const int AnomalyChronologyLimit = 8;

    /// <summary>
    /// One concrete RCB owns its sequence history within this association.
    /// Optional RptID/DataSet aliases do NOT identify independent streams.
    /// </summary>
    internal static string StreamKey(string? plannedRcb, NativeReportFrameMetadata frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var rcb = string.IsNullOrWhiteSpace(frame.ReportControlReference)
            ? plannedRcb : frame.ReportControlReference;
        return (rcb ?? string.Empty).Trim().Replace('$', '.')
            .Replace("..", ".").ToLowerInvariant();
    }

    private static string CanonicalDataSet(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.')
            .Replace("..", ".");

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

        if (state.FramesSeen < long.MaxValue) state.FramesSeen++;
        if (frame.ReceivedAt != default)
        {
            state.FirstFrameReceivedAtUtc ??= frame.ReceivedAt;
            state.LastFrameReceivedAtUtc = frame.ReceivedAt;
        }
        if (frame.SequenceNumber.HasValue)
        {
            if (state.SequencedFrames < long.MaxValue) state.SequencedFrames++;
            if (frame.SequenceNumber.Value <= MaxSequenceNumber)
                state.FirstSequenceNumber ??= frame.SequenceNumber;
        }
        else if (state.MissingSequenceFrames < long.MaxValue) state.MissingSequenceFrames++;
        if (frame.SubSequenceNumber.HasValue && state.SegmentedFrames < long.MaxValue)
            state.SegmentedFrames++;
        if (!string.IsNullOrEmpty(frame.EntryIdHex) && state.EntryIdPresentFrames < long.MaxValue)
            state.EntryIdPresentFrames++;
        if (frame.GeneralInterrogationReasonSeen == true && state.GiReasonFrames < long.MaxValue)
            state.GiReasonFrames++;
        if (frame.IntegrityReasonSeen == true && state.IntegrityReasonFrames < long.MaxValue)
            state.IntegrityReasonFrames++;
        if (!frame.GeneralInterrogationReasonSeen.HasValue &&
            !frame.IntegrityReasonSeen.HasValue &&
            state.ReasonUnavailableFrames < long.MaxValue)
            state.ReasonUnavailableFrames++;

        List<string>? findings = null;
        void Warn(string message)
        {
            (findings ??= new List<string>(2)).Add(message);
            if (state.AnomalyFindings < long.MaxValue) state.AnomalyFindings++;
        }

        // Optional metadata variations cannot create another independent RCB.
        if (!string.IsNullOrWhiteSpace(frame.ReportId))
        {
            if (!string.IsNullOrWhiteSpace(state.ReportId) &&
                !string.Equals(state.ReportId, frame.ReportId, StringComparison.Ordinal))
                Warn("RptID changed under the same RCB; continuity must be requalified");
            state.ReportId = frame.ReportId;
        }
        if (!string.IsNullOrWhiteSpace(frame.DataSetReference))
        {
            if (!string.IsNullOrWhiteSpace(state.DataSetReference) &&
                !string.Equals(CanonicalDataSet(state.DataSetReference),
                    CanonicalDataSet(frame.DataSetReference), StringComparison.OrdinalIgnoreCase))
                Warn("DataSet changed under the same RCB; schema and continuity must be requalified");
            state.DataSetReference = frame.DataSetReference;
        }

        var priorEntryIdPresent = !string.IsNullOrEmpty(state.LastEntryIdHex);
        var priorEntryIdProven = state.LastEntryIdProvenByOptFlds;
        var entryIdPresent = !string.IsNullOrEmpty(frame.EntryIdHex);
        var entryIdChanged = priorEntryIdPresent && entryIdPresent &&
            !string.Equals(state.LastEntryIdHex, frame.EntryIdHex, StringComparison.OrdinalIgnoreCase);
        // Avoid formatting provenance on every healthy InformationReport:
        // materialize it only when there is an actual continuity finding.
        string EntryContext() => buffered
            ? $"; BRCB EntryID previousPresent={priorEntryIdPresent}, currentPresent={entryIdPresent}, changed={entryIdChanged}"
            : string.Empty;
        // This context is observational: a successful GI=true write or GI
        // inclusion reason may correlate with SqNum reset but can never
        // certify replay correctness / absence of lost buffered entries.
        string CausalContext() =>
            $"; activation RptEna={WriteVerdict(state.RptEnaWriteAccepted)}, " +
            $"GIwrite={WriteVerdict(state.GiWriteAccepted)}, " +
            $"GIreason={WriteVerdict(frame.GeneralInterrogationReasonSeen)}, " +
            $"startReturnUtc={state.ActivationReturnedAtUtc?.ToString("O") ?? "unknown"}, " +
            $"frameReceivedUtc={frame.ReceivedAt:O}; GI correlation is not proof of a harmless reset";

        // One causal classification per actually observed SqNum discontinuity.
        // EntryID is an opaque octet string, never a numeric continuity proof.
        void RecordSequenceCorrelation()
        {
            if (state.SequenceDiscontinuities < long.MaxValue)
                state.SequenceDiscontinuities++;
            if (buffered && priorEntryIdPresent && entryIdPresent &&
                state.LastEntryIdProvenByOptFlds && frame.OptFldsEntryId == true &&
                IsBoundedHex(frame.OptFldsRawHex))
            {
                if (entryIdChanged)
                {
                    if (state.SequenceWithChangedEntryId < long.MaxValue)
                        state.SequenceWithChangedEntryId++;
                }
                else if (state.SequenceWithRepeatedEntryId < long.MaxValue)
                    state.SequenceWithRepeatedEntryId++;
            }
            else if (state.SequenceWithoutComparableEntryId < long.MaxValue)
                state.SequenceWithoutComparableEntryId++;

            if (frame.GeneralInterrogationReasonSeen == true &&
                state.SequenceWithGiReason < long.MaxValue)
                state.SequenceWithGiReason++;
            if (frame.BufferOverflow == true && state.SequenceWithOverflow < long.MaxValue)
                state.SequenceWithOverflow++;
        }

        // Raw OptFlds is authoritative decoder evidence. Unknown stays UNKNOWN;
        // a missing value cannot be interpreted as an omitted wire field.
        var hasOptFlds = frame.OptFldsSequenceNumber.HasValue &&
                         frame.OptFldsEntryId.HasValue &&
                         IsBoundedHex(frame.OptFldsRawHex);
        if (hasOptFlds)
        {
            if (state.OptFldsDecodedFrames < long.MaxValue) state.OptFldsDecodedFrames++;
            state.LastOptFldsHex = frame.OptFldsRawHex;
            if (frame.OptFldsSequenceNumber == true)
            {
                if (state.SqNumAdvertisedFrames < long.MaxValue) state.SqNumAdvertisedFrames++;
                if (!frame.SequenceNumber.HasValue)
                {
                    if (state.SqNumAdvertisedMissingFrames < long.MaxValue)
                        state.SqNumAdvertisedMissingFrames++;
                    Warn("Report OptFlds advertises SqNum, but ARIEC decoded no SqNum; inspect decoder and on-wire field completeness");
                }
            }
            else
            {
                if (state.SqNumOmittedFrames < long.MaxValue) state.SqNumOmittedFrames++;
                if (frame.SequenceNumber.HasValue)
                    Warn("Report OptFlds omits SqNum, but decoded frame supplied SqNum; metadata provenance is inconsistent");
            }

            if (frame.OptFldsEntryId == true)
            {
                if (state.EntryIdAdvertisedFrames < long.MaxValue) state.EntryIdAdvertisedFrames++;
                if (!entryIdPresent && state.EntryIdAdvertisedEmptyFrames < long.MaxValue)
                    state.EntryIdAdvertisedEmptyFrames++;
            }
            else
            {
                if (state.EntryIdOmittedFrames < long.MaxValue) state.EntryIdOmittedFrames++;
                if (entryIdPresent)
                    Warn("Report OptFlds omits EntryID, but decoded frame supplied EntryID; metadata provenance is inconsistent");
            }

            if (frame.OptFldsBufferOverflow == false && frame.BufferOverflow.HasValue)
                Warn("Report OptFlds omits BufOvfl, but decoded frame supplied it; metadata provenance is inconsistent");
            if (frame.OptFldsConfRev == false && frame.ConfRev.HasValue)
                Warn("Report OptFlds omits ConfRev, but decoded frame supplied it; metadata provenance is inconsistent");
        }
        else if (state.OptFldsUnknownFrames < long.MaxValue)
        {
            state.OptFldsUnknownFrames++;
        }

        if (frame.BufferOverflow == true)
        {
            if (state.BufferOverflows < long.MaxValue) state.BufferOverflows++;
            Warn(buffered
                ? "BRCB BufOvfl=true: possible loss of buffered entries; continuity cannot be certified from SqNum alone"
                : "URCB carried unexpected BufOvfl=true; inspect report OptionFields/decoder attribution");
        }

        if (frame.ConfRev.HasValue)
        {
            if (state.ConfigurationRevision.HasValue &&
                state.ConfigurationRevision.Value != frame.ConfRev.Value)
            {
                if (state.ConfRevChanges < long.MaxValue) state.ConfRevChanges++;
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
                             $"subSqNum={previousSub + 1}; received sqNum={current}, subSqNum={sub}" + EntryContext());
                }
                else
                {
                    if (sub != 0)
                        Warn($"Segmented report starts at SubSqNum={sub}, expected=0; " +
                             "prior segment evidence is absent" + EntryContext());
                    var discontinuity = DescribeSequenceAnomaly(state.LastSequenceNumber, current);
                    if (discontinuity is not null)
                    {
                        CaptureAnomaly(state, frame);
                        RecordSequenceCorrelation();
                        Warn(discontinuity + EntryContext() + CausalContext());
                    }
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
                         EntryContext());
                state.AwaitingMoreSegments = false;
                state.SegmentedSequenceNumber = null;
                state.LastSubSequenceNumber = null;
                var discontinuity = DescribeSequenceAnomaly(state.LastSequenceNumber, current);
                if (discontinuity is not null)
                {
                    CaptureAnomaly(state, frame);
                    RecordSequenceCorrelation();
                    Warn(discontinuity + EntryContext() + CausalContext());
                }
                state.LastSequenceNumber = current;
            }
        }
        else if (frame.SubSequenceNumber.HasValue || frame.MoreSegmentsFollow == true)
        {
            Warn("Segmented report metadata has no SqNum; segment continuity cannot be checked");
        }

        if (entryIdPresent)
        {
            state.LastEntryIdHex = frame.EntryIdHex;
            state.LastEntryIdProvenByOptFlds = hasOptFlds && frame.OptFldsEntryId == true;
        }
        else
        {
            // Missing current EntryID invalidates comparisons with future frames;
            // never compare across an opaque, unobserved resume cursor.
            state.LastEntryIdHex = string.Empty;
            state.LastEntryIdProvenByOptFlds = false;
        }

        if (buffered)
        {
            // Observational classification only. Both OptFlds must prove the
            // opaque cursor to label successive values as changed or repeated.
            var comparableEntryId = priorEntryIdPresent && entryIdPresent &&
                priorEntryIdProven && hasOptFlds && frame.OptFldsEntryId == true;
            var relation = !entryIdPresent ? "absent" :
                !comparableEntryId ? "unverified" : entryIdChanged ? "changed" : "repeated";
            var evidence = new Iec61850BrcbChronologyFrame
            {
                FrameOrdinal = state.FramesSeen,
                ReceivedAtUtc = frame.ReceivedAt == default ? null : frame.ReceivedAt,
                SequenceNumber = frame.SequenceNumber,
                GiReason = frame.GeneralInterrogationReasonSeen,
                BufferOverflow = frame.BufferOverflow,
                EntryIdRelation = relation,
                FindingCount = findings?.Count ?? 0
            };
            RecordChronology(state, evidence);
            if (evidence.FindingCount > 0)
                RecordAnomalyChronology(state, evidence);
        }

        if (findings is not null)
        {
            if (state.AnomalousFrames < long.MaxValue)
                state.AnomalousFrames++;
            state.FramesAfterLastFinding = 0;
            // Missing decoder timestamps remain unknown; do not reuse older time.
            state.LastFindingReceivedAtUtc = frame.ReceivedAt == default ? null : frame.ReceivedAt;
        }
        else if (state.AnomalousFrames > 0 && state.FramesAfterLastFinding < long.MaxValue)
        {
            // Receive-order observation only; no claim of event delivery completeness.
            state.FramesAfterLastFinding++;
        }
        return findings is null ? Array.Empty<string>() : findings;
    }

    internal static Iec61850ReportContinuitySnapshot Snapshot(
        IReadOnlyDictionary<string, Iec61850ReportContinuityState> streams,
        long processUpdatesSeen,
        int untrackedStreamCount,
        int maxShown = 12)
    {
        ArgumentNullException.ThrowIfNull(streams);
        var ordered = streams.Values
            .OrderBy(state => state.ReportControlReference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(state => state.ReportId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(state => state.DataSetReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var shown = ordered.Take(Math.Clamp(maxShown, 0, 32)).Select(state =>
            new Iec61850ReportContinuityStreamSnapshot
            {
                Rcb = state.ReportControlReference,
                DataSet = state.DataSetReference,
                ReportId = state.ReportId,
                Buffered = state.Buffered,
                Frames = state.FramesSeen,
                Sequenced = state.SequencedFrames,
                SequenceMissing = state.MissingSequenceFrames,
                Segmented = state.SegmentedFrames,
                Findings = state.AnomalyFindings,
                AnomalousFrames = state.AnomalousFrames,
                FramesAfterLastFinding = state.FramesAfterLastFinding,
                FirstFrameReceivedAtUtc = state.FirstFrameReceivedAtUtc,
                LastFrameReceivedAtUtc = state.LastFrameReceivedAtUtc,
                LastFindingReceivedAtUtc = state.LastFindingReceivedAtUtc,
                Chronology = CopyChronology(state),
                RecentAnomalies = CopyAnomalyChronology(state),
                ChronologyInitialCount = state.InitialChronologyCount,
                ChronologyRecentCount = state.RecentChronologyCount,
                ChronologyOmittedFrames = state.Buffered
                    ? Math.Max(0, state.FramesSeen - state.InitialChronologyCount - state.RecentChronologyCount)
                    : 0,
                SequenceDiscontinuities = state.SequenceDiscontinuities,
                SequenceWithChangedEntryId = state.SequenceWithChangedEntryId,
                SequenceWithRepeatedEntryId = state.SequenceWithRepeatedEntryId,
                SequenceWithoutComparableEntryId = state.SequenceWithoutComparableEntryId,
                SequenceWithGiReason = state.SequenceWithGiReason,
                SequenceWithOverflow = state.SequenceWithOverflow,
                Overflow = state.BufferOverflows,
                ConfRevChanges = state.ConfRevChanges,
                EntryIdPresent = state.EntryIdPresentFrames,
                FirstSqNum = state.FirstSequenceNumber,
                LastSqNum = state.LastSequenceNumber,
                OptFldsDecoded = state.OptFldsDecodedFrames,
                OptFldsUnknown = state.OptFldsUnknownFrames,
                SqNumAdvertised = state.SqNumAdvertisedFrames,
                SqNumOmitted = state.SqNumOmittedFrames,
                SqNumAdvertisedMissing = state.SqNumAdvertisedMissingFrames,
                EntryIdAdvertised = state.EntryIdAdvertisedFrames,
                EntryIdOmitted = state.EntryIdOmittedFrames,
                EntryIdEmptyOrUnprojected = state.EntryIdAdvertisedEmptyFrames,
                LastOptFldsHex = state.LastOptFldsHex,
                RptEnaWriteAccepted = state.RptEnaWriteAccepted,
                GiWriteAccepted = state.GiWriteAccepted,
                ActivationReturnedAtUtc = state.ActivationReturnedAtUtc,
                GiReasonFrames = state.GiReasonFrames,
                IntegrityReasonFrames = state.IntegrityReasonFrames,
                ReasonUnavailableFrames = state.ReasonUnavailableFrames,
                LastAnomalyPriorSqNum = state.LastAnomalyPriorSqNum,
                LastAnomalyCurrentSqNum = state.LastAnomalyCurrentSqNum,
                LastAnomalyGiReason = state.LastAnomalyGiReason,
                LastAnomalyReceivedAtUtc = state.LastAnomalyReceivedAtUtc
            }).ToArray();
        return new Iec61850ReportContinuitySnapshot
        {
            ProcessUpdatesSeen = processUpdatesSeen,
            StreamCount = ordered.Length,
            UntrackedStreamCount = untrackedStreamCount,
            Frames = ordered.Sum(state => state.FramesSeen),
            Sequenced = ordered.Sum(state => state.SequencedFrames),
            Findings = ordered.Sum(state => state.AnomalyFindings),
            AnomalousFrames = ordered.Sum(state => state.AnomalousFrames),
            SequenceDiscontinuities = ordered.Sum(state => state.SequenceDiscontinuities),
            Overflows = ordered.Sum(state => state.BufferOverflows),
            OptFldsDecoded = ordered.Sum(state => state.OptFldsDecodedFrames),
            OptFldsUnknown = ordered.Sum(state => state.OptFldsUnknownFrames),
            Streams = shown
        };
    }

    private static void RecordChronology(
        Iec61850ReportContinuityState state, Iec61850BrcbChronologyFrame item)
    {
        if (state.InitialChronologyCount < InitialChronologyLimit)
        {
            var initial = state.InitialChronology ??= new Iec61850BrcbChronologyFrame[InitialChronologyLimit];
            initial[state.InitialChronologyCount++] = item;
        }
        else
        {
            var recent = state.RecentChronology ??= new Iec61850BrcbChronologyFrame[RecentChronologyLimit];
            recent[state.RecentChronologyNext] = item;
            state.RecentChronologyNext = (state.RecentChronologyNext + 1) % RecentChronologyLimit;
            if (state.RecentChronologyCount < RecentChronologyLimit)
                state.RecentChronologyCount++;
        }
    }

    private static void RecordAnomalyChronology(
        Iec61850ReportContinuityState state, Iec61850BrcbChronologyFrame item)
    {
        var ring = state.AnomalyChronology ??=
            new Iec61850BrcbChronologyFrame[AnomalyChronologyLimit];
        ring[state.AnomalyChronologyNext] = item;
        state.AnomalyChronologyNext =
            (state.AnomalyChronologyNext + 1) % AnomalyChronologyLimit;
        if (state.AnomalyChronologyCount < AnomalyChronologyLimit)
            state.AnomalyChronologyCount++;
    }

    private static IReadOnlyList<Iec61850BrcbChronologyFrame> CopyAnomalyChronology(
        Iec61850ReportContinuityState state)
    {
        var count = state.AnomalyChronologyCount;
        if (count == 0)
            return Array.Empty<Iec61850BrcbChronologyFrame>();
        var copy = new Iec61850BrcbChronologyFrame[count];
        for (var index = 0; index < count; index++)
        {
            var source = (state.AnomalyChronologyNext - count + index +
                AnomalyChronologyLimit) % AnomalyChronologyLimit;
            copy[index] = state.AnomalyChronology![source];
        }
        return copy;
    }

    private static IReadOnlyList<Iec61850BrcbChronologyFrame> CopyChronology(
        Iec61850ReportContinuityState state)
    {
        var initialCount = state.InitialChronologyCount;
        var recentCount = state.RecentChronologyCount;
        if (initialCount + recentCount == 0)
            return Array.Empty<Iec61850BrcbChronologyFrame>();

        var copy = new Iec61850BrcbChronologyFrame[initialCount + recentCount];
        if (initialCount > 0)
            Array.Copy(state.InitialChronology!, 0, copy, 0, initialCount);
        for (var index = 0; index < recentCount; index++)
        {
            var source = (state.RecentChronologyNext - recentCount + index + RecentChronologyLimit)
                % RecentChronologyLimit;
            copy[initialCount + index] = state.RecentChronology![source];
        }
        return copy;
    }

    private static string WriteVerdict(bool? value)
        => value is true ? "accepted/yes" : value is false ? "rejected/no" : "UNKNOWN";

    private static void CaptureAnomaly(
        Iec61850ReportContinuityState state, NativeReportFrameMetadata frame)
    {
        state.LastAnomalyPriorSqNum = state.LastSequenceNumber;
        state.LastAnomalyCurrentSqNum = frame.SequenceNumber;
        state.LastAnomalyGiReason = frame.GeneralInterrogationReasonSeen;
        state.LastAnomalyReceivedAtUtc = frame.ReceivedAt == default ? null : frame.ReceivedAt;
    }

    private static bool IsBoundedHex(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 16 || (value.Length & 1) != 0)
            return false;
        foreach (var character in value)
        {
            if (!((character >= '0' && character <= '9') ||
                  (character >= 'a' && character <= 'f') ||
                  (character >= 'A' && character <= 'F')))
                return false;
        }
        return true;
    }

    private static string? DescribeSequenceAnomaly(ulong? previous, ulong current)
    {
        if (!previous.HasValue)
            return null; // First observed report is not necessarily the first server report.

        var prior = previous.Value;
        if ((prior < MaxSequenceNumber && current == prior + 1) ||
            (prior == MaxSequenceNumber && current == 0))
            return null;

        var classification = current == prior ? "duplicate/replay" :
            current < prior ? "backward/reset/replay" : "forward gap";
        return $"Report sequence discontinuity ({classification}): previous={prior}, " +
               $"current={current}; RptEna/reconnect/GI context is not yet verified; no event-loss conclusion";
    }
}
