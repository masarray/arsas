using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class FatVerificationPersistencePackageTests
{
    [Fact]
    public async Task SameSclReopen_RestoresOperatorDispositionAndCurrentEvidence()
    {
        using var temp = new TempDirectory();
        var scl = Path.Combine(temp.Path, "relay.cid");
        BuildFixture().Save(scl);
        var projects = Path.Combine(temp.Path, "projects");
        var first = await new FatSclWorkspaceBootstrapService().OpenAsync(new[] { scl }, projects);
        var signal = Assert.Single(first.Project.Signals);
        signal.RemoveFromFat();
        signal.SetCurrentEvidence(Evidence(FatValueSlot.Value1, "12.3"));
        signal.SetCurrentEvidence(Evidence(FatValueSlot.Value2, "13.7"));
        FatVerificationPersistenceService.SaveNow(first);

        var reopened = await new FatSclWorkspaceBootstrapService().OpenAsync(new[] { scl }, projects);
        var restored = Assert.Single(reopened.Project.Signals);
        Assert.Equal(FatSignalDisposition.ExcludedByOperator, restored.Disposition);
        Assert.Equal("12.3", restored.Value1Evidence?.RawValue);
        Assert.Equal("13.7", restored.Value2Evidence?.RawValue);
    }

    [Fact]
    public async Task PortableArsasRoundTrip_RebuildsFromSclThenRestoresFatState()
    {
        using var temp = new TempDirectory();
        var scl = Path.Combine(temp.Path, "relay.cid");
        BuildFixture().Save(scl);
        var sourceProjects = Path.Combine(temp.Path, "source-projects");
        var launch = await new FatSclWorkspaceBootstrapService().OpenAsync(new[] { scl }, sourceProjects);
        var signal = Assert.Single(launch.Project.Signals);
        signal.RemoveFromFat();
        signal.SetCurrentEvidence(Evidence(FatValueSlot.Value1, "100.0"));
        signal.SetCurrentEvidence(Evidence(FatValueSlot.Value2, "110.0"));
        var package = await FatVerificationPackageService.ExportAsync(launch, Path.Combine(temp.Path, "fat-project"));

        using (var archive = ZipFile.OpenRead(package))
        {
            Assert.NotNull(archive.GetEntry("manifest.json"));
            Assert.NotNull(archive.GetEntry(FatVerificationPersistenceService.SnapshotFileName));
            Assert.NotNull(archive.GetEntry("report/FAT-v2-Report.pdf"));
            Assert.NotNull(archive.GetEntry("report/FAT-v2-Results.xlsx"));
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("workbook", StringComparison.OrdinalIgnoreCase));
        }

        var reopened = await FatVerificationPackageService.OpenAsync(
            package,
            Path.Combine(temp.Path, "imported-projects"));
        var restored = Assert.Single(reopened.Project.Signals);
        Assert.Equal(FatSignalDisposition.ExcludedByOperator, restored.Disposition);
        Assert.Equal("100.0", restored.Value1Evidence?.RawValue);
        Assert.Equal("110.0", restored.Value2Evidence?.RawValue);
        Assert.Single(reopened.SourceFiles);
        Assert.Equal(IoFatSourceKinds.Scl, reopened.SourceFiles[0].Source.Kind);
    }

    [Fact]
    public async Task TamperedPackagedScl_IsRejectedBeforeEngineeringStateIsTrusted()
    {
        using var temp = new TempDirectory();
        var scl = Path.Combine(temp.Path, "relay.cid");
        BuildFixture().Save(scl);
        var launch = await new FatSclWorkspaceBootstrapService().OpenAsync(
            new[] { scl }, Path.Combine(temp.Path, "projects"));
        var package = await FatVerificationPackageService.ExportAsync(launch, Path.Combine(temp.Path, "fat-project"));

        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            var sourceEntry = archive.Entries.Single(entry => entry.FullName.StartsWith("source/", StringComparison.OrdinalIgnoreCase));
            var name = sourceEntry.FullName;
            sourceEntry.Delete();
            var replacement = archive.CreateEntry(name);
            await using var stream = replacement.Open();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("tampered"));
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FatVerificationPackageService.OpenAsync(package, Path.Combine(temp.Path, "import")));
        Assert.Contains("SHA-256", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReportsUseGenericValueTerminologyAndPreserveRemovedRows()
    {
        using var temp = new TempDirectory();
        var scl = Path.Combine(temp.Path, "relay.cid");
        BuildFixture().Save(scl);
        var launch = await new FatSclWorkspaceBootstrapService().OpenAsync(
            new[] { scl }, Path.Combine(temp.Path, "projects"));
        var signal = Assert.Single(launch.Project.Signals);
        signal.RemoveFromFat();
        signal.SetCurrentEvidence(Evidence(FatValueSlot.Value1, "1.25"));
        signal.SetCurrentEvidence(Evidence(FatValueSlot.Value2, "2.50"));

        var pdf = FatVerificationReportService.GeneratePdf(launch, DateTimeOffset.Parse("2026-09-02T00:00:00Z"));
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(pdf, 0, 8), StringComparison.Ordinal);
        var pdfText = Encoding.ASCII.GetString(pdf);
        Assert.Contains("Value 1", pdfText, StringComparison.Ordinal);
        Assert.Contains("Value 2", pdfText, StringComparison.Ordinal);
        Assert.DoesNotContain("ONObservedValue", pdfText, StringComparison.Ordinal);

        var xlsx = FatVerificationReportService.GenerateXlsx(launch);
        using var memory = new MemoryStream(xlsx);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = await reader.ReadToEndAsync();
        Assert.Contains("Value1", sheet, StringComparison.Ordinal);
        Assert.Contains("Value2", sheet, StringComparison.Ordinal);
        Assert.Contains("ExcludedByOperator", sheet, StringComparison.Ordinal);
        Assert.Contains("1.25", sheet, StringComparison.Ordinal);
        Assert.Contains("2.50", sheet, StringComparison.Ordinal);
    }

    private static FatValueEvidence Evidence(FatValueSlot slot, string raw)
        => new(
            Guid.NewGuid(), slot, FatEvidenceCaptureKind.OperatorSnapshot, raw,
            DateTimeOffset.Parse("2026-09-02T00:00:00Z"), null, "Good", "test", 1, 1);

    private static XDocument BuildFixture()
    {
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        return new XDocument(
            new XElement(ns + "SCL",
                new XAttribute("version", "2007"),
                new XAttribute("revision", "B"),
                new XElement(ns + "Header", new XAttribute("id", "FAT_P5")),
                new XElement(ns + "IED",
                    new XAttribute("name", "IED_P5"),
                    new XElement(ns + "AccessPoint",
                        new XAttribute("name", "S1"),
                        new XElement(ns + "Server",
                            new XElement(ns + "LDevice",
                                new XAttribute("inst", "Application"),
                                new XElement(ns + "LN0",
                                    new XAttribute("lnClass", "LLN0"),
                                    new XAttribute("lnType", "LLN0Type"),
                                    new XElement(ns + "DataSet",
                                        new XAttribute("name", "FAT"),
                                        new XElement(ns + "FCDA",
                                            new XAttribute("ldInst", "MEAS"),
                                            new XAttribute("lnClass", "MMXU"),
                                            new XAttribute("lnInst", "1"),
                                            new XAttribute("doName", "Ana01"),
                                            new XAttribute("fc", "MX"))))),
                            new XElement(ns + "LDevice",
                                new XAttribute("inst", "MEAS"),
                                new XElement(ns + "LN0",
                                    new XAttribute("lnClass", "LLN0"),
                                    new XAttribute("lnType", "LLN0Type")),
                                new XElement(ns + "LN",
                                    new XAttribute("lnClass", "MMXU"),
                                    new XAttribute("inst", "1"),
                                    new XAttribute("lnType", "MMXUType")))))),
                new XElement(ns + "DataTypeTemplates",
                    new XElement(ns + "LNodeType",
                        new XAttribute("id", "LLN0Type"),
                        new XAttribute("lnClass", "LLN0")),
                    new XElement(ns + "LNodeType",
                        new XAttribute("id", "MMXUType"),
                        new XAttribute("lnClass", "MMXU"),
                        new XElement(ns + "DO",
                            new XAttribute("name", "Ana01"),
                            new XAttribute("type", "MvType"))),
                    new XElement(ns + "DOType",
                        new XAttribute("id", "MvType"),
                        new XAttribute("cdc", "MV"),
                        new XElement(ns + "DA",
                            new XAttribute("name", "mag"),
                            new XAttribute("bType", "Struct"),
                            new XAttribute("type", "AnalogueValue"),
                            new XAttribute("fc", "MX"))),
                    new XElement(ns + "DAType",
                        new XAttribute("id", "AnalogueValue"),
                        new XElement(ns + "BDA",
                            new XAttribute("name", "f"),
                            new XAttribute("bType", "FLOAT32"))))));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "arsas-fat-v2-p5-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
            catch { }
        }
    }
}
