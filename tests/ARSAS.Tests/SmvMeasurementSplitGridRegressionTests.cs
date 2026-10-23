using System.IO;
using System.Linq;
using System.Xml.Linq;
using ArIED61850Tester.Views;

namespace ARSAS.Tests;

public sealed class SmvMeasurementSplitGridRegressionTests
{
    [Fact]
    public void MeasurementArea_UsesTwoSideBySideCoolBlueFourChannelGrids()
    {
        var xaml = Source("Views/SmvInlineViewer.xaml");
        var xml = XDocument.Parse(xaml);
        var grids = xml.Descendants().Where(x => x.Name.LocalName == "DataGrid").ToArray();
        Assert.Equal(2, grids.Length);
        Assert.Contains("x:Name=\"SmvVoltageMeasurementsGrid\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding VoltageMeasurements}\"", xaml);
        Assert.Contains("x:Name=\"SmvCurrentMeasurementsGrid\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding CurrentMeasurements}\"", xaml);
        Assert.Contains("Header=\"RMS\"", xaml);
        Assert.Contains("Header=\"Angle\"", xaml);
        Assert.DoesNotContain("Header=\"Fund.\"", xaml);
        Assert.DoesNotContain("Header=\"Unit\"", xaml);
        Assert.Contains("Value=\"#F8FBFF\"", xaml);
        Assert.Contains("Value=\"#EDF5FF\"", xaml);
        Assert.Contains("Value=\"#E8F2FF\"", xaml);
        Assert.All(grids, grid =>
        {
            Assert.Equal("False", (string?)grid.Attribute("AutoGenerateColumns"));
            Assert.Equal("False", (string?)grid.Attribute("CanUserSortColumns"));
            Assert.Equal("36", (string?)grid.Attribute("RowHeight"));
            Assert.Equal("{StaticResource SmvMeasurementGrid}", (string?)grid.Attribute("Style"));
            var columnElement = grid.Elements().Single(x => x.Name.LocalName == "DataGrid.Columns");
            Assert.Equal(new[] { "Channel", "RMS", "Angle" },
                columnElement.Elements().Select(x => (string?)x.Attribute("Header")).ToArray());
        });
    }

    [Fact]
    public void GroupingKeepsOriginalEightRawValuesAndHonorsExplicit9_2LeOrder()
    {
        // RAW groups are visual-only; do not manufacture voltage or current scaling.
        for (var ch = 1; ch <= 8; ch++)
        {
            var raw = SmvEngineeringDisplay.Describe(ch, SmvDisplayUnits.RawCounts);
            Assert.Equal("counts", raw.Unit);
            Assert.Equal(ch <= 4, raw.IsVoltage);
            var configured = SmvEngineeringDisplay.Describe(ch, SmvDisplayUnits.NineTwoLe);
            Assert.Equal(ch >= 5, configured.IsVoltage);
            Assert.Equal(ch >= 5 ? "V" : "A", configured.Unit);
        }

        var code = Source("Views/SmvInlineViewer.xaml.cs");
        Assert.Contains("new ListCollectionView(Measurements)", code);
        Assert.Contains("SmvEngineeringDisplay.Describe(row.ChannelIndex, ActiveUnits).IsVoltage", code);
        Assert.Contains("!SmvEngineeringDisplay.Describe(row.ChannelIndex, ActiveUnits).IsVoltage", code);
        Assert.Contains("var next = SmvEngineeringDisplay.Present(raw, profile);", code);
        Assert.Contains("Measurements[rowIndex] = next;", code);
        Assert.Contains("Notify(nameof(VoltageGroupHeading))", code);
        Assert.Contains("Notify(nameof(CurrentGroupHeading))", code);
        Assert.Contains("grouping is unverified", code);
    }

    private static string Source(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, relative);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        throw new FileNotFoundException(relative);
    }
}
