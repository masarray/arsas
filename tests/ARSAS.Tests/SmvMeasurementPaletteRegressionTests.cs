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

        var grid = Assert.Single(document.Descendants(wpf + "DataGrid"));
        Assert.Equal("{StaticResource SmvMeasurementGrid}", (string?)grid.Attribute("Style"));
        Assert.Equal("{StaticResource SmvMeasurementHeader}", (string?)grid.Attribute("ColumnHeaderStyle"));
        Assert.Equal("{StaticResource SmvMeasurementCell}", (string?)grid.Attribute("CellStyle"));
        Assert.Equal("{StaticResource SmvMeasurementRow}", (string?)grid.Attribute("RowStyle"));
        Assert.Equal("#F8FBFF", (string?)grid.Attribute("Background"));
        Assert.Equal("#F8FBFF", (string?)grid.Attribute("RowBackground"));
        Assert.Equal("#EDF5FF", (string?)grid.Attribute("AlternatingRowBackground"));
        Assert.Equal("2", (string?)grid.Attribute("AlternationCount"));

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
        Assert.Contains("ItemsSource=\"{Binding Measurements}\"", xml);
        Assert.Contains("<dig:WaveformPanelControl", xml);
        Assert.Contains("<dig:PhasorDiagramControl", xml);
        foreach (var binding in new[] { "DisplayName", "RmsText", "FundamentalRmsText", "AngleText", "Unit" })
            Assert.Contains($"Binding=\"{{Binding {binding}}}\"", xml.Replace("Text=\"{Binding", "Binding=\"{Binding"));
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
