using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatCanonicalEvidenceMigrationRegressionTests
{
    [Fact]
    public void EngineeringStaticDataSetMigration_RemovesLegacyManualRowAndKeepsUniqueEvidence()
    {
        const string runtime = "AA1E1F06R4V1T3p1_OperationalValues/RPRE_MMXU1.A.phsA.cVal.mag.f";
        var canonical = StaticPoint("scl-static-1", runtime);
        var manual = ManualPoint("scl-manual-7496d038be4fdc18e340", runtime);
        Bind(canonical, runtime);
        Bind(manual, runtime);

        var value1 = Evidence(FatValueSlot.Value1, "10.1");
        var value2 = Evidence(FatValueSlot.Value2, "12.7");
        manual.Runtime.Value1Evidence = value1;
        manual.Runtime.Value2Evidence = value2;
        manual.Runtime.State = IoTestPointState.Passed;
        manual.Runtime.StatusReason = "legacy completed result";
        manual.Runtime.Attempt = 2;

        var project = Project(canonical, manual);

        var result = IoFatCanonicalEvidenceMigrationService.MigrateAndRemoveLegacyManualRows(project);

        Assert.Equal(1, result.RemovedManualRows);
        Assert.Equal(1, result.MigratedEvidenceRows);
        Assert.Equal(0, result.AmbiguousEvidenceRows);
        Assert.Single(project.Ieds[0].TestPoints);
        Assert.Same(canonical, project.Ieds[0].TestPoints[0]);
        Assert.DoesNotContain(project.Ieds[0].TestPoints, IoFatCanonicalEvidenceMigrationService.IsLegacyManualWorkspaceRow);
        Assert.Same(value1, canonical.Runtime.Value1Evidence);
        Assert.Same(value2, canonical.Runtime.Value2Evidence);
        Assert.Equal(IoTestPointState.Passed, canonical.Runtime.State);
        Assert.Equal(2, canonical.Runtime.Attempt);
    }

    [Fact]
    public void EngineeringStaticDataSetMigration_AmbiguousLegacyEvidenceNeverCreatesOrChoosesDuplicateAuthority()
    {
        const string runtime = "AA1E1F06R4LD0/GGIO1.AnIn1.mag.f";
        var canonicalA = StaticPoint("scl-static-a", runtime, "IED/LLN0.dsA");
        var canonicalB = StaticPoint("scl-static-b", runtime, "IED/LLN0.dsB");
        var manual = ManualPoint("scl-manual-aaaaaaaaaaaaaaaaaaaa", runtime);
        Bind(canonicalA, runtime);
        Bind(canonicalB, runtime);
        Bind(manual, runtime);
        manual.Runtime.Value1Evidence = Evidence(FatValueSlot.Value1, "3.14");

        var project = Project(canonicalA, canonicalB, manual);

        var result = IoFatCanonicalEvidenceMigrationService.MigrateAndRemoveLegacyManualRows(project);

        Assert.Equal(1, result.RemovedManualRows);
        Assert.Equal(0, result.MigratedEvidenceRows);
        Assert.Equal(1, result.AmbiguousEvidenceRows);
        Assert.Equal(2, project.SignalCount);
        Assert.All(project.Ieds[0].TestPoints, point => Assert.False(IoFatCanonicalEvidenceMigrationService.IsLegacyManualWorkspaceRow(point)));
        Assert.Null(canonicalA.Runtime.Value1Evidence);
        Assert.Null(canonicalB.Runtime.Value1Evidence);
    }

    [Fact]
    public void ManualOnlyLegacyProject_IsNotCanonicalizedByStaticDataSetMigration()
    {
        const string runtime = "AA1E1F06R4LD0/GGIO1.Ind1.stVal";
        var manual = ManualPoint("scl-manual-bbbbbbbbbbbbbbbbbbbb", runtime);
        var project = Project(manual);

        var result = IoFatCanonicalEvidenceMigrationService.MigrateAndRemoveLegacyManualRows(project);

        Assert.Equal(0, result.RemovedManualRows);
        Assert.Single(project.Ieds[0].TestPoints);
        Assert.Same(manual, project.Ieds[0].TestPoints[0]);
    }

    [Fact]
    public void EngineeringBootstrap_EnforcesCanonicalRowCountBeforeProductionGridIsShown()
    {
        var source = Read("MainWindow.ProductionFatEngineeringBootstrap.cs");
        var synchronize = source.IndexOf("SynchronizeImportedSclFatWithEngineering(launch.Project);", StringComparison.Ordinal);
        var migrate = source.IndexOf("MigrateAndRemoveLegacyManualRows(launch.Project)", StringComparison.Ordinal);
        var invariant = source.IndexOf("launch.Project.SignalCount != canonicalStaticRowCount", StringComparison.Ordinal);
        var show = source.IndexOf("await ShowIoTestingWorkspaceAsync(launch, importWarningCount: 0);", StringComparison.Ordinal);

        Assert.True(synchronize >= 0);
        Assert.True(migrate > synchronize, "Legacy evidence migration must run after Engineering synchronization.");
        Assert.True(invariant > migrate, "Canonical row-count invariant must run after legacy rows are removed.");
        Assert.True(show > invariant, "The production FAT grid must not be exposed before canonical row-count validation.");
    }

    private static IoTestProject Project(params IoTestPointPlan[] points)
        => new()
        {
            ProjectId = "canonical-regression",
            SchemaVersion = "ARSAS-FAT-SCL-1.0",
            ProjectName = "Canonical regression",
            Ieds = new List<IoTestIedPlan>
            {
                new()
                {
                    IedName = "AA1E1F06R4",
                    IpAddress = "192.168.81.103",
                    TestPoints = points.ToList()
                }
            }
        };

    private static IoTestPointPlan StaticPoint(
        string id,
        string runtimeReference,
        string dataSet = "AA1E1F06R4LD0/LLN0.OperationalValues")
        => new()
        {
            TestPointId = id,
            IedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            SignalName = "Static member",
            ObjectReference = runtimeReference,
            FunctionalConstraint = "MX",
            ExpectedOnText = "Value 1",
            ExpectedOffText = "Value 2",
            DataType = "FLOAT32",
            SignalAddress = "source-sha",
            DataSetName = dataSet,
            SourceIecReference = runtimeReference,
            ReportDisplayReference = runtimeReference,
            EventLogSearchReference = runtimeReference,
            SignalKind = FatSignalKind.Analog,
            CaptureMode = FatCaptureMode.OperatorSnapshot,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = IoTestSignalSelectionService.SclDataSetAuthorityBindingStatus,
            BindingEvidence = "Static SCL DataSet authority"
        };

    private static IoTestPointPlan ManualPoint(string id, string runtimeReference)
        => new()
        {
            TestPointId = id,
            IedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            SignalName = "Legacy manual alias",
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

    private static void Bind(IoTestPointPlan point, string runtimeReference)
        => point.ApplyLiveBinding(
            IoTestLiveBindingState.LivePointReady,
            "field-proven primary leaf",
            "device-1",
            runtimeReference);

    private static FatValueEvidence Evidence(FatValueSlot slot, string raw)
        => new(
            Guid.NewGuid(),
            slot,
            FatEvidenceCaptureKind.OperatorSnapshot,
            raw,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "GOOD",
            "regression",
            1,
            1);

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
