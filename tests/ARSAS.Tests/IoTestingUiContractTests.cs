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
    public void IoTestingWindow_ExposesCalmExportActionsWithoutPathNoise()
    {
        var document = XDocument.Load(FindRepoFile("IoListTestingWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var buttonContents = document
            .Descendants(presentation + "Button")
            .Select(button => (string?)button.Attribute("Content"))
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .Cast<string>()
            .ToList();

        Assert.Contains("Save", buttonContents);
        Assert.Contains("Excel", buttonContents);
        Assert.Contains("PDF", buttonContents);
        Assert.Contains("Export .arsas", buttonContents);
        Assert.Contains("Engineering", buttonContents);
        Assert.DoesNotContain("Autosave enabled", document.ToString(), StringComparison.Ordinal);
        Assert.Equal("{Binding Storage.SnapshotPath}",
            document.Descendants(presentation + "TextBlock")
                .Select(text => (string?)text.Attribute("ToolTip"))
                .First(value => value == "{Binding Storage.SnapshotPath}"));
    }

    [Fact]
    public void IoTestingWindow_ConnectsWithoutLockingExplorerNavigation()
    {
        var document = XDocument.Load(FindRepoFile("IoListTestingWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var startButton = document
            .Descendants(presentation + "Button")
            .Single(button => ((string?)button.Attribute("Click")) == "StartSession_Click");

        Assert.Equal("{Binding StartWorkflowText}", (string?)startButton.Attribute("Content"));
        Assert.Equal("{Binding CanStartWorkflow}", (string?)startButton.Attribute("IsEnabled"));
        Assert.Contains("report-first acquisition", document.ToString(), StringComparison.Ordinal);
        Assert.Contains("HeaderSpinnerRotate", document.ToString(), StringComparison.Ordinal);
        Assert.Contains("CardSpinnerRotate", document.ToString(), StringComparison.Ordinal);

        var windowSource = File.ReadAllText(FindRepoFile("IoListTestingWindow.xaml.cs"));
        Assert.Contains("Connect & Start IED", windowSource, StringComparison.Ordinal);
        Assert.Contains("PrepareIoTestIedForFatAsync", windowSource, StringComparison.Ordinal);
        Assert.Contains("public bool CanSelectIed => true", windowSource, StringComparison.Ordinal);
        Assert.Contains("var selectedIed = SelectedIed", windowSource, StringComparison.Ordinal);
        Assert.Contains("Session.Start(selectedIed)", windowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void IoFatAutomaticPreparation_UsesSmartReportingWithoutProcessControls()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.AutoConnect.cs"));

        Assert.Contains("AllowDynamicDataSetWrites = true", source, StringComparison.Ordinal);
        Assert.Contains("ConnectAndConfigureDeviceAsync", source, StringComparison.Ordinal);
        Assert.Contains("StartDeviceMonitorAsync", source, StringComparison.Ordinal);
        Assert.Contains("configured RCB", source, StringComparison.Ordinal);
        Assert.Contains("dynamic DataSet/URCB", source, StringComparison.Ordinal);
        Assert.Contains("bounded MMS verification/fallback", source, StringComparison.Ordinal);
        Assert.Contains("WaitForIoFatAcquisitionAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteControlAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InspectControlAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IoTestingWindow_UsesRelayTimestampEvidenceAndPremiumGridDensity()
    {
        var document = XDocument.Load(FindRepoFile("IoListTestingWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var text = document.ToString();

        Assert.Contains("Runtime.OnRelayTimestampText", text, StringComparison.Ordinal);
        Assert.Contains("Runtime.OffRelayTimestampText", text, StringComparison.Ordinal);
        Assert.Contains("ON · RELAY TIME", text, StringComparison.Ordinal);
        Assert.Contains("OFF · RELAY TIME", text, StringComparison.Ordinal);
        Assert.Contains(
            document.Descendants(presentation + "Setter"),
            setter => (string?)setter.Attribute("Property") == "MinHeight" &&
                      (string?)setter.Attribute("Value") == "48");
        Assert.Contains("RelayIcon", text, StringComparison.Ordinal);
        Assert.Contains("CardStateText", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Runtime.OnEvidence.CapturedAt", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Runtime.OffEvidence.CapturedAt", text, StringComparison.Ordinal);
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
