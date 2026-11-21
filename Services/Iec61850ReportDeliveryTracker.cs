using System.Globalization;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Classifies report delivery using association continuity and the original wire
/// TimeOfEntry, never the point's process timestamp, BufOvfl, or ReasonForInclusion.
/// This diagnostic-only tracker does not write RCBs or perform MMS reads.
/// </summary>
public sealed class Iec61850ReportDeliveryTracker
{
    private sealed class Stream
    {
        public string LastAcceptedEntryId { get; set; } = "";
        public ulong? LastConfRev { get; set; }
        public bool TrustedOnlineClock { get; set; }

        // Frozen at disconnect. Updates accepted on the new association must
        // never overwrite the evidence used to judge the offline interval.
        public string OfflineCursor { get; set; } = "";
        public ulong? OfflineConfRev { get; set; }
        public bool OfflineClockTrusted { get; set; }
    }

    private readonly Dictionary<string, Stream> _streams =
        new(StringComparer.OrdinalIgnoreCase);
    private long _association;
    private DateTimeOffset _associatedAtUtc;
    private DateTimeOffset? _disconnectedAtUtc;
    private bool _reconnected;

    private static readonly TimeSpan BoundaryGuard = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LiveReceiveWindow = TimeSpan.FromSeconds(30);
    private const int MaxStreams = 64;
    private const int MaxEntryIdChars = 128;

    public void OnAssociated(long association, DateTimeOffset connectedAtUtc)
    {
        if (association == 0 || association == _association)
            return;
        _association = association;
        _associatedAtUtc = connectedAtUtc.ToUniversalTime();
        _reconnected = _disconnectedAtUtc.HasValue;
    }

    public void OnDisconnected(DateTimeOffset disconnectedAtUtc)
    {
        if (_association == 0 || _disconnectedAtUtc.HasValue &&
            !_reconnected)
            return;
        // Record only one boundary for a loss of association, not every
        // successive failed reconnection attempt.
        if (_disconnectedAtUtc.HasValue && _reconnected &&
            _associatedAtUtc <= _disconnectedAtUtc.Value)
            return;
        _disconnectedAtUtc = disconnectedAtUtc.ToUniversalTime();
        _reconnected = false;
        foreach (var state in _streams.Values)
        {
            state.OfflineCursor = state.LastAcceptedEntryId;
            state.OfflineConfRev = state.LastConfRev;
            state.OfflineClockTrusted = state.TrustedOnlineClock;
        }
    }

    /// <summary>
    /// A manual PurgeBuf attempt invalidates old EntryID sequence evidence even
    /// when the MMS response is lost. No network access or IED mutation here.
    /// A verified BR reference invalidates only its own bounded stream;
    /// an unidentifiable reference fails closed by clearing all cursors.
    /// </summary>
    public void InvalidateForManualPurge(string? rcbReference)
    {
        if (string.IsNullOrWhiteSpace(rcbReference) ||
            !BrowserPurgeBufPolicy.SameExactBrcbReference(rcbReference, rcbReference))
        {
            _streams.Clear();
            return;
        }

        foreach (var key in _streams.Keys
                     .Where(candidate => BrowserPurgeBufPolicy.SameExactBrcbReference(
                         candidate, rcbReference)).ToArray())
            _streams.Remove(key);
    }
    public Iec61850ReportDeliveryEvidence Classify(
        NativeReportFrameMetadata frame, long association)
    {
        if (association == 0 || association != _association ||
            string.IsNullOrWhiteSpace(frame.ReportControlReference) ||
            frame.ReceivedAt == default ||
            !TryWireTime(frame.ReportTimestamp, out var timeOfEntry))
            return Iec61850ReportDeliveryEvidence.Unknown;

        var received = frame.ReceivedAt.ToUniversalTime();
        if (received < _associatedAtUtc)
            return Iec61850ReportDeliveryEvidence.Unknown;

        // Historical BRCB backlog is not restricted to an offline interval
        // previously observed by this client. An IED can retain real buffered
        // entries from BEFORE the very first ARSAS connection. Require decoded
        // wire EntryID + reason bits, a synchronized IEC TimeOfEntry, an exact
        // BR RCB, and the report's ORIGINAL creation time preceding ACSE/MMS
        // association. The process timestamp and mere BRCB naming are NEVER proof.
        // Reject GI/integrity and last-accepted cursor duplicates explicitly.
        // This does not write EntryID, ResvTms, PurgeBuf or any RCB attribute.
        if (IsProvenHistoricalBrcbFrame(frame, timeOfEntry, received))
        {
            return new Iec61850ReportDeliveryEvidence(
                "Buffer",
                "Decoded BRCB EntryID and synchronized wire TimeOfEntry prove that " +
                "this non-GI/non-integrity report existed before this MMS association. " +
                "It is historical BRCB delivery, even without an earlier client cursor.");
        }

        if (_reconnected && _disconnectedAtUtc is { } disconnected &&
            _streams.TryGetValue(frame.ReportControlReference, out var old) &&
            old.OfflineClockTrusted &&
            old.OfflineCursor.Length > 0 &&
            frame.EntryIdHex.Length is > 0 and <= MaxEntryIdChars &&
            !frame.EntryIdHex.Equals(old.OfflineCursor, StringComparison.OrdinalIgnoreCase) &&
            old.OfflineConfRev.HasValue && frame.ConfRev == old.OfflineConfRev &&
            timeOfEntry >= disconnected + BoundaryGuard &&
            timeOfEntry < _associatedAtUtc - BoundaryGuard)
        {
            return new Iec61850ReportDeliveryEvidence(
                "Buffer",
                "Wire TimeOfEntry is inside the observed offline interval; matched RCB/ConfRev, " +
                "previously accepted EntryID cursor and trusted online device clock.");
        }

        // This is evidence of a timely report generated during the current
        // association, not a claim about unspecified vendor driver buffers.
        if (timeOfEntry >= _associatedAtUtc + BoundaryGuard &&
            timeOfEntry <= received &&
            received - timeOfEntry <= LiveReceiveWindow &&
            (!_reconnected || !_streams.TryGetValue(frame.ReportControlReference, out var state) ||
             state.TrustedOnlineClock))
        {
            return new Iec61850ReportDeliveryEvidence(
                "Live", "Wire TimeOfEntry is recent and inside the current association.");
        }

        return Iec61850ReportDeliveryEvidence.Unknown;
    }

