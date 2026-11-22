using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P9SoeControlAuditTests
{
    [Fact]
    public void DualRcbSameExactProcessEventIsOneSoeEvenWithOldReplayBetween()
    {
        var guard = new Iec61850SoeRepeatGuard();
        Assert.True(guard.Admit(true, true, "dchg", "StaticBrcb",
            "2026-11-22 06:26:23.143", "Open [01]"));
        // Independent historical Intermediate: different process timestamp.
        Assert.True(guard.Admit(true, true, "dchg", "StaticBrcb",
            "2026-11-22 05:25:32.110", "Intermediate [00]"));
        // Second BRCB reports the SAME original Open with the SAME process t.
        Assert.False(guard.Admit(true, true, "dchg", "StaticBrcb",
            "2026-11-22 06:26:23.143", "Open [01]"));
        Assert.Equal(2, guard.TrackedIdentityCount);
    }

    [Fact]
    public void DifferentTimestampOrValueIsAlwaysAnotherPhysicalEdge()
    {
        var guard = new Iec61850SoeRepeatGuard();
        Assert.True(guard.Admit(true, true, "dchg", "StaticBrcb",
            "2026-11-22 06:00:00.001", "Open [01]"));
        Assert.True(guard.Admit(true, true, "dchg", "StaticBrcb",
            "2026-11-22 06:00:00.002", "Open [01]"));
        Assert.True(guard.Admit(true, true, "dchg", "StaticBrcb",
            "2026-11-22 06:00:00.001", "Closed [10]"));
    }

    [Theory]
    [InlineData(false, true, "dchg", "2026-11-22 06:00:00.001")]
    [InlineData(true, false, "dchg", "2026-11-22 06:00:00.001")]
    [InlineData(true, true, "gi", "2026-11-22 06:00:00.001")]
    [InlineData(true, true, "integrity", "2026-11-22 06:00:00.001")]
    [InlineData(true, true, "dchg", "-")]
    [InlineData(true, true, "dchg", "Unknown")]
    [InlineData(true, true, "dchg", "2026-11-22 06:00:00")]
    public void DoNotSilentlyDeduplicateWithoutExactWireDataChangeAndProcessTime(
        bool report, bool frame, string reason, string timestamp)
    {
        var guard = new Iec61850SoeRepeatGuard();
        Assert.True(guard.Admit(report, frame, reason, "StaticBrcb", timestamp, "Open [01]"));
        Assert.True(guard.Admit(report, frame, reason, "StaticBrcb", timestamp, "Open [01]"));
        Assert.Equal(0, guard.TrackedIdentityCount);
    }

    [Fact]
    public void HistoryIsBoundedPerPointAndEvictsWithoutInfiniteMemory()
    {
        var guard = new Iec61850SoeRepeatGuard();
        for (var i = 0; i < 70; i++)
            Assert.True(guard.Admit(true, true, "dchg", "StaticBrcb",
                new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc)
                    .AddMilliseconds(i).ToString("O"), "Open [01]"));
        Assert.Equal(32, guard.TrackedIdentityCount);
        Assert.True(guard.Admit(true, true, "dchg", "StaticBrcb",
            "2026-11-22T00:00:00.0000000Z", "Open [01]"));
    }

    [Fact]
    public void PositiveTerminationWithIndependentFeedbackIsConfirmedNotMerelySent()
    {
        var result = new Iec61850ControlCommandResult
        {
            IsSuccess = true, ServiceAccepted = true, FeedbackConfirmed = true,
            CommandTerminationReceived = true, PositiveTermination = true,
            FeedbackValue = "Open [01]", CompletionState = "Completed",
            WireSteps = new[]
            {
                new Iec61850ControlWireEvidence { Action = "SelectWithValue", RequestAccepted = true },
                new Iec61850ControlWireEvidence { Action = "Operate", RequestAccepted = true }
            }
        };
        Assert.Equal("Control confirmed", Iec61850ControlAuditPolicy.Outcome(result));
        Assert.Equal("Control confirmed", Iec61850ControlAuditPolicy.Outcome(
            new Iec61850ControlCommandResult
            {
                ControlModelText = "Direct Operate · Normal security",
                ServiceAccepted = true, IsSuccess = true, FeedbackConfirmed = true
            }));
        Assert.Equal("Control accepted", Iec61850ControlAuditPolicy.Outcome(
            new Iec61850ControlCommandResult
            {
                ControlModelText = "SBO · Enhanced security",
                ServiceAccepted = true, IsSuccess = true, FeedbackConfirmed = true,
                CommandTerminationReceived = false
            }));
        Assert.Contains("termination=positive", Iec61850ControlAuditPolicy.Evidence(result));
        Assert.Contains("feedback=confirmed Open", Iec61850ControlAuditPolicy.Evidence(result));
    }

    [Fact]
    public void RejectionIsNotMisdiagnosedAsInterlockOrAuthority()
    {
        var result = new Iec61850ControlCommandResult
        {
            ServiceAccepted = false, CommandTerminationReceived = true,
            PositiveTermination = false, ControlError = "no-error",
            AddCause = "unknown", WireSteps = new[]
            {
                new Iec61850ControlWireEvidence { Action = "SelectWithValue", RequestAccepted = false }
            }
        };
        Assert.Equal("Control rejected", Iec61850ControlAuditPolicy.Outcome(result));
        Assert.DoesNotContain("interlock", Iec61850ControlAuditPolicy.Evidence(result),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Control not sent", Iec61850ControlAuditPolicy.Outcome(
            new Iec61850ControlCommandResult { CompletionState = "NotSent" }));
        Assert.Equal("Control unverified", Iec61850ControlAuditPolicy.Outcome(
            new Iec61850ControlCommandResult
            {
                CompletionState = "NotSent", RequestHex = "actual-outbound-wire-bytes"
            }));
        Assert.Equal("Control accepted", Iec61850ControlAuditPolicy.Outcome(
            new Iec61850ControlCommandResult { ServiceAccepted = true, IsSuccess = true }));
        Assert.Equal("Control unverified", Iec61850ControlAuditPolicy.Outcome(
            new Iec61850ControlCommandResult { ServiceAccepted = true, FeedbackConfirmed = false }));

        // An explicit IED AddCause authorizes a precise reason. A generic
        // MMS denial does NOT authorize guessing Local/Remote/interlock.
        var interlocked = new Iec61850ControlCommandResult
        {
            Stage = "Operate", AddCause = "blocked-by-interlocking",
            WireSteps = new[] { new Iec61850ControlWireEvidence
            { Action = "Operate", RequestAccepted = false } }
        };
        Assert.Contains("IED blocked the command by interlocking",
            Iec61850ControlAuditPolicy.Evidence(interlocked));
        Assert.Contains("confidence=IEDExplicitAddCause",
            Iec61850ControlAuditPolicy.Evidence(interlocked));
        var authority = new Iec61850ControlCommandResult
        {
            Stage = "Operate", AddCause = "no-access-authority",
            WireSteps = new[] { new Iec61850ControlWireEvidence
            { Action = "Operate", RequestAccepted = false } }
        };
        Assert.Contains("insufficient control access authority",
            Iec61850ControlAuditPolicy.Evidence(authority));
    }

    [Fact]
    public void ControlRowNeverPretendsToBeIedProcessReportOrTimestamp()
    {
        var at = new DateTimeOffset(2026, 11, 21, 23, 26, 23, TimeSpan.Zero);
        var entry = new Iec61850EventEntry
        {
            EventKind = "Control",
            ClientEventUtc = at,
            DeviceTimestamp = "-",
            SourceMode = "Operator control",
            ControlOutcome = "Control confirmed",
            NewValue = "Open [01]",
            Quality = string.Empty,
        };
        Assert.True(entry.IsControlEvent);
        Assert.Equal("Control confirmed", entry.EventLabel);
        Assert.Equal("MMS control", entry.AcquisitionShort);
        Assert.Equal("Control", entry.ReportChannel);
        Assert.Equal("ARSAS client clock (local)", entry.TimestampOrigin);
        Assert.Equal("-", entry.DeviceTimestamp);
        Assert.Equal(Iec61850QualityPresentation.Unknown, entry.QualityTone);
        Assert.Equal(string.Empty, entry.QualityDisplay);
        Assert.Contains("no IED process timestamp or quality", entry.EventTimeContext);
        Assert.StartsWith("Request ", entry.EventValue);
        Assert.NotEqual("-", entry.EventTimestamp);
        Assert.Empty(entry.RoutedReportControlReferences);
    }

    [Fact]
    public void CommandHasLiterallyBlankQualityWhileProcessQIsPreserved()
    {
        var command = new Iec61850EventEntry
        {
            EventKind = "Control", Quality = "-", ClientEventUtc = DateTimeOffset.UtcNow
        };
        Assert.Equal(string.Empty, command.QualityDisplay); // even legacy placeholder is invisible
        var process = new Iec61850EventEntry
        {
            EventKind = "Process", Quality = "Invalid",
            DeviceTimestamp = "2026-11-22 05:25:32.110",
            SourceMode = "Static DataSet: StaticBrcb",
            Reason = "dchg"
        };
        Assert.Equal("Invalid", process.QualityDisplay);
        Assert.Contains("An older process time alone does not prove replay",
            process.EventTimeContext);
        Assert.Equal(process.DeviceTimestamp, process.EventTimestamp);
    }

    [Fact]
    public void HistoricalReportShowsDistinctIedTimeAndLocalReceptionOnHover()
    {
        var oldProcessTime = "2026-11-22 05:25:32.110";
        var received = new DateTimeOffset(2026, 11, 22, 0, 25, 55,
            TimeSpan.Zero);
        var process = new Iec61850EventEntry
        {
            DeviceTimestamp = oldProcessTime,
            SourceMode = "Static DataSet: StaticBrcb",
            Reason = "dchg",
            Quality = "good",
            ReportFrame = new NativeReportFrameMetadata { ReceivedAt = received },
            VerifiedBufferedReplay = true
        };
        Assert.Equal(oldProcessTime, process.EventTimestamp);
        Assert.Equal("good", process.QualityDisplay);
        Assert.Contains(received.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"),
            process.EventTimeContext);
        Assert.Contains("Historical BRCB replay corroborated", process.EventTimeContext);
        Assert.Contains("reception order", process.EventTimeContext);
    }

    [Fact]
    public void RuntimeSoeAndControlShareOneAuditLedgerButKeepIndependentAuthority()
    {
        static string Source(string filename)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var path = Path.Combine(dir.FullName, filename);
                if (File.Exists(path)) return File.ReadAllText(path);
                dir = dir.Parent;
            }
            throw new FileNotFoundException(filename);
        }
        var runtime = Source("Services/Iec61850MonitorRuntime.cs");
        var audit = Source("Services/Iec61850MonitorRuntime.ControlAudit.cs");
        var facade = Source("Services/UiResponsiveIec61850MonitorRuntimeFacade.cs");
        var p1 = Source("MainWindow.P1CommandLiveFreshness.cs");
        var ui = Source("MainWindow.xaml.cs");
        var xaml = Source("MainWindow.xaml");

        Assert.Contains("state.SoeRepeatGuard.Admit(", runtime);
        Assert.Contains("PublishControlAudit(session, request,", runtime);
        Assert.Contains("EventRaised?.Invoke(new Iec61850EventEntry", audit);
        Assert.Contains("Sequence = Interlocked.Increment(ref _eventSequence)", audit);
        Assert.Contains("ControlFeedback = result is { FeedbackConfirmed: true }", audit);
        Assert.Contains("if (entry.IsControlEvent)", facade);
        Assert.Contains("if (entry.IsControlEvent)", p1);
        Assert.Contains("if (!eventEntry.IsControlEvent)", ui);
        Assert.Contains("Binding=\"{Binding EventTimestamp}\"", xaml);
        Assert.Contains("Csv(entry.ControlEvidence)", ui);
        Assert.Contains("if (result.FeedbackConfirmed &&", ui);
        Assert.Contains("RecordUnsentDockControl(device, signal", ui);
        Assert.Contains("Binding=\"{Binding QualityDisplay}\"", xaml);
        Assert.Contains("Binding=\"{Binding EventTimeContext}\"", xaml);
        Assert.Contains("TargetName=\"QualityBadge\" Property=\"Visibility\" Value=\"Collapsed\"", xaml);
        Assert.Contains("Quality = string.Empty", audit);
    }
}
