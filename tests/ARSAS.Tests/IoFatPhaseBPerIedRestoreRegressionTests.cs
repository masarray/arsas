using System.Security.Cryptography;
using System.Text;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;
using Xunit;

namespace ARSAS.Tests;

public sealed class IoFatPhaseBPerIedRestoreRegressionTests
{
    [Fact]
    public async Task Reopen_RestoresOnlyMatchingPointInsideMatchingIed()
    {
        var root = TempDirectory();
        var sourcePath = Path.Combine(root, "phase-b-source.xlsx");
        await File.WriteAllBytesAsync(sourcePath, Encoding.UTF8.GetBytes("phase-b-stable-source"));
        var sourceHash = Hash(sourcePath);
        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");

        // Deliberately reuse TP-001 on two IEDs. Point IDs are local to their IED owner;
        // this must never collapse into a project-global evidence key.
        var saved = Project(sourceHash,
            Ied("IED-A", "192.168.81.101", Point("TP-001", "IED-A", "192.168.81.101", "IED-ALD0/GGIO1.Ind1.stVal")),
            Ied("IED-B", "192.168.81.102", Point("TP-001", "IED-B", "192.168.81.102", "IED-BLD0/GGIO1.Ind1.stVal")));

        using (var session = Session(saved, evidenceRoot))
        {
            var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
                saved, session, sourcePath, projectsRoot, evidenceRoot);
            using (opened.Workspace)
            {
                CompletePass(opened.Project.Ieds[0].TestPoints[0]);
                CompletePass(opened.Project.Ieds[1].TestPoints[0]);
                opened.Workspace.SaveNow();
            }
        }

        var current = Project(sourceHash,
            Ied("IED-A", "192.168.81.101", Point("TP-001", "IED-A", "192.168.81.101", "IED-ALD0/GGIO1.Ind1.stVal")),
            // Same local point ID, but its evidence-critical IEC reference changed.
            Ied("IED-B", "192.168.81.102", Point("TP-001", "IED-B", "192.168.81.102", "IED-BLD0/GGIO1.Ind2.stVal")));

