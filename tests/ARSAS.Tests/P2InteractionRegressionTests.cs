namespace ARSAS.Tests;

public sealed class P2InteractionRegressionTests
{
    [Fact]
    public void App_InstallsP2InteractionLayer_AfterGridUx()
    {
        var source = File.ReadAllText(FindRepoFile("App.xaml.cs"));

        var grid = source.IndexOf("GridUxBehavior.Install();", StringComparison.Ordinal);
        var p2 = source.IndexOf("P2InteractionBehavior.Install();", StringComparison.Ordinal);

        Assert.True(grid >= 0 && p2 > grid, "P2 interaction behavior must install after the existing grid UX layer.");
    }

    [Fact]
    public void SmoothScroll_PreservesVirtualization_AndUsesPixelScroll()
    {
        var source = File.ReadAllText(FindRepoFile("P2InteractionBehavior.cs"));

        Assert.Contains("VirtualizingPanel.SetIsVirtualizing(items, true)", source, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.SetVirtualizationMode(items, VirtualizationMode.Recycling)", source, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.SetScrollUnit(items, ScrollUnit.Pixel)", source, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.SetCanContentScroll(items, true)", source, StringComparison.Ordinal);
        Assert.Contains("Interval = TimeSpan.FromMilliseconds(16)", source, StringComparison.Ordinal);
        Assert.Contains("e.Delta / 120d", source, StringComparison.Ordinal);
        Assert.Contains("state.TargetOffset + deltaPixels", source, StringComparison.Ordinal);
        Assert.Contains("remaining * 0.24d", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetCanContentScroll(items, false)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SmoothScroll_DoesNotHijackPlatformModifierGestures()
    {
        var source = File.ReadAllText(FindRepoFile("P2InteractionBehavior.cs"));

        Assert.Contains("ModifierKeys.Control | ModifierKeys.Shift", source, StringComparison.Ordinal);
        Assert.Contains("FindAncestor<ScrollBar>(source)", source, StringComparison.Ordinal);
        Assert.Contains("IsDropDownOpen: true", source, StringComparison.Ordinal);
        Assert.Contains("AcceptsReturn: true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IedFinder_SearchesOperatorRelevantIdentityFields_AndShowsMatchCount()
    {
        var source = File.ReadAllText(FindRepoFile("P2InteractionBehavior.cs"));

        Assert.Contains("P2IedFinder", source, StringComparison.Ordinal);
        Assert.Contains("Find IED — name, IP, endpoint or status", source, StringComparison.Ordinal);
        Assert.Contains("device.Name", source, StringComparison.Ordinal);
        Assert.Contains("device.IpAddress", source, StringComparison.Ordinal);
        Assert.Contains("device.EndpointText", source, StringComparison.Ordinal);
        Assert.Contains("device.Status", source, StringComparison.Ordinal);
        Assert.Contains("device.LogicalDeviceSummary", source, StringComparison.Ordinal);
        Assert.Contains("count == 1 ? \"1 match\"", source, StringComparison.Ordinal);
        Assert.Contains("state.IedList.ScrollIntoView", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingLiveSearches_GainResultCount_AndNextPreviousNavigation()
    {
        var source = File.ReadAllText(FindRepoFile("P2InteractionBehavior.cs"));

        Assert.Contains("ExplorerLiveSearchBox", source, StringComparison.Ordinal);
        Assert.Contains("GlobalLiveSearchBox", source, StringComparison.Ordinal);
        Assert.Contains("GlobalLiveGrid", source, StringComparison.Ordinal);
        Assert.Contains("SelectedDevice.Points", source, StringComparison.Ordinal);
        Assert.Contains("Enter next", source, StringComparison.Ordinal);
        Assert.Contains("Shift+Enter previous", source, StringComparison.Ordinal);
        Assert.Contains("grid.ScrollIntoView(item)", source, StringComparison.Ordinal);
        Assert.Contains("row.BringIntoView()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyboardFastFind_OffersContextSearch_IedSearch_AndFindNext()
    {
        var source = File.ReadAllText(FindRepoFile("P2InteractionBehavior.cs"));

        Assert.Contains("e.Key == Key.K", source, StringComparison.Ordinal);
        Assert.Contains("e.Key == Key.F", source, StringComparison.Ordinal);
        Assert.Contains("e.Key == Key.F3", source, StringComparison.Ordinal);
        Assert.Contains("ModifierKeys.Shift", source, StringComparison.Ordinal);
        Assert.Contains("FocusIedFinder(owner, switchToExplorer: true)", source, StringComparison.Ordinal);
        Assert.Contains("target.SelectAll()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P2InteractionLayer_RemainsPresentationOnly()
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
