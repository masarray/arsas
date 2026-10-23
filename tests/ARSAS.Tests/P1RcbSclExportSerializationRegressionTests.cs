using System.Text.Json;
using System.Xml.Linq;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester;

namespace ARSAS.Tests;

public sealed class P1RcbSclExportSerializationRegressionTests
{
    [Fact]
    public void LegacySasReport_SerializesScalarEvidenceWithoutXmlObjectGraph()
    {
        var root = new XElement("SCL");
        for (var index = 0; index < 80; index++)
            root.Add(new XAttribute($"a{index}", index));
        root.Add(new XElement("Header", new XAttribute("id", "x")));

        var result = new LegacySasSclExportResult
        {
            Document = new XDocument(root),
            InputPath = "input.scd",
            OutputPath = "output.cid",
            ReportPath = "output.legacy-sas-rcb-report.json",
            SummaryPath = "output.legacy-sas-rcb-summary.md",
            IedName = "IED1",
            AccessPointName = "AP1",
            SclSchema = "Edition 1",
            RetainedReportControlReference = "IED1LD0/LLN0.RP.RCB1",
            RetainedDataSetName = "DS1",
            RetainedDataSetMemberCount = 1,
            RemovedReportControlCount = 3,
            RemovedDataSetCount = 0
        };

        var json = ArIED61850Tester.LegacySasSclExporter.SerializeSafeReportForTest(result);

        Assert.DoesNotContain("\"document\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("firstAttribute", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nextAttribute", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"retainedReportControlReference\"", json, StringComparison.Ordinal);
        Assert.Contains("IED1LD0/LLN0.RP.RCB1", json, StringComparison.Ordinal);
        Assert.Contains("\"retainedDataSetMemberCount\": 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacySasReport_OldDirectResultSerializationStillDemonstratesXmlCycleRisk()
    {
        var root = new XElement("SCL");
        for (var index = 0; index < 80; index++)
            root.Add(new XAttribute($"a{index}", index));

        var result = new LegacySasSclExportResult
        {
            Document = new XDocument(root)
        };

        var exception = Record.Exception(() => JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.NotNull(exception);
        Assert.IsType<JsonException>(exception);
    }

    [Fact]
    public void SourceBackedRcbExport_UsesPinnedEngineBuildButNeverSerializesEngineResultDirectly()
    {
        var main = Read("MainWindow.RcbExport.cs");
        var bridge = Read("LegacySasSclExporter.SafeBridge.cs");

        Assert.Contains("LegacySasSclExporter.WriteFiles(", main, StringComparison.Ordinal);
        Assert.Contains("namespace ArIED61850Tester;", bridge, StringComparison.Ordinal);
        Assert.Contains(
            "AR.Iec61850.Scl.Export.LegacySasSclExporter.Build",
            bridge,
            StringComparison.Ordinal);
        Assert.Contains("built.Document.Save(writer)", bridge, StringComparison.Ordinal);
        Assert.Contains("SerializeSafeReportForTest(written)", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Serialize(written", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ReferenceHandler.Preserve", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void RelayBenchFixes_AreNotChangedBySclSerializationBridge()
    {
        var bridge = Read("LegacySasSclExporter.SafeBridge.cs");

        Assert.DoesNotContain("ControlSynchroCheck", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlInterlockCheck", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("P1CommandFreshness", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("RcbGrid", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("RelayFascia", bridge, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
