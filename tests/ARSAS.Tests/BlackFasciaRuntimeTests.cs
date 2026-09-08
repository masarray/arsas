using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class BlackFasciaRuntimeTests
{
    [Fact]
    public void RuntimeFascia_MirrorsBlackSvgLedIdsAndKeepsThemVisibleAtCardScale()
    {
        var document = XDocument.Load(FindRepoFile("Resources/ArvrelMiniIedFascia.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var template = document
            .Descendants(presentation + "ControlTemplate")
            .Single(node => (string?)node.Attribute(x + "Key") == "ArvrelMiniIedRelayFrontPanelTemplate");

        foreach (var ledName in new[] { "LED1", "LED2", "LED3" })
        {
            var led = template
                .Descendants(presentation + "Ellipse")
                .Single(node => (string?)node.Attribute(x + "Name") == ledName);
            Assert.Equal("{StaticResource ArsasIedConnectionLed}", (string?)led.Attribute("Style"));
        }

        var ledStyle = document
            .Descendants(presentation + "Style")
            .Single(node => (string?)node.Attribute(x + "Key") == "ArsasIedConnectionLed");
        var setters = ledStyle
            .Elements(presentation + "Setter")
            .ToDictionary(
                node => (string?)node.Attribute("Property") ?? string.Empty,
                node => (string?)node.Attribute("Value") ?? string.Empty,
                StringComparer.Ordinal);

        Assert.Equal("22.4", setters["Width"]);
        Assert.Equal("18.4", setters["Height"]);

        var source = document.ToString();
        Assert.Contains("black-fascia-ied.svg", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{Binding IsConnected}", source, StringComparison.Ordinal);
        Assert.Contains("{Binding IsLiveConnected}", source, StringComparison.Ordinal);
        Assert.Contains("#FF5538", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#55FF79", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#2DE57A", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BlackFasciaSvg_DeclaresThreeOperatorLedIds()
    {
        var source = File.ReadAllText(FindRepoFile("Assets/black-fascia-ied.svg"));

        Assert.Contains("id=\"LED1\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"LED2\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"LED3\"", source, StringComparison.Ordinal);
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
