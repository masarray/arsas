using System.IO.Compression;
using System.Security.Cryptography;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoTestPersistenceTests
{
    [Fact]
    public async Task LocalSnapshot_RestoresCompletedEvidenceOnNextWorkbookOpen()
    {
        var root = TempDirectory();
        var workbook = Path.Combine(root, "source.xlsx");
        await File.WriteAllBytesAsync(workbook, new byte[] { 1, 2, 3, 4, 5 });
        var hash = Hash(workbook);
        var project = Project(hash);
        CompletePass(project.Ieds[0].TestPoints[0]);
        var session = Session(project, Path.Combine(root, "evidence"));
        using (var stored = await IoTestWorkspacePersistence.OpenWorkbookAsync(
                   project,
                   session,
                   workbook,
                   Path.Combine(root, "projects"),
                   Path.Combine(root, "evidence")))
        {
            stored.Workspace.SaveNow();
        }
        session.Dispose();

        var freshImport = Project(hash);
        var restored = await IoTestWorkspaceBootstrapService.OpenWorkbookAsync(
            freshImport,
            workbook,
            Path.Combine(root, "projects"),
            Path.Combine(root, "evidence"),
            Session);
        using (restored.Session)
        using (restored.Workspace)
        {
            var point = restored.Project.Ieds[0].TestPoints[0];
            Assert.True(restored.RestoredProgress);
            Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
            Assert.NotNull(point.Runtime.OnEvidence);
            Assert.NotNull(point.Runtime.OffEvidence);
            Assert.True(File.Exists(restored.Workspace.SnapshotPath));
        }
    }

    [Fact]
    public async Task LocalSnapshot_PartialOnBecomesReviewAfterContinuityIsLost()
    {
        var root = TempDirectory();
        var workbook = Path.Combine(root, "source.xlsx");
        await File.WriteAllBytesAsync(workbook, new byte[] { 8, 7, 6, 5 });
        var hash = Hash(workbook);
        var project = Project(hash);
        CaptureOnOnly(project.Ieds[0].TestPoints[0]);
        var session = Session(project, Path.Combine(root, "evidence"));
        using (var stored = await IoTestWorkspacePersistence.OpenWorkbookAsync(
                   project,
                   session,
                   workbook,
                   Path.Combine(root, "projects"),
                   Path.Combine(root, "evidence")))
        {
            stored.Workspace.SaveNow();
        }
        session.Dispose();

        var restored = await IoTestWorkspaceBootstrapService.OpenWorkbookAsync(
            Project(hash),
            workbook,
            Path.Combine(root, "projects"),
            Path.Combine(root, "evidence"),
            Session);
        using (restored.Session)
        using (restored.Workspace)
        {
            var point = restored.Project.Ieds[0].TestPoints[0];
            Assert.Equal(IoTestPointState.Review, point.Runtime.State);
            Assert.NotNull(point.Runtime.OnEvidence);
            Assert.Null(point.Runtime.OffEvidence);
            Assert.Contains("continuity", point.Runtime.StatusReason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task PortablePackage_RoundTripsProgressAndPrintableReport()
    {
        var root = TempDirectory();
        var workbook = Path.Combine(root, "source.xlsx");
        await File.WriteAllBytesAsync(workbook, new byte[] { 10, 20, 30, 40, 50 });
        var hash = Hash(workbook);
        var project = Project(hash);
        CompletePass(project.Ieds[0].TestPoints[0]);
        var evidenceRoot = Path.Combine(root, "evidence-a");
        var session = Session(project, evidenceRoot);
        var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
            project,
            session,
            workbook,
            Path.Combine(root, "projects-a"),
            evidenceRoot);
        var package = Path.Combine(root, "handover.arsas-iofat");
        using (session)
        using (opened.Workspace)
        {
            await opened.Workspace.ExportPackageAsync(package);
        }

        using (var archive = ZipFile.OpenRead(package))
        {
            Assert.NotNull(archive.GetEntry("manifest.json"));
            Assert.NotNull(archive.GetEntry("project.snapshot.json"));
            Assert.NotNull(archive.GetEntry("report/IO-FAT-Report.html"));
            using var reader = new StreamReader(archive.GetEntry("report/IO-FAT-Report.html")!.Open());
            var report = await reader.ReadToEndAsync();
            Assert.Contains("ARSAS IO List FAT Evidence Report", report, StringComparison.Ordinal);
            Assert.Contains("CB closed", report, StringComparison.Ordinal);
            Assert.Contains("Passed", report, StringComparison.Ordinal);
        }

        var imported = await IoTestWorkspaceBootstrapService.OpenPackageAsync(
            package,
            Path.Combine(root, "projects-b"),
            Path.Combine(root, "evidence-b"),
            Session);
        using (imported.Session)
        using (imported.Workspace)
        {
            Assert.Equal(IoTestPointState.Passed, imported.Project.Ieds[0].TestPoints[0].Runtime.State);
            Assert.True(File.Exists(imported.Workspace.SourceWorkbookPath));
            Assert.True(File.Exists(imported.Workspace.SnapshotPath));
        }
    }

    [Fact]
    public async Task PortablePackage_RejectsTamperedSnapshot()
    {
        var root = TempDirectory();
        var workbook = Path.Combine(root, "source.xlsx");
        await File.WriteAllBytesAsync(workbook, new byte[] { 3, 1, 4, 1, 5 });
        var project = Project(Hash(workbook));
        var session = Session(project, Path.Combine(root, "evidence"));
        var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
            project,
            session,
            workbook,
            Path.Combine(root, "projects"),
            Path.Combine(root, "evidence"));
        var package = Path.Combine(root, "handover.arsas-iofat");
        using (session)
        using (opened.Workspace)
            await opened.Workspace.ExportPackageAsync(package);

        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("project.snapshot.json")!;
            entry.Delete();
            var replacement = archive.CreateEntry("project.snapshot.json");
            await using var writer = new StreamWriter(replacement.Open());
            await writer.WriteAsync("{\"tampered\":true}");
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            IoTestWorkspaceBootstrapService.OpenPackageAsync(
                package,
                Path.Combine(root, "projects-import"),
                Path.Combine(root, "evidence-import"),
                Session));
    }

    private static void CompletePass(IoTestPointPlan point)
    {
        var evaluator = new IoTestTransitionEvaluator();
        evaluator.StartAttempt(point, Observation(false, 1));
        evaluator.Observe(point, Observation(true, 2));
        evaluator.Observe(point, Observation(false, 3));
    }

    private static void CaptureOnOnly(IoTestPointPlan point)
    {
        var evaluator = new IoTestTransitionEvaluator();
        evaluator.StartAttempt(point, Observation(false, 1));
        evaluator.Observe(point, Observation(true, 2));
    }

    private static IoTestObservation Observation(bool state, long sequence)
    {
        var timestamp = new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero).AddMilliseconds(sequence * 100);
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

    private static IoTestProject Project(string workbookHash)
    {
        var point = new IoTestPointPlan
        {
            TestPointId = "TP-001",
            IedName = "AA1C1F03R4",
            IpAddress = "192.168.81.70",
            SignalName = "CB closed",
            ObjectReference = "AA1C1F03R4ADD/GGIO6.CBClsd.stVal",
            FunctionalConstraint = "ST",
            ExpectedOnText = "Active",
            ExpectedOffText = "InActive",
            ImportReady = true,
            BindingStatus = "CID_DATASET_EXACT"
        };
        return new IoTestProject
        {
            ProjectId = "CCPP-260728",
            SchemaVersion = "ARSAS-FAT-IO-1.0",
            ProjectName = "CCPP FAT",
            SourceWorkbookName = "source.xlsx",
            SourceWorkbookSha256 = workbookHash,
            Ieds =
            {
                new IoTestIedPlan
                {
                    IedName = point.IedName,
                    IpAddress = point.IpAddress,
                    IedRole = "Protection IED",
                    Location = "CCPP",
                    VoltageLevel = "11 kV",
                    TestPoints = { point }
                }
            }
        };
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
