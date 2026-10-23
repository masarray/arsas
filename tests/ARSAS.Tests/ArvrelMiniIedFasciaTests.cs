using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class ArvrelMiniIedFasciaTests
{
    [Fact]
    public void CompactArvrelFascia_IsVectorOnlyAndKeepsRecognizableRelayHardware()
    {
        var document = XDocument.Load(FindRepoFile("Resources/ArvrelMiniIedFascia.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var template = document
            .Descendants(presentation + "ControlTemplate")
            .Single(node =>
                (string?)node.Attribute(x + "Key") == "ArvrelMiniIedRelayFrontPanelTemplate");

        var root = template.Elements(presentation + "Grid").Single();
        Assert.Equal("50", (string?)root.Attribute("Width"));
        Assert.Equal("50", (string?)root.Attribute("Height"));

        var namedParts = template
            .Descendants()
            .Select(node => (string?)node.Attribute(x + "Name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("RelayStateRail", namedParts);

        // The operator-supplied SVG is transcribed to native WPF vector primitives so no
        // raster/image dependency is introduced at card scale.
        Assert.Empty(template.Descendants(presentation + "Image"));
        Assert.NotEmpty(template.Descendants(presentation + "TextBlock"));
        Assert.NotEmpty(template.Descendants(presentation + "Rectangle"));
        Assert.NotEmpty(template.Descendants(presentation + "Ellipse"));
        Assert.NotEmpty(template.Descendants(presentation + "Line"));
        Assert.NotEmpty(template.Descendants(presentation + "Path"));

        var templateText = template.ToString();
        var documentText = document.ToString();
        Assert.Contains("{TemplateBinding Foreground}", templateText, StringComparison.Ordinal);
        Assert.Contains("ArsasIedConnectionLed", documentText, StringComparison.Ordinal);
        Assert.Contains("{Binding IsMonitoring}", documentText, StringComparison.Ordinal);
        Assert.Contains("{Binding IsLiveConnected}", documentText, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding IsConnected}", documentText, StringComparison.Ordinal);
        Assert.Contains("#FF5538", documentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#2DE57A", documentText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppStartup_ReplacesLegacyCardTemplateWithPackagedArvrelVector()
    {
        var source = File.ReadAllText(FindRepoFile("App.xaml.cs"));

        Assert.Contains("InstallArvrelMiniIedFascia();", source, StringComparison.Ordinal);
        Assert.Contains(
            "/ARSAS;component/Resources/ArvrelMiniIedFascia.xaml",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "fasciaResources[\"ArvrelMiniIedRelayFrontPanelTemplate\"]",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Resources[\"IedRelayFrontPanelTemplate\"] = template;",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExplorerAndIoFatCards_ContinueToUseOneReusableTemplateKey()
    {
        var explorer = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        var ioFat = File.ReadAllText(FindRepoFile("IoListTestingWindow.xaml"));

        Assert.Contains("Template=\"{StaticResource IedRelayFrontPanelTemplate}\"", explorer, StringComparison.Ordinal);
        Assert.Contains("Template=\"{StaticResource IedRelayFrontPanelTemplate}\"", ioFat, StringComparison.Ordinal);
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
