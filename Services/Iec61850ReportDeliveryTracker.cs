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

    private static bool TryWireTime(string value, out DateTimeOffset result)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
}

public sealed record Iec61850ReportDeliveryEvidence(string Label, string Basis)
{
    public static Iec61850ReportDeliveryEvidence Unknown { get; } =
        new("Unknown", "Wire delivery origin not independently proven.");
}
