using System.IO;
using System.Linq;
using System.Xml.Linq;
using ArIED61850Tester.Views;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Real Windows FAT reports: power overlapped with the channel tables, current
/// sat above the Ua phase reference, and SV switching blanked the whole UI.
/// These contracts are source/layout and electrical display safeguards.
/// </summary>
public sealed class SmvP3FatUxRegressionTests
{
    [Fact]
    public void MeasurementsPanel_HasSevenDefinedRows_PowerAndFooterCannotShareOneSlot()
    {
        var xaml = XDocument.Parse(Read("Views/SmvInlineViewer.xaml"));
        var heading = xaml.Descendants().Single(x =>
            x.Name.LocalName == "TextBlock" &&
            (string?)x.Attribute("Text") == "Measurements");
        var panel = heading.Parent!.Parent!;
        var definitions = panel.Elements().Single(x =>
            x.Name.LocalName == "Grid.RowDefinitions");
        Assert.Equal(7, definitions.Elements().Count());
        var power = panel.Descendants().Single(x =>
            x.Name.LocalName == "Border" &&
            (string?)x.Attribute("ToolTip") == "{Binding PowerDetails}");
        Assert.Equal("4", (string?)power.Attribute("Grid.Row"));
        var footer = panel.Descendants().Single(x =>
            x.Name.LocalName == "TextBlock" &&
            (string?)x.Attribute("Text") == "{Binding UnitStatus}");
        Assert.Equal("6", (string?)footer.Attribute("Grid.Row"));
        Assert.DoesNotContain("IsExpanded=\"False\"", power.ToString());
        Assert.Contains("Text=\"{Binding PowerStatusShort}\"", power.ToString());
        Assert.Equal(2, xaml.Descendants().Count(x => x.Name.LocalName == "DataGrid"));
    }

    [Fact]
    public void TrueVoltageIsFirstPlotInMappedProfile_AndRawOriginalOrderIsUnchanged()
    {
        var wave = Read("Ports/DigSubAnalyzer/Controls/WaveformPanelControl.cs");
        var mapped = wave.IndexOf("if (nineTwoLe)", StringComparison.Ordinal);
        Assert.True(mapped >= 0);
        var voltage = wave.IndexOf("visibleLanes.Add(new LaneDefinition(\"VOLTAGE · Ch5–8\"", mapped, StringComparison.Ordinal);
        var current = wave.IndexOf("visibleLanes.Add(new LaneDefinition(\"CURRENT · Ch1–4\"", mapped, StringComparison.Ordinal);
        Assert.True(voltage > mapped && current > voltage);
        var raw = wave.IndexOf("visibleLanes.Add(new LaneDefinition(\"RAW Ch1–4\"", current, StringComparison.Ordinal);
        Assert.True(raw > current);
    }

    [Fact]
    public void LiveUi_StagesOneCoherentSnapshotOffThread_AndDoesNotClearEightRowsEachRefresh()
    {
        var code = Read("Views/SmvInlineViewer.xaml.cs");
        Assert.Contains("var vectors = SmvRawVectorMath.Analyze(snapshot);", code);
        Assert.Contains("var waveform = SmvDigSubPresentationAdapter.BuildWaveform(snapshot, profile);", code);
        Assert.Contains("if (generation != _generation || analysis != _analysisGeneration", code);
        Assert.Contains("CurrentWaveform = rendered.waveform;", code);
        Assert.Contains("if (_analysisRunning)", code);
        Assert.Contains("_pendingAnalysis = new PendingAnalysis(snapshot, selectedKey, generation);", code);
        Assert.Contains("while (_pendingAnalysis is { } pending)", code);
        Assert.Contains("if (generation != _generation || analysis != _analysisGeneration", code);
        Assert.Contains("Measurements[rowIndex] = next;", code);
        Assert.Contains("if (!Equals(Measurements[rowIndex], next))", code);
        Assert.Contains("selectedKey != Volatile.Read(ref _selectedKey)", code);
        var presentation = code[code.IndexOf("private void UpdateUnitPresentation(", StringComparison.Ordinal)..];
        Assert.DoesNotContain("Measurements.Clear();", presentation);
    }

    [Fact]
    public void RawAndNineTwoLe_AreSameWireMagnitudesButExplicitlyDifferentUnitsAndGroups()
    {
        var i = new SmvRawVectorMeasurement(1, "Ch1", 1001.931, 1001.931, 15, true);
        var u = new SmvRawVectorMeasurement(5, "Ch5", 10000.039, 10000.039, 0, true);
        var iRaw = SmvEngineeringDisplay.Present(i, SmvDisplayUnits.RawCounts);
        var uRaw = SmvEngineeringDisplay.Present(u, SmvDisplayUnits.RawCounts);
        var current = SmvEngineeringDisplay.Present(i, SmvDisplayUnits.NineTwoLe);
        var voltage = SmvEngineeringDisplay.Present(u, SmvDisplayUnits.NineTwoLe);
        Assert.Equal("counts", iRaw.Unit);
        Assert.Equal("counts", uRaw.Unit);
        Assert.Equal(1.001931, current.Rms, 6);
        Assert.Equal(100.00039, voltage.Rms, 6);
        Assert.Equal("A", current.Unit);
        Assert.Equal("V", voltage.Unit);
        var view = Read("Views/SmvInlineViewer.xaml.cs");
        Assert.Contains("RAW Group A · Ch1–4 (counts)", view);
        Assert.Contains("RAW Group B · Ch5–8 (counts)", view);
        Assert.Contains("Voltage/Current grouping is unverified", view);
    }

    private static string Read(string relativePath)
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var file = Path.Combine(d.FullName, relativePath);
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(relativePath);
    }
}
