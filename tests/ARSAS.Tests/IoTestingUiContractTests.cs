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
    public void IoTestingLauncher_UsesArsasProjectAndNativePdfWording()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.cs"));

        Assert.Contains("InstallFirstRunTestingChoices", source, StringComparison.Ordinal);
        Assert.Contains("GENERAL IEC 61850 TESTING", source, StringComparison.Ordinal);
        Assert.Contains("FAT / IO LIST TESTING", source, StringComparison.Ordinal);
        Assert.Contains("Open IO List Workbook", source, StringComparison.Ordinal);
        Assert.Contains("Open ARSAS Project", source, StringComparison.Ordinal);
        Assert.Contains("IoFatProjectPackageService.OpenDialogFilter", source, StringComparison.Ordinal);
        Assert.Contains("native PDF report", source, StringComparison.Ordinal);
        Assert.DoesNotContain("printable browser report", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actionPanel.Children.Insert", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InstallIoListTestingLauncher", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IoTestingWindow_ExposesAutosaveExcelPdfAndArsasProjectActions()
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
        Assert.Contains("Export Excel", buttonContents);
        Assert.Contains("Export PDF", buttonContents);
        Assert.Contains("Export .arsas", buttonContents);
        Assert.Contains(
            document.Descendants(presentation + "TextBlock"),
            text => ((string?)text.Attribute("Text"))?.Contains("Autosave enabled", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void IoFatPackageService_UsesShortExtensionAndBundlesNativeReports()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatProjectPackageService.cs"));

        Assert.Contains("PackageExtension = \".arsas\"", source, StringComparison.Ordinal);
        Assert.Contains("LegacyPackageExtension = \".arsas-iofat\"", source, StringComparison.Ordinal);
        Assert.Contains("report/IO-FAT-Report.pdf", source, StringComparison.Ordinal);
        Assert.Contains("report/IO-FAT-Results.xlsx", source, StringComparison.Ordinal);
        Assert.Contains("reportSha256", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resultWorkbookSha256", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IO-FAT-Report.html", source, StringComparison.Ordinal);
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
