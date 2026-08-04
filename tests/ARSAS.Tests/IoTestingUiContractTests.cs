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
        Assert.Equal(
            "{Binding Storage.SnapshotPath}",
            document.Descendants(presentation + "TextBlock")
                .Select(text => (string?)text.Attribute("ToolTip"))
                .First(value => value == "{Binding Storage.SnapshotPath}"));
    }

    [Fact]
    public void IoTestingWindow_ConnectsWithoutLockingExplorerNavigationAndProtectsCompletedEvidence()
    {
        var document = XDocument.Load(FindRepoFile("IoListTestingWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var startButton = document
            .Descendants(presentation + "Button")
            .Single(button => ((string?)button.Attribute("Click")) == "StartSelectedIedSafely_Click");
        var text = document.ToString();

        Assert.Equal("{Binding SelectedStartWorkflowText}", (string?)startButton.Attribute("Content"));
        Assert.Equal("{Binding SelectedCanStartWorkflow}", (string?)startButton.Attribute("IsEnabled"));
        Assert.Contains("report-first acquisition", text, StringComparison.Ordinal);
        Assert.DoesNotContain("HeaderSpinnerRotate", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CardSpinnerRotate", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CardSpinner", text, StringComparison.Ordinal);
        Assert.Contains(
            document.Descendants(presentation + "ProgressBar"),
            progress => (string?)progress.Attribute("IsIndeterminate") == "True");

        var windowSource = File.ReadAllText(FindRepoFile("IoListTestingWindow.xaml.cs"));
        Assert.Contains("public bool CanSelectIed => true", windowSource, StringComparison.Ordinal);

        var contextSource = File.ReadAllText(FindRepoFile("IoListTestingWindow.ContextUx.cs"));
        Assert.Contains("PrepareIoTestIedForFatAsync", contextSource, StringComparison.Ordinal);
        Assert.Contains("var selectedIed = SelectedIed", contextSource, StringComparison.Ordinal);
        Assert.Contains("point.Runtime.IsComplete", contextSource, StringComparison.Ordinal);
        Assert.Contains("point.TestEnabled = false", contextSource, StringComparison.Ordinal);
        Assert.Contains("point.TestEnabled = true", contextSource, StringComparison.Ordinal);
        Assert.Contains("Session.Start(selectedIed)", contextSource, StringComparison.Ordinal);
        Assert.Contains("Retest completed evidence?", contextSource, StringComparison.Ordinal);
    }

    [Fact]
    public void IoTestingWindow_UsesSelectedIedContextAndWorkspacePreviewToggle()
    {
        var document = XDocument.Load(FindRepoFile("IoListTestingWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var text = document.ToString();

        var previewToggle = document
            .Descendants(presentation + "Button")
            .Single(button => (string?)button.Attribute(x + "Name") == "WorkspacePreviewToggle");
        Assert.Equal("TogglePrintPreview_Click", (string?)previewToggle.Attribute("Click"));
        Assert.Equal("Print Preview", (string?)previewToggle.Attribute("Content"));

        Assert.DoesNotContain("{Binding Session.StateText}", text, StringComparison.Ordinal);
        Assert.Contains("{Binding SelectedCanPause}", text, StringComparison.Ordinal);
        Assert.Contains("{Binding SelectedCanResume}", text, StringComparison.Ordinal);
        Assert.Contains("{Binding SelectedCanStop}", text, StringComparison.Ordinal);
        Assert.Contains("{Binding SelectedFooterStatusText}", text, StringComparison.Ordinal);
        Assert.Contains("{Binding SelectedProgressText}", text, StringComparison.Ordinal);
        Assert.Contains("{Binding SelectedEvidenceCount, Mode=OneWay}", text, StringComparison.Ordinal);

        var contextSource = File.ReadAllText(FindRepoFile("IoListTestingWindow.ContextUx.cs"));
        Assert.Contains("ReferenceEquals(Session.ActiveIed, SelectedIed)", contextSource, StringComparison.Ordinal);
        Assert.Contains("AdoptWorkspacePreviewToggle", contextSource, StringComparison.Ordinal);
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
        Assert.Contains("SettleIoFatReportPriorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("rebuilding the report plan once", source, StringComparison.Ordinal);
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
        Assert.Contains("✔ PASS", text, StringComparison.Ordinal);
        Assert.Contains("AllPassedVisibilityConverter", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Runtime.OnEvidence.CapturedAt", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Runtime.OffEvidence.CapturedAt", text, StringComparison.Ordinal);
    }

    [Fact]
    public void IedCards_UseReusableNumericalRelayFrontPanelInsteadOfCalculatorKeypad()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var resources = XDocument.Load(FindRepoFile("App.xaml"));
        var template = resources
            .Descendants(presentation + "ControlTemplate")
            .Single(node => (string?)node.Attribute(x + "Key") == "IedRelayFrontPanelTemplate");
        var namedParts = template
            .Descendants()
            .Select(node => (string?)node.Attribute(x + "Name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("RelayFasciaArtwork", namedParts);
        Assert.Contains("RelayStateRail", namedParts);
        Assert.Contains("Assets/ied-protection-relay-fascia.png", template.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(FindRepoFile("Assets/ied-protection-relay-fascia.png")));

        var project = File.ReadAllText(FindRepoFile("ArIED61850Tester.csproj"));
        Assert.Contains("Assets\\ied-protection-relay-fascia.png", project, StringComparison.Ordinal);

        var explorer = XDocument.Load(FindRepoFile("MainWindow.xaml"));
        var explorerIcon = explorer
            .Descendants(presentation + "Control")
            .Single(node => (string?)node.Attribute(x + "Name") == "RelayDeviceIcon");
        var explorerBadge = explorer
            .Descendants(presentation + "Border")
            .Single(node => (string?)node.Attribute(x + "Name") == "MonitorStateBadge");
        Assert.Empty(explorerIcon.Descendants(presentation + "DropShadowEffect"));
        Assert.Equal("StackPanel", explorerBadge.Parent?.Name.LocalName);
        Assert.Contains(
            explorerBadge.Parent!.Descendants(presentation + "Control"),
            node => (string?)node.Attribute(x + "Name") == "RelayDeviceIcon");

        var ioTesting = XDocument.Load(FindRepoFile("IoListTestingWindow.xaml"));
        var ioBadge = ioTesting
            .Descendants(presentation + "Border")
            .Single(node => (string?)node.Attribute(x + "Name") == "StateBadge");
        Assert.Equal("StackPanel", ioBadge.Parent?.Name.LocalName);
        Assert.Contains(
            ioBadge.Parent!.Descendants(presentation + "Control"),
            node => (string?)node.Attribute(x + "Name") == "RelayIcon");

        var explorerText = explorer.ToString();
        var ioTestingText = ioTesting.ToString();
        Assert.Contains("IedRelayFrontPanelTemplate", explorerText, StringComparison.Ordinal);
        Assert.Contains("IedRelayFrontPanelTemplate", ioTestingText, StringComparison.Ordinal);
        Assert.DoesNotContain("M 2 0 L 2 20", explorerText, StringComparison.Ordinal);
        Assert.DoesNotContain("M 2 0 L 2 20", ioTestingText, StringComparison.Ordinal);
    }

    [Fact]
    public void IoTestingWindow_UsesBalancedInitialGridWidthsAndCenteredRelayHeaders()
    {
        var document = XDocument.Load(FindRepoFile("IoListTestingWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var signal = FindColumn(document, presentation, "SIGNAL");
        var reference = FindColumn(document, presentation, "IEC REFERENCE");
        var acquisition = FindColumn(document, presentation, "ACQUISITION");
        var onTime = FindColumn(document, presentation, "ON · RELAY TIME");
        var offTime = FindColumn(document, presentation, "OFF · RELAY TIME");

        Assert.Equal("1.55*", (string?)signal.Attribute("Width"));
        Assert.Equal("1.85*", (string?)reference.Attribute("Width"));
        Assert.Equal("136", (string?)acquisition.Attribute("Width"));
        Assert.Equal("168", (string?)onTime.Attribute("Width"));
        Assert.Equal("168", (string?)offTime.Attribute("Width"));
        Assert.Equal("{StaticResource CenteredFatGridHeader}", (string?)onTime.Attribute("HeaderStyle"));
        Assert.Equal("{StaticResource CenteredFatGridHeader}", (string?)offTime.Attribute("HeaderStyle"));
    }

    [Fact]
    public void IoTestingWindow_UsesFlatOperationalColumnsAndFinalResultOnly()
    {
        var document = XDocument.Load(FindRepoFile("IoListTestingWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var live = FindColumn(document, presentation, "LIVE");
        var acquisition = FindColumn(document, presentation, "ACQUISITION");
        var status = FindColumn(document, presentation, "STATUS");
        var result = FindColumn(document, presentation, "RESULT");

        Assert.Empty(live.Descendants(presentation + "Border"));
        Assert.Empty(acquisition.Descendants(presentation + "Border"));
        Assert.Empty(result.Descendants(presentation + "Border"));

        Assert.Contains("Runtime.StateText", status.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Runtime.StateText", result.ToString(), StringComparison.Ordinal);
        Assert.Contains("✔ PASS", result.ToString(), StringComparison.Ordinal);
        Assert.Contains("✖ FAILED", result.ToString(), StringComparison.Ordinal);
        Assert.Contains("⚠ REVIEW", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Ready for ON", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius", result.ToString(), StringComparison.Ordinal);
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

    private static XElement FindColumn(XDocument document, XNamespace presentation, string header)
        => document
            .Descendants(presentation + "DataGridTemplateColumn")
            .Single(column => string.Equals((string?)column.Attribute("Header"), header, StringComparison.Ordinal));

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
