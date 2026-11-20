using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class ProductionFatEngineeringTabRegressionTests
{
    [Fact]
    public void ProductionFatTab_NormalEntryHasNoLegacyProjectionBootstrapModule()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatTab.cs"));
        var repoRoot = FindRepoRoot();

        Assert.False(
            File.Exists(Path.Combine(repoRoot, "MainWindow.ProductionFatEngineeringBootstrap.cs")),
            "P5 removes the automatic Engineering -> legacy IoTest bootstrap module from normal FAT navigation.");
        Assert.False(
            File.Exists(Path.Combine(repoRoot, "Services", "IoTesting", "IoFatEngineeringWorkspaceProjectionService.cs")),
            "P5 removes the obsolete Engineering -> IoTest projection service rather than leaving a dormant second-row authority.");

        Assert.Contains("NativeFatTab.Content = BuildProductionFatPermanentHost();", source, StringComparison.Ordinal);
        Assert.Contains("SynchronizeProductionFatSelectedIed();", source, StringComparison.Ordinal);
        Assert.Contains("BindNativeFatCanonicalRows();", source, StringComparison.Ordinal);

        foreach (var forbidden in new[]
                 {
                     "QueueProductionFatEngineeringBootstrap",
                     "EnsureProductionFatFromEngineeringAsync",
                     "IoFatEngineeringWorkspaceProjectionService",
                     "IoTestWorkspaceBootstrapService",
                     "OpenDescribedSourcesAsync",
                     "ShowIoTestingWorkspaceAsync",
                     "ShowProductionFatBootstrapState"
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }

        // Explicit/manual compatibility remains a deliberate operator boundary only.
        Assert.Contains("MountProductionFatWorkspace", source, StringComparison.Ordinal);
        Assert.Contains("FAT compatibility workspace", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SeventhFatDestination_IsCanonicalMainWindowSiblingWithOneNavigationOwner()
    {
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        var mainSource = File.ReadAllText(FindRepoFile("MainWindow.xaml.cs"));
        var bridgeSource = File.ReadAllText(FindRepoFile("MainWindow.NativeFatWorkspace.cs"));
        var productionSource = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatTab.cs"));
        var repoRoot = FindRepoRoot();

        // Navigation identity is unchanged; glyph and caption are children now.
        var document = XDocument.Parse(xaml);
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var fatButton = Assert.Single(document.Descendants(p + "Button"),
            button => (string?)button.Attribute(x + "Name") == "NavNativeFatButton");
        Assert.Equal("6", (string?)fatButton.Attribute("Grid.Column"));
        Assert.Equal("6", (string?)fatButton.Attribute("Tag"));
        Assert.Equal("NavButton_Click", (string?)fatButton.Attribute("Click"));
        Assert.Equal("{StaticResource SegmentedNavButton}", (string?)fatButton.Attribute("Style"));
        Assert.Contains(fatButton.Descendants(p + "TextBlock"),
            label => (string?)label.Attribute("Text") == "Signal Testing");
        Assert.Contains(fatButton.Descendants(p + "Path"),
            path => (string?)path.Attribute("Data") == "{StaticResource OperatorFatIcon}");
        Assert.Contains("Grid.ColumnSpan=\"8\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NativeFatTab\" Header=\"FAT\"", xaml, StringComparison.Ordinal);

        Assert.Contains("EngineeringNavigationRoute.TabForButton(index)", mainSource, StringComparison.Ordinal);
        Assert.Contains("var cellWidth = contentWidth / 8d", mainSource, StringComparison.Ordinal);
        Assert.Contains("NavNativeFatButton", mainSource, StringComparison.Ordinal);

        Assert.Contains("private const int NativeFatWorkspaceIndex = 6", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("QueueNativeFatNavigationGeometry", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeFatTab", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeFatNavButton", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildNativeFatWorkspaceContent", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DataGrid", bridgeSource, StringComparison.Ordinal);

        Assert.Contains("NativeFatTab.Content = BuildProductionFatPermanentHost();", productionSource, StringComparison.Ordinal);
        Assert.Contains("NavNativeFatButton.ToolTip", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductionFatNavButton_Click", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeFatInstalled", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeFatTab", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeFatNavButton", productionSource, StringComparison.Ordinal);

        Assert.False(
            File.Exists(Path.Combine(repoRoot, "MainWindow.ProductionFatNavigationParity.cs")),
            "Do not reintroduce a second ApplicationIdle navigation parity owner. MainWindow is the seven-slot navigation authority.");
    }

    [Fact]
    public void ExistingWorkspaceSelectionSideEffects_RemainProtectedWhileAddingFat()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.xaml.cs"));

        Assert.Contains("device.ClearUnreadEvents()", source, StringComparison.Ordinal);
        Assert.Contains("ActivateGooseSubscriberWorkspace()", source, StringComparison.Ordinal);
        Assert.Contains("ClearDiagnosticAlert()", source, StringComparison.Ordinal);
        Assert.Contains("UpdateNavigationVisuals(MainTabs.SelectedIndex, animate: true)", source, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
        => Path.Combine(FindRepoRoot(), relativePath);

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests", "ARSAS.Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repository root from '{AppContext.BaseDirectory}'.");
    }
}
