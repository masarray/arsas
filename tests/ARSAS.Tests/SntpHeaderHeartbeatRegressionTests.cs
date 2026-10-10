using ArIED61850Tester;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SntpHeaderHeartbeatRegressionTests
{
    private static SntpClockServiceSnapshot Snapshot(
        SntpClockServiceState state, long requests = 0, long replies = 0,
        DateTimeOffset? lastRequest = null, DateTimeOffset? lastReply = null) =>
        new(state, "", null, null, 0, true, SntpClockTransportMode.None,
            0, requests, replies, lastRequest, lastReply);

    [Fact]
    public void Off_OrIdleServer_NeverInventsTraffic()
    {
        var began = DateTimeOffset.UtcNow;
        var existing = Snapshot(SntpClockServiceState.Serving, 50, 40,
            began.AddMinutes(-30), began.AddMinutes(-30));
        var off = SntpHeaderHeartbeatPolicy.Evaluate(false, existing, began, 0, 0);
        Assert.Equal(SntpHeaderHeartbeatTone.Off, off.Tone);
        Assert.False(off.Serving);
        Assert.False(off.NewRequest);
        Assert.False(off.NewReply);
        var idle = SntpHeaderHeartbeatPolicy.Evaluate(true, existing, began, 50, 40);
        Assert.Equal(SntpHeaderHeartbeatTone.Serving, idle.Tone);
        Assert.False(idle.NewReply);
    }

    [Fact]
    public void RealPackets_ProduceOncePerUpdatedCounterAndDoNotTreatHistoryAsActivity()
    {
        var began = DateTimeOffset.UtcNow;
        var request = Snapshot(SntpClockServiceState.Serving, 9, 4, began.AddMilliseconds(10), null);
        var first = SntpHeaderHeartbeatPolicy.Evaluate(true, request, began, 8, 4);
        Assert.Equal(SntpHeaderHeartbeatTone.Request, first.Tone);
        Assert.True(first.NewRequest);
        Assert.False(first.NewReply);
        Assert.False(SntpHeaderHeartbeatPolicy.Evaluate(true, request, began, 9, 4).NewRequest);

        var reply = Snapshot(SntpClockServiceState.Serving, 9, 5,
            began.AddMilliseconds(10), began.AddMilliseconds(12));
        var second = SntpHeaderHeartbeatPolicy.Evaluate(true, reply, began, 9, 4);
        Assert.True(second.NewReply);
        Assert.Equal(SntpHeaderHeartbeatTone.Reply, second.Tone);
        Assert.False(SntpHeaderHeartbeatPolicy.Evaluate(true, reply, began, 9, 5).NewReply);
    }

    [Fact]
    public void FailedService_DoesNotImplySuccessfulClockSync()
    {
        var began = DateTimeOffset.UtcNow;
        var faulted = SntpHeaderHeartbeatPolicy.Evaluate(true,
            Snapshot(SntpClockServiceState.Faulted, 10, 10, began, began), began, 0, 0);
        Assert.Equal(SntpHeaderHeartbeatTone.Fault, faulted.Tone);
        Assert.False(faulted.Serving);
        Assert.False(faulted.NewReply);
    }

    [Fact]
    public void Header_ReusesActualARSASTokensAndOneTimeWpfAnimations()
    {
        var source = Read("MainWindow.ClockSyncToggle.cs");
        foreach (var token in new[] { "Ink", "Muted", "Accent", "Line", "SurfaceElevated", "BorderStrong", "Success", "Warning", "Danger", "AppFontFamily" })
            Assert.Contains($"FindResource(\"{token}\")", source, StringComparison.Ordinal);
        Assert.Contains("Name = \"GlobalSntpServerToggle\"", source, StringComparison.Ordinal);
        Assert.Contains("Name = \"GlobalSntpPcIpPicker\"", source, StringComparison.Ordinal);
        Assert.Contains("Name = \"GlobalSntpTrafficIndicator\"", source, StringComparison.Ordinal);
        Assert.Contains("FontSize = 12.8", source, StringComparison.Ordinal);
        Assert.Contains("FontWeights.SemiBold", source, StringComparison.Ordinal);
        Assert.Contains("DoubleAnimation", source, StringComparison.Ordinal);
        Assert.Contains("SntpHeaderHeartbeatPolicy.Evaluate", source, StringComparison.Ordinal);
        Assert.Contains("state.NewRequest || state.NewReply", source, StringComparison.Ordinal);
        Assert.Contains("_globalSntpBreathing", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Thread(", source, StringComparison.Ordinal);
    }

    private static string Read(string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, path);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
