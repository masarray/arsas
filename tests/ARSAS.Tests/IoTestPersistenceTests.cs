using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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
        var stored = await IoTestWorkspacePersistence.OpenWorkbookAsync(
            project,
            session,
            workbook,
            Path.Combine(root, "projects"),
            Path.Combine(root, "evidence"));
        using (stored.Workspace)
            stored.Workspace.SaveNow();
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
            Assert.False(point.TestEnabled);
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
        var stored = await IoTestWorkspacePersistence.OpenWorkbookAsync(
            project,
            session,
            workbook,
            Path.Combine(root, "projects"),
            Path.Combine(root, "evidence"));
        using (stored.Workspace)
            stored.Workspace.SaveNow();
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
            Assert.False(point.TestEnabled);
            Assert.Contains("continuity", point.Runtime.StatusReason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NativePdfReport_GeneratesRealPagedIoFatEvidence()
    {
        var project = Project(new string('a', 64));
        CompletePass(project.Ieds[0].TestPoints[0]);
        var bytes = IoFatPdfReportService.Generate(
            project,
            new DateTimeOffset(2026, 7, 28, 17, 30, 0, TimeSpan.FromHours(7)));
        var text = Encoding.ASCII.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.Contains("IEC 61850 FAT Evidence Report", text, StringComparison.Ordinal);
        Assert.Contains("CB closed", text, StringComparison.Ordinal);
        Assert.Contains("AA1C1F03R4ADD/GGIO6.CBClsd.stVal", text, StringComparison.Ordinal);
        Assert.Contains("xref", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
        Assert.True(bytes.Length > 2_000);
    }

    [Fact]
    public async Task ExcelResultExport_WritesEvidenceIntoCopyWithoutChangingSource()
    {
        var root = TempDirectory();
        var source = Path.Combine(root, "source.xlsx");
        CreateResultWorkbook(source);
        var sourceHash = Hash(source);
        var project = Project(sourceHash);
        CompletePass(project.Ieds[0].TestPoints[0]);
        var output = Path.Combine(root, "result.xlsx");

        await IoFatExcelResultExportService.ExportAsync(source, output, project);

        Assert.Equal(sourceHash, Hash(source));
        Assert.True(File.Exists(output));
        using var archive = ZipFile.OpenRead(output);
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var xml = await reader.ReadToEndAsync();
        Assert.Contains("PASS", xml, StringComparison.Ordinal);
        Assert.Contains("2026-07-28 10:00:00.200 +00:00", xml, StringComparison.Ordinal);
        Assert.Contains("Good", xml, StringComparison.Ordinal);
        Assert.Contains("BRCB", xml, StringComparison.Ordinal);
        Assert.Contains("PASS: ON and OFF transitions captured in order", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArsasProject_RoundTripsProgressAndNativeReports()
    {
        var root = TempDirectory();
        var workbook = Path.Combine(root, "source.xlsx");
        CreateResultWorkbook(workbook);
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
        var package = Path.Combine(root, "handover.arsas");
        using (session)
        using (opened.Workspace)
        {
            var exported = await IoFatProjectPackageService.ExportAsync(
                opened.Workspace,
                session,
                package);
            Assert.Equal(package, exported);
        }

        await IoFatProjectPackageService.ValidateAsync(package);
        using (var archive = ZipFile.OpenRead(package))
        {
            Assert.NotNull(archive.GetEntry("manifest.json"));
            Assert.NotNull(archive.GetEntry("project.snapshot.json"));
            var pdfEntry = archive.GetEntry("report/IO-FAT-Report.pdf");
            Assert.NotNull(pdfEntry);
            await using (var pdfStream = pdfEntry!.Open())
            {
                using var memory = new MemoryStream();
                await pdfStream.CopyToAsync(memory);
                var report = Encoding.ASCII.GetString(memory.ToArray());
                Assert.StartsWith("%PDF-1.4", report, StringComparison.Ordinal);
                Assert.Contains("CB closed", report, StringComparison.Ordinal);
                Assert.Contains("PASSED", report, StringComparison.Ordinal);
            }

            var excelEntry = archive.GetEntry("report/IO-FAT-Results.xlsx");
            Assert.NotNull(excelEntry);
            await using var excelStream = excelEntry!.Open();
            using var excelMemory = new MemoryStream();
            await excelStream.CopyToAsync(excelMemory);
            using var resultArchive = new ZipArchive(new MemoryStream(excelMemory.ToArray()), ZipArchiveMode.Read);
            using var sheetReader = new StreamReader(resultArchive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
            var resultXml = await sheetReader.ReadToEndAsync();
            Assert.Contains("PASS", resultXml, StringComparison.Ordinal);
            Assert.Contains("BRCB", resultXml, StringComparison.Ordinal);
        }

        var imported = await IoTestWorkspaceBootstrapService.OpenPackageAsync(
            package,
            Path.Combine(root, "projects-b"),
            Path.Combine(root, "evidence-b"),
            Session);
        using (imported.Session)
        using (imported.Workspace)
        {
            var point = imported.Project.Ieds[0].TestPoints[0];
            Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
            Assert.False(point.TestEnabled);
            Assert.True(File.Exists(imported.Workspace.SourceWorkbookPath));
            Assert.True(File.Exists(imported.Workspace.SnapshotPath));
        }
    }

    [Fact]
    public async Task ArsasProject_LegacyExtensionRemainsReadable()
    {
        var root = TempDirectory();
        var workbook = Path.Combine(root, "source.xlsx");
        CreateResultWorkbook(workbook);
        var project = Project(Hash(workbook));
        CompletePass(project.Ieds[0].TestPoints[0]);
        var evidenceRoot = Path.Combine(root, "evidence");
        var session = Session(project, evidenceRoot);
        var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
            project,
            session,
            workbook,
            Path.Combine(root, "projects"),
            evidenceRoot);
        var modern = Path.Combine(root, "handover.arsas");
        using (session)
        using (opened.Workspace)
            await IoFatProjectPackageService.ExportAsync(opened.Workspace, session, modern);

        var legacy = Path.Combine(root, "handover.arsas-iofat");
        File.Copy(modern, legacy);
        Assert.True(IoFatProjectPackageService.IsSupportedPackagePath(modern));
        Assert.True(IoFatProjectPackageService.IsSupportedPackagePath(legacy));

        var imported = await IoTestWorkspaceBootstrapService.OpenPackageAsync(
            legacy,
            Path.Combine(root, "projects-import"),
            Path.Combine(root, "evidence-import"),
            Session);
        using (imported.Session)
        using (imported.Workspace)
            Assert.Equal(IoTestPointState.Passed, imported.Project.Ieds[0].TestPoints[0].Runtime.State);
    }

    [Fact]
    public async Task ArsasProject_RejectsTamperedSnapshot()
    {
        var root = TempDirectory();
        var workbook = Path.Combine(root, "source.xlsx");
        CreateResultWorkbook(workbook);
        var project = Project(Hash(workbook));
        var session = Session(project, Path.Combine(root, "evidence"));
        var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
            project,
            session,
            workbook,
            Path.Combine(root, "projects"),
            Path.Combine(root, "evidence"));
        var package = Path.Combine(root, "handover.arsas");
        using (session)
        using (opened.Workspace)
            await IoFatProjectPackageService.ExportAsync(opened.Workspace, session, package);

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

    [Fact]
    public async Task ArsasProject_RejectsTamperedNativeReport()
    {
        var root = TempDirectory();
        var workbook = Path.Combine(root, "source.xlsx");
        CreateResultWorkbook(workbook);
        var project = Project(Hash(workbook));
        var session = Session(project, Path.Combine(root, "evidence"));
        var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
            project,
            session,
            workbook,
            Path.Combine(root, "projects"),
            Path.Combine(root, "evidence"));
        var package = Path.Combine(root, "handover.arsas");
        using (session)
        using (opened.Workspace)
            await IoFatProjectPackageService.ExportAsync(opened.Workspace, session, package);

        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("report/IO-FAT-Report.pdf")!;
            entry.Delete();
            var replacement = archive.CreateEntry("report/IO-FAT-Report.pdf");
            await using var writer = new StreamWriter(replacement.Open());
            await writer.WriteAsync("tampered report");
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            IoFatProjectPackageService.ValidateAsync(package));
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

    private static void CreateResultWorkbook(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """);
        WriteEntry(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
        WriteEntry(archive, "xl/workbook.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="ARSAS_SIGNAL_IMPORT" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """);

        var headers = new[]
        {
            "TestPointId", "ONObservedValue", "ONIEDTimestamp", "ONARSASTimestamp", "ONQuality",
            "ONAcquisitionSource", "ONResult", "OFFObservedValue", "OFFIEDTimestamp", "OFFARSASTimestamp",
            "OFFQuality", "OFFAcquisitionSource", "OFFResult", "OverallResult", "TestNotes"
        };
        var headerCells = string.Concat(headers.Select((header, index) => InlineCell(ColumnName(index) + "1", header)));
        var sheet = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <dimension ref="A1:O2"/>
              <sheetData>
                <row r="1">{headerCells}</row>
                <row r="2">{InlineCell("A2", "TP-001")}</row>
              </sheetData>
            </worksheet>
            """;
        WriteEntry(archive, "xl/worksheets/sheet1.xml", sheet);
    }

    private static string InlineCell(string reference, string value)
        => $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{value}</t></is></c>";

    private static string ColumnName(int zeroBasedIndex)
    {
        var value = zeroBasedIndex + 1;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
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
