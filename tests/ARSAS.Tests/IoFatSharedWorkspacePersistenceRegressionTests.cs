using System.Security.Cryptography;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatSharedWorkspacePersistenceRegressionTests
{
    [Fact]
    public async Task SameSclReopen_RestoresManualWorkspaceRow_SelectionTestScopeAndFatDispositionIndependently()
    {
        var root = TempDirectory();
        var source = Path.Combine(root, "manual.cid");
        await File.WriteAllBytesAsync(source, new byte[] { 0x53, 0x43, 0x4C, 0x2D, 0x31 });
        var sourceSha = Hash(source);
        var sourceInputs = new[] { new IoFatSourceInput(source, IoFatSourceKinds.Scl) };
        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");

        var manual = ManualPoint(sourceSha);
        manual.WorkspaceSelected = false;
        manual.TestEnabled = false;
        manual.RemoveFromFat();

        var original = SclProject(manual);
        var originalSession = Session(original, evidenceRoot);
        var opened = await IoTestWorkspacePersistence.OpenSourcesAsync(
            original,
            originalSession,
            sourceInputs,
            projectsRoot,
            evidenceRoot);
        using (originalSession)
        using (opened.Workspace)
            opened.Workspace.SaveNow();

        // A fresh direct-SCL importer does not fabricate manual non-DataSet rows. The
        // same-source workspace continuation must re-materialize the persisted manual row
        // before restoring its three independent operator authorities.
        var fresh = SclProject();
        var restored = await IoTestWorkspaceBootstrapService.OpenSourcesAsync(
            fresh,
            sourceInputs,
            projectsRoot,
            evidenceRoot,
            Session);

        using (restored.Session)
        using (restored.Workspace)
        {
            Assert.True(restored.RestoredProgress);
            var point = Assert.Single(restored.Project.Ieds[0].TestPoints);
            Assert.Equal(manual.TestPointId, point.TestPointId);
            Assert.Equal(IoTestSignalSelectionService.SclWorkspaceAuthorityBindingStatus, point.BindingStatus);
            Assert.Equal(sourceSha, point.SignalAddress);
            Assert.False(point.WorkspaceSelected);
            Assert.False(point.TestEnabled);
            Assert.False(point.IsIncludedInFat);
        }
    }

    [Fact]
    public async Task SameSclReopen_RestoresStaticWorkspaceSelection_WithoutChangingFatTestOrDisposition()
    {
        var root = TempDirectory();
        var source = Path.Combine(root, "static.cid");
        await File.WriteAllBytesAsync(source, new byte[] { 0x53, 0x43, 0x4C, 0x2D, 0x32 });
        var sourceSha = Hash(source);
        var sourceInputs = new[] { new IoFatSourceInput(source, IoFatSourceKinds.Scl) };
        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");

        var originalPoint = StaticPoint(sourceSha);
        originalPoint.WorkspaceSelected = false;
        originalPoint.TestEnabled = true;
        var original = SclProject(originalPoint);
        var originalSession = Session(original, evidenceRoot);
        var opened = await IoTestWorkspacePersistence.OpenSourcesAsync(
            original,
            originalSession,
            sourceInputs,
            projectsRoot,
            evidenceRoot);
        using (originalSession)
        using (opened.Workspace)
            opened.Workspace.SaveNow();

        // Fresh import defaults the same static DataSet member selected. Continuation must
        // restore the Engineering/shared selection without coupling it to FAT TEST state.
        var freshPoint = StaticPoint(sourceSha);
        freshPoint.WorkspaceSelected = true;
        freshPoint.TestEnabled = true;
        var restored = await IoTestWorkspaceBootstrapService.OpenSourcesAsync(
            SclProject(freshPoint),
            sourceInputs,
            projectsRoot,
            evidenceRoot,
            Session);

        using (restored.Session)
        using (restored.Workspace)
        {
            var point = Assert.Single(restored.Project.Ieds[0].TestPoints);
            Assert.False(point.WorkspaceSelected);
            Assert.True(point.TestEnabled);
            Assert.True(point.IsIncludedInFat);
        }
    }

    private static IoTestPointPlan ManualPoint(string sourceSha)
        => new()
        {
            TestPointId = "scl-manual-0123456789abcdef0123",
            IedName = "IED1",
            IpAddress = "192.0.2.10",
            SignalName = "Manual GGIO indication",
            ObjectReference = "IED1LD/GGIO1.Ind1.stVal",
            FunctionalConstraint = "ST",
            ExpectedOnText = "TRUE",
            ExpectedOffText = "FALSE",
            DataType = "BOOLEAN",
            SignalAddress = sourceSha,
            SourceIecReference = "IED1LD/GGIO1.Ind1.stVal",
            ReportDisplayReference = "IED1LD/GGIO1.Ind1.stVal",
            EventLogSearchReference = "IED1LD/GGIO1.Ind1.stVal",
            SignalKind = FatSignalKind.Discrete,
            CaptureMode = FatCaptureMode.AutomaticTransition,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = IoTestSignalSelectionService.SclWorkspaceAuthorityBindingStatus,
            BindingEvidence = "Shared SCL workspace authority"
        };

    private static IoTestPointPlan StaticPoint(string sourceSha)
        => new()
        {
            TestPointId = "scl-static-0001",
            IedName = "IED1",
            IpAddress = "192.0.2.10",
            SignalName = "Static GGIO indication",
            ObjectReference = "IED1LD/GGIO1.Ind2.stVal",
            FunctionalConstraint = "ST",
            ExpectedOnText = "TRUE",
            ExpectedOffText = "FALSE",
            DataType = "BOOLEAN",
            SignalAddress = sourceSha,
            DataSetName = "IED1LD/LLN0.Events",
            SourceIecReference = "IED1LD/GGIO1.Ind2.stVal",
            ReportDisplayReference = "IED1LD/GGIO1.Ind2.stVal",
            EventLogSearchReference = "IED1LD/GGIO1.Ind2.stVal",
            SourceRow = 1,
            SignalKind = FatSignalKind.Discrete,
            CaptureMode = FatCaptureMode.AutomaticTransition,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = IoTestSignalSelectionService.SclDataSetAuthorityBindingStatus,
            BindingEvidence = "Static SCL DataSet authority"
        };

    private static IoTestProject SclProject(params IoTestPointPlan[] points)
        => new()
        {
            ProjectId = "SHARED-SCL-WORKSPACE",
            SchemaVersion = "ARSAS-FAT-SCL-1.0",
            ProjectName = "Shared SCL workspace regression",
            Ieds = new List<IoTestIedPlan>
            {
                new()
                {
                    IedName = "IED1",
                    IpAddress = "192.0.2.10",
                    TestPoints = points.ToList()
                }
            }
        };

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
