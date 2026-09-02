using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class ThdMeasurementAndWorkspaceBridgeRegressionTests
{
    [Theory]
    [InlineData("IEDTHD/I_MHAI1.ThdA.phsA.cVal.mag.f")]
    [InlineData("IEDTHD/I_MHAI1.ThdA.phsB.cVal.mag.f")]
    [InlineData("IEDTHD/I_MHAI1.ThdA.phsC.cVal.mag.f")]
    [InlineData("IEDTHD/V_MHAI1.ThdPPV.phsAB.cVal.mag.f")]
    [InlineData("IEDTHD/V_MHAI1.ThdPPV.phsBC.cVal.mag.f")]
    [InlineData("IEDTHD/V_MHAI1.ThdPPV.phsCA.cVal.mag.f")]
    public void NumericThdPhaseMagnitude_IsPublishableToLiveRuntime(string reference)
    {
        var signal = Signal(reference, reference, "IEDTHD/LLN0.Analog");

        Assert.False(signal.IsRawAttribute);
        Assert.True(signal.IsValueSignal);
        Assert.True(signal.CanPublishAsSignal);
        Assert.True(signal.CanPublishToRuntime);
    }

    [Theory]
    [InlineData("IEDTHD/I_MHAI1.ThdA")]
    [InlineData("IEDTHD/V_MHAI1.ThdPPV")]
    public void ThreePhaseThdParent_IsPublishableAsCompositeLiveValue(string reference)
    {
        var signal = Signal(reference, reference, "IEDTHD/LLN0.Analog");

        Assert.True(SignalDefinition.IsThreePhaseMeasurementAggregate(reference));
        Assert.False(signal.IsRawAttribute);
        Assert.True(signal.CanPublishToRuntime);
    }

    [Theory]
    [InlineData("IEDTHD/I_MHAI1.ThdA.phsA.q")]
    [InlineData("IEDTHD/V_MHAI1.ThdPPV.phsAB.t")]
    public void ThdCompanion_RemainsNonPublishable(string reference)
    {
        var signal = Signal(reference, reference, "IEDTHD/LLN0.Analog");

        Assert.False(signal.CanPublishAsSignal);
    }

    [Theory]
    [InlineData("IEDENERGY/XPRE_MMTR1.DmdWhMV.mag.f")]
    [InlineData("IEDENERGY/XPRE_MMTR1.DmdWhMV.instMag.f")]
    public void DemandEnergyMagnitude_IsNotDiscardedAsStatisticNoise(string reference)
    {
        var signal = Signal(reference, reference, "IEDENERGY/LLN0.Analog");

        Assert.False(signal.IsRawAttribute);
        Assert.True(signal.CanPublishToRuntime);
    }

    [Fact]
    public void ObjectLevelDemandEnergyDataSetMember_IsPublishableForScalarProjection()
    {
        const string reference = "IEDENERGY/XPRE_MMTR1.DmdWhMV";
        var signal = Signal(reference, reference, "IEDENERGY/LLN0.Analog");

        Assert.True(SignalDefinition.IsDemandEnergyAggregate(reference));
        Assert.False(signal.IsRawAttribute);
        Assert.True(signal.CanPublishToRuntime);
    }

    [Fact]
    public void FatFastPath_SkipsConnectedReconciliation_AndUsesSchemaSafeParentReads()
    {
        var root = FindRepoRoot();
        var autoConnect = File.ReadAllText(Path.Combine(root, "MainWindow.IoTesting.AutoConnect.cs"));
        var runtime = File.ReadAllText(Path.Combine(root, "Services", "Iec61850MonitorRuntime.cs"));
        var resolver = File.ReadAllText(Path.Combine(root, "Services", "IecSignalReadResolver.cs"));
        var aggregate = File.ReadAllText(Path.Combine(root, "Services", "SchemaSafeAggregateProjectionService.cs"));

        Assert.Contains("if (hasSclRuntimeAuthority)", autoConnect, StringComparison.Ordinal);
        Assert.Contains("IoTestReconciliationCache.Invalidate(device)", autoConnect, StringComparison.Ordinal);
        Assert.Contains("RequiresExactMmsValueAuthority(point.IecReference)", runtime, StringComparison.Ordinal);
        Assert.Contains("ReadSchemaSafeAggregateAsync", resolver, StringComparison.Ordinal);
        Assert.Contains("TryBuildSchemaSafeAggregateReadPlan", resolver, StringComparison.Ordinal);
        Assert.Contains("schema-safe-three-phase-exact-leaf-reads", resolver, StringComparison.Ordinal);
        Assert.Contains("schema-safe-demand-energy-exact-leaf-read", resolver, StringComparison.Ordinal);
        Assert.Contains("TryResolvePreferredAttributeReference", aggregate, StringComparison.Ordinal);
        Assert.DoesNotContain("Children.Take(3)", aggregate, StringComparison.Ordinal);
    }

    [Fact]
    public void FatWorkspace_ExposesIncrementalSclIedImport()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "IoListTestingWindow.xaml"));
        var importer = File.ReadAllText(Path.Combine(root, "Services", "IoTesting", "IoFatSclProjectImportService.cs"));

        Assert.Contains("AddFatIedFromScl_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("ImportAdditionalAsync", importer, StringComparison.Ordinal);
        Assert.Contains("AddRuntimeWorkspaces", importer, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshSclSelection_OverridesOlderEngineeringSelection_ThenSynchronizesBothWays()
    {
        const string dataSet = "IEDTHD/LLN0.Analog";
        const string member = "IEDTHD/I_MHAI1.ThdA.phsA";
        const string runtime = member + ".cVal.mag.f";
        var signal = Signal(runtime, member, dataSet);
        var device = Device(signal);
        var point = Point(member, runtime, dataSet);
        var ied = Ied(point);

        // Simulate a stale persisted checkbox plus an older/narrower Engineering selection.
        // Fresh raw SCL import must restore the included static DataSet member to checked and
        // push that selection into Engineering instead of inheriting the older false state.
        point.TestEnabled = false;
        IoFatEngineeringSelectionBridge.Initialize(
            ied,
            device,
            preserveExistingEngineeringSelection: true);
        Assert.True(point.TestEnabled);
        Assert.True(signal.IsSelected);

        // After initialization the shared bridge remains bidirectional: Engineering changes
        // still update FAT, and FAT checkbox changes still update the Engineering catalog.
        signal.IsSelected = false;
        Assert.True(IoFatEngineeringSelectionBridge.ApplyEngineeringSignalSelection(
            signal,
            selected: false,
            ied,
            device));
        Assert.False(point.TestEnabled);
        Assert.True(point.IsIncludedInFat);

        point.TestEnabled = true;
        Assert.True(IoFatEngineeringSelectionBridge.ApplyFatPointSelection(point, device));
        Assert.True(signal.IsSelected);
    }

    [Fact]
    public void RemovedSclFatRow_EngineeringSelectionChangesCheckboxButNeverRestoresDisposition()
    {
        const string dataSet = "IEDTHD/LLN0.Analog";
        const string member = "IEDTHD/I_MHAI1.ThdA.phsB";
        const string runtime = member + ".cVal.mag.f";
        var signal = Signal(runtime, member, dataSet);
        signal.IsSelected = true;
        var device = Device(signal);
        var point = Point(member, runtime, dataSet);
        var ied = Ied(point);

        point.RemoveFromFat();
        Assert.False(point.IsIncludedInFat);
        Assert.True(point.TestEnabled);

        signal.IsSelected = false;
        Assert.True(IoFatEngineeringSelectionBridge.ApplyEngineeringSignalSelection(
            signal,
            selected: false,
            ied,
            device));
        Assert.False(point.IsIncludedInFat);
        Assert.False(point.TestEnabled);

        signal.IsSelected = true;
        Assert.True(IoFatEngineeringSelectionBridge.ApplyEngineeringSignalSelection(
            signal,
            selected: true,
            ied,
            device));
        Assert.False(point.IsIncludedInFat);
        Assert.True(point.TestEnabled);

        point.RestoreToFat();
        Assert.True(point.IsIncludedInFat);
        Assert.True(point.TestEnabled);
    }

    [Fact]
    public void NewDirectSclFatWorkspace_SelectsTheSameCanonicalEngineeringRows()
    {
        const string dataSet = "IEDTHD/LLN0.Analog";
        const string member = "IEDTHD/V_MHAI1.ThdPPV.phsAB";
        const string runtime = member + ".cVal.mag.f";
        var canonical = Signal(runtime, member, dataSet);
        var duplicatePresentationLeaf = Signal(runtime, runtime, dataSet);
        var device = Device(canonical, duplicatePresentationLeaf);
        var point = Point(member, runtime, dataSet);

        IoFatEngineeringSelectionBridge.Initialize(
            Ied(point),
            device,
            preserveExistingEngineeringSelection: false);

        Assert.True(canonical.IsSelected);
        Assert.False(duplicatePresentationLeaf.IsSelected);
        Assert.Same(canonical, IoFatEngineeringSelectionBridge.FindSignal(point, device));
    }

    private static SignalDefinition Signal(string runtime, string display, string dataSet) => new()
    {
        Name = "THD",
        ObjectReference = runtime,
        DisplayReference = display,
        FunctionalConstraint = "MX",
        DataType = "FLOAT32",
        Category = "DataSet",
        DataSetReference = dataSet,
        ProbeStatus = "Not probed",
        Value = "-",
        Quality = "Unknown"
    };

    private static Iec61850MonitorDevice Device(params SignalDefinition[] signals)
    {
        var device = new Iec61850MonitorDevice
        {
            Name = "IEDTHD",
            SclIedName = "IEDTHD",
            IpAddress = "192.0.2.20",
            Port = 102
        };
        device.Signals.AddRange(signals);
        return device;
    }

    private static IoTestIedPlan Ied(IoTestPointPlan point) => new()
    {
        IedName = "IEDTHD",
        IpAddress = "192.0.2.20",
        TestPoints = [point]
    };

    private static IoTestPointPlan Point(string member, string runtime, string dataSet) => new()
    {
        TestPointId = "thd-phase",
        IedName = "IEDTHD",
        IpAddress = "192.0.2.20",
        SignalName = "THD",
        ObjectReference = runtime,
        FunctionalConstraint = "MX",
        ExpectedOnText = "Value 1",
        ExpectedOffText = "Value 2",
        DataSetName = dataSet,
        SourceIecReference = member,
        ReportDisplayReference = member,
        EventLogSearchReference = member,
        BindingStatus = IoTestSignalSelectionService.SclDataSetAuthorityBindingStatus,
        ImportReady = true,
        TestEnabled = true,
        SignalKind = FatSignalKind.Analog,
        CaptureMode = FatCaptureMode.OperatorSnapshot
    };

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ArIED61850Tester.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
