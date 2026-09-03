using System.IO.Compression;
using System.Text.Json;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatSourceBundlePersistenceTests
{
    [Fact]
    public void SourceSetFingerprint_IsOrderIndependentAndLegacyWorkbookStoragePathIsStable()
    {
        var a = new IoFatSourceDescriptor("a", IoFatSourceKinds.Scl, "A.cid", new string('1', 64), 100);
        var b = new IoFatSourceDescriptor("b", IoFatSourceKinds.Scl, "B.cid", new string('2', 64), 200);

        Assert.Equal(
            IoFatSourceIdentity.ComputeSetFingerprint(new[] { a, b }),
            IoFatSourceIdentity.ComputeSetFingerprint(new[] { b, a }));

        var legacy = Project();
        legacy = new IoTestProject
        {
            ProjectId = legacy.ProjectId,
            SchemaVersion = legacy.SchemaVersion,
            ProjectName = legacy.ProjectName,
            SourceWorkbookName = "source.xlsx",
            SourceWorkbookSha256 = new string('a', 64),
            Ieds = legacy.Ieds
        };
        var workbook = IoFatSourceIdentity.LegacyWorkbook(legacy.SourceWorkbookName, legacy.SourceWorkbookSha256);
        IoFatSourceIdentity.AttachOrValidate(legacy, new[] { workbook });

        Assert.Equal(new string('a', 64), IoFatSourceIdentity.ProjectStorageFingerprint(legacy));
    }

    [Fact]
    public async Task MultiSclWorkspace_RestoresProgressWithSourcesPresentedInDifferentOrder()
    {
        var root = TempDirectory();
        var first = Path.Combine(root, "relay-a.cid");
        var second = Path.Combine(root, "relay-b.scd");
        await File.WriteAllTextAsync(first, "<SCL><IED name=\"A\"/></SCL>");
        await File.WriteAllTextAsync(second, "<SCL><IED name=\"B\"/></SCL>");
        var projectsRoot = Path.Combine(root, "projects");
        var evidenceRoot = Path.Combine(root, "evidence");

        var opened = await IoTestWorkspaceBootstrapService.OpenSourcesAsync(
            Project(),
            new[]
            {
                new IoFatSourceInput(first, IoFatSourceKinds.Scl),
                new IoFatSourceInput(second, IoFatSourceKinds.Scl)
            },
            projectsRoot,
            evidenceRoot,
            Session);
        using (opened.Session)
        using (opened.Workspace)
        {
            Assert.Equal(2, opened.Project.Sources.Count);
            Assert.Equal(2, opened.Workspace.SourceFiles.Count);
            Assert.Empty(opened.Workspace.SourceWorkbookPath);
            opened.Project.Ieds[0].TestPoints[0].TestEnabled = false;
            opened.Workspace.SaveNow();
        }

        var restored = await IoTestWorkspaceBootstrapService.OpenSourcesAsync(
            Project(),
            new[]
            {
                new IoFatSourceInput(second, IoFatSourceKinds.Scl),
                new IoFatSourceInput(first, IoFatSourceKinds.Scl)
            },
            projectsRoot,
            evidenceRoot,
            Session);
        using (restored.Session)
        using (restored.Workspace)
        {
            Assert.True(restored.RestoredProgress);
            Assert.False(restored.Project.Ieds[0].TestPoints[0].TestEnabled);
            Assert.Equal(2, restored.Workspace.SourceFiles.Count);
            Assert.All(restored.Workspace.SourceFiles, source => Assert.True(File.Exists(source.LocalPath)));
        }
    }

    [Fact]
    public async Task ArsasProject_MultiSclRoundTripsWithoutFakeWorkbookOrExcelCopy()
    {
        var root = TempDirectory();
        var first = Path.Combine(root, "relay-a.cid");
        var second = Path.Combine(root, "relay-b.scd");
        await File.WriteAllTextAsync(first, "<SCL><IED name=\"A\"/></SCL>");
        await File.WriteAllTextAsync(second, "<SCL><IED name=\"B\"/></SCL>");
        var evidenceRoot = Path.Combine(root, "evidence-a");
        var opened = await IoTestWorkspaceBootstrapService.OpenSourcesAsync(
            Project(),
            new[]
            {
                new IoFatSourceInput(first, IoFatSourceKinds.Scl),
                new IoFatSourceInput(second, IoFatSourceKinds.Scl)
            },
            Path.Combine(root, "projects-a"),
            evidenceRoot,
            Session);
        var package = Path.Combine(root, "multi-scl.arsas");
        using (opened.Session)
        using (opened.Workspace)
            await IoFatProjectPackageService.ExportAsync(opened.Workspace, opened.Session, package);

        await IoFatProjectPackageService.ValidateAsync(package);
        using (var archive = ZipFile.OpenRead(package))
        {
            Assert.Null(archive.GetEntry("report/IO-FAT-Results.xlsx"));
            using var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
            using var manifest = JsonDocument.Parse(await reader.ReadToEndAsync());
            var rootElement = manifest.RootElement;
            Assert.Equal(2, rootElement.GetProperty("sourceFiles").GetArrayLength());
            Assert.False(string.IsNullOrWhiteSpace(rootElement.GetProperty("sourceSetSha256").GetString()));
            Assert.Equal(string.Empty, rootElement.GetProperty("sourceWorkbookEntry").GetString());
            foreach (var source in rootElement.GetProperty("sourceFiles").EnumerateArray())
                Assert.NotNull(archive.GetEntry(source.GetProperty("entry").GetString()!));
        }

        var imported = await IoTestWorkspaceBootstrapService.OpenPackageAsync(
            package,
            Path.Combine(root, "projects-b"),
            Path.Combine(root, "evidence-b"),
            Session);
        using (imported.Session)
        using (imported.Workspace)
        {
            Assert.Equal(2, imported.Project.Sources.Count);
            Assert.Equal(2, imported.Workspace.SourceFiles.Count);
            Assert.Empty(imported.Workspace.SourceWorkbookPath);
            Assert.All(imported.Workspace.SourceFiles, source => Assert.True(File.Exists(source.LocalPath)));
        }
    }

    [Fact]
    public async Task ArsasProject_RejectsTamperedSclSource()
    {
        var root = TempDirectory();
        var source = Path.Combine(root, "relay.cid");
        await File.WriteAllTextAsync(source, "<SCL><IED name=\"A\"/></SCL>");
        var opened = await IoTestWorkspaceBootstrapService.OpenSourcesAsync(
            Project(),
            new[] { new IoFatSourceInput(source, IoFatSourceKinds.Scl) },
            Path.Combine(root, "projects"),
            Path.Combine(root, "evidence"),
            Session);
        var package = Path.Combine(root, "tamper.arsas");
        using (opened.Session)
        using (opened.Workspace)
            await IoFatProjectPackageService.ExportAsync(opened.Workspace, opened.Session, package);

        string sourceEntry;
        using (var archive = ZipFile.OpenRead(package))
        using (var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open()))
        using (var manifest = JsonDocument.Parse(await reader.ReadToEndAsync()))
            sourceEntry = manifest.RootElement.GetProperty("sourceFiles")[0].GetProperty("entry").GetString()!;

        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            archive.GetEntry(sourceEntry)!.Delete();
            var replacement = archive.CreateEntry(sourceEntry);
            await using var writer = new StreamWriter(replacement.Open());
            await writer.WriteAsync("<SCL>tampered</SCL>");
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => IoFatProjectPackageService.ValidateAsync(package));
    }

    private static IoTestProject Project()
    {
        var point = new IoTestPointPlan
        {
            TestPointId = "P3-TP-001",
            IedName = "IED-A",
            IpAddress = "192.0.2.10",
            SignalName = "Dataset member",
            ObjectReference = "IED-ALD0/GGIO1.Ind1.stVal",
            FunctionalConstraint = "ST",
            ExpectedOnText = "TRUE",
            ExpectedOffText = "FALSE",
            ImportReady = true,
            BindingStatus = "SCL_DATASET_EXACT"
        };
        return new IoTestProject
        {
            ProjectId = "P3-SOURCE-BUNDLE",
            SchemaVersion = "ARSAS-FAT-IO-1.0",
            ProjectName = "P3 source bundle",
            Ieds =
            {
                new IoTestIedPlan
                {
                    IedName = point.IedName,
                    IpAddress = point.IpAddress,
                    TestPoints = { point }
                }
            }
        };
    }

    private static IoTestSessionController Session(IoTestProject project, string evidenceRoot)
        => new(project, _ => null, action => action(), evidenceRoot);

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
