using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatRelayBenchP0RegressionTests
{
    [Fact]
    public void ReportBackedAnalog_LatchesFirstGoodValueAndFirstChangedValueImmediately()
    {
        var point = NewAnalogPoint("TOTVA");
        var coordinator = new FatAutoCaptureCoordinator();

        var value1 = coordinator.Observe(point, Observation("0", 1, "BRCB"));

        Assert.NotNull(value1.Evidence);
        Assert.Equal(FatValueSlot.Value1, value1.Evidence!.Slot);
        Assert.Equal("0", value1.Evidence.RawValue);
        Assert.Equal(FatAutoCaptureStage.WaitingChange, value1.Stage);
        point.Runtime.SetFatValueEvidence(value1.Evidence);

        var value2 = coordinator.Observe(point, Observation("1000", 2, "BRCB"));

        Assert.NotNull(value2.Evidence);
        Assert.Equal(FatValueSlot.Value2, value2.Evidence!.Slot);
        Assert.Equal("1000", value2.Evidence.RawValue);
        Assert.Equal(FatAutoCaptureStage.Complete, value2.Stage);
    }

    [Fact]
    public void ReportBackedAnalog_EquivalentNoiseDoesNotConsumeValue2()
    {
        var point = NewAnalogPoint("TOTVA-NOISE");
        var coordinator = new FatAutoCaptureCoordinator();

        var baseline = coordinator.Observe(point, Observation("1000", 1, "InformationReport/BRCB"));
        Assert.NotNull(baseline.Evidence);
        point.Runtime.SetFatValueEvidence(baseline.Evidence!);

        var equivalent = coordinator.Observe(point, Observation("1000.2", 2, "InformationReport/BRCB"));
        Assert.Null(equivalent.Evidence);
        Assert.Equal(FatAutoCaptureStage.WaitingChange, equivalent.Stage);

        var changed = coordinator.Observe(point, Observation("1001", 3, "InformationReport/BRCB"));
        Assert.NotNull(changed.Evidence);
        Assert.Equal(FatValueSlot.Value2, changed.Evidence!.Slot);
        Assert.Equal("1001", changed.Evidence.RawValue);
    }

    [Fact]
    public void PolledAnalog_KeepsSettlingGuard()
    {
        var point = NewAnalogPoint("POLL-FALLBACK");
        var coordinator = new FatAutoCaptureCoordinator();

        var first = coordinator.Observe(point, Observation("12.5", 1, "MMS-POLL"));
        var second = coordinator.Observe(point, Observation("12.5", 2, "MMS-POLL"));
        var third = coordinator.Observe(point, Observation("12.5", 3, "MMS-POLL"));

        Assert.Null(first.Evidence);
        Assert.Null(second.Evidence);
        Assert.NotNull(third.Evidence);
        Assert.Equal(FatValueSlot.Value1, third.Evidence!.Slot);
    }

    [Fact]
    public void EvidenceJournal_DoesNotForcePhysicalDiskBarrierOnEveryAppend()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            Path.Combine("Services", "IoTesting", "IoTestEvidenceJournal.cs")));

        Assert.DoesNotContain("FileOptions.WriteThrough", source, StringComparison.Ordinal);
        Assert.Contains("private void FlushVisible() => _writer.Flush();", source, StringComparison.Ordinal);
        Assert.Contains("_stream.Flush(flushToDisk: true);", source, StringComparison.Ordinal);
        Assert.Contains("FileOptions.SequentialScan", source, StringComparison.Ordinal);
    }

    private static IoTestPointPlan NewAnalogPoint(string id)
        => new()
        {
            TestPointId = id,
            IedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            SignalName = id,
            ObjectReference = $"AA1E1F06R4/PPRE_MMXU1.{id}",
            FunctionalConstraint = "MX",
            ExpectedOnText = "Value 1",
            ExpectedOffText = "Value 2",
            SignalKind = FatSignalKind.Analog,
            CaptureMode = FatCaptureMode.OperatorSnapshot,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = "CID_DATASET_EXACT"
        };

    private static IoTestObservation Observation(string rawValue, long sequence, string source)
    {
        var captured = new DateTimeOffset(2026, 9, 6, 9, 0, 0, TimeSpan.Zero)
            .AddMilliseconds(sequence * 10);
        return new IoTestObservation(
            null,
            rawValue,
            captured,
            captured.AddMilliseconds(-2),
            "Good",
            source,
            sequence,
            1);
    }

    private static string FindRepositoryFile(string relativePath)
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
