using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatEngineeringAuthorityNormalizationRegressionTests
{
    [Fact]
    public void EngineeringProjectionAuthority_IsRecognizedBeforeLegacyManualMigration()
    {
        const string runtime = "AA1E1F06R4V1T3p1_OperationalValues/RPRE_MMXU1.A.phsA.cVal.mag.f";
        var canonical = new IoTestPointPlan
        {
            TestPointId = "scl-source-static-1",
            IedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            SignalName = "Engineering static member",
            ObjectReference = runtime,
            FunctionalConstraint = "MX",
            DataSetName = "AA1E1F06R4LD0/LLN0.OperationalValues",
            SourceIecReference = runtime,
            ReportDisplayReference = runtime,
            EventLogSearchReference = runtime,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = IoTestSignalSelectionService.EngineeringSclDataSetAuthorityBindingStatus
        };
        canonical.ApplyLiveBinding(
            IoTestLiveBindingState.LivePointReady,
            "Engineering live point",
            "device-1",
            runtime);

        var manual = new IoTestPointPlan
        {
            TestPointId = "scl-manual-7496d038be4fdc18e340",
            IedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            SignalName = "Restored legacy alias",
            ObjectReference = runtime,
            FunctionalConstraint = "MX",
            SourceIecReference = runtime,
            ReportDisplayReference = runtime,
            EventLogSearchReference = runtime,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = IoTestSignalSelectionService.SclWorkspaceAuthorityBindingStatus
        };
        manual.ApplyLiveBinding(
            IoTestLiveBindingState.LivePointReady,
            "Legacy live alias",
            "device-1",
            runtime);
        manual.Runtime.Value1Evidence = new FatValueEvidence(
            Guid.NewGuid(),
            FatValueSlot.Value1,
            FatEvidenceCaptureKind.OperatorSnapshot,
            "10.1",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "GOOD",
            "field regression",
            1,
            1);

        var project = new IoTestProject
        {
            ProjectId = "field-authority-regression",
            SchemaVersion = "ARSAS-FAT-SCL-1.0",
            ProjectName = "Field authority regression",
            Ieds = new List<IoTestIedPlan>
            {
                new()
                {
                    IedName = "AA1E1F06R4",
                    IpAddress = "192.168.81.103",
                    TestPoints = new List<IoTestPointPlan> { canonical, manual }
                }
            }
        };

        // Critical field contract: Engineering projection rows must already be recognized
        // as static DataSet authority before synchronize/migration runs. BindingStatus is
        // immutable provenance and must not be rewritten later.
        Assert.True(IoTestSignalSelectionService.IsSclDataSetAuthority(canonical));

        var result = IoFatCanonicalEvidenceMigrationService.MigrateAndRemoveLegacyManualRows(project);

        Assert.Equal(1, result.RemovedManualRows);
        Assert.Equal(1, result.MigratedEvidenceRows);
        Assert.Single(project.Ieds[0].TestPoints);
        Assert.Same(canonical, project.Ieds[0].TestPoints[0]);
        Assert.Equal(IoTestSignalSelectionService.EngineeringSclDataSetAuthorityBindingStatus, canonical.BindingStatus);
        Assert.True(IoTestSignalSelectionService.IsSclDataSetAuthority(canonical));
        Assert.NotNull(canonical.Runtime.Value1Evidence);
        Assert.DoesNotContain(project.Ieds[0].TestPoints, IoFatCanonicalEvidenceMigrationService.IsLegacyManualWorkspaceRow);
    }
}
