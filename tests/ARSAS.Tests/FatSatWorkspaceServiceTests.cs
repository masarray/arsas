using System.IO.Compression;
using System.Text.Json;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class FatSatWorkspaceServiceTests
{
    [Fact]
    public void CreateDefault_ProvidesBoundedIec61850TestPlan()
    {
        var service = new FatSatWorkspaceService();
        var document = service.CreateDefault();
        var summary = service.Summarize(document);

        Assert.Equal(FatSatWorkspaceDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.True(document.TestCases.Count >= 8);
        Assert.Contains(document.TestCases, item => item.Area == "Sampled Values");
        Assert.Contains(document.TestCases, item => item.Area == "Control");
        Assert.Equal(document.TestCases.Count, summary.NotRun);
        Assert.False(summary.IsComplete);
    }

    [Fact]
    public async Task SaveAndOpen_RoundTripsSchemaAndOutcomes()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "project.arsas-fat.json");
            var service = new FatSatWorkspaceService();
            var document = service.CreateDefault();
            document.ProjectName = "Substation A FAT";
            document.OperatorName = "Operator 1";
            document.TestCases[0].Result = FatSatTestResult.Pass;
            document.TestCases[0].ActualResult = "Identity confirmed.";

            await service.SaveAsync(path, document);
            var reopened = await service.OpenAsync(path);

            Assert.Equal(document.WorkspaceId, reopened.WorkspaceId);
            Assert.Equal("Substation A FAT", reopened.ProjectName);
            Assert.Equal(FatSatTestResult.Pass, reopened.TestCases[0].Result);
            Assert.Equal("Identity confirmed.", reopened.TestCases[0].ActualResult);
            Assert.False(File.Exists(path + ".partial"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportAuditPackage_WritesWorkspaceReportEvidenceAndChecksums()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var evidencePath = Path.Combine(directory, "sv-evidence.zip");
            await File.WriteAllBytesAsync(evidencePath, [1, 2, 3, 4, 5, 6]);
            var outputPath = Path.Combine(directory, "fat-sat-audit.zip");
            var service = new FatSatWorkspaceService();
            var document = service.CreateDefault();
            document.ProjectName = "Bay 1 SAT";
            document.ApplicationVersion = "1.6.19";
            document.ApplicationCommit = "app123";
            document.EngineRepository = "masarray/ARIEC61850";
            document.EngineReference = "main";
            document.EngineCommit = "0f8453182957900bc6d91287fb8177c8d9762188";
            var sampleTest = document.TestCases.First(item => item.Area == "Sampled Values");
            sampleTest.Result = FatSatTestResult.Review;
            sampleTest.ActualResult = "Capture received; semantic mapping remains unresolved.";
            sampleTest.Evidence.Add(await service.CreateEvidenceReferenceAsync(evidencePath));

            var result = await service.ExportAuditPackageAsync(outputPath, document);

            Assert.True(File.Exists(outputPath));
            Assert.Matches("^[0-9a-f]{64}$", result.PackageSha256);
            Assert.Equal(1, result.Summary.EvidenceFiles);
            Assert.True(result.Summary.HasBlockingOutcome);

            using var archive = ZipFile.OpenRead(outputPath);
            Assert.NotNull(archive.GetEntry("workspace.json"));
            Assert.NotNull(archive.GetEntry("report.md"));
            Assert.NotNull(archive.GetEntry("SHA256SUMS.txt"));
            var evidenceEntry = Assert.Single(archive.Entries.Where(entry => entry.FullName.StartsWith("evidence/", StringComparison.Ordinal)));
            Assert.EndsWith("sv-evidence.zip", evidenceEntry.FullName, StringComparison.Ordinal);

            var packagedWorkspace = await ReadEntryAsync(archive, "workspace.json");
            using var json = JsonDocument.Parse(packagedWorkspace);
            var packagedSourcePath = json.RootElement
                .GetProperty("testCases")
                .EnumerateArray()
                .First(item => item.GetProperty("area").GetString() == "Sampled Values")
                .GetProperty("evidence")[0]
                .GetProperty("sourcePath")
                .GetString();
            Assert.StartsWith("evidence/", packagedSourcePath, StringComparison.Ordinal);
            Assert.DoesNotContain(directory, packagedWorkspace, StringComparison.OrdinalIgnoreCase);

            var report = await ReadEntryAsync(archive, "report.md");
            Assert.Contains("REVIEW REQUIRED", report, StringComparison.Ordinal);
            Assert.Contains("Bay 1 SAT", report, StringComparison.Ordinal);
            Assert.Contains("0f8453182957900bc6d91287fb8177c8d9762188", report, StringComparison.Ordinal);

            var checksums = await ReadEntryAsync(archive, "SHA256SUMS.txt");
            Assert.Contains("  workspace.json", checksums, StringComparison.Ordinal);
            Assert.Contains("  report.md", checksums, StringComparison.Ordinal);
            Assert.Contains($"  {evidenceEntry.FullName}", checksums, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportAuditPackage_RejectsEvidenceChangedAfterAttachment()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var evidencePath = Path.Combine(directory, "capture.pcapng");
            await File.WriteAllBytesAsync(evidencePath, [10, 20, 30]);
            var service = new FatSatWorkspaceService();
            var document = service.CreateDefault();
            var testCase = document.TestCases[0];
            testCase.Evidence.Add(await service.CreateEvidenceReferenceAsync(evidencePath));
            await File.WriteAllBytesAsync(evidencePath, [10, 20, 31]);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.ExportAuditPackageAsync(Path.Combine(directory, "invalid.zip"), document));

            Assert.Contains("hash changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(directory, "invalid.zip")));
            Assert.False(File.Exists(Path.Combine(directory, "invalid.zip.partial")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OpenAsync_RejectsUnsupportedSchemaVersion()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "future.json");
            await File.WriteAllTextAsync(path, "{\"schemaVersion\":99,\"workspaceId\":\"00000000-0000-0000-0000-000000000001\",\"testCases\":[]}");
            var service = new FatSatWorkspaceService();

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.OpenAsync(path));

            Assert.Contains("Unsupported FAT/SAT schema version", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arsas-fat-sat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Missing entry {name}");
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
