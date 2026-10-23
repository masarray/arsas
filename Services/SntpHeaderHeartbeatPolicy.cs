using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// Pure, allocation-free translation of SNTP service telemetry into compact
/// header presentation. Never polls sockets and never claims relay sync.
/// </summary>
public enum SntpHeaderHeartbeatTone { Off, Serving, Request, Reply, Fault }

public readonly record struct SntpHeaderHeartbeatState(
    SntpHeaderHeartbeatTone Tone, bool Serving, bool NewRequest, bool NewReply);

public static class SntpHeaderHeartbeatPolicy
{
    public static SntpHeaderHeartbeatState Evaluate(
        bool enabled, SntpClockServiceSnapshot snapshot, DateTimeOffset visualSessionUtc,
        long previouslyRenderedRequests, long previouslyRenderedReplies)
    {
        var serving = enabled && snapshot.State == SntpClockServiceState.Serving;
        if (!enabled)
            return new(SntpHeaderHeartbeatTone.Off, false, false, false);
        if (snapshot.State is SntpClockServiceState.Faulted or SntpClockServiceState.PortUnavailable)
            return new(SntpHeaderHeartbeatTone.Fault, false, false, false);
        if (!serving)
            return new(SntpHeaderHeartbeatTone.Off, false, false, false);
        var requestActive = snapshot.LastRequestUtc >= visualSessionUtc;
        var replyActive = snapshot.LastReplyUtc >= visualSessionUtc;
        var newRequest = requestActive && snapshot.ClientRequestCount > previouslyRenderedRequests;
        var newReply = replyActive && snapshot.ReplyCount > previouslyRenderedReplies;
        return new(
            replyActive ? SntpHeaderHeartbeatTone.Reply :
            requestActive ? SntpHeaderHeartbeatTone.Request :
            SntpHeaderHeartbeatTone.Serving,
            true, newRequest, newReply);
    }
}
