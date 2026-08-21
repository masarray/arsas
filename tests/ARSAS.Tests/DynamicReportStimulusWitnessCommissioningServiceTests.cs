using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class DynamicReportStimulusWitnessCommissioningServiceTests
{
    [Theory]
    [InlineData("Native MMS Confirmed-Read decoded value: false.", "false")]
    [InlineData("Native MMS Confirmed-Read decoded value: true.", "true")]
    [InlineData("Native MMS Confirmed-Read decoded value: 12.5.", "12.5")]
    [InlineData("custom-value", "custom-value")]
    public void WitnessValueExtractor_NormalizesReadEvidence(string message, string expected)
    {
        Assert.Equal(expected, DynamicReportStimulusWitnessCommissioningService.ExtractWitnessValue(message));
    }

    [Fact]
    public void WitnessComparison_ReportsOnlyQualifiedIndexesThatActuallyChanged()
    {
        var refs = new[] { "LD0/GGIO1$ST$A$stVal", "LD0/GGIO1$ST$B$stVal", "LD0/GGIO1$ST$C$stVal" };
        var observed = DateTimeOffset.Parse("2026-08-21T10:00:00Z");

        var transitions = DynamicReportStimulusWitnessCommissioningService.CompareStimulusWitnessSamples(
            refs,
            ["false", "false", "true"],
            ["false", "true", "false"],
            observed);

        Assert.Equal(2, transitions.Count);
        Assert.Equal(1, transitions[0].Index);
        Assert.Equal(refs[1], transitions[0].MemberReference);
        Assert.Equal("false", transitions[0].BeforeValue);
        Assert.Equal("true", transitions[0].AfterValue);
        Assert.Equal(2, transitions[1].Index);
        Assert.Equal(observed, transitions[1].ObservedAtUtc);
    }

    [Fact]
    public void WitnessComparison_IgnoresReadFailureSentinels()
    {
        var transitions = DynamicReportStimulusWitnessCommissioningService.CompareStimulusWitnessSamples(
            ["LD0/GGIO1$ST$A$stVal"],
            ["false"],
            ["<read-failed>"],
            DateTimeOffset.UtcNow);

        Assert.Empty(transitions);
    }

    [Fact]
    public void WitnessComparison_RejectsMismatchedArrayLengths()
    {
        Assert.Throws<ArgumentException>(() =>
            DynamicReportStimulusWitnessCommissioningService.CompareStimulusWitnessSamples(
                ["A", "B"],
                ["false"],
                ["false", "true"],
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void G25A1_SourceKeepsWitnessReadOnlyAndCoreNoGi()
    {
        var witness = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "DynamicReportStimulusWitnessCommissioningService.cs"));
        var core = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "DynamicReportSpontaneousDataChangeCommissioningService.cs"));
        var ui = File.ReadAllText(Path.Combine(RepoRoot(), "DynamicReportQualificationUiBehavior.cs"));
        var runtime = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "Iec61850MonitorRuntime.cs"));

        Assert.Contains("ReadSingleVariableAsync", witness, StringComparison.Ordinal);
        Assert.Contains("probeReportAttributes: false", witness, StringComparison.Ordinal);
        Assert.Contains("G2.5-A1 WITNESS READY", witness, StringComparison.Ordinal);
        Assert.Contains("CompareStimulusWitnessSamples", witness, StringComparison.Ordinal);
        Assert.Contains("Intersect(changedIndexes)", witness, StringComparison.Ordinal);

        Assert.DoesNotContain("WriteReportAttributeAsync", witness, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareDynamicRcbCommissioningFieldsAsync", witness, StringComparison.Ordinal);
        Assert.DoesNotContain("DefineNamedVariableListAsync", witness, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteNamedVariableListAsync", witness, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitor", witness, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", witness, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible", witness, StringComparison.Ordinal);

        Assert.Contains("triggerGeneralInterrogation: false", core, StringComparison.Ordinal);
        Assert.DoesNotContain("triggerGeneralInterrogation: true", core, StringComparison.Ordinal);
        Assert.Contains("G2.5-A1 spontaneous dchg + independent stimulus witness", ui, StringComparison.Ordinal);
        Assert.Contains("DO NOT stimulate yet", ui, StringComparison.Ordinal);
        Assert.Contains("G2.5-A1 WITNESS READY", ui, StringComparison.Ordinal);
        Assert.Contains("DynamicReportStimulusWitnessCommissioningService", ui, StringComparison.Ordinal);

        Assert.DoesNotContain("AllowDynamicBrcb = true", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicUrcb = true", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void G25A1_UsesBoundedWindowShorterThanCoreProofWindow()
    {
        Assert.True(DynamicReportStimulusWitnessCommissioningService.WitnessWindow > TimeSpan.Zero);
        Assert.True(DynamicReportStimulusWitnessCommissioningService.WitnessWindow < DynamicReportSpontaneousDataChangeCommissioningService.SpontaneousProofWindow);
        Assert.True(DynamicReportStimulusWitnessCommissioningService.WitnessInterCycleDelay > TimeSpan.Zero);
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ArIED61850Tester.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("ARSAS repository root not found.");
    }
}
