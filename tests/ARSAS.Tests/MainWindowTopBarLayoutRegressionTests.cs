namespace ARSAS.Tests;

public sealed class MainWindowTopBarLayoutRegressionTests
{
    [Fact]
    public void NavigationLayout_UsesResponsiveWidthsAndFullWideLabels()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NavigationLayoutFix.cs"));

        Assert.Contains("WideNavWidth = 840d", source, StringComparison.Ordinal);
        Assert.Contains("MediumNavWidth = 720d", source, StringComparison.Ordinal);
        Assert.Contains("CompactNavWidth = 580d", source, StringComparison.Ordinal);
        Assert.Contains("\"IEC 61850 Explorer\"", source, StringComparison.Ordinal);
        Assert.Contains("\"GOOSE Subscriber\"", source, StringComparison.Ordinal);
        Assert.Contains("var labels = wide ? FullLabels : CompactLabels", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionPill_IsDerivedFromActualNavCellWidth_NotFixed150Pixels()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NavigationLayoutFix.cs"));

        Assert.Contains("var cellWidth = contentWidth / 5d", source, StringComparison.Ordinal);
        Assert.Contains("* cellWidth", source, StringComparison.Ordinal);
        Assert.Contains("pill.Width = Math.Max(1d, cellWidth - 2d)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("* 150d", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsiveCorrection_RunsAfterLegacySelectionAnimationAndOnWindowResize()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NavigationLayoutFix.cs"));

        Assert.Contains("window.SizeChanged += MainWindow_SizeChanged", source, StringComparison.Ordinal);
        Assert.Contains("tabs.SelectionChanged += MainTabs_SelectionChanged", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Loaded", source, StringComparison.Ordinal);
        Assert.Contains("QueuePillCorrection(window, animate: true)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactHeader_DoesNotRemoveWorkspaceFunctions()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NavigationLayoutFix.cs"));

        Assert.Contains("engineeringText.Text = medium ? \"ENGINEERING\" : \"ENG\"", source, StringComparison.Ordinal);
        Assert.Contains("loaded ? \"FAT · LOADED\" : \"FAT\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("modeShell.Visibility = Visibility.Collapsed", source, StringComparison.Ordinal);
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
