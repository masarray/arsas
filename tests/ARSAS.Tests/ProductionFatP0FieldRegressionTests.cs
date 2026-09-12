using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class ProductionFatP0FieldRegressionTests
{
    [Fact]
    public void AutomaticStaticDataSetScope_RetiresManualAliasBeforeSessionPreflight()
    {
        const string staticReference = "AA1E1F06R4V1T3p1_OperationalValues/RPRE_MMXU1.A.phsA";
        const string primaryLiveLeaf = "AA1E1F06R4V1T3p1_OperationalValues/RPRE_MMXU1.A.phsA.cVal.mag.f";

        var staticPoint = StaticPoint(staticReference);
        var manualAlias = ManualPoint(primaryLiveLeaf);
        staticPoint.ApplyLiveBinding(
            IoTestLiveBindingState.LivePointReady,
            "regression live binding",
            "device-1",
            primaryLiveLeaf);
        manualAlias.ApplyLiveBinding(
            IoTestLiveBindingState.LivePointReady,
            "regression live binding",
            "device-1",
            primaryLiveLeaf);

        var ied = Ied(staticPoint, manualAlias);
        var ready = IoTestSessionPreflight.Validate(ied);

        Assert.True(ready.Succeeded, ready.Message);
        Assert.True(staticPoint.WorkspaceSelected);
        Assert.False(manualAlias.WorkspaceSelected);
        Assert.True(manualAlias.TestEnabled);
        Assert.True(manualAlias.IsIncludedInFat);
        Assert.Equal(primaryLiveLeaf, manualAlias.LiveSignalReference);

        var retired = IoFatEngineeringSelectionBridge.RetireManualWorkspaceRowsForStaticDataSetMode(ied);
        Assert.Equal(0, retired);
    }

    [Fact]
    public void Preflight_LiveDuplicateGroup_RetiresRestoredManualIdEvenWhenLegacyBindingStatusWasLost()
    {
        const string staticReference = "AA1E1F06R4V1T3p1_OperationalValues/RPRE_MMXU1.A.phsA";
        const string primaryLiveLeaf = "AA1E1F06R4V1T3p1_OperationalValues/RPRE_MMXU1.A.phsA.cVal.mag.f";

        var staticPoint = StaticPoint(staticReference);
        var restoredManualAlias = ManualPoint(
            primaryLiveLeaf,
            bindingStatus: "LEGACY_RESTORED_SCL_ALIAS",
            testPointId: "scl-manual-7496d038be4fdc18e340");
        staticPoint.ApplyLiveBinding(
            IoTestLiveBindingState.LivePointReady,
            "field-proven primary leaf",
            "device-1",
            primaryLiveLeaf);
        restoredManualAlias.ApplyLiveBinding(
            IoTestLiveBindingState.LivePointReady,
            "field-proven primary leaf",
            "device-1",
            primaryLiveLeaf);

        var ied = Ied(staticPoint, restoredManualAlias);
        var ready = IoTestSessionPreflight.Validate(ied);

        Assert.True(ready.Succeeded, ready.Message);
        Assert.True(staticPoint.WorkspaceSelected);
        Assert.False(restoredManualAlias.WorkspaceSelected);
        Assert.True(restoredManualAlias.TestEnabled);
        Assert.True(restoredManualAlias.IsIncludedInFat);
        Assert.Contains("Retired 1 stale manual live-reference alias", ready.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preflight_TrueNonManualDuplicate_RemainsBlocked()
    {
        const string staticReference = "AA1E1F06R4V1T3p1_OperationalValues/RPRE_MMXU1.A.phsA";
        const string primaryLiveLeaf = "AA1E1F06R4V1T3p1_OperationalValues/RPRE_MMXU1.A.phsA.cVal.mag.f";

        var staticPoint = StaticPoint(staticReference);
        var ambiguousLegacyPoint = ManualPoint(
            primaryLiveLeaf,
            bindingStatus: "LEGACY_WORKBOOK_MAPPING",
            testPointId: "legacy-import-duplicate");
        staticPoint.ApplyLiveBinding(
            IoTestLiveBindingState.LivePointReady,
            "field-proven primary leaf",
            "device-1",
            primaryLiveLeaf);
        ambiguousLegacyPoint.ApplyLiveBinding(
            IoTestLiveBindingState.LivePointReady,
            "field-proven primary leaf",
            "device-1",
            primaryLiveLeaf);

        var ied = Ied(staticPoint, ambiguousLegacyPoint);
        var blocked = IoTestSessionPreflight.Validate(ied);

        Assert.False(blocked.Succeeded);
        Assert.Contains("multiple enabled test points", blocked.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(primaryLiveLeaf, blocked.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(staticPoint.WorkspaceSelected);
        Assert.True(ambiguousLegacyPoint.WorkspaceSelected);
    }

    [Fact]
    public void StaticDataSetModeCleanup_DoesNotTouchManualOnlyProjects()
    {
        var manual = ManualPoint("AA1E1F06R4LD0/GGIO1.Ind1.stVal");
        var ied = Ied(manual);

        var retired = IoFatEngineeringSelectionBridge.RetireManualWorkspaceRowsForStaticDataSetMode(ied);

        Assert.Equal(0, retired);
        Assert.True(manual.WorkspaceSelected);
        Assert.True(manual.TestEnabled);
        Assert.True(manual.IsIncludedInFat);
    }

    private static IoTestPointPlan StaticPoint(string staticReference)
        => new()
        {
            TestPointId = "scl-static-fat-member",
            IedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            SignalName = "A PhsA",
            ObjectReference = staticReference,
            FunctionalConstraint = "MX",
            ExpectedOnText = "Value 1",
            ExpectedOffText = "Value 2",
            DataType = "FLOAT32",
            SignalAddress = "source-sha",
            DataSetName = "AA1E1F06R4LD0/LLN0.OperationalValues",
            SourceIecReference = staticReference,
            ReportDisplayReference = staticReference,
            EventLogSearchReference = staticReference,
            SourceRow = 1,
            SignalKind = FatSignalKind.Analog,
            CaptureMode = FatCaptureMode.OperatorSnapshot,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = IoTestSignalSelectionService.SclDataSetAuthorityBindingStatus,
            BindingEvidence = "Static SCL DataSet authority"
        };

    private static IoTestPointPlan ManualPoint(
        string runtimeReference,
        string? bindingStatus = null,
        string testPointId = "scl-manual-8498597f6ee9a39943c0")
        => new()
        {
            TestPointId = testPointId,
            IedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            SignalName = "A PhsA scalar alias",
            ObjectReference = runtimeReference,
            FunctionalConstraint = "MX",
            ExpectedOnText = "Value 1",
            ExpectedOffText = "Value 2",
            DataType = "FLOAT32",
            SignalAddress = "source-sha",
            SourceIecReference = runtimeReference,
            ReportDisplayReference = runtimeReference,
            EventLogSearchReference = runtimeReference,
            SignalKind = FatSignalKind.Analog,
            CaptureMode = FatCaptureMode.OperatorSnapshot,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = bindingStatus ?? IoTestSignalSelectionService.SclWorkspaceAuthorityBindingStatus,
            BindingEvidence = "Shared SCL workspace authority"
        };

    private static IoTestIedPlan Ied(params IoTestPointPlan[] points)
        => new()
        {
            IedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            TestPoints = points.ToList()
        };
}
