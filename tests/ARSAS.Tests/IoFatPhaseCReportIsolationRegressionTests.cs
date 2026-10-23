using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;
using Xunit;

namespace ARSAS.Tests;

public sealed class IoFatPhaseCReportIsolationRegressionTests
{
    [Fact]
    public void PerIedReport_IsADeepPointInTimeSnapshot_AndExcludesSiblingIed()
    {
        var pointA = Point("TP-001", "IED-A", "192.0.2.10", "IED-ALD0/GGIO1.Ind1.stVal");
        var pointB = Point("TP-001", "IED-B", "192.0.2.11", "IED-BLD0/GGIO1.Ind1.stVal");
        var iedA = Ied("IED-A", "192.0.2.10", pointA);
        var iedB = Ied("IED-B", "192.0.2.11", pointB);
        var project = Project(iedA, iedB);
        CompletePass(pointA, 10);
        CompletePass(pointB, 20);

        var report = IoFatReportPreviewService.CreateIedScopedProject(project, iedA);
        var reportIed = Assert.Single(report.Ieds);
        var reportPoint = Assert.Single(reportIed.TestPoints);

        Assert.Equal("IED-A", reportIed.IedName);
        Assert.Equal("TP-001", reportPoint.TestPointId);
        Assert.NotSame(iedA, reportIed);
        Assert.NotSame(pointA, reportPoint);
        Assert.NotSame(pointA.Runtime, reportPoint.Runtime);
        Assert.Equal(IoTestPointState.Passed, reportPoint.Runtime.State);
        Assert.Equal(pointA.Runtime.OnEvidence!.EvidenceId, reportPoint.Runtime.OnEvidence!.EvidenceId);
        Assert.Equal(pointA.Runtime.OffEvidence!.EvidenceId, reportPoint.Runtime.OffEvidence!.EvidenceId);

        // Mutate the live workspace after the artifact exists. The report snapshot must
        // retain the evidence and inclusion state captured at creation time.
        pointA.TestEnabled = false;
        pointA.WorkspaceSelected = false;
        pointA.RemoveFromFat();
        new IoTestTransitionEvaluator().StartAttempt(pointA, Observation(false, 99));

        Assert.True(reportPoint.TestEnabled);
        Assert.True(reportPoint.WorkspaceSelected);
        Assert.True(reportPoint.IsIncludedInFat);
        Assert.Equal(IoTestPointState.Passed, reportPoint.Runtime.State);
        Assert.NotNull(reportPoint.Runtime.OnEvidence);
        Assert.NotNull(reportPoint.Runtime.OffEvidence);
        Assert.DoesNotContain(report.Ieds, candidate => candidate.IedName == "IED-B");
    }

    [Fact]
    public void ReportScope_ExcludesRowsRemovedBeforeSnapshot_EvenWhenOldEvidenceExists()
    {
        var included = Point("A-1", "IED-A", "192.0.2.20", "IED-ALD0/GGIO1.Ind1.stVal");
        var removed = Point("A-2", "IED-A", "192.0.2.20", "IED-ALD0/GGIO1.Ind2.stVal");
        var ied = Ied("IED-A", "192.0.2.20", included, removed);
        var project = Project(ied);
        CompletePass(included, 10);
        CompletePass(removed, 20);
        removed.RemoveFromFat();

        var report = IoFatReportPreviewService.CreateIedScopedProject(project, ied);
        var reportIed = Assert.Single(report.Ieds);
        var reportPoint = Assert.Single(reportIed.TestPoints);

        Assert.Equal("A-1", reportPoint.TestPointId);
        Assert.DoesNotContain(reportIed.TestPoints, point => point.TestPointId == "A-2");
    }

    [Fact]
    public void CombinedReport_AllowsSameLocalPointIdAcrossDifferentIeds_WithoutEvidenceLeakage()
    {
        var pointA = Point("LOCAL-1", "IED-A", "192.0.2.30", "IED-ALD0/GGIO1.Ind1.stVal");
        var pointB = Point("LOCAL-1", "IED-B", "192.0.2.31", "IED-BLD0/GGIO1.Ind9.stVal");
        var iedA = Ied("IED-A", "192.0.2.30", pointA);
        var iedB = Ied("IED-B", "192.0.2.31", pointB);
        var project = Project(iedA, iedB);
        CompletePass(pointA, 10);
        CompletePass(pointB, 40);

        var report = IoFatReportPreviewService.CreateScopedProject(project, new[] { iedA, iedB });

        Assert.Equal(2, report.Ieds.Count);
        var reportA = report.Ieds.Single(candidate => candidate.IedName == "IED-A").TestPoints.Single();
        var reportB = report.Ieds.Single(candidate => candidate.IedName == "IED-B").TestPoints.Single();
        Assert.Equal("LOCAL-1", reportA.TestPointId);
        Assert.Equal("LOCAL-1", reportB.TestPointId);
        Assert.Equal("IED-ALD0/GGIO1.Ind1.stVal", reportA.ObjectReference);
        Assert.Equal("IED-BLD0/GGIO1.Ind9.stVal", reportB.ObjectReference);
        Assert.NotEqual(reportA.Runtime.OnEvidence!.EvidenceId, reportB.Runtime.OnEvidence!.EvidenceId);
    }

