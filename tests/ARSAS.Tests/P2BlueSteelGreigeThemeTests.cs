using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class P2BlueSteelGreigeThemeTests
{
    [Fact]
    public void Theme_UsesBlueSteelShellAndWarmGreigeZebraRows()
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
        Assert.Equal("#F3F0EA", ResourceValue("SolidColorBrush", "GreigeRowA", "Color"));
        Assert.Equal("#ECE8E1", ResourceValue("SolidColorBrush", "GreigeRowB", "Color"));
        Assert.Equal("#E1DDD5", ResourceValue("SolidColorBrush", "GreigeHeader", "Color"));

        var appBackground = document.Descendants(presentation + "LinearGradientBrush")
            .Single(node => (string?)node.Attribute(x + "Key") == "AppBackgroundGradient")
            .ToString();
        Assert.Contains("#1E394E", appBackground, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#2B4A60", appBackground, StringComparison.OrdinalIgnoreCase);

        var modernGrid = document.Descendants(presentation + "Style")
            .Single(node => (string?)node.Attribute(x + "Key") == "ModernDataGrid")
            .ToString();
        Assert.Contains("RowBackground", modernGrid, StringComparison.Ordinal);
        Assert.Contains("GreigeRowA", modernGrid, StringComparison.Ordinal);
        Assert.Contains("AlternatingRowBackground", modernGrid, StringComparison.Ordinal);
        Assert.Contains("GreigeRowB", modernGrid, StringComparison.Ordinal);
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
    public void RuntimeAdapter_KeepsMaximizedWorkstationAndFatZebraRows()
    {
        var source = File.ReadAllText(FindRepoFile("P2BlueSteelGreigeUx.cs"));

        Assert.Contains("window.WindowState = WindowState.Maximized", source, StringComparison.Ordinal);
        Assert.Contains("P2MainIedListItemStyle", source, StringComparison.Ordinal);
        Assert.Contains("P2FatIedListItemStyle", source, StringComparison.Ordinal);
        Assert.Contains("P2FatDataGridRow", source, StringComparison.Ordinal);
        Assert.Contains("GreigeRowA", source, StringComparison.Ordinal);
        Assert.Contains("GreigeRowB", source, StringComparison.Ordinal);
        Assert.Contains("AlternationCount = 2", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RelayFascia_IsLightBlueSteelInsteadOfNearBlack()
    {
        var source = File.ReadAllText(FindRepoFile("Resources/ArvrelMiniIedFascia.xaml"));

        Assert.Contains("#A9B5BA", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#87979F", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#EDF3EE", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#1B2328", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#0A1013", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{TemplateBinding Foreground}", source, StringComparison.Ordinal);
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