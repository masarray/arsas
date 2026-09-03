using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatSharedWorkspaceRuntimeDedupRegressionTests
{
    [Fact]
    public void RedundantManualRuntimeOverlay_IsRetiredBeforeFatPreflight()
    {
        const string runtime = "AA1E1F00R1ADD/GGIO2.TimeSynchrnz.stVal";
        var staticA = StaticPoint(
            "scl-static-a",
            runtime,
            "AA1E1F00R1ADD/GGIO2.TimeSynchrnz",
            "AA1E1F00R1LD0/LLN0.EventsA",
            1);
        var staticB = StaticPoint(
            "scl-static-b",
            runtime,
            "AA1E1F00R1ADD/GGIO2.TimeSynchrnz.stVal",
            "AA1E1F00R1LD0/LLN0.EventsB",
            2);
        var manual = ManualPoint(runtime);
        var ied = Ied(staticA, staticB, manual);

        var blocked = IoTestSessionPreflight.Validate(ied);
        Assert.False(blocked.Succeeded);
        Assert.Contains("multiple enabled test points", blocked.Message, StringComparison.OrdinalIgnoreCase);

        var retired = IoFatEngineeringSelectionBridge.RetireRedundantManualWorkspaceRows(ied);

        Assert.Equal(1, retired);
        Assert.False(manual.WorkspaceSelected);
        Assert.True(manual.TestEnabled);
        Assert.True(manual.IsIncludedInFat);
        Assert.True(staticA.WorkspaceSelected);
        Assert.True(staticB.WorkspaceSelected);

        var ready = IoTestSessionPreflight.Validate(ied);
        Assert.True(ready.Succeeded, ready.Message);
    }

    [Fact]
    public void StaticRuntimeCoverage_PreservesDistinctMembershipFanOut()
    {
        const string runtime = "AA1E1F00R1ADD/GGIO2.TimeSynchrnz.stVal";
        var staticA = StaticPoint(
            "scl-static-a",
            runtime,
            "AA1E1F00R1ADD/GGIO2.TimeSynchrnz",
            "AA1E1F00R1LD0/LLN0.EventsA",
            1);
        var staticB = StaticPoint(
            "scl-static-b",
            runtime,
            "AA1E1F00R1ADD/GGIO2.TimeSynchrnz.stVal",
            "AA1E1F00R1LD0/LLN0.EventsB",
            2);
        var manual = ManualPoint(runtime);
        var ied = Ied(staticA, staticB, manual);

        var coverage = IoFatEngineeringSelectionBridge.FindStaticDataSetRuntimeCoverage(
            ied,
            runtime,
            "ST");

        Assert.Equal(2, coverage.Count);
        Assert.Contains(staticA, coverage);
        Assert.Contains(staticB, coverage);
        Assert.DoesNotContain(manual, coverage);
    }

    [Fact]
    public void ManualOnlyRuntime_RemainsSharedWorkspaceScope()
    {
        const string runtime = "AA1E1F00R1ADD/GGIO2.PrSetChgd.stVal";
        var manual = ManualPoint(runtime);
        var ied = Ied(manual);

        var retired = IoFatEngineeringSelectionBridge.RetireRedundantManualWorkspaceRows(ied);

        Assert.Equal(0, retired);
        Assert.True(manual.WorkspaceSelected);
        Assert.True(manual.TestEnabled);
        Assert.True(manual.IsIncludedInFat);
    }

    private static IoTestPointPlan StaticPoint(
        string id,
        string runtimeReference,
        string staticReference,
        string dataSet,
        int memberIndex)
        => new()
        {
            TestPointId = id,
            IedName = "AA1E1F00R1",
            IpAddress = "192.168.81.83",
            SignalName = staticReference,
            ObjectReference = runtimeReference,
            FunctionalConstraint = "ST",
            ExpectedOnText = "TRUE",
            ExpectedOffText = "FALSE",
            DataType = "BOOLEAN",
            SignalAddress = "source-sha",
            DataSetName = dataSet,
            SourceIecReference = staticReference,
            ReportDisplayReference = staticReference,
            EventLogSearchReference = runtimeReference,
            SourceRow = memberIndex,
            SignalKind = FatSignalKind.Discrete,
            CaptureMode = FatCaptureMode.AutomaticTransition,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = IoTestSignalSelectionService.SclDataSetAuthorityBindingStatus,
            BindingEvidence = "Static SCL DataSet authority"
        };

    private static IoTestPointPlan ManualPoint(string runtimeReference)
        => new()
        {
            TestPointId = "scl-manual-8498597f6ee9a39943c0",
            IedName = "AA1E1F00R1",
            IpAddress = "192.168.81.83",
            SignalName = "GGIO2.TimeSynchrnz.stVal",
            ObjectReference = runtimeReference,
            FunctionalConstraint = "ST",
            ExpectedOnText = "TRUE",
            ExpectedOffText = "FALSE",
            DataType = "BOOLEAN",
            SignalAddress = "source-sha",
            SourceIecReference = runtimeReference,
            ReportDisplayReference = runtimeReference,
            EventLogSearchReference = runtimeReference,
            SignalKind = FatSignalKind.Discrete,
            CaptureMode = FatCaptureMode.AutomaticTransition,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = IoTestSignalSelectionService.SclWorkspaceAuthorityBindingStatus,
            BindingEvidence = "Shared SCL workspace authority"
        };

    private static IoTestIedPlan Ied(params IoTestPointPlan[] points)
        => new()
        {
            IedName = "AA1E1F00R1",
            IpAddress = "192.168.81.83",
            TestPoints = points.ToList()
        };
}
