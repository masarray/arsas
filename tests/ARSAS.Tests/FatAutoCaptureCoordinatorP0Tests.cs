using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class FatAutoCaptureCoordinatorP0Tests
{
    [Fact]
    public void StaticDataSetAnalog_CapturesValue1AndValue2FromAuthoritativeReports()
    {
        var point = AnalogPoint();
        var coordinator = new FatAutoCaptureCoordinator();

        var first = coordinator.Observe(point, Observation("12.34", 1, "Static DataSet: StaticBrcb"));
        Assert.NotNull(first.Evidence);
        Assert.Equal(FatValueSlot.Value1, first.Evidence!.Slot);
        Assert.Equal("12.34", first.Evidence.RawValue);
        point.Runtime.SetFatValueEvidence(first.Evidence);

        var second = coordinator.Observe(point, Observation("18.90", 2, "BRCB InformationReport"));
        Assert.NotNull(second.Evidence);
        Assert.Equal(FatValueSlot.Value2, second.Evidence!.Slot);
        Assert.Equal("18.90", second.Evidence.RawValue);
    }

    [Fact]
    public void PollBackedAnalog_StillRequiresStableSampleWindow()
    {
        var point = AnalogPoint();
        var coordinator = new FatAutoCaptureCoordinator();

        Assert.Null(coordinator.Observe(point, Observation("12.3400", 1, "MMS" )).Evidence);
        Assert.Null(coordinator.Observe(point, Observation("12.3401", 2, "MMS" )).Evidence);
        var third = coordinator.Observe(point, Observation("12.3402", 3, "MMS" ));

        Assert.NotNull(third.Evidence);
        Assert.Equal(FatValueSlot.Value1, third.Evidence!.Slot);
    }

    [Theory]
    [InlineData("true", "True")]
    [InlineData("TRUE", "True")]
    [InlineData("false", "False")]
    [InlineData("False", "False")]
    [InlineData("Closed", "Closed")]
    [InlineData("12.34 A", "12.34 A")]
    public void FatPresentation_CanonicalizesOnlyBooleanText(string raw, string expected)
        => Assert.Equal(expected, IoFatValuePresentation.Canonicalize(raw));

    private static IoTestPointPlan AnalogPoint()
        => new()
        {
            TestPointId = "P0-ANALOG-1",
            IedName = "IED1",
            IpAddress = "192.0.2.1",
            SignalName = "Current A",
            ObjectReference = "IED1MEAS/MMXU1.A.phsA.cVal.mag.f",
            FunctionalConstraint = "MX",
            ExpectedOnText = "Value 1",
            ExpectedOffText = "Value 2",
            DataType = "FLOAT32",
            SignalKind = FatSignalKind.Analog,
            CaptureMode = FatCaptureMode.OperatorSnapshot,
            ImportReady = true,
            BindingStatus = "SCL_DATASET_AUTHORITY"
        };

    private static IoTestObservation Observation(string value, long sequence, string source)
        => new(
            null,
            value,
            new DateTimeOffset(2026, 9, 6, 1, 0, 0, TimeSpan.Zero).AddSeconds(sequence),
            new DateTimeOffset(2026, 9, 6, 1, 0, 0, TimeSpan.Zero).AddSeconds(sequence).AddMilliseconds(-2),
            "Good",
            source,
            sequence,
            1);
}
