using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class SaveSclEditionToggleClippingRegressionTests
{
    [Fact]
    public void EditionToggle_ReservesInternalVerticalSafeAreaAndUsesInternalFocusCue()
    {
        var source = File.ReadAllText(FindRepoFile("SaveSclWindow.xaml"));
        var document = XDocument.Parse(source);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var indicator = document.Descendants(presentation + "Border")
            .Single(node => (string?)node.Attribute(x + "Name") == "EditionIndicator");

        Assert.Equal("60", (string?)indicator.Attribute("Height"));
        Assert.Equal("5,6", (string?)indicator.Attribute("Padding"));
        Assert.Equal("False", (string?)indicator.Attribute("ClipToBounds"));

        var style = document.Descendants(presentation + "Style")
            .Single(node => (string?)node.Attribute(x + "Key") == "EditionToggleButton");
        var setters = style.Elements(presentation + "Setter").ToArray();

        string? SetterValue(string property)
            => setters.Single(node => (string?)node.Attribute("Property") == property)
                .Attribute("Value")?.Value;

        Assert.Equal("44", SetterValue("Height"));
        Assert.Equal("Center", SetterValue("VerticalAlignment"));
        Assert.Equal("{x:Null}", SetterValue("FocusVisualStyle"));
        Assert.Equal("False", SetterValue("ClipToBounds"));

        // 60 DIP shell with 6 DIP top/bottom padding leaves intentional breathing room
        // around the 44 DIP toggle instead of the old exact 4 + 44 + 4 = 52 fit.
        Assert.True(60 - (6 + 6) > 44);
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
