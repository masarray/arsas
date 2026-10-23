using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class SignalSelectionGlobalSearchClearRegressionTests
{
    [Fact]
    public void GlobalSearchClear_UsesLucideVectorModernHitTargetAndHidesWhenEmpty()
    {
        var source = File.ReadAllText(FindRepoFile("SignalSelectionWizardWindow.xaml"));
        var document = XDocument.Parse(source);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var reusableStyle = document.Descendants(presentation + "Style")
            .Single(style => (string?)style.Attribute(x + "Key") == "GlobalSearchClearButton");
        var reusableSetters = reusableStyle.Elements(presentation + "Setter").ToArray();

        string? SetterValue(string property)
            => reusableSetters.Single(setter => (string?)setter.Attribute("Property") == property)
                .Attribute("Value")?.Value;

        Assert.Equal("28", SetterValue("Width"));
        Assert.Equal("28", SetterValue("Height"));
        Assert.Equal("Transparent", SetterValue("Background"));
        Assert.Equal("{x:Null}", SetterValue("FocusVisualStyle"));
        Assert.Equal("False", SetterValue("ClipToBounds"));

        var clearButton = document.Descendants(presentation + "Button")
            .Single(button => (string?)button.Attribute(x + "Name") == "GlobalSearchClearButton");

        Assert.Equal("ClearGlobalFilter_Click", (string?)clearButton.Attribute("Click"));
        Assert.Equal("Clear search", (string?)clearButton.Attribute("ToolTip"));
        Assert.Equal("Clear global search", (string?)clearButton.Attribute("AutomationProperties.Name"));
        Assert.Null(clearButton.Attribute("Content"));
        Assert.Null(clearButton.Attribute("Style"));

        var icon = clearButton.Descendants(presentation + "Path").Single();
        Assert.Equal("{StaticResource LucideX}", (string?)icon.Attribute("Data"));
        Assert.Equal("1.9", (string?)icon.Attribute("StrokeThickness"));

        var viewbox = clearButton.Descendants(presentation + "Viewbox").Single();
        Assert.Equal("14", (string?)viewbox.Attribute("Width"));
        Assert.Equal("14", (string?)viewbox.Attribute("Height"));

        var localStyle = clearButton.Element(presentation + "Button.Style")?
            .Element(presentation + "Style")
            ?? throw new Xunit.Sdk.XunitException("Global search clear button local style is missing.");
        Assert.Equal("{StaticResource GlobalSearchClearButton}", (string?)localStyle.Attribute("BasedOn"));

        var emptyTextTrigger = localStyle.Descendants(presentation + "DataTrigger")
            .Single(trigger => (string?)trigger.Attribute("Value") == string.Empty);
        Assert.Contains("ElementName=GlobalSearchTextBox", (string?)emptyTextTrigger.Attribute("Binding") ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(emptyTextTrigger.Elements(presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Collapsed");

        Assert.DoesNotContain("Content=\"×\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Style=\"{StaticResource MiniChipButton}\"", clearButton.ToString(), StringComparison.Ordinal);
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