    private bool IsProvenHistoricalBrcbFrame(
        NativeReportFrameMetadata frame, DateTimeOffset timeOfEntry,
        DateTimeOffset received)
    {
        // For historical classification we must have the quality byte, not
        // merely an ISO string with unknown synchronization provenance.
        if (!frame.ReportControlReference.Replace('
    /// It must be called AFTER the runtime has accepted that update.
    /// </summary>
    public void RecordAccepted(NativeReportFrameMetadata? frame, long association)
    {
        if (frame is null || association == 0 || association != _association ||
            string.IsNullOrWhiteSpace(frame.ReportControlReference) ||
            frame.ReceivedAt == default)
            return;
        if (!_streams.TryGetValue(frame.ReportControlReference, out var state))
        {
            if (_streams.Count >= MaxStreams)
                return;
            state = new Stream();
            _streams.Add(frame.ReportControlReference, state);
        }

        if (frame.ConfRev.HasValue && state.LastConfRev.HasValue &&
            frame.ConfRev != state.LastConfRev)
        {
            // A different dataset/configuration invalidates the old cursor.
            state.OfflineCursor = "";
            state.TrustedOnlineClock = false;
            state.OfflineClockTrusted = false;
        }
        state.LastConfRev = frame.ConfRev;
        if (frame.EntryIdHex.Length is > 0 and <= MaxEntryIdChars)
            state.LastAcceptedEntryId = frame.EntryIdHex;

        if (TryWireTime(frame.ReportTimestamp, out var time) &&
            time >= _associatedAtUtc + BoundaryGuard &&
            Math.Abs((frame.ReceivedAt.ToUniversalTime() - time).TotalSeconds) <=
                LiveReceiveWindow.TotalSeconds)
            state.TrustedOnlineClock = true;
    }

    private static bool TryWireTime(string? value, out DateTimeOffset result)
    {
        result = default;
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 100)
            return false;

        // ARIEC61850 MmsDataValueRenderer emits full-precision IEC UTC-Time
        // as "yyyy-MM-dd HH:mm:ss.fffffff UTC (q=0xNN)". Quality is part
        // of the evidence: 0x40 = clockFailure, 0x20 = not synchronized.
        // 0x80 is leapSecondsKnown and is not a clock-failure indicator.
        var qualityStart = text.LastIndexOf(" (q=0x", StringComparison.OrdinalIgnoreCase);
        if (qualityStart >= 0)
        {
            if (!text.EndsWith(')') ||
                !byte.TryParse(text[(qualityStart + " (q=0x".Length)..^1], NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var quality) ||
                (quality & 0x60) != 0)
                return false;
            text = text[..qualityStart].TrimEnd();
        }

        if (text.EndsWith(" UTC", StringComparison.OrdinalIgnoreCase))
        {
            var utc = text[..^4];
            return DateTimeOffset.TryParseExact(utc,
                new[] { "yyyy-MM-dd HH:mm:ss.fffffff", "yyyy-MM-dd HH:mm:ss.fffff" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out result);
        }

        // Strictly no local-time conversion: an unknown offset is UTC only
        // because the decoded IEC 61850 field itself is named TimeOfEntry UTC.
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}

public sealed record Iec61850ReportDeliveryEvidence(string Label, string Basis)
{
    public static Iec61850ReportDeliveryEvidence Unknown { get; } =
        new("Unknown", "Wire delivery origin not independently proven.");
}
, '.').Contains(
                ".BR.", StringComparison.OrdinalIgnoreCase) ||
            frame.OptFldsEntryId != true ||
            frame.OptFldsReasonForInclusion != true ||
            frame.GeneralInterrogationReasonSeen != false ||
            frame.IntegrityReasonSeen != false ||
            !HasExplicitWireTimeQuality(frame.ReportTimestamp) ||
            !IsValidEntryIdHex(frame.EntryIdHex) ||
            timeOfEntry >= _associatedAtUtc - BoundaryGuard ||
            timeOfEntry > received)
            return false;

        // The same accepted EntryID is retransmission of an already observed
        // cursor, NOT evidence of another historical event.
        return !_streams.TryGetValue(frame.ReportControlReference, out var old) ||
               !frame.EntryIdHex.Equals(old.LastAcceptedEntryId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidEntryIdHex(string? hex) =>
        !string.IsNullOrEmpty(hex) &&
        hex.Length <= MaxEntryIdChars && hex.Length % 2 == 0 &&
        hex.All(Uri.IsHexDigit);

    private static bool HasExplicitWireTimeQuality(string? wireTime)
    {
        if (string.IsNullOrWhiteSpace(wireTime)) return false;
        var qualityStart = wireTime.LastIndexOf(" (q=0x",
            StringComparison.OrdinalIgnoreCase);
        if (qualityStart < 0 || !wireTime.EndsWith(')')) return false;
        return byte.TryParse(wireTime[(qualityStart + " (q=0x".Length)..^1],
                   NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                   out var quality) && (quality & 0x60) == 0;
    }

    /// <summary>
    /// Only a schema-accepted process update advances the remembered cursor.
    /// It must be called AFTER the runtime has accepted that update.
    /// </summary>
    public void RecordAccepted(NativeReportFrameMetadata? frame, long association)
    {
        if (frame is null || association == 0 || association != _association ||
            string.IsNullOrWhiteSpace(frame.ReportControlReference) ||
            frame.ReceivedAt == default)
            return;
        if (!_streams.TryGetValue(frame.ReportControlReference, out var state))
        {
            if (_streams.Count >= MaxStreams)
                return;
            state = new Stream();
            _streams.Add(frame.ReportControlReference, state);
        }

        if (frame.ConfRev.HasValue && state.LastConfRev.HasValue &&
            frame.ConfRev != state.LastConfRev)
        {
            // A different dataset/configuration invalidates the old cursor.
            state.OfflineCursor = "";
            state.TrustedOnlineClock = false;
            state.OfflineClockTrusted = false;
        }
        state.LastConfRev = frame.ConfRev;
        if (frame.EntryIdHex.Length is > 0 and <= MaxEntryIdChars)
            state.LastAcceptedEntryId = frame.EntryIdHex;

        if (TryWireTime(frame.ReportTimestamp, out var time) &&
            time >= _associatedAtUtc + BoundaryGuard &&
            Math.Abs((frame.ReceivedAt.ToUniversalTime() - time).TotalSeconds) <=
                LiveReceiveWindow.TotalSeconds)
            state.TrustedOnlineClock = true;
    }

    private static bool TryWireTime(string? value, out DateTimeOffset result)
    {
        result = default;
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 100)
            return false;

        // ARIEC61850 MmsDataValueRenderer emits full-precision IEC UTC-Time
        // as "yyyy-MM-dd HH:mm:ss.fffffff UTC (q=0xNN)". Quality is part
        // of the evidence: 0x40 = clockFailure, 0x20 = not synchronized.
        // 0x80 is leapSecondsKnown and is not a clock-failure indicator.
        var qualityStart = text.LastIndexOf(" (q=0x", StringComparison.OrdinalIgnoreCase);
        if (qualityStart >= 0)
        {
            if (!text.EndsWith(')') ||
                !byte.TryParse(text[(qualityStart + " (q=0x".Length)..^1], NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var quality) ||
                (quality & 0x60) != 0)
                return false;
            text = text[..qualityStart].TrimEnd();
        }

        if (text.EndsWith(" UTC", StringComparison.OrdinalIgnoreCase))
        {
            var utc = text[..^4];
            return DateTimeOffset.TryParseExact(utc,
                new[] { "yyyy-MM-dd HH:mm:ss.fffffff", "yyyy-MM-dd HH:mm:ss.fffff" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out result);
        }

        // Strictly no local-time conversion: an unknown offset is UTC only
        // because the decoded IEC 61850 field itself is named TimeOfEntry UTC.
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}

public sealed record Iec61850ReportDeliveryEvidence(string Label, string Basis)
{
    public static Iec61850ReportDeliveryEvidence Unknown { get; } =
        new("Unknown", "Wire delivery origin not independently proven.");
}
