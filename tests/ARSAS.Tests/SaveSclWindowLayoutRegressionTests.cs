using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class SaveSclWindowLayoutRegressionTests
{
    [Fact]
    public void SaveSclDialog_SizesToContentAndReservesFooterShadowSafeArea()
    {
        var source = File.ReadAllText(FindRepoFile("SaveSclWindow.xaml"));
        var document = XDocument.Parse(source);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var window = document.Root
            ?? throw new Xunit.Sdk.XunitException("SaveSclWindow root element is missing.");

        Assert.Equal("Height", (string?)window.Attribute("SizeToContent"));
        Assert.Null(window.Attribute("Height"));

        var rootGrid = window.Elements(presentation + "Grid").Single();
        Assert.Equal("20,20,20,28", (string?)rootGrid.Attribute("Margin"));

        var rows = rootGrid.Element(presentation + "Grid.RowDefinitions")?
            .Elements(presentation + "RowDefinition")
            .ToArray()
            ?? [];

        Assert.Equal(2, rows.Length);
        Assert.All(rows, row => Assert.Equal("Auto", (string?)row.Attribute("Height")));

        var footer = rootGrid.Elements(presentation + "StackPanel")
            .Single(panel => (string?)panel.Attribute("Grid.Row") == "1");
        Assert.Equal("0,16,0,0", (string?)footer.Attribute("Margin"));
        Assert.Equal("Right", (string?)footer.Attribute("HorizontalAlignment"));
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
