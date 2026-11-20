using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// Structural WPF contract only. Actual DPI and window geometry remain
/// a separate Windows rendered-workflow acceptance gate.
/// </summary>
public sealed class P0DLiveSignalViewportContractTests
{
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void LiveSignalValues_StayInFiniteStarViewport_WithVirtualizedIndependentScrolling()
    {
        var xaml = XDocument.Load(Path.Combine(FindRepositoryRoot(), "MainWindow.xaml"));
        var viewport = xaml.Descendants().Single(e =>
            e.Name.LocalName == "DataGrid" &&
            (string?)e.Attribute(Xaml + "Name") == "ExplorerLiveValuesGrid");

        Assert.Equal("2", (string?)viewport.Attribute("Grid.Row"));
        Assert.Equal("0", (string?)viewport.Attribute("MinHeight"));
        Assert.Equal("Stretch", (string?)viewport.Attribute("VerticalAlignment"));
        Assert.Equal("Auto", (string?)viewport.Attribute("ScrollViewer.VerticalScrollBarVisibility"));
        Assert.Equal("Auto", (string?)viewport.Attribute("ScrollViewer.HorizontalScrollBarVisibility"));
        Assert.Equal("True", (string?)viewport.Attribute("ScrollViewer.CanContentScroll"));
        Assert.Equal("True", (string?)viewport.Attribute("EnableRowVirtualization"));
        Assert.Equal("True", (string?)viewport.Attribute("VirtualizingPanel.IsVirtualizing"));
        Assert.Equal("Recycling", (string?)viewport.Attribute("VirtualizingPanel.VirtualizationMode"));
        Assert.Equal("2", (string?)viewport.Attribute("FrozenColumnCount"));

        var host = viewport.Parent;
        Assert.NotNull(host);
        Assert.Equal("Grid", host!.Name.LocalName);
        var rows = host.Elements().Single(e => e.Name.LocalName == "Grid.RowDefinitions")
            .Elements().ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal("*", (string?)rows[2].Attribute("Height"));
    }

    [Fact]
    public void CommandDock_RemainsSeparateFromValuesStarRegion()
    {
        var xaml = XDocument.Load(Path.Combine(FindRepositoryRoot(), "MainWindow.xaml"));
        var viewport = xaml.Descendants().Single(e =>
            (string?)e.Attribute(Xaml + "Name") == "ExplorerLiveValuesGrid");
        var card = viewport.Parent?.Parent;
        var shell = card?.Parent;
        Assert.NotNull(shell);
        Assert.Equal("Grid", shell!.Name.LocalName);
        var shellRows = shell.Elements()
            .Single(e => e.Name.LocalName == "Grid.RowDefinitions")
            .Elements().ToArray();
        Assert.Equal(2, shellRows.Length);
        Assert.Equal("*", (string?)shellRows[0].Attribute("Height"));
        Assert.Equal("Auto", (string?)shellRows[1].Attribute("Height"));

        Assert.Equal("0", (string?)card!.Attribute("Grid.Row"));
        var dock = shell.Elements().Single(e =>
            (string?)e.Attribute(Xaml + "Name") == "CommandPanelExpander");
        Assert.Equal("1", (string?)dock.Attribute("Grid.Row"));
    }

    private static string FindRepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null &&
               !File.Exists(Path.Combine(folder.FullName, "MainWindow.xaml")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new InvalidOperationException(
            "Test requires the tracked MainWindow.xaml.");
    }
}
