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

        Assert.Contains("ArvrelMiniShell", namedParts);
        Assert.Contains("ArvrelMiniLedBank", namedParts);
        Assert.Contains("ArvrelMiniLcd", namedParts);
        Assert.Contains("ArvrelMiniKeypad", namedParts);
        Assert.Contains("RelayStateRail", namedParts);

        Assert.Empty(template.Descendants(presentation + "Image"));
        Assert.Empty(template.Descendants(presentation + "TextBlock"));
        Assert.Contains("{TemplateBinding Foreground}", template.ToString(), StringComparison.Ordinal);
        Assert.Contains("#2E6F9E", document.ToString(), StringComparison.OrdinalIgnoreCase);
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
