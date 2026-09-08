using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatBenchFreezeRegressionTests
{
    [Fact]
    public void EvidenceDrain_DoesNotOverwriteSharedEngineeringLiveProjection()
    {
        var point = Point();
        point.Runtime.CurrentValue = "Open [01]";
        point.Runtime.CurrentQuality = "Good";
        point.Runtime.CurrentSource = "Shared Engineering process image";
        point.Runtime.CurrentIedTimestamp = "2026-09-07 05:34:39.182";

        var coordinator = new IoTestRollingCaptureCoordinator(new IoTestTransitionEvaluator());
        coordinator.Start(point, Observation(false, "Closed [10]", 10));
        coordinator.Observe(point, Observation(true, "Open [01]", 11));
        coordinator.Observe(point, Observation(false, "Closed [10]", 12));

        Assert.Equal("Open [01]", point.Runtime.CurrentValue);
        Assert.Equal("Good", point.Runtime.CurrentQuality);
        Assert.Equal("Shared Engineering process image", point.Runtime.CurrentSource);
        Assert.Equal("2026-09-07 05:34:39.182", point.Runtime.CurrentIedTimestamp);
    }

    [Fact]
    public void FatWindow_CoalescesLegacySessionWrapperNotifications()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.P1NotificationThrottle.cs"));

        Assert.Contains("Session.PropertyChanged -= window.Session_PropertyChanged", source, StringComparison.Ordinal);
        Assert.Contains("Session.PropertyChanged += window.P1Session_PropertyChanged", source, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _p1WindowRefreshScheduled, 1)", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Background", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled = false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceCoordinator_DoesNotOwnOperatorFacingLiveValue()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoTestRollingCaptureCoordinator.cs"));

        Assert.Contains("ApplyEvidenceObservationState", source, StringComparison.Ordinal);
        Assert.DoesNotContain("target.ApplyObservation(observation)", source, StringComparison.Ordinal);
        Assert.Contains("shared Engineering process image", source, StringComparison.OrdinalIgnoreCase);
    }

    private static IoTestPointPlan Point() => new()
    {
        TestPointId = "TP-CSWI-POS",
        IedName = "AA1E1F06R4",
        IpAddress = "192.168.81.103",
        SignalName = "Pos",
        ObjectReference = "AA1E1F06R4Q0/CSWI1.Pos",
        FunctionalConstraint = "ST",
        ExpectedOnText = "Closed",
        ExpectedOffText = "Open",
        ExpectedOnRaw = 2,
        ExpectedOffRaw = 1,
        DataType = "DPC",
        ImportReady = true,
        BindingStatus = "SCL_DATASET_EXACT"
    };

    private static IoTestObservation Observation(bool state, string raw, long sequence)
    {
        var captured = new DateTimeOffset(2026, 9, 7, 5, 34, 39, TimeSpan.FromHours(7))
            .AddMilliseconds(sequence);
        return new IoTestObservation(
            state,
            raw,
            captured,
            captured.AddMilliseconds(-1),
            "Good",
            "BRCB",
            sequence,
            1);
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
