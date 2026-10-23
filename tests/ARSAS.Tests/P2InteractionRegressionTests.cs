namespace ARSAS.Tests;

public sealed class P2InteractionRegressionTests
{
    [Fact]
    public void App_InstallsSmoothScrollLayer_AfterGridUx()
    {
        var source = File.ReadAllText(FindRepoFile("App.xaml.cs"));

        var grid = source.IndexOf("GridUxBehavior.Install();", StringComparison.Ordinal);
        var p2 = source.IndexOf("P2InteractionBehavior.Install();", StringComparison.Ordinal);

        Assert.True(grid >= 0 && p2 > grid, "Smooth scrolling must install after the existing grid UX layer.");
    }

    [Fact]
    public void SmoothScroll_DoesNotInstallKeyboardShortcutsOrSearchUi()
    {
        var source = File.ReadAllText(FindRepoFile("P2InteractionBehavior.cs"));

        Assert.DoesNotContain("Keyboard.PreviewKeyDownEvent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindow_PreviewKeyDown", source, StringComparison.Ordinal);
        Assert.DoesNotContain("P2IedFinder", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExplorerLiveSearchBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GlobalLiveSearchBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollIntoView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectionViewSource", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SmoothScroll_DoesNotMutateVirtualizationAfterLayoutStarts()
    {
        var source = File.ReadAllText(FindRepoFile("P2InteractionBehavior.cs"));

        Assert.DoesNotContain("ItemsControl_Loaded", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VirtualizingPanel.SetVirtualizationMode", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VirtualizingPanel.SetIsVirtualizing", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VirtualizingPanel.SetScrollUnit", source, StringComparison.Ordinal);
        Assert.Contains("ConfigurePixelScrollStylesBeforeWindowCreation", source, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.ScrollUnitProperty", source, StringComparison.Ordinal);
        Assert.Contains("ScrollUnit.Pixel", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SmoothScroll_UsesAccumulatedRenderRateEasing()
    {
        var source = File.ReadAllText(FindRepoFile("P2InteractionBehavior.cs"));

        Assert.Contains("Mouse.PreviewMouseWheelEvent", source, StringComparison.Ordinal);
        Assert.Contains("Interval = TimeSpan.FromMilliseconds(16)", source, StringComparison.Ordinal);
        Assert.Contains("e.Delta / 120d", source, StringComparison.Ordinal);
        Assert.Contains("state.TargetOffset + deltaPixels", source, StringComparison.Ordinal);
        Assert.Contains("remaining * 0.24d", source, StringComparison.Ordinal);
        Assert.Contains("viewer.ScrollToVerticalOffset", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SmoothScroll_PreservesPlatformOwnedWheelGestures()
    {
        var source = File.ReadAllText(FindRepoFile("P2InteractionBehavior.cs"));

        Assert.Contains("ModifierKeys.Control | ModifierKeys.Shift", source, StringComparison.Ordinal);
        Assert.Contains("FindAncestor<ScrollBar>(source)", source, StringComparison.Ordinal);
        Assert.Contains("IsDropDownOpen: true", source, StringComparison.Ordinal);
        Assert.Contains("AcceptsReturn: true", source, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.GetScrollUnit(items) != ScrollUnit.Pixel", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SmoothScrollLayer_RemainsPresentationOnly()
    {
        var source = File.ReadAllText(FindRepoFile("P2InteractionBehavior.cs"));
        var app = File.ReadAllText(FindRepoFile("App.xaml.cs"));
        var engineLock = File.ReadAllText(FindRepoFile(Path.Combine("engines", "ARIEC61850.lock.json")));

        Assert.DoesNotContain("Iec61850MonitorRuntime", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadValueAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitoring", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReportControl", source, StringComparison.Ordinal);
        Assert.DoesNotContain("P2InteractionBehavior", engineLock, StringComparison.Ordinal);
        Assert.Contains("P2InteractionBehavior.Install();", app, StringComparison.Ordinal);
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

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
