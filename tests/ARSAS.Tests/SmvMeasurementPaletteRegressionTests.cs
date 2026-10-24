using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// Protect the operator-accepted SMV Measurements cool-blue surface from
/// reverting to OS-default beige rows or unrelated global style changes.
/// </summary>
public sealed class SmvMeasurementPaletteRegressionTests
{
    [Fact]
    public void Measurements_HasScopedCoolBlueGridRowsHeaderAndCells()
    {
        var document = XDocument.Parse(Read("Views/SmvInlineViewer.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        // The final operator-accepted layout has TWO synchronized DataGrids:
        // voltage and current. Do not force the obsolete single-grid layout.
        var grids = document.Descendants(wpf + "DataGrid").ToArray();
        Assert.Equal(2, grids.Length);
        foreach (var (name, view) in new[]
        {
            ("SmvVoltageMeasurementsGrid", "VoltageMeasurements"),
            ("SmvCurrentMeasurementsGrid", "CurrentMeasurements")
        })
        {
            var grid = grids.Single(e => (string?)e.Attribute(x + "Name") == name);
            Assert.Equal("{Binding " + view + "}", (string?)grid.Attribute("ItemsSource"));
            Assert.Equal("True", (string?)grid.Attribute("IsReadOnly"));
            Assert.Equal("True", (string?)grid.Attribute("EnableRowVirtualization"));
            Assert.Equal("False", (string?)grid.Attribute("CanUserSortColumns"));
            Assert.Equal("None", (string?)grid.Attribute("GridLinesVisibility"));
            Assert.Equal("{StaticResource SmvMeasurementGrid}", (string?)grid.Attribute("Style"));
            Assert.Equal("{StaticResource SmvMeasurementHeader}", (string?)grid.Attribute("ColumnHeaderStyle"));
            Assert.Equal("{StaticResource SmvMeasurementCell}", (string?)grid.Attribute("CellStyle"));
            Assert.Equal("{StaticResource SmvMeasurementRow}", (string?)grid.Attribute("RowStyle"));
            Assert.Equal("#F8FBFF", (string?)grid.Attribute("Background"));
            Assert.Equal("#F8FBFF", (string?)grid.Attribute("RowBackground"));
            Assert.Equal("#EDF5FF", (string?)grid.Attribute("AlternatingRowBackground"));
            Assert.Equal("2", (string?)grid.Attribute("AlternationCount"));
            Assert.Equal(3, grid.Descendants(wpf + "DataGridColumn").Count() +
                grid.Descendants(wpf + "DataGridTemplateColumn").Count() +
                grid.Descendants(wpf + "DataGridTextColumn").Count());
        }

        AssertStyle(document, wpf, x, "SmvMeasurementGrid", "ModernDataGrid",
            ("Background", "#F8FBFF"), ("RowBackground", "#F8FBFF"),
            ("AlternatingRowBackground", "#EDF5FF"));
        AssertStyle(document, wpf, x, "SmvMeasurementHeader", "{x:Type DataGridColumnHeader}",
            ("Background", "#E8F2FF"), ("BorderBrush", "#D5E4F6"));
        AssertStyle(document, wpf, x, "SmvMeasurementCell", "{x:Type DataGridCell}",
            ("Background", "Transparent"), ("BorderBrush", "#E2EBF7"));
        AssertStyle(document, wpf, x, "SmvMeasurementRow", "{x:Type DataGridRow}",
            ("BorderBrush", "#E2EBF7"));
        var row = document.Descendants(wpf + "Style")
            .Single(e => (string?)e.Attribute(x + "Key") == "SmvMeasurementRow");
        var triggers = row.Descendants(wpf + "Trigger").ToArray();
        Assert.Contains(triggers, t => (string?)t.Attribute("Property") == "ItemsControl.AlternationIndex" &&
                                       (string?)t.Attribute("Value") == "1" &&
                                       t.Descendants(wpf + "Setter").Any(s => (string?)s.Attribute("Value") == "#EDF5FF"));
        Assert.Contains(triggers, t => (string?)t.Attribute("Property") == "IsMouseOver" &&
                                       t.Descendants(wpf + "Setter").Any(s => (string?)s.Attribute("Value") == "#E4F0FF"));
    }

    [Fact]
    public void StylingOnly_LeavesEngineeringBindingsAndDigSubPlotsIntact()
    {
        var xml = Read("Views/SmvInlineViewer.xaml");
        Assert.Contains("ItemsSource=\"{Binding VoltageMeasurements}\"", xml);
        Assert.Contains("ItemsSource=\"{Binding CurrentMeasurements}\"", xml);
        Assert.Contains("<dig:WaveformPanelControl", xml);
        Assert.Contains("<dig:PhasorDiagramControl", xml);

        var document = XDocument.Parse(xml);
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var columns = document.Descendants(wpf + "DataGridTextColumn").ToArray();
        Assert.Equal(2, columns.Count(e => (string?)e.Attribute("Binding") == "{Binding RmsText}"));
        Assert.Equal(2, columns.Count(e => (string?)e.Attribute("Binding") == "{Binding AngleText}"));
        Assert.Equal(2, document.Descendants(wpf + "TextBlock")
            .Count(e => (string?)e.Attribute("Text") == "{Binding DisplayName}"));

        // Both panels must project ONE authoritative, change-notified eight-row
        // model. Preserve the engineering values and stable row lifetimes.
        var viewer = Read("Views/SmvInlineViewer.xaml.cs");
        var liveRow = Read("Views/SmvMeasurementLiveRow.cs");
        Assert.Contains("ObservableCollection<SmvMeasurementLiveRow> Measurements", viewer);
        Assert.Equal(2, viewer.Split("new ListCollectionView(Measurements)").Length - 1);
        Assert.Contains("SmvEngineeringDisplay.Describe(row.ChannelIndex, ActiveUnits).IsVoltage", viewer);
        Assert.Contains("!SmvEngineeringDisplay.Describe(row.ChannelIndex, ActiveUnits).IsVoltage", viewer);
        Assert.Contains("VoltageMeasurements.Refresh();", viewer);
        Assert.Contains("CurrentMeasurements.Refresh();", viewer);
        Assert.Contains("next.ChannelIndex != _current.ChannelIndex", liveRow);
        foreach (var field in new[] { "DisplayName", "Unit", "RmsText", "FundamentalRmsText", "AngleText" })
            Assert.Contains("nameof(" + field + ")", liveRow);
    }

    private static void AssertStyle(
        XDocument document, XNamespace wpf, XNamespace x, string key, string parent,
        params (string Property, string Value)[] setters)
    {
        var style = document.Descendants(wpf + "Style")
            .Single(e => (string?)e.Attribute(x + "Key") == key);
        Assert.Contains(parent, (string?)style.Attribute("BasedOn") ?? string.Empty);
        foreach (var (property, value) in setters)
            Assert.Contains(style.Elements(wpf + "Setter"),
                e => (string?)e.Attribute("Property") == property &&
                     (string?)e.Attribute("Value") == value);
    }

    private static string Read(string path)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        throw new FileNotFoundException(path);
    }
}
