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
        var blocked = IoTestSessionPreflight.Validate(ied);
        Assert.False(blocked.Succeeded);
        Assert.Contains("multiple enabled test points", blocked.Message, StringComparison.OrdinalIgnoreCase);

        var retired = IoFatEngineeringSelectionBridge.RetireManualWorkspaceRowsForStaticDataSetMode(ied);

        Assert.Equal(1, retired);
        Assert.True(staticPoint.WorkspaceSelected);
        Assert.False(manualAlias.WorkspaceSelected);
        Assert.True(manualAlias.TestEnabled);
        Assert.True(manualAlias.IsIncludedInFat);
        Assert.Equal(primaryLiveLeaf, manualAlias.LiveSignalReference);

        var ready = IoTestSessionPreflight.Validate(ied);
        Assert.True(ready.Succeeded, ready.Message);
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

    [Fact]
    public void EngineeringBootstrap_AppliesStaticCleanupBeforeProductionWindowIsShown()
    {
        var source = Read("MainWindow.ProductionFatEngineeringBootstrap.cs");
        var synchronize = source.IndexOf("SynchronizeImportedSclFatWithEngineering(launch.Project);", StringComparison.Ordinal);
        var retire = source.IndexOf("RetireManualWorkspaceRowsForStaticDataSetMode", StringComparison.Ordinal);
        var show = source.IndexOf("await ShowIoTestingWorkspaceAsync(launch, importWarningCount: 0);", StringComparison.Ordinal);

        Assert.True(synchronize >= 0, "Engineering/FAT synchronization call is missing.");
        Assert.True(retire > synchronize, "Static DataSet cleanup must happen after shared selection synchronization so newly-created manual aliases are also retired.");
        Assert.True(show > retire, "Static DataSet cleanup must complete before the production FAT workspace/session can be exposed.");
    }

    [Fact]
    public void EmbeddedAutomaticBootstrap_NeverHidesEngineeringWindow()
    {
        var source = Read("MainWindow.ProductionFatNoFlicker.cs");

        Assert.Contains("public new void Hide()", source, StringComparison.Ordinal);
        Assert.Contains("ShouldKeepEngineeringVisibleDuringProductionFatBootstrap", source, StringComparison.Ordinal);
        Assert.Contains("_productionFatEngineeringBootstrapBusy", source, StringComparison.Ordinal);
        Assert.Contains("ProductionFatTabReady", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MainTabs.SelectedIndex == NativeFatWorkspaceIndex", source, StringComparison.Ordinal);
        Assert.Contains("base.Hide();", source, StringComparison.Ordinal);
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

    private static IoTestPointPlan ManualPoint(string runtimeReference)
        => new()
        {
            TestPointId = "scl-manual-8498597f6ee9a39943c0",
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
            BindingStatus = IoTestSignalSelectionService.SclWorkspaceAuthorityBindingStatus,
            BindingEvidence = "Shared SCL workspace authority"
        };

    private static IoTestIedPlan Ied(params IoTestPointPlan[] points)
        => new()
        {
            IedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            TestPoints = points.ToList()
        };

    private static string Read(string relativePath)
        => File.ReadAllText(FindRepoFile(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

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

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
