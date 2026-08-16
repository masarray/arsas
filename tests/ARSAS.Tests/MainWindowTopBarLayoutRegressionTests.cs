namespace ARSAS.Tests;

public sealed class MainWindowTopBarLayoutRegressionTests
{
    [Fact]
    public void NavigationLayout_UsesResponsiveWidthsAndConsistentSixDestinationLabels()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NavigationLayoutFix.cs"));

        Assert.Contains("WideNavWidth = 990d", source, StringComparison.Ordinal);
        Assert.Contains("MediumNavWidth = 900d", source, StringComparison.Ordinal);
        Assert.Contains("CompactNavWidth = 720d", source, StringComparison.Ordinal);
        Assert.Contains("\"IEC 61850 Explorer\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Alarm\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Alarm Annunciator\"", source, StringComparison.Ordinal);
        Assert.Contains("\"GOOSE Subscriber\"", source, StringComparison.Ordinal);
        Assert.Contains("var labels = wide ? FullLabels : CompactLabels", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionPill_IsDerivedFromActualSixCellNavWidth_NotFixed150Pixels()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NavigationLayoutFix.cs"));

        Assert.Contains("var cellWidth = contentWidth / 6d", source, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(tabs.SelectedIndex, 0, 5) * cellWidth", source, StringComparison.Ordinal);
        Assert.Contains("pill.Width = Math.Max(1d, cellWidth - 2d)", source, StringComparison.Ordinal);
        Assert.Contains("FindName(\"WorkflowPillTranslate\") as TranslateTransform", source, StringComparison.Ordinal);
        Assert.Contains("pill.RenderTransform is TransformGroup", source, StringComparison.Ordinal);
        Assert.DoesNotContain("* 150d", source, StringComparison.Ordinal);
        Assert.DoesNotContain("contentWidth / 5d", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsiveCorrection_RunsAfterSelectionResizeAndRepeatedNavClick()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NavigationLayoutFix.cs"));

        Assert.Contains("window.SizeChanged += MainWindow_SizeChanged", source, StringComparison.Ordinal);
        Assert.Contains("tabs.SelectionChanged += MainTabs_SelectionChanged", source, StringComparison.Ordinal);
        Assert.Contains("Button.ClickEvent", source, StringComparison.Ordinal);
        Assert.Contains("OnMainWindowButtonClick", source, StringComparison.Ordinal);
        Assert.Contains("NavAlarmButton", source, StringComparison.Ordinal);
        Assert.Contains("A repeated click on the already-selected tab", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Loaded", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ContextIdle", source, StringComparison.Ordinal);
        Assert.Contains("QueuePillCorrection(window, animate: true)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsiveLabels_DoNotReplaceDiagnosticsAlertContentTree()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NavigationLayoutFix.cs"));
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));

        Assert.Contains("if (index < labels.Length)", source, StringComparison.Ordinal);
        Assert.Contains("NavDiagnosticsButton", source, StringComparison.Ordinal);
        Assert.Contains("DiagnosticsAlertVisibility", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("button.Content = labels[index];\n            button.MinHeight", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactHeader_DoesNotRemoveWorkspaceFunctions()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NavigationLayoutFix.cs"));

        Assert.Contains("engineeringText.Text = medium ? \"ENGINEERING\" : \"ENG\"", source, StringComparison.Ordinal);
        Assert.Contains("loaded ? \"FAT · LOADED\" : \"FAT\"", source, StringComparison.Ordinal);
        Assert.Contains("WorkspaceModeChild_SizeChanged", source, StringComparison.Ordinal);
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
