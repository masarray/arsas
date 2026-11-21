using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using ArIED61850Tester;

namespace ARSAS.Tests;

/// <summary>
/// Real WPF STA behavior smoke: separate data clipboard selection from
/// navigation/command grids, and retain specialized context menus.
/// </summary>
[Collection("WpfStaIsolation")]
public sealed class EngineeringGridClipboardBehaviorTests
{
    [Fact]
    public void ReadOnlyDataGridGetsExcelSelectionAndSingleContextMenu()
    {
        RunSta(() =>
        {
            GridClipboardBehavior.Install();
            var grid = new DataGrid { IsReadOnly = true };
            GridClipboardBehavior.SetExcelCells(grid, true);
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, grid));

            Assert.Equal(DataGridSelectionMode.Extended, grid.SelectionMode);
            Assert.Equal(DataGridSelectionUnit.CellOrRowHeader, grid.SelectionUnit);
            var menu = Assert.IsType<ContextMenu>(grid.ContextMenu);
            Assert.Equal(6, menu.Items.Count); // 5 copy actions plus separator
            Assert.Equal(DataGridClipboardCopyMode.IncludeHeader, grid.ClipboardCopyMode);

            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, grid));
            Assert.Same(menu, grid.ContextMenu);
            Assert.Equal(6, menu.Items.Count);
        });
    }

    [Fact]
    public void IdentityAndMutableGridsRetainTheirOwnSelectionAndMenus()
    {
        RunSta(() =>
        {
            GridClipboardBehavior.Install();
            var rcb = new DataGrid
            {
                Name = "IedBrowserTreeGrid",
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow
            };
            rcb.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, rcb));
            Assert.Equal(DataGridSelectionMode.Single, rcb.SelectionMode);
            Assert.Equal(DataGridSelectionUnit.FullRow, rcb.SelectionUnit);
            Assert.NotNull(rcb.ContextMenu);

            var custom = new ContextMenu();
            var other = new DataGrid { IsReadOnly = true, ContextMenu = custom };
            other.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, other));
            Assert.Same(custom, other.ContextMenu);

            var command = new DataGrid { IsReadOnly = false };
            command.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, command));
            Assert.Null(command.ContextMenu);

            var browser = new ListView { Name = "IedBrowserDetailList" };
            browser.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, browser));
            Assert.Equal(SelectionMode.Single, browser.SelectionMode);
            Assert.NotNull(browser.ContextMenu);
        });
    }

    [Fact]
    public void DataTablesHaveExcelOptInButActionGridsAndTreeKeepRowSelection()
    {
        var doc = XDocument.Load(FindSource("MainWindow.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace ux = "clr-namespace:ArIED61850Tester";

        var grids = doc.Descendants(wpf + "DataGrid").ToArray();
        foreach (var name in new[]
        {
            "ExplorerLiveValuesGrid", "GlobalLiveGrid", "EventDetailsGrid"
        })
        {
            var grid = Assert.Single(grids, g =>
                (string?)g.Attribute(x + "Name") == name);
            Assert.Equal("True", (string?)grid.Attribute(
                ux + "GridClipboardBehavior.ExcelCells"));
        }

        foreach (var name in new[]
        {
            "EquipmentCommandGrid", "EventLogGrid", "IedBrowserTreeGrid",
            "IedBrowserFilesGrid"
        })
        {
            var grid = Assert.Single(grids, g =>
                (string?)g.Attribute(x + "Name") == name);
            Assert.Null(grid.Attribute(ux + "GridClipboardBehavior.ExcelCells"));
        }

        var templates = doc.Descendants(wpf + "DataGridTemplateColumn").ToArray();
        foreach (var (header, binding) in new[]
        {
            ("Value", "{Binding DisplayValue}"),
            ("Quality", "{Binding Quality}"),
            ("Objects", "{Binding Title}"),
            ("Filename", "{Binding Name}")
        })
            Assert.Contains(templates, col =>
                (string?)col.Attribute("Header") == header &&
                (string?)col.Attribute("ClipboardContentBinding") == binding);

        var smv = XDocument.Load(FindSource("SmvViewerWindow.xaml"));
        Assert.All(smv.Descendants(wpf + "DataGrid").Where(col =>
            ((string?)col.Attribute("ItemsSource")) is "{Binding RawVectorRows}" or
                "{Binding SnapshotChannels}"), col =>
            Assert.Equal("True", (string?)col.Attribute(
                ux + "GridClipboardBehavior.ExcelCells")));
    }

    private static string FindSource(string path)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, path);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException(path);
    }

    private static void RunSta(Action action)
    {
        if (!OperatingSystem.IsWindows()) return;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)),
            "STA clipboard-UX test did not terminate");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
