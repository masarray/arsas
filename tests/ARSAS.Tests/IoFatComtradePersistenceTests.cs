using System.Security.Cryptography;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatComtradePersistenceTests
{
    [Fact]
    public async Task LatestRemoteComtrade_SurvivesWorkspaceSnapshotRestore()
    {
        var root = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var workbook = Path.Combine(root, "source.xlsx");
        await File.WriteAllBytesAsync(workbook, new byte[] { 1, 6, 1, 8, 5, 0 });
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(workbook))).ToLowerInvariant();
        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");

        var initial = BuildProject(hash);
        var initialSession = Session(initial, evidenceRoot);
        var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
            initial,
            initialSession,
            workbook,
            projectsRoot,
            evidenceRoot);
        using (initialSession)
        using (opened.Workspace)
        {
            var ied = opened.Project.Ieds[0];
            ied.LatestComtradeFiles = "FRA00028.cfg + FRA00028.dat";
            ied.LatestComtradeRemotePath = "FRA00028.cfg";
            ied.LatestComtradeCompleteness = "CFG + DAT";
            ied.LatestComtradeAcquisitionSource = IoFatRemoteComtradeEvidenceService.AcquisitionSource;
            ied.LatestComtradeModifiedAtUtc = new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);
            ied.LatestComtradeCapturedAtUtc = new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);
            ied.LatestComtradeFileCount = 2;
            ied.LatestComtradeKnownSizeBytes = 4096;
            opened.Workspace.SaveNow();
        }

        var restored = await IoTestWorkspaceBootstrapService.OpenWorkbookAsync(
            BuildProject(hash),
            workbook,
            projectsRoot,
            evidenceRoot,
            Session);
        using (restored.Session)
        using (restored.Workspace)
        {
            var ied = restored.Project.Ieds[0];
            Assert.True(restored.RestoredProgress);
            Assert.True(ied.HasRemoteComtradeEvidence);
            Assert.Equal("FRA00028.cfg + FRA00028.dat", ied.LatestComtradeFiles);
            Assert.Equal("FRA00028.cfg", ied.LatestComtradeRemotePath);
            Assert.Equal("CFG + DAT", ied.LatestComtradeCompleteness);
            Assert.Equal(IoFatRemoteComtradeEvidenceService.AcquisitionSource, ied.LatestComtradeAcquisitionSource);
            Assert.Equal(2, ied.LatestComtradeFileCount);
            Assert.Equal(4096, ied.LatestComtradeKnownSizeBytes);
        }
    }

    private static IoTestProject BuildProject(string hash) => new()
    {
        ProjectId = "COMTRADE-PERSISTENCE",
        SchemaVersion = IoTestImportValidator.SupportedSchemaVersion,
        ProjectName = "COMTRADE Persistence",
        SourceWorkbookName = "source.xlsx",
        SourceWorkbookSha256 = hash,
        Ieds =
        [
            new IoTestIedPlan
            {
                IedName = "AA1C1F03R4",
                IpAddress = "192.168.81.70"
            }
        ]
    };

    private static IoTestSessionController Session(IoTestProject project, string evidenceRoot)
        => new(project, _ => null, action => action(), evidenceRoot);
}