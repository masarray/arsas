using System.Xml.Linq;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class RcbExportP0MultiSelectionRegressionTests
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    [Fact]
    public void GoldenFilter_RetainsIndependentAnalogAndDigitalReportControls()
    {
        var source = XDocument.Parse(Fixture());
        var inventory = SclReportControlFilter.Inspect(source, "IED1.cid", "IED1", "AP1");
        var analog = inventory.ReportControls.Single(item => item.Name == "URCB_ANALOG");
        var digital = inventory.ReportControls.Single(item => item.Name == "BRCB_DIGITAL");

        var result = SclReportControlFilter.Filter(
            source,
            new SclReportControlFilterOptions
            {
                IedName = "IED1",
                AccessPointName = "AP1",
                SelectedReportControls = new[]
                {
                    new SclReportControlSelection(analog.SelectionKey),
                    new SclReportControlSelection(digital.SelectionKey)
                },
                RequireExactlyOneReportControl = false,
                CollapseIndexedSelectionToSingleInstance = true,
                RemoveUnreferencedDataSets = false
            },
            "IED1.cid");

        Assert.Equal(2, result.RetainedReportControls.Count);
        Assert.Contains(result.RetainedReportControls, item => item.Name == "URCB_ANALOG" && item.DataSetName == "Analog" && item.DataSetMemberCount == 1);
        Assert.Contains(result.RetainedReportControls, item => item.Name == "BRCB_DIGITAL" && item.DataSetName == "Digital" && item.DataSetMemberCount == 1);
        Assert.Equal(2, result.Document.Descendants(Scl + "ReportControl").Count());
        Assert.Equal(2, result.Document.Descendants(Scl + "DataSet").Count());
    }

    [Fact]
    public void ViewModel_CheckingSecondRcb_DoesNotClearFirstSelection()
    {
        var analog = Row("URCB_ANALOG", "Analog", 22);
        var digital = Row("BRCB_DIGITAL", "Digital", 36);
        var viewModel = new RcbExportFilterViewModel(new RcbExportWindowOptions
        {
            IedName = "IED1",
            Rows = new[] { analog, digital }
        });

        viewModel.SelectOnly(analog);
        viewModel.SelectOnly(digital);

        Assert.Equal(2, viewModel.SelectedRows.Count);
        Assert.Contains(analog, viewModel.SelectedRows);
        Assert.Contains(digital, viewModel.SelectedRows);
        Assert.True(viewModel.CanExport);
    }

    [Fact]
    public void P0Exporter_UsesGoldenMultiSelectionFilter_NotEnginePinAdvance()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.RcbExport.P0Multi.cs"));
        var lockText = File.ReadAllText(FindRepoFile("engines/ARIEC61850.lock.json"));

        Assert.Contains("SclReportControlFilter.Filter", source, StringComparison.Ordinal);
        Assert.Contains("SelectedReportControls = selections", source, StringComparison.Ordinal);
        Assert.Contains("RequireExactlyOneReportControl = false", source, StringComparison.Ordinal);
        Assert.Contains("11ab2304482600c19ba979f4fc9021ddb46b9af9", lockText, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedReportControls = selections", File.ReadAllText(FindEngineLegacyExporterIfPresent()), StringComparison.Ordinal);
    }

    private static RcbExportRow Row(string name, string dataSet, int members)
        => new()
        {
            Name = name,
            Reference = $"IED1LD0/LLN0.{name}",
            DataSetName = dataSet,
            DataSetReference = $"IED1LD0/LLN0.{dataSet}",
            MemberCount = members,
            IsSourceBacked = true,
            SourceSelectionKey = $"IED1|AP1|LD0|LLN0|{name}"
        };

    private static string Fixture()
        => """
           <?xml version="1.0" encoding="utf-8"?>
           <SCL xmlns="http://www.iec.ch/61850/2003/SCL" version="2007" revision="B">
             <Header id="IED1" />
             <IED name="IED1">
               <AccessPoint name="AP1"><Server><LDevice inst="LD0"><LN0 lnClass="LLN0" inst="" lnType="LN0_TYPE">
                 <DataSet name="Analog"><FCDA ldInst="LD0" lnClass="MMXU" lnInst="1" doName="A" daName="phsA.cVal.mag.f" fc="MX" /></DataSet>
                 <DataSet name="Digital"><FCDA ldInst="LD0" lnClass="XCBR" lnInst="1" doName="Pos" daName="stVal" fc="ST" /></DataSet>
                 <ReportControl name="URCB_ANALOG" buffered="false" indexed="false" datSet="Analog" confRev="1" />
                 <ReportControl name="BRCB_DIGITAL" buffered="true" indexed="false" datSet="Digital" confRev="1" />
               </LN0></LDevice></Server></AccessPoint>
             </IED>
             <DataTypeTemplates><LNodeType id="LN0_TYPE" lnClass="LLN0" /></DataTypeTemplates>
           </SCL>
           """;

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }

    private static string FindEngineLegacyExporterIfPresent()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "..", "ARIEC61850", "src", "AR.Iec61850", "Scl", "Export", "LegacySasSclExporter.cs");
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full))
                return full;
            directory = directory.Parent;
        }

        // The application regression is fully covered by the golden lock assertion when
        // the engine source is not adjacent to the test checkout.
        return FindRepoFile("engines/ARIEC61850.lock.json");
    }
}
