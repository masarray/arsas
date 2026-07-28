using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class IoTestingUiContractTests
{
    [Fact]
    public void IoListTestingWindow_ReadOnlyRunBindingsAreExplicitlyOneWay()
    {
        var document = XDocument.Load(FindRepoFile("IoListTestingWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var boundRuns = document
            .Descendants(presentation + "Run")
            .Select(run => (string?)run.Attribute("Text"))
            .Where(text => text?.Contains("{Binding", StringComparison.Ordinal) == true)
            .Cast<string>()
            .ToList();

        Assert.NotEmpty(boundRuns);
        Assert.All(
            boundRuns,
            binding => Assert.Contains("Mode=OneWay", binding, StringComparison.Ordinal));
    }

    [Fact]
    public void IoTestingLauncher_UsesFirstRunChoiceCardsInsteadOfHeaderInjection()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.cs"));

        Assert.Contains("InstallFirstRunTestingChoices", source, StringComparison.Ordinal);
        Assert.Contains("GENERAL IEC 61850 TESTING", source, StringComparison.Ordinal);
        Assert.Contains("FAT / IO LIST TESTING", source, StringComparison.Ordinal);
        Assert.Contains("Open IO List Workbook", source, StringComparison.Ordinal);
        Assert.Contains("Open FAT Handover Package", source, StringComparison.Ordinal);
        Assert.Contains("IoTestWorkspaceBootstrapService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("actionPanel.Children.Insert", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InstallIoListTestingLauncher", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IoTestingWindow_ExposesAutosaveAndPortableHandoverActions()
    {
        var document = XDocument.Load(FindRepoFile("IoListTestingWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var buttonContents = document
            .Descendants(presentation + "Button")
            .Select(button => (string?)button.Attribute("Content"))
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .Cast<string>()
            .ToList();

        Assert.Contains("Save Progress", buttonContents);
        Assert.Contains("Export Handover", buttonContents);
        Assert.Contains(
            document.Descendants(presentation + "TextBlock"),
            text => ((string?)text.Attribute("Text"))?.Contains("Autosave enabled", StringComparison.Ordinal) == true);
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
