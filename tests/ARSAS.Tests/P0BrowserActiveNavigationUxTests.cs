namespace ARSAS.Tests;

public sealed class P0BrowserActiveNavigationUxTests
{
    private static string BrowserMarkup()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml")))
            root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.xaml"))
            .Split("<!-- Read-only engineering workspace: one virtual tree")[1];
    }

    [Fact]
    public void ActiveExplorerRowUsesPersistentBlueSelectionAndRetainsSplitter()
    {
        var xaml = BrowserMarkup();
        var start = xaml.IndexOf("x:Name=\"IedBrowserTreeGrid\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("</DataGrid>", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var tree = xaml[start..end];
        Assert.Contains("<Trigger Property=\"IsSelected\" Value=\"True\">", tree);
        Assert.Contains("BorderBrush\" Value=\"#2563EB\"", tree);
        Assert.Contains("Background\" Value=\"#C7E1FF\"", tree);
        Assert.Contains("AncestorType={x:Type DataGridRow}", tree);
        Assert.Contains("IedBrowserPaneSplitter", xaml);
        Assert.Contains("Grid.Column=\"1\"", xaml);
    }

    [Fact]
    public void LargeBrowserObservationProvenanceDoesNotAutoOpenAsTooltip()
    {
        var xaml = BrowserMarkup();
        Assert.DoesNotContain("ToolTip=\"{Binding ObservationTooltip}\"", xaml);
        Assert.DoesNotContain("ToolTip=\"{Binding Key}\"", xaml);
        Assert.DoesNotContain("ToolTip=\"{Binding Tooltip}\"", xaml);
        Assert.Contains("Text=\"{Binding ObservationSource}\"", xaml);
        Assert.Contains("Text=\"{Binding Value}\"", xaml);
        // Compact action help remains available, data evidence is not
        // shown as a screen-wide strip that blocks the detail grid.
        Assert.Contains("ToolTip=\"Expand / collapse\"", xaml);
    }
}
