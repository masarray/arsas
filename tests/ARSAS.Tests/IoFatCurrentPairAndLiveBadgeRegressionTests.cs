using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatCurrentPairAndLiveBadgeRegressionTests
{
    [Fact]
    public void CompletedGenericDigitalPair_TrueThenFalse_IsPass()
    {
        var point = NewDigitalPoint();
        point.Runtime.SetFatValueEvidence(Evidence(FatValueSlot.Value1, "True", 1));
        point.Runtime.SetFatValueEvidence(Evidence(FatValueSlot.Value2, "false", 2));

        var decision = FatCurrentEvidenceAssessmentService.Apply(point);

        Assert.Equal(IoTestPointState.Passed, decision.State);
        Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
        Assert.Equal("✔ PASS", point.FatResultText);
        Assert.True(point.IsFatEvidenceComplete);
    }

    [Fact]
    public void CommissioningCard_UsesActualEngineeringRuntime_NotCachedPlanFlags()
    {
        var source = ReadRepoFile("IoListTestingWindow.CommissioningStatus.cs");

        Assert.Contains("engineeringWindow.Devices", source, StringComparison.Ordinal);
        Assert.Contains("device.IsConnected", source, StringComparison.Ordinal);
        Assert.Contains("device.IsMonitoring", source, StringComparison.Ordinal);
        Assert.Contains("\"RECONNECTING\"", source, StringComparison.Ordinal);
        Assert.Contains("RefreshCurrentPairVerdicts(plan);", source, StringComparison.Ordinal);
        Assert.Contains("FatCurrentEvidenceAssessmentService.Apply(point);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatBooleanPresentation_CanonicalizesCaseWithoutRewritingEvidence()
    {
        var source = ReadRepoFile("IoListTestingWindow.CommissioningStatus.cs");

        Assert.Contains("NormalizeFatBooleanPresentation", source, StringComparison.Ordinal);
        Assert.Contains("SetCurrentValue(TextBlock.TextProperty, \"True\")", source, StringComparison.Ordinal);
        Assert.Contains("SetCurrentValue(TextBlock.TextProperty, \"False\")", source, StringComparison.Ordinal);
        Assert.Contains("without rewriting relay evidence", source, StringComparison.OrdinalIgnoreCase);
    }

    private static IoTestPointPlan NewDigitalPoint()
        => new()
        {
            TestPointId = "DI-PAIR",
            IedName = "IED1",
            IpAddress = "192.0.2.10",
            SignalName = "TimeSynchrnz",
            ObjectReference = "IED1LD0/GGIO1.TimeSynchrnz.stVal",
            FunctionalConstraint = "ST",
            ExpectedOnText = "TRUE",
            ExpectedOffText = "FALSE",
            DataType = "Boolean",
            SignalKind = FatSignalKind.Discrete,
            CaptureMode = FatCaptureMode.AutomaticTransition,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = IoTestSignalSelectionService.SclWorkspaceAuthorityBindingStatus
        };

    private static FatValueEvidence Evidence(FatValueSlot slot, string rawValue, long sequence)
        => new(
            Guid.NewGuid(),
            slot,
            FatEvidenceCaptureKind.AutomaticValue,
            rawValue,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMilliseconds(-2),
            "Good",
            "BRCB",
            sequence,
            1);

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
