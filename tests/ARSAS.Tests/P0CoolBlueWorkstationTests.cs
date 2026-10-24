using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// P0 visual contract: one cool-blue source across engineering grids, and no
/// automatic main-window maximization after the first frame.
/// </summary>
public sealed class P0CoolBlueWorkstationTests
{
    [Fact]
    public void AllEngineeringGridsShareCoolBlueSemanticResources()
    {
        var xaml = XDocument.Parse(Read("Resources/P2BlueSteelGreige.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var colors = new Dictionary<string, string>
        {
            ["CoolPanelColor"] = "#F8FBFF",
            ["CoolAlternateColor"] = "#EDF5FF",
            ["CoolHeaderColor"] = "#E8F2FF",
            ["CoolBorderColor"] = "#D7E5F6",
            ["CoolHoverColor"] = "#E4F0FF"
        };
        foreach (var (name, color) in colors)
            Assert.Equal(color, xaml.Descendants(wpf + "Color")
                .Single(e => (string?)e.Attribute(x + "Key") == name).Value);

        var modern = xaml.Descendants(wpf + "Style")
            .Single(e => (string?)e.Attribute(x + "Key") == "ModernDataGrid");
        Assert.Contains(modern.Elements(wpf + "Setter"), e =>
            (string?)e.Attribute("Property") == "RowBackground" &&
            (string?)e.Attribute("Value") == "{StaticResource GridRowPrimary}");
        Assert.Contains(modern.Elements(wpf + "Setter"), e =>
            (string?)e.Attribute("Property") == "AlternatingRowBackground" &&
            (string?)e.Attribute("Value") == "{StaticResource GridRowAlternate}");

        var industrial = XDocument.Parse(Read("Resources/P2IndustrialControls.xaml"));
        var header = industrial.Descendants(wpf + "Style")
            .Single(e => (string?)e.Attribute(x + "Key") == "IndustrialGridHeader");
        Assert.Contains(header.Elements(wpf + "Setter"), e =>
            (string?)e.Attribute("Property") == "Background" &&
            (string?)e.Attribute("Value") == "{DynamicResource GridHeaderSurface}");
    }

    [Fact]
    public void WindowGeometryIsUserControlledAfterCenteredNormalStartup()
    {
        foreach (var path in new[] { "MainWindow.xaml", "IoListTestingWindow.xaml" })
        {
            var root = XDocument.Parse(Read(path)).Root!;
            Assert.Equal("Normal", (string?)root.Attribute("WindowState"));
            Assert.InRange(double.Parse(root.Attribute("MinHeight")!.Value), 600d, 700d);
        }
        var main = XDocument.Parse(Read("MainWindow.xaml")).Root!;
        Assert.Equal("CenterScreen", (string?)main.Attribute("WindowStartupLocation"));

        var adapter = Read("P2BlueSteelGreigeUx.cs");
        Assert.DoesNotContain("WindowState.Maximized", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyEngineeringGrid", adapter, StringComparison.Ordinal);

        var startup = Read("WorkstationWindowStartupBounds.cs");
        Assert.Contains("SystemParameters.WorkArea", startup, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation.Manual", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeChanged +=", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("Activated +=", startup, StringComparison.Ordinal);
        Assert.Contains("WorkstationWindowStartupBounds.Apply(window);", Read("App.xaml.cs"), StringComparison.Ordinal);
    }

    private static string Read(string path)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var full = Path.Combine(directory.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        throw new FileNotFoundException(path);
    }
}
