using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// Compile-time WPF layout contract; runtime DPI screenshot acceptance remains
/// a separate Windows integration test, never asserted by XML inspection.
/// </summary>
public sealed class P0DLiveSignalViewportContractTests
{
    [Fact]
    public void LiveSignalViewport_RemainsInStarRegion_WithIndependentScrolling()
    {
        var root = FindRepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "MainWindow.xaml"));
        var grid = xaml.Descendants().Single(x => x.Name.LocalName == "DataGrid" &&
            (string?)x.Attribute(XName.Get("Name",
                "http://schemas.microsoft.com/winfx/2006/xaml")) == "ExplorerLiveValuesGrid");
        Assert.Equal("2", (string?)grid.Attribute("Grid.Row"));
        Assert.Equal("Stretch", (string?)grid.Attribute("VerticalAlignment"));
        Assert.Equal("0", (string?)grid.Attribute("MinHeight"));
        Assert.Equal("Auto", (string?)grid.Attribute(XName.Get(
            "ScrollViewer.VerticalScrollBarVisibility",
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation")));
        Assert.Equal("True", (string?)grid.Attribute(XName.Get(
            "VirtualizingPanel.IsVirtualizing",
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation")));
        Assert.Equal("Recycling", (string?)grid.Attribute(XName.Get(
            "VirtualizingPanel.VirtualizationMode",
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation")));
        var container = grid.Parent;
        Assert.NotNull(container);
        var definitions = container!.Elements().First(x =>
            x.Name.LocalName == "Grid.RowDefinitions")
            .Elements().ToArray();
        Assert.Equal("*", (string?)definitions[2].Attribute("Height"));
        var outerGrid = container.Parent?.Parent;
        Assert.NotNull(outerGrid);
        Assert.Equal("Grid", outerGrid!.Name.LocalName);
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
