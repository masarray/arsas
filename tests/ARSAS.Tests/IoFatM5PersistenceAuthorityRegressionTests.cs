using System.Security.Cryptography;
using System.Text;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;
using Xunit;

namespace ARSAS.Tests;

public sealed class IoFatM5PersistenceAuthorityRegressionTests
{
    [Fact]
    public async Task Reconcile_DeviceIdMismatch_FailsClosedWithoutRestoringEvidence()
    {
        var root = TempDirectory();
        var sourcePath = await WriteSourceAsync(root, "m5-device-mismatch.xlsx", "m5-device-mismatch-source");
        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");

        var savedIed = Ied("IED-A", "192.168.90.11", "engineering-device-old");
        var saved = Project(Hash(sourcePath), savedIed);
        using (var session = Session(saved, evidenceRoot))
        {
            var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
                saved, session, sourcePath, projectsRoot, evidenceRoot);
            using (opened.Workspace)
            {
                CompletePass(opened.Project.Ieds.Single().TestPoints.Single());
                opened.Workspace.SaveNow();
            }
        }

        var evidenceFilesBefore = EvidenceFiles(evidenceRoot);
        var current = Project(Hash(sourcePath), Ied("IED-A", "192.168.90.11", "engineering-device-new"));

        var reopened = await IoTestWorkspaceBootstrapService.OpenWorkbookAsync(
            current, sourcePath, projectsRoot, evidenceRoot, Session);
        using (reopened.Session)
        using (reopened.Workspace)
        {
            Assert.Same(current, reopened.Project);
            var point = reopened.Project.Ieds.Single().TestPoints.Single();
            Assert.Equal(IoTestPointState.NotStarted, point.Runtime.State);
            Assert.Null(point.Runtime.OnEvidence);
            Assert.Null(point.Runtime.OffEvidence);
        }

        Assert.Equal(evidenceFilesBefore, EvidenceFiles(evidenceRoot));
    }

    [Fact]
    public async Task Reconcile_MatchingDeviceId_RestoresExistingEvidenceWithoutCreatingNewEvidenceFiles()
    {
        var root = TempDirectory();
        var sourcePath = await WriteSourceAsync(root, "m5-device-match.xlsx", "m5-device-match-source");
        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");
        const string deviceId = "engineering-device-ied-a";

        var saved = Project(Hash(sourcePath), Ied("IED-A", "192.168.90.21", deviceId));
        using (var session = Session(saved, evidenceRoot))
        {
            var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
                saved, session, sourcePath, projectsRoot, evidenceRoot);
            using (opened.Workspace)
            {
                CompletePass(opened.Project.Ieds.Single().TestPoints.Single());
                opened.Workspace.SaveNow();
            }
        }

        var evidenceFilesBefore = EvidenceFiles(evidenceRoot);
        var current = Project(Hash(sourcePath), Ied("IED-A", "192.168.90.21", deviceId));

        var reopened = await IoTestWorkspaceBootstrapService.OpenWorkbookAsync(
            current, sourcePath, projectsRoot, evidenceRoot, Session);
        using (reopened.Session)
        using (reopened.Workspace)
        {
            Assert.Same(current, reopened.Project);
            Assert.True(reopened.RestoredProgress);
            var point = reopened.Project.Ieds.Single().TestPoints.Single();
            Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
            Assert.NotNull(point.Runtime.OnEvidence);
            Assert.NotNull(point.Runtime.OffEvidence);
        }

        // Background/bootstrap reconciliation may adopt already-persisted evidence into the
        // fresh Engineering plan, but it is not an evidence-producing operation. Only an
        // explicit FAT session/capture path is allowed to create journal evidence.
        Assert.Equal(evidenceFilesBefore, EvidenceFiles(evidenceRoot));
    }

    [Fact]
    public async Task Reconcile_LegacyEmptyDeviceId_RemainsCompatibleWithExactIecEndpoint()
    {
        var root = TempDirectory();
        var sourcePath = await WriteSourceAsync(root, "m5-legacy-device.xlsx", "m5-legacy-device-source");
        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");

        // Empty DeviceId represents historical snapshots created before the M5 authority key.
        var saved = Project(Hash(sourcePath), Ied("IED-A", "192.168.90.31", deviceId: null));
        using (var session = Session(saved, evidenceRoot))
        {
            var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
                saved, session, sourcePath, projectsRoot, evidenceRoot);
            using (opened.Workspace)
            {
                CompletePass(opened.Project.Ieds.Single().TestPoints.Single());
                opened.Workspace.SaveNow();
            }
        }

        var current = Project(Hash(sourcePath), Ied("IED-A", "192.168.90.31", "engineering-device-now-known"));
        var reopened = await IoTestWorkspaceBootstrapService.OpenWorkbookAsync(
            current, sourcePath, projectsRoot, evidenceRoot, Session);
        using (reopened.Session)
        using (reopened.Workspace)
        {
            var point = reopened.Project.Ieds.Single().TestPoints.Single();
            Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
            Assert.NotNull(point.Runtime.OnEvidence);
            Assert.NotNull(point.Runtime.OffEvidence);
        }
    }

    private static IoTestProject Project(string sourceHash, IoTestIedPlan ied)
    {
        var project = new IoTestProject
        {
            ProjectId = "M5-PERSISTENCE-AUTHORITY",
            SchemaVersion = "ARSAS-FAT-IO-1.0",
            ProjectName = "M5 persistence authority",
            SourceWorkbookName = "m5.xlsx",
            SourceWorkbookSha256 = sourceHash
        };
        project.Ieds.Add(ied);
        return project;
    }

    private static IoTestIedPlan Ied(string name, string ip, string? deviceId)
    {
        var ied = new IoTestIedPlan
        {
            IedName = name,
            IpAddress = ip,
            IedRole = "Protection IED",
            Location = "FAT bench"
        };
        if (!string.IsNullOrWhiteSpace(deviceId))
            ied.ApplyLiveDeviceBinding(deviceId, "Engineering identity bound");
        ied.TestPoints.Add(Point("TP-001", name, ip, $"{name}LD0/GGIO1.Ind1.stVal"));
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
        Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
    }

    private static IoTestObservation Observation(bool state, long sequence)
    {
        var timestamp = new DateTimeOffset(2026, 9, 10, 3, 0, 0, TimeSpan.Zero).AddMilliseconds(sequence * 100);
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

    private static async Task<string> WriteSourceAsync(string root, string fileName, string content)
    {
        var path = Path.Combine(root, fileName);
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    private static string[] EvidenceFiles(string evidenceRoot)
        => !Directory.Exists(evidenceRoot)
            ? Array.Empty<string>()
            : Directory.EnumerateFiles(evidenceRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(evidenceRoot, path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

    private static string Hash(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