        var reopened = await IoTestWorkspaceBootstrapService.OpenWorkbookAsync(
            current, sourcePath, projectsRoot, evidenceRoot, Session);
        using (reopened.Session)
        using (reopened.Workspace)
        {
            Assert.True(reopened.RestoredProgress);

            var pointA = reopened.Project.Ieds.Single(ied => ied.IedName == "IED-A").TestPoints.Single();
            Assert.Equal(IoTestPointState.Passed, pointA.Runtime.State);
            Assert.NotNull(pointA.Runtime.OnEvidence);
            Assert.NotNull(pointA.Runtime.OffEvidence);
            Assert.Equal("True", pointA.Runtime.OnEvidence!.RawValue);
            Assert.Equal("False", pointA.Runtime.OffEvidence!.RawValue);

            var pointB = reopened.Project.Ieds.Single(ied => ied.IedName == "IED-B").TestPoints.Single();
            Assert.Equal("IED-BLD0/GGIO1.Ind2.stVal", pointB.ObjectReference);
            Assert.Equal(IoTestPointState.NotStarted, pointB.Runtime.State);
            Assert.Null(pointB.Runtime.OnEvidence);
            Assert.Null(pointB.Runtime.OffEvidence);
        }
    }

    [Fact]
    public async Task Reopen_DoesNotReintroduceIedRemovedFromCurrentScope()
    {
        var root = TempDirectory();
        var sourcePath = Path.Combine(root, "phase-b-scope.xlsx");
        await File.WriteAllBytesAsync(sourcePath, Encoding.UTF8.GetBytes("phase-b-scope-source"));
        var sourceHash = Hash(sourcePath);
        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");

        var saved = Project(sourceHash,
            Ied("IED-A", "192.168.81.111", Point("A-1", "IED-A", "192.168.81.111", "IED-ALD0/GGIO1.Ind1.stVal")),
            Ied("IED-B", "192.168.81.112", Point("B-1", "IED-B", "192.168.81.112", "IED-BLD0/GGIO1.Ind1.stVal")));

        using (var session = Session(saved, evidenceRoot))
        {
            var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
                saved, session, sourcePath, projectsRoot, evidenceRoot);
            using (opened.Workspace)
            {
                CompletePass(opened.Project.Ieds[0].TestPoints[0]);
                CompletePass(opened.Project.Ieds[1].TestPoints[0]);
                opened.Workspace.SaveNow();
            }
        }

        var current = Project(sourceHash,
            Ied("IED-A", "192.168.81.111", Point("A-1", "IED-A", "192.168.81.111", "IED-ALD0/GGIO1.Ind1.stVal")));

        var reopened = await IoTestWorkspaceBootstrapService.OpenWorkbookAsync(
            current, sourcePath, projectsRoot, evidenceRoot, Session);
        using (reopened.Session)
        using (reopened.Workspace)
        {
            Assert.Single(reopened.Project.Ieds);
            Assert.Equal("IED-A", reopened.Project.Ieds[0].IedName);
            Assert.Equal(IoTestPointState.Passed, reopened.Project.Ieds[0].TestPoints[0].Runtime.State);
        }
    }

    [Fact]
    public async Task Reopen_RequiresTechnicalKeyAndIpForIedOwnership()
    {
        var root = TempDirectory();
        var sourcePath = Path.Combine(root, "phase-b-identity.xlsx");
        await File.WriteAllBytesAsync(sourcePath, Encoding.UTF8.GetBytes("phase-b-identity-source"));
        var sourceHash = Hash(sourcePath);
        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");

        var saved = Project(sourceHash,
            Ied("IED-A", "192.168.81.121", Point("A-1", "IED-A", "192.168.81.121", "IED-ALD0/GGIO1.Ind1.stVal")));
        using (var session = Session(saved, evidenceRoot))
        {
            var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
                saved, session, sourcePath, projectsRoot, evidenceRoot);
            using (opened.Workspace)
            {
                CompletePass(opened.Project.Ieds[0].TestPoints[0]);
                opened.Workspace.SaveNow();
            }
        }

        var current = Project(sourceHash,
            Ied("IED-A", "192.168.81.122", Point("A-1", "IED-A", "192.168.81.122", "IED-ALD0/GGIO1.Ind1.stVal")));

        var reopened = await IoTestWorkspaceBootstrapService.OpenWorkbookAsync(
            current, sourcePath, projectsRoot, evidenceRoot, Session);
        using (reopened.Session)
        using (reopened.Workspace)
        {
            var point = reopened.Project.Ieds.Single().TestPoints.Single();
            Assert.Equal(IoTestPointState.NotStarted, point.Runtime.State);
            Assert.Null(point.Runtime.OnEvidence);
            Assert.Null(point.Runtime.OffEvidence);
        }
    }

    [Fact]
    public void ConfigurationFingerprint_IsDeterministicAndOrderIndependentAtIedLevel()
    {
        var first = Ied("IED-A", "192.168.81.130",
            Point("P-2", "IED-A", "192.168.81.130", "IED-ALD0/GGIO1.Ind2.stVal"),
            Point("P-1", "IED-A", "192.168.81.130", "IED-ALD0/GGIO1.Ind1.stVal"));
        var second = Ied("IED-A", "192.168.81.130",
            Point("P-1", "IED-A", "192.168.81.130", "IED-ALD0/GGIO1.Ind1.stVal"),
            Point("P-2", "IED-A", "192.168.81.130", "IED-ALD0/GGIO1.Ind2.stVal"));

        Assert.Equal(
            IoTestPerIedProgressIdentity.IedConfigurationFingerprint(first),
            IoTestPerIedProgressIdentity.IedConfigurationFingerprint(second));

        var changed = Ied("IED-A", "192.168.81.130",
            Point("P-1", "IED-A", "192.168.81.130", "IED-ALD0/GGIO1.Ind9.stVal"),
            Point("P-2", "IED-A", "192.168.81.130", "IED-ALD0/GGIO1.Ind2.stVal"));
        Assert.NotEqual(
            IoTestPerIedProgressIdentity.IedConfigurationFingerprint(first),
            IoTestPerIedProgressIdentity.IedConfigurationFingerprint(changed));
    }

    private static IoTestProject Project(string sourceHash, params IoTestIedPlan[] ieds)
    {
        var project = new IoTestProject
        {
            ProjectId = "PHASE-B-PROJECT",
            SchemaVersion = "ARSAS-FAT-IO-1.0",
            ProjectName = "Phase B FAT",
            SourceWorkbookName = "phase-b.xlsx",
            SourceWorkbookSha256 = sourceHash
        };
        project.Ieds.AddRange(ieds);
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

    private static IoTestPointPlan Point(string id, string iedName, string ip, string objectReference)
        => new()
        {
            TestPointId = id,
            IedName = iedName,
            IpAddress = ip,
            SignalName = "Binary indication",
            ObjectReference = objectReference,
            FunctionalConstraint = "ST",
            ExpectedOnText = "Active",
            ExpectedOffText = "InActive",
            ExpectedOnRaw = 1,
            ExpectedOffRaw = 0,
            DataType = "SDI",
            SourceIecReference = objectReference,
            EventLogSearchReference = objectReference,
            SignalKind = FatSignalKind.Discrete,
            CaptureMode = FatCaptureMode.AutomaticTransition,
            ImportReady = true,
            BindingStatus = "CID_DATASET_EXACT"
        };

    private static void CompletePass(IoTestPointPlan point)
    {
        var evaluator = new IoTestTransitionEvaluator();
        evaluator.StartAttempt(point, Observation(false, 1));
        evaluator.Observe(point, Observation(true, 2));
        evaluator.Observe(point, Observation(false, 3));
    }

    private static IoTestObservation Observation(bool state, long sequence)
    {
        var timestamp = new DateTimeOffset(2026, 9, 7, 2, 30, 0, TimeSpan.Zero).AddMilliseconds(sequence * 100);
        return new IoTestObservation(
            state,
            state ? "True" : "False",
            timestamp,
            timestamp.AddMilliseconds(-3),
            "Good",
            "BRCB",
            sequence,
            1);
    }

    private static IoTestSessionController Session(IoTestProject project, string evidenceRoot)
        => new(project, _ => null, action => action(), evidenceRoot);

    private static string Hash(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}