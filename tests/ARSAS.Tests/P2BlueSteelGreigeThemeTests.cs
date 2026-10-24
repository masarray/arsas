using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class P2BlueSteelGreigeThemeTests
{
    [Fact]
    public void Theme_UsesBlueSteelShellAndCoolBlueZebraRows()
    {
        var source = File.ReadAllText(FindRepoFile("Resources/P2BlueSteelGreige.xaml"));
        var document = XDocument.Parse(source);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        string ResourceValue(string elementName, string key, string attribute)
            => document.Descendants(presentation + elementName)
                .Single(node => (string?)node.Attribute(x + "Key") == key)
                .Attribute(attribute)?.Value
                ?? throw new Xunit.Sdk.XunitException($"Missing {key}.{attribute}");

        Assert.Equal("#2A465B", ResourceValue("SolidColorBrush", "BlueSteelNavSurface", "Color"));
        Assert.Equal("#F8FBFF", document.Descendants(presentation + "Color")
            .Single(node => (string?)node.Attribute(x + "Key") == "CoolPanelColor").Value);
        Assert.Equal("#EDF5FF", document.Descendants(presentation + "Color")
            .Single(node => (string?)node.Attribute(x + "Key") == "CoolAlternateColor").Value);
        Assert.Equal("#E8F2FF", document.Descendants(presentation + "Color")
            .Single(node => (string?)node.Attribute(x + "Key") == "CoolHeaderColor").Value);
        Assert.Equal("{StaticResource CoolPanelColor}", ResourceValue("SolidColorBrush", "GreigeRowA", "Color"));
        Assert.Equal("{StaticResource CoolAlternateColor}", ResourceValue("SolidColorBrush", "GreigeRowB", "Color"));
        Assert.Equal("{StaticResource CoolHeaderColor}", ResourceValue("SolidColorBrush", "GreigeHeader", "Color"));

        var appBackground = document.Descendants(presentation + "LinearGradientBrush")
            .Single(node => (string?)node.Attribute(x + "Key") == "AppBackgroundGradient")
            .ToString();
        Assert.Contains("#1E394E", appBackground, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#2B4A60", appBackground, StringComparison.OrdinalIgnoreCase);

        var modernGrid = document.Descendants(presentation + "Style")
            .Single(node => (string?)node.Attribute(x + "Key") == "ModernDataGrid")
            .ToString();
        Assert.Contains("RowBackground", modernGrid, StringComparison.Ordinal);
        Assert.Contains("GridRowPrimary", modernGrid, StringComparison.Ordinal);
        Assert.Contains("AlternatingRowBackground", modernGrid, StringComparison.Ordinal);
        Assert.Contains("GridRowAlternate", modernGrid, StringComparison.Ordinal);
        Assert.Contains("AlternationCount", modernGrid, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_InstallsThemeBeforeRelayFasciaAndAppliesWindowAdapter()
    {
        var app = File.ReadAllText(FindRepoFile("App.xaml.cs"));

        var themeIndex = app.IndexOf("InstallP2BlueSteelGreigeTheme();", StringComparison.Ordinal);
        var fasciaIndex = app.IndexOf("InstallArvrelMiniIedFascia();", StringComparison.Ordinal);
        Assert.True(themeIndex >= 0, "P2 theme install call is missing.");
        Assert.True(fasciaIndex > themeIndex, "P2 theme must be installed before the fascia template.");
        Assert.Contains("/ARSAS;component/Resources/P2BlueSteelGreige.xaml", app, StringComparison.Ordinal);
        Assert.Contains("P2BlueSteelGreigeUx.ApplyToOpenWindows(this);", app, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeAdapter_PreservesWindowGeometryAndBlueGridRows()
    {
        var source = File.ReadAllText(FindRepoFile("P2BlueSteelGreigeUx.cs"));

        Assert.DoesNotContain("WindowState.Maximized", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyEngineeringGrid", source, StringComparison.Ordinal);
        Assert.Contains("P2MainIedListItemStyle", source, StringComparison.Ordinal);
        Assert.Contains("P2FatIedListItemStyle", source, StringComparison.Ordinal);
        Assert.Contains("P2FatDataGridRow", source, StringComparison.Ordinal);
        Assert.Contains("GridRowPrimary", source, StringComparison.Ordinal);
        Assert.Contains("GridRowAlternate", source, StringComparison.Ordinal);
        Assert.Contains("AlternationCount = 2", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RelayFascia_UsesOperatorSuppliedSvgVectorAndKeepsSemanticStateRail()
    {
        var source = File.ReadAllText(FindRepoFile("Resources/ArvrelMiniIedFascia.xaml"));
        var svg = File.ReadAllText(FindRepoFile("Assets/RelayFascia.svg"));

        Assert.Contains("ArsasIedConnectionLed", source, StringComparison.Ordinal);
        Assert.Contains("#FF5538", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#2DE57A", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{TemplateBinding Foreground}", source, StringComparison.Ordinal);
        Assert.Contains("RelayStateRail", source, StringComparison.Ordinal);
        Assert.Contains("width=\"256\"", svg, StringComparison.Ordinal);
        Assert.Contains("id=\"ledRed\"", svg, StringComparison.Ordinal);
        Assert.Contains("id=\"ledGreen\"", svg, StringComparison.Ordinal);
    }

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

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
