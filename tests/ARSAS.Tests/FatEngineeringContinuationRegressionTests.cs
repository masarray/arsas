using System.Security.Cryptography;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class FatEngineeringContinuationRegressionTests
{
    [Fact]
    public void SclContinuation_UsesContentHashInsteadOfFilenameOrSourceId()
    {
        var hashA = new string('a', 64);
        var hashB = new string('b', 64);
        var saved = new[]
        {
            new IoFatSourceDescriptor("saved-a", IoFatSourceKinds.Scl, "relay-original.cid", hashA, 100),
            new IoFatSourceDescriptor("saved-b", IoFatSourceKinds.Scl, "station-original.scd", hashB, 200)
        };
        var reopened = new[]
        {
            new IoFatSourceDescriptor("new-b", IoFatSourceKinds.Scl, "staged-copy.scd", hashB, 200),
            new IoFatSourceDescriptor("new-a", IoFatSourceKinds.Scl, "renamed-copy.cid", hashA, 100)
        };

        Assert.NotEqual(
            IoFatSourceIdentity.ComputeSetFingerprint(saved),
            IoFatSourceIdentity.ComputeSetFingerprint(reopened));
        Assert.True(IoFatSourceIdentity.SameSclContentSet(saved, reopened));
    }

    [Theory]
    [InlineData("ARSAS-FAT-SCL-1.0", "ARSAS-FAT-SCL-1.1", true)]
    [InlineData("ARSAS-FAT-SCL-1.9", "ARSAS-FAT-SCL-1.0", true)]
    [InlineData("ARSAS-FAT-SCL-1.0", "ARSAS-FAT-SCL-2.0", false)]
    [InlineData("ARSAS-FAT-IO-1.0", "ARSAS-FAT-SCL-1.0", false)]
    public void ContinuationSchema_AllowsOnlyCompatibleSclMajor(
        string saved,
        string current,
        bool expected)
    {
        Assert.Equal(expected, IoFatSourceIdentity.CompatibleContinuationSchema(saved, current));
    }

    [Fact]
    public async Task EngineeringToFatReopen_RestoresProgressAcrossCompatibleSclSchemaRevision()
    {
        var root = TempDirectory();
        var source = Path.Combine(root, "engineering.cid");
        await File.WriteAllBytesAsync(source, new byte[] { 0x53, 0x43, 0x4C, 0x2D, 0x45, 0x4E, 0x47 });
        var sourceSha = Hash(source);
        var sourceInputs = new[] { new IoFatSourceInput(source, IoFatSourceKinds.Scl) };
        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");

        var originalPoint = StaticPoint(sourceSha);
        originalPoint.TestEnabled = false;
        var original = SclProject(
            "SHARED-SCL-WORKSPACE",
            "ARSAS-FAT-SCL-1.1",
            originalPoint);
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

        var restored = await IoTestWorkspaceBootstrapService.OpenSourcesAsync(
            SclProject(
                "SHARED-SCL-WORKSPACE",
                "ARSAS-FAT-SCL-1.0",
                StaticPoint(sourceSha)),
            sourceInputs,
            projectsRoot,
            evidenceRoot,
            Session);

        using (restored.Session)
        using (restored.Workspace)
        {
            Assert.True(restored.RestoredProgress);
            Assert.False(Assert.Single(restored.Project.Ieds[0].TestPoints).TestEnabled);
            Assert.DoesNotContain(
                restored.Warnings,
                warning => warning.Contains("different FAT source set or schema", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                restored.Warnings,
                warning => warning.Contains("did not match the current FAT source-set identity", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task EngineeringToFatReopen_RestoresAcrossRenamedIdenticalSclWithoutGuessingEvidence()
    {
        var root = TempDirectory();
        var originalSource = Path.Combine(root, "relay-original.cid");
        var reopenedSource = Path.Combine(root, "relay-copy.cid");
        var bytes = new byte[] { 0x53, 0x43, 0x4C, 0x2D, 0x52, 0x45, 0x4E, 0x41, 0x4D, 0x45 };
        await File.WriteAllBytesAsync(originalSource, bytes);
        await File.WriteAllBytesAsync(reopenedSource, bytes);
        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");

        var originalPoint = StaticPoint(
            Hash(originalSource),
            testPointId: "scl-old-source-static-0001",
            signalAddressOverride: "old-filename-derived-source-id");
        originalPoint.TestEnabled = false;
        var original = SclProject(
            "FAT-SCL-OLD-STAGING-ID",
            "ARSAS-FAT-SCL-1.0",
            originalPoint);
        var originalSession = Session(original, evidenceRoot);
        var opened = await IoTestWorkspacePersistence.OpenSourcesAsync(
            original,
            originalSession,
            new[] { new IoFatSourceInput(originalSource, IoFatSourceKinds.Scl) },
            projectsRoot,
            evidenceRoot);
        using (originalSession)
        using (opened.Workspace)
            opened.Workspace.SaveNow();

        var freshPoint = StaticPoint(
            Hash(reopenedSource),
            testPointId: "scl-new-source-static-0001",
            signalAddressOverride: "new-filename-derived-source-id");

        var restored = await IoTestWorkspaceBootstrapService.OpenSourcesAsync(
            SclProject(
                "FAT-SCL-NEW-STAGING-ID",
                "ARSAS-FAT-SCL-1.0",
                freshPoint),
            new[] { new IoFatSourceInput(reopenedSource, IoFatSourceKinds.Scl) },
            projectsRoot,
            evidenceRoot,
            Session);

        using (restored.Session)
        using (restored.Workspace)
        {
            Assert.True(restored.RestoredProgress);
            var point = Assert.Single(restored.Project.Ieds[0].TestPoints);
            Assert.Equal("scl-new-source-static-0001", point.TestPointId);
            Assert.Equal("new-filename-derived-source-id", point.SignalAddress);
            Assert.False(point.TestEnabled);
        }
    }

    private static IoTestPointPlan StaticPoint(
        string sourceSha,
        string testPointId = "scl-static-0001",
        string? signalAddressOverride = null)
        => new()
        {
            TestPointId = testPointId,
            IedName = "IED1",
            IpAddress = "192.0.2.10",
            SignalName = "Static GGIO indication",
            ObjectReference = "IED1LD/GGIO1.Ind2.stVal",
            FunctionalConstraint = "ST",
            ExpectedOnText = "TRUE",
            ExpectedOffText = "FALSE",
            DataType = "BOOLEAN",
            SignalAddress = signalAddressOverride ?? sourceSha,
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

    private static IoTestProject SclProject(
        string projectId,
        string schemaVersion,
        params IoTestPointPlan[] points)
        => new()
        {
            ProjectId = projectId,
            SchemaVersion = schemaVersion,
            ProjectName = "Engineering to FAT continuation regression",
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
