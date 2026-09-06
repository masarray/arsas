using System.Reflection;
using ArIED61850Tester.Models;
using UiRuntime = ArIED61850Tester.Iec61850MonitorRuntime;

namespace ARSAS.Tests;

public sealed class IoFatCommandFreshnessBehaviorTests
{
    [Fact]
    public async Task ConfirmedCommand_RejectsStalePollAndPhantomSoe_WhileReportRemainsAuthoritative()
    {
        await using var runtime = new UiRuntime();
        var visiblePoints = new List<Iec61850PointSnapshot>();
        var visibleEvents = new List<Iec61850EventEntry>();
        runtime.PointUpdated += visiblePoints.Add;
        runtime.EventRaised += visibleEvents.Add;

        var point = new Iec61850MonitorPoint
        {
            DeviceId = "ied-1",
            DeviceName = "Relay A",
            SignalName = "CSWI.Pos",
            IecReference = "RelayA/CSWI1.Pos.stVal",
            IecDataType = "Dbpos"
        };

        // 1. The command engine publishes the accepted/feedback-proven target immediately.
        InvokePoint(runtime, Snapshot(
            point,
            previous: "Open [01]",
            value: "Closed [10]",
            reason: "confirmed command feedback / awaiting matching dchg",
            report: false,
            edge: true,
            sequence: 10));
        InvokeEvent(runtime, Event(
            point,
            oldValue: "Open [01]",
            newValue: "Closed [10]",
            reason: "confirmed command feedback / awaiting matching dchg",
            sequence: 1));

        Assert.Single(visiblePoints);
        Assert.Single(visibleEvents);
        Assert.Equal("Closed [10]", visiblePoints[0].Value);
        Assert.Equal("Closed [10]", visibleEvents[0].NewValue);

        // 2. One old MMS verification sample arrives after the command. The inner runtime
        // can inherit the previous command reason text here, so provenance must come from
        // PointUpdated.IsReportTraffic rather than parsing Event.Reason.
        InvokePoint(runtime, Snapshot(
            point,
            previous: "Closed [10]",
            value: "Open [01]",
            reason: "confirmed command feedback / awaiting matching dchg",
            report: false,
            edge: true,
            sequence: 11));
        InvokeEvent(runtime, Event(
            point,
            oldValue: "Closed [10]",
            newValue: "Open [01]",
            reason: "confirmed command feedback / awaiting matching dchg",
            sequence: 2));

        Assert.Single(visiblePoints); // no Closed -> Open live-value flash
        Assert.Single(visibleEvents); // no phantom Falling/Open SOE

        // 3. The relay's real dchg confirms the already-published Closed state. The live
        // snapshot is allowed through as process authority, but the duplicate return SOE is
        // suppressed because the command transition was already recorded once.
        InvokePoint(runtime, Snapshot(
            point,
            previous: "Open [01]",
            value: "Closed [10]",
            reason: "dchg",
            report: true,
            edge: true,
            sequence: 12));
        InvokeEvent(runtime, Event(
            point,
            oldValue: "Open [01]",
            newValue: "Closed [10]",
            reason: "dchg",
            sequence: 3));

        Assert.Equal(2, visiblePoints.Count);
        Assert.Single(visibleEvents);
        Assert.Equal("Closed [10]", visiblePoints[^1].Value);
        Assert.True(visiblePoints[^1].IsReportTraffic);

        // 4. A later contradictory REPORT is a genuine process transition. It must not be
        // hidden by the freshness fence; both live value and SOE pass immediately.
        InvokePoint(runtime, Snapshot(
            point,
            previous: "Closed [10]",
            value: "Open [01]",
            reason: "dchg",
            report: true,
            edge: true,
            sequence: 13));
        InvokeEvent(runtime, Event(
            point,
            oldValue: "Closed [10]",
            newValue: "Open [01]",
            reason: "dchg",
            sequence: 4));

        Assert.Equal(3, visiblePoints.Count);
        Assert.Equal(2, visibleEvents.Count);
        Assert.Equal("Open [01]", visiblePoints[^1].Value);
        Assert.Equal("Open [01]", visibleEvents[^1].NewValue);
    }

    private static Iec61850PointSnapshot Snapshot(
        Iec61850MonitorPoint point,
        string previous,
        string value,
        string reason,
        bool report,
        bool edge,
        long sequence)
        => new()
        {
            Point = point,
            PreviousValue = previous,
            Value = value,
            Quality = "Good",
            DeviceTimestamp = "2026-09-06T12:00:00Z",
            SourceMode = report ? "Static: BRCB01" : "Static: BRCB01",
            Reason = reason,
            Status = "Live",
            IsValueEdge = edge,
            IsReportTraffic = report,
            Sequence = sequence
        };

    private static Iec61850EventEntry Event(
        Iec61850MonitorPoint point,
        string oldValue,
        string newValue,
        string reason,
        long sequence)
        => new()
        {
            Sequence = sequence,
            DeviceId = point.DeviceId,
            PointKey = point.PointKey,
            DeviceTimestamp = "2026-09-06T12:00:00Z",
            DeviceName = point.DeviceName,
            IpAddress = point.IpAddress,
            SignalName = point.SignalName,
            IecReference = point.IecReference,
            IecDataType = point.IecDataType,
            OldValue = oldValue,
            NewValue = newValue,
            Quality = "Good",
            SourceMode = "Static: BRCB01",
            Reason = reason
        };

    private static void InvokePoint(UiRuntime runtime, Iec61850PointSnapshot snapshot)
        => ForwardPointMethod.Invoke(runtime, new object[] { snapshot });

    private static void InvokeEvent(UiRuntime runtime, Iec61850EventEntry entry)
        => ForwardEventMethod.Invoke(runtime, new object[] { entry });

    private static MethodInfo ForwardPointMethod { get; } =
        typeof(UiRuntime).GetMethod("ForwardPointUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(UiRuntime).FullName, "ForwardPointUpdate");

    private static MethodInfo ForwardEventMethod { get; } =
        typeof(UiRuntime).GetMethod("ForwardEventRaised", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(UiRuntime).FullName, "ForwardEventRaised");
}
