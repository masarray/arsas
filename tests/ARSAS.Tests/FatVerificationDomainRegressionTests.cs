using ArIED61850Tester.Models.IoTesting;

namespace ARSAS.Tests;

public sealed class FatVerificationDomainRegressionTests
{
    [Fact]
    public void RemoveAndRestore_AreOperatorDispositionOnly_AndPreserveEvidence()
    {
        var signal = BuildSignal("fat-1");
        var value1 = Evidence(FatValueSlot.Value1, "99.72 A");
        var value2 = Evidence(FatValueSlot.Value2, "498.61 A");
        signal.SetCurrentEvidence(value1);
        signal.SetCurrentEvidence(value2);
        var project = new FatVerificationProject { Signals = new List<FatVerificationSignal> { signal } };

        Assert.True(project.RemoveSignal(signal.SignalId));
        Assert.Empty(project.IncludedSignals);
        Assert.Single(project.RemovedSignals);
        Assert.Same(value1, signal.Value1Evidence);
        Assert.Same(value2, signal.Value2Evidence);

        Assert.True(project.RestoreSignal(signal.SignalId));
        Assert.Single(project.IncludedSignals);
        Assert.Empty(project.RemovedSignals);
        Assert.Same(value1, signal.Value1Evidence);
        Assert.Same(value2, signal.Value2Evidence);
    }

    [Fact]
    public void RestoreSignals_SupportsBulkSelection_WithoutTouchingUnselectedRows()
    {
        var a = BuildSignal("fat-a");
        var b = BuildSignal("fat-b");
        var c = BuildSignal("fat-c");
        a.RemoveFromFat();
        b.RemoveFromFat();
        c.RemoveFromFat();
        var project = new FatVerificationProject
        {
            Signals = new List<FatVerificationSignal> { a, b, c }
        };

        var restored = project.RestoreSignals(new[] { a.SignalId, c.SignalId });

        Assert.Equal(2, restored);
        Assert.True(a.IsIncludedInFat);
        Assert.False(b.IsIncludedInFat);
        Assert.True(c.IsIncludedInFat);
        Assert.Single(project.RemovedSignals);
        Assert.Same(b, project.RemovedSignals[0]);
    }

    [Fact]
    public void ReplacingCurrentEvidence_DoesNotChangeFatInclusion()
    {
        var signal = BuildSignal("fat-rolling");
        signal.RemoveFromFat();
        var first = Evidence(FatValueSlot.Value1, "100.0 A");
        var replacement = Evidence(FatValueSlot.Value1, "101.0 A");

        signal.SetCurrentEvidence(first);
        signal.SetCurrentEvidence(replacement);

        Assert.False(signal.IsIncludedInFat);
        Assert.Same(replacement, signal.Value1Evidence);
    }

    private static FatVerificationSignal BuildSignal(string id)
        => new()
        {
            SignalId = id,
            IedName = "IED1",
            DataSetReference = "IED1LD0/LLN0$DS",
            DataSetMemberIndex = 0,
            StaticMemberReference = "IED1MEAS/MMXU1.A.phsA",
            RuntimeReference = "IED1MEAS/MMXU1.A.phsA.cVal.mag.f",
            SignalName = "A.phsA",
            FunctionalConstraint = "MX",
            DataType = "FLOAT32",
            SignalKind = FatSignalKind.Analog,
            CaptureMode = FatCaptureMode.OperatorSnapshot
        };

    private static FatValueEvidence Evidence(FatValueSlot slot, string rawValue)
        => new(
            Guid.NewGuid(),
            slot,
            FatEvidenceCaptureKind.OperatorSnapshot,
            rawValue,
            new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 9, 59, 59, 900, TimeSpan.Zero),
            "good",
            "MMS",
            1,
            1);
}
