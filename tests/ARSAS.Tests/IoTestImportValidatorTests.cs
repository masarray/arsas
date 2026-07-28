using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoTestImportValidatorTests
{
    private readonly IoTestImportValidator _validator = new();

    [Fact]
    public void ValidProject_IsAccepted()
    {
        var project = CreateProject(CreatePoint("TP-001"));

        var result = _validator.Validate(project);

        Assert.True(result.IsValid);
        Assert.Equal(1, result.IedCount);
        Assert.Equal(1, result.SignalCount);
        Assert.Equal(1, result.ReadySignalCount);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void DuplicateTestPointId_IsRejected()
    {
        var project = CreateProject(CreatePoint("TP-001"), CreatePoint("TP-001"));

        var result = _validator.Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, finding => finding.Code == "TEST_POINT_DUPLICATE");
    }

    [Fact]
    public void ImportReadySignalWithoutReference_IsRejected()
    {
        var point = CreatePoint("TP-001", objectReference: string.Empty);
        var project = CreateProject(point);

        var result = _validator.Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, finding => finding.Code == "REFERENCE_REQUIRED");
    }

    [Fact]
    public void NonReadySignal_IsRetainedAsWarning()
    {
        var point = CreatePoint("TP-001", objectReference: string.Empty, importReady: false);
        var project = CreateProject(point);

        var result = _validator.Validate(project);

        Assert.True(result.IsValid);
        Assert.Contains(result.Findings, finding =>
            finding.Code == "SIGNAL_REVIEW_REQUIRED" &&
            finding.Severity == IoTestImportFindingSeverity.Warning);
    }

    [Fact]
    public void UnsupportedSchema_IsRejected()
    {
        var project = CreateProject(CreatePoint("TP-001"));
        project = new IoTestProject
        {
            ProjectId = project.ProjectId,
            ProjectName = project.ProjectName,
            SchemaVersion = "ARSAS-FAT-IO-9.9",
            Ieds = project.Ieds
        };

        var result = _validator.Validate(project);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, finding => finding.Code == "SCHEMA_UNSUPPORTED");
    }

    private static IoTestProject CreateProject(params IoTestPointPlan[] points)
    {
        return new IoTestProject
        {
            ProjectId = "CCPP-260728",
            ProjectName = "CCPP FAT",
            SchemaVersion = IoTestImportValidator.SupportedSchemaVersion,
            Ieds =
            {
                new IoTestIedPlan
                {
                    IedName = "AA1C1F03R4",
                    IpAddress = "192.168.81.70",
                    IedRole = "BCU - 6MD85",
                    TestPoints = points.ToList()
                }
            }
        };
    }

    private static IoTestPointPlan CreatePoint(
        string testPointId,
        string objectReference = "AA1C1F03R4ADD/GGIO6.CBClsd.stVal",
        bool importReady = true)
    {
        return new IoTestPointPlan
        {
            TestPointId = testPointId,
            IedName = "AA1C1F03R4",
            IpAddress = "192.168.81.70",
            SignalName = "CB closed",
            ObjectReference = objectReference,
            FunctionalConstraint = "ST",
            ExpectedOnText = "Active",
            ExpectedOffText = "InActive",
            ImportReady = importReady,
            BindingStatus = importReady ? "CID_DATASET_EXACT" : "REFERENCE_MISSING",
            SourceSheet = "ARSAS_SIGNAL_IMPORT",
            SourceRow = 2
        };
    }
}
