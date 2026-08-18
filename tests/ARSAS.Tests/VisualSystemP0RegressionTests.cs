namespace ARSAS.Tests;

public sealed class VisualSystemP0RegressionTests
{
    [Fact]
    public void AppTheme_ExposesOneCompactWorkspaceTypographyAndSurfaceContract()
    {
        var app = File.ReadAllText(FindRepoFile("App.xaml"));

        Assert.Contains("x:Key=\"WorkspaceTitle\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"WorkspaceSubtitle\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"WorkspaceCard\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SearchSurface\"", app, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"FontSize\" Value=\"16\"/>", Slice(app, "x:Key=\"WorkspaceTitle\"", "</Style>"), StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"FontSize\" Value=\"11\"/>", Slice(app, "x:Key=\"WorkspaceSubtitle\"", "</Style>"), StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"12\"/>", Slice(app, "x:Key=\"WorkspaceCard\"", "</Style>"), StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Effect\" Value=\"{x:Null}\"/>", Slice(app, "x:Key=\"WorkspaceCard\"", "</Style>"), StringComparison.Ordinal);
    }

    [Fact]
    public void Navigation_UsesStableTypographyAndKeyboardOnlyInternalFocusRing()
    {
        var app = File.ReadAllText(FindRepoFile("App.xaml"));
        var nav = Slice(app, "x:Key=\"SegmentedNavButton\"", "</Style>");

        Assert.Contains("FontSize\" Value=\"12.5\"", nav, StringComparison.Ordinal);
        Assert.Contains("FocusVisualStyle\" Value=\"{x:Null}\"", nav, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"KeyboardFocusRing\"", nav, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsKeyboardFocused\"", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("Property=\"FontSize\" Value=\"13.45\"", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("Property=\"FontSize\" Value=\"12.55\"", nav, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWorkspaces_UseSharedWorkspaceSurfaceAndHeaderTypography()
    {
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));

        Assert.Contains("Style=\"{StaticResource WorkspaceCard}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Global Multi-IED Live Monitor\" Style=\"{StaticResource WorkspaceTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"IEC 61850 Sequence of Events\" Style=\"{StaticResource WorkspaceTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Diagnostics &amp; Communication Journal\" Style=\"{StaticResource WorkspaceTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Alarm Annunciator\" Style=\"{StaticResource WorkspaceTitle}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Every IED keeps its own connection, report subscription, validated reads, and event-driven RCB state.", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SCADA/SAS SOE: state values use ARSAS blue for active", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveMonitor_GlobalSearchIsPrimary_AndColumnFiltersAreProgressiveDisclosure()
    {
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        var behavior = File.ReadAllText(FindRepoFile("GridUxBehavior.cs"));
        var bridge = File.ReadAllText(FindRepoFile("MainWindow.GlobalLiveSearch.cs"));
        var section = Slice(xaml, "<!-- GLOBAL MULTI-IED LIVE MONITOR -->", "<!-- EVENT LOG -->");

        Assert.Contains("x:Name=\"GlobalLiveSearchBox\"", section, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlobalLiveFiltersButton\"", section, StringComparison.Ordinal);
        Assert.Contains("GlobalLiveFilters_Click", section, StringComparison.Ordinal);
        Assert.Contains("SetGlobalRapidFiltersExpanded", behavior, StringComparison.Ordinal);
        Assert.Contains("FiltersExpanded", behavior, StringComparison.Ordinal);
        Assert.Contains("new GridLength(0)", behavior, StringComparison.Ordinal);
        Assert.Contains("ColumnHeaderHeight = expanded ? 68 : 34", behavior, StringComparison.Ordinal);
        Assert.Contains("SetGlobalRapidFiltersExpanded(GlobalLiveGrid", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeNavigation_UsesStableP0Style_WhileResponsiveFixOwnsFinalWidth()
    {
        var runtime = File.ReadAllText(FindRepoFile("SasOperationalUiPolicy.cs"));
        var responsive = File.ReadAllText(FindRepoFile("MainWindow.NavigationLayoutFix.cs"));

        Assert.Contains("shell.Width = 760", runtime, StringComparison.Ordinal);
        Assert.Contains("shell.CornerRadius = new CornerRadius(14)", runtime, StringComparison.Ordinal);
        Assert.Contains("button.FontSize = 12.5", runtime, StringComparison.Ordinal);
        Assert.Contains("button.FocusVisualStyle = null", runtime, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"10\"", runtime, StringComparison.Ordinal);
        Assert.Contains("WideNavWidth = 990d", responsive, StringComparison.Ordinal);
        Assert.Contains("MediumNavWidth = 900d", responsive, StringComparison.Ordinal);
        Assert.Contains("CompactNavWidth = 720d", responsive, StringComparison.Ordinal);
        Assert.Contains("shell.Width = shellWidth", responsive, StringComparison.Ordinal);
    }

    [Fact]
    public void P0_KeepsTheReviewedAriecEnginePinUntouched()
    {
        var testDirectory = Path.GetDirectoryName(FindRepoFile("MainWindow.xaml"))!;
        var engineLock = File.ReadAllText(Path.Combine(testDirectory, "engines", "ARIEC61850.lock.json"));

        Assert.Contains("9ca4ee490a27046d5d2ffdc21f3614b2d7d58725", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"sourcePullRequest\": 84", engineLock, StringComparison.Ordinal);
    }

    private static string Slice(string source, string start, string end)
    {
        var a = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(a >= 0, $"Start marker not found: {start}");
        var b = source.IndexOf(end, a + start.Length, StringComparison.Ordinal);
        Assert.True(b > a, $"End marker not found: {end}");
        return source[a..b];
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
        throw new FileNotFoundException(relativePath);
    }
}
