using System.Security.Cryptography;
using System.Text;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;
using Xunit;

namespace ARSAS.Tests;

public sealed class P0ClosedIedLifecycleBehaviorTests
{
    [Fact]
    public async Task MatchingReimport_RestoresEvidenceButRequiresFreshLiveAssociation()
    {
        var root = TempDirectory();
        var sourcePath = Path.Combine(root, "p0-close-source.xlsx");
        await File.WriteAllBytesAsync(sourcePath, Encoding.UTF8.GetBytes("p0-close-stable-source"));
        var sourceHash = Hash(sourcePath);
        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");

        var original = Project(sourceHash,
            Ied("IED-A", "192.168.90.101",
                Point("TP-001", "IED-A", "192.168.90.101", "IED-ALD0/GGIO1.Ind1.stVal")));

        using var session = Session(original, evidenceRoot);
        var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
            original,
            session,
            sourcePath,
            projectsRoot,
            evidenceRoot);
        using (opened.Workspace)
        {
            var savedPoint = opened.Project.Ieds.Single().TestPoints.Single();
            CompletePass(savedPoint);
            opened.Project.Ieds.Single().LatestComtradeFiles = "fault.cfg|fault.dat";
            opened.Project.Ieds.Single().LatestComtradeFileCount = 2;
            opened.Workspace.SaveNow();

            var checkpointPath = IoFatClosedIedCheckpointService.SaveFromCurrentSnapshot(
                opened.Workspace,
                opened.Project.Ieds.Single());
            Assert.True(File.Exists(checkpointPath));

            var reimported = Ied(
                "IED-A",
                "192.168.90.101",
                Point("TP-001", "IED-A", "192.168.90.101", "IED-ALD0/GGIO1.Ind1.stVal"));

            var restore = IoFatClosedIedCheckpointService.TryRestore(opened.Workspace, reimported);

            Assert.True(restore.Found);
            Assert.Equal(1, restore.RestoredPointCount);
            var restored = reimported.TestPoints.Single();
            Assert.Equal(IoTestPointState.Passed, restored.Runtime.State);
            Assert.NotNull(restored.Runtime.OnEvidence);
            Assert.NotNull(restored.Runtime.OffEvidence);
            Assert.Equal("True", restored.Runtime.OnEvidence!.RawValue);
            Assert.Equal("False", restored.Runtime.OffEvidence!.RawValue);
            Assert.Equal("-", restored.Runtime.CurrentValue);
            Assert.Equal("Unknown", restored.Runtime.CurrentQuality);
            Assert.Equal(-1, restored.Runtime.LastSequence);
            Assert.Equal(-1, restored.Runtime.ConnectionGeneration);
            Assert.Equal("fault.cfg|fault.dat", reimported.LatestComtradeFiles);
            Assert.Equal(2, reimported.LatestComtradeFileCount);
        }
    }

    [Fact]
    public async Task ReimportWithChangedPointConfiguration_DoesNotApplyHistoricalEvidence()
    {
        var root = TempDirectory();
        var sourcePath = Path.Combine(root, "p0-close-mismatch.xlsx");
        await File.WriteAllBytesAsync(sourcePath, Encoding.UTF8.GetBytes("p0-close-mismatch-source"));
        var sourceHash = Hash(sourcePath);
        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");

        var original = Project(sourceHash,
            Ied("IED-A", "192.168.90.111",
                Point("TP-001", "IED-A", "192.168.90.111", "IED-ALD0/GGIO1.Ind1.stVal")));

        using var session = Session(original, evidenceRoot);
        var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
            original,
            session,
            sourcePath,
            projectsRoot,
            evidenceRoot);
        using (opened.Workspace)
        {
            CompletePass(opened.Project.Ieds.Single().TestPoints.Single());
            opened.Workspace.SaveNow();
            IoFatClosedIedCheckpointService.SaveFromCurrentSnapshot(
                opened.Workspace,
                opened.Project.Ieds.Single());

            var changed = Ied(
                "IED-A",
                "192.168.90.111",
                Point("TP-001", "IED-A", "192.168.90.111", "IED-ALD0/GGIO1.Ind2.stVal"));

            var restore = IoFatClosedIedCheckpointService.TryRestore(opened.Workspace, changed);

            Assert.True(restore.Found);
            Assert.Equal(0, restore.RestoredPointCount);
            var point = changed.TestPoints.Single();
            Assert.Equal(IoTestPointState.NotStarted, point.Runtime.State);
            Assert.Null(point.Runtime.OnEvidence);
            Assert.Null(point.Runtime.OffEvidence);
        }
    }

    private static IoTestProject Project(string sourceHash, params IoTestIedPlan[] ieds)
    {
        var project = new IoTestProject
        {
            ProjectId = "P0-CLOSED-IED-PROJECT",
            SchemaVersion = "ARSAS-FAT-IO-1.0",
            ProjectName = "P0 Closed IED",
            SourceWorkbookName = "p0-close.xlsx",
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
            ExpectedOnText = "True",
            ExpectedOffText = "False",
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
        var timestamp = new DateTimeOffset(2026, 9, 7, 8, 0, 0, TimeSpan.Zero)
            .AddMilliseconds(sequence * 100);
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