    [Fact]
    public void ReportScope_FailsClosedForDuplicateTechnicalKeyAndIpIdentity()
    {
        var iedA1 = Ied("IED-A", "192.0.2.40", Point("A-1", "IED-A", "192.0.2.40", "IED-ALD0/GGIO1.Ind1.stVal"));
        var iedA2 = Ied("IED-A", "192.0.2.40", Point("A-2", "IED-A", "192.0.2.40", "IED-ALD0/GGIO1.Ind2.stVal"));
        var project = Project(iedA1, iedA2);

        var error = Assert.Throws<InvalidDataException>(() =>
            IoFatReportPreviewService.CreateScopedProject(project, new[] { iedA1, iedA2 }));

        Assert.Contains("duplicate IED identity", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IED-A|192.0.2.40", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportScope_PreservesExactSourceSetIdentity()
    {
        var ied = Ied("IED-A", "192.0.2.50", Point("A-1", "IED-A", "192.0.2.50", "IED-ALD0/GGIO1.Ind1.stVal"));
        var project = Project(ied);
        var source = new IoFatSourceDescriptor(
            "source-a",
            IoFatSourceKinds.Scl,
            "IED-A.cid",
            new string('a', 64),
            1234);
        project.SetSources(new[] { source }, IoFatSourceIdentity.ComputeSetFingerprint(new[] { source }));

        var report = IoFatReportPreviewService.CreateIedScopedProject(project, ied);

        Assert.Equal(project.SourceSetSha256, report.SourceSetSha256);
        var reportSource = Assert.Single(report.Sources);
        Assert.Equal(source.SourceId, reportSource.SourceId);
        Assert.Equal(source.Sha256, reportSource.Sha256);
    }

    private static IoTestProject Project(params IoTestIedPlan[] ieds)
    {
        var project = new IoTestProject
        {
            ProjectId = "PHASE-C-REPORT",
            SchemaVersion = "ARSAS-FAT-SCL-1.0",
            ProjectName = "Phase C per-IED report"
        };
        project.Ieds.AddRange(ieds);
        project.InitializeRuntimeNotifications();
        return project;
    }

    private static IoTestIedPlan Ied(string name, string ip, params IoTestPointPlan[] points)
    {
        var ied = new IoTestIedPlan
        {
            IedName = name,
            IpAddress = ip,
            IedRole = "Protection IED",
            Location = "FAT bench"
        };
        ied.TestPoints.AddRange(points);
        return ied;
    }

    private static IoTestPointPlan Point(string id, string iedName, string ip, string reference)
        => new()
        {
            TestPointId = id,
            IedName = iedName,
            IpAddress = ip,
            SignalName = id,
            ObjectReference = reference,
            FunctionalConstraint = "ST",
            ExpectedOnText = "True",
            ExpectedOffText = "False",
            ExpectedOnRaw = 1,
            ExpectedOffRaw = 0,
            DataType = "BOOLEAN",
            SourceIecReference = reference,
            ReportDisplayReference = reference,
            EventLogSearchReference = reference,
            SignalKind = FatSignalKind.Discrete,
            CaptureMode = FatCaptureMode.AutomaticTransition,
            ImportReady = true,
            BindingStatus = "SCL_DATASET_AUTHORITY"
        };

    private static void CompletePass(IoTestPointPlan point, long sequenceBase)
    {
        var evaluator = new IoTestTransitionEvaluator();
        evaluator.StartAttempt(point, Observation(false, sequenceBase));
        evaluator.Observe(point, Observation(true, sequenceBase + 1));
        evaluator.Observe(point, Observation(false, sequenceBase + 2));
        Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
    }

    private static IoTestObservation Observation(bool state, long sequence)
    {
        var timestamp = new DateTimeOffset(2026, 9, 7, 3, 0, 0, TimeSpan.Zero).AddMilliseconds(sequence * 10);
        return new IoTestObservation(
            state,
            state ? "True" : "False",
            timestamp,
            timestamp.AddMilliseconds(-2),
            "Good",
            "BRCB",
            sequence,
            1);
    }
}
