using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using ArIED61850Tester;
using ArIED61850Tester.Models;

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
            // The base ARSAS themes draw selected cells transparent; the
            // opt-in overlay must supply visible selection background.
            Assert.NotNull(grid.CellStyle);
            var trigger = Assert.Single(grid.CellStyle.Triggers.OfType<Trigger>(),
                t => t.Property == DataGridCell.IsSelectedProperty &&
                     Equals(t.Value, true));
            Assert.Contains(trigger.Setters.OfType<Setter>(),
                setter => setter.Property == Control.BackgroundProperty);

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
            var nested = Assert.Single(custom.Items.OfType<MenuItem>(),
                item => (string?)item.Header == "Copy");
            Assert.Equal(6, nested.Items.Count); // five actions and separator
            GridClipboardBehavior.AppendCopyActions(other, custom);
            Assert.Single(custom.Items); // one copy submenu; never duplicated

            var command = new DataGrid
            {
                Name = "EquipmentCommandGrid",
                IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow
            };
            command.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, command));
            Assert.NotNull(command.ContextMenu);
            Assert.Equal(6, command.ContextMenu.Items.Count);
            Assert.Equal(DataGridSelectionMode.Single, command.SelectionMode);
            Assert.Equal(DataGridSelectionUnit.FullRow, command.SelectionUnit);
            Assert.Null(command.CellStyle);
            command.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, command));
            Assert.Equal(6, command.ContextMenu.Items.Count);

            var browser = new ListView
            {
                Name = "IedBrowserDetailList",
                SelectionMode = SelectionMode.Single // matches actual MainWindow XAML
            };
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
            "ExplorerLiveValuesGrid", "GlobalLiveGrid", "EventLogGrid",
            "EventDetailsGrid"
        })
        {
            var grid = Assert.Single(grids, g =>
                (string?)g.Attribute(x + "Name") == name);
            Assert.Equal("True", (string?)grid.Attribute(
                ux + "GridClipboardBehavior.ExcelCells"));
        }

        foreach (var name in new[]
        {
            "EquipmentCommandGrid", "IedBrowserTreeGrid",
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

    [Fact]
    public void ExactRowMenuKeepsExistingDownloadAndCopiesPinnedRow()
    {
        RunSta(() =>
        {
            var grid = new DataGrid { IsReadOnly = true };
            GridClipboardBehavior.Install();
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, grid));
            var file = new object();
            var menu = new ContextMenu
            {
                Items = { new MenuItem { Header = "Download complete record" } }
            };
            GridClipboardBehavior.AppendCopyActions(grid, menu, file);
            Assert.Equal(3, menu.Items.Count);
            Assert.Equal("Download complete record",
                Assert.IsType<MenuItem>(menu.Items[0]).Header);
            var copy = Assert.IsType<MenuItem>(menu.Items[2]);
            Assert.Equal("Copy", copy.Header);
            Assert.Equal(3, copy.Items.Count);
            Assert.Equal("Copy this row",
                Assert.IsType<MenuItem>(copy.Items[1]).Header);
        });
    }

    [Fact]
    public void DedicatedFatAndFilesContextMenusKeepSharedCopyActions()
    {
        var fat = File.ReadAllText(FindSource("IoListTestingWindow.FatV2Ux.cs"));
        var files = File.ReadAllText(FindSource("MainWindow.IedBrowser.Files.cs"));
        Assert.Contains("AppendCopyActions(_fatSignalsGrid, actions)", fat);
        Assert.Contains("AppendCopyActions(IedBrowserFilesGrid, rowMenu, file)", files);
        Assert.Contains("ClipboardContentBinding = new Binding(\"Runtime.CurrentValue\")", fat);
        Assert.Contains("nameof(IoTestPointPlan.Value1Text)", fat);
    }

    [Fact]
    public void EventInspectorDoesNotReflowDuringMouseRangeSelection()
    {
        var xaml = File.ReadAllText(FindSource("MainWindow.xaml"));
        var code = File.ReadAllText(FindSource("MainWindow.EventDetails.cs"));
        Assert.Contains("EventLogGrid_PreviewMouseLeftButtonUp", xaml);
        Assert.Contains("EventLogGrid_CurrentCellChanged", xaml);
        Assert.Contains("Mouse.LeftButton == MouseButtonState.Released", code);
        Assert.Contains("DispatcherPriority.Background", code);
        Assert.Contains("EventLogGrid.CurrentCell.Item as Iec61850EventEntry", code);
    }

    [Fact]
    public void EveryVisualTemplateColumnHasCopyBindingExceptPureCommandButtons()
    {
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var files = new[]
        {
            "MainWindow.xaml", "BrowserBatchWriteWindow.xaml",
            "FaultRecordWindow.xaml", "RcbExportFilterWindow.xaml",
            "SignalSelectionWizardWindow.xaml", "IoListTestingWindow.xaml",
            "Views/GooseSubscriberView.xaml",
            "Views/GooseSubscriberLiteView.xaml",
            "Views/SmvInlineViewer.xaml"
        };
        foreach (var file in files)
        {
            var doc = XDocument.Load(FindSource(file));
            foreach (var column in doc.Descendants(p + "DataGridTemplateColumn"))
            {
                if (file == "MainWindow.xaml" &&
                    (string?)column.Attribute("Header") == "Control")
                    continue; // Live Open/Close etc. are UI actions, not values.
                Assert.True(column.Attribute("ClipboardContentBinding") is not null ||
                    column.Element(p + "DataGridTemplateColumn.ClipboardContentBinding") is not null,
                    file + ": missing ClipboardContentBinding for template " +
                    ((string?)column.Attribute("Header") ?? "(header template)"));
            }
        }
    }

    [Fact]
    public void CommandChecksCopyReadsAllThreeActualFlagsWithoutModifyingThem()
    {
        var signal = new SignalDefinition
        {
            ControlInterlockCheck = true,
            ControlSynchroCheck = false,
            ControlTestMode = true
        };
        Assert.Equal("Interlock=True; Sync=False; Test=True",
            signal.ControlChecksCopyText);
        signal.ControlSynchroCheck = true;
        Assert.Equal("Interlock=True; Sync=True; Test=True",
            signal.ControlChecksCopyText);
        Assert.True(signal.ControlInterlockCheck);
        Assert.True(signal.ControlSynchroCheck);
        Assert.True(signal.ControlTestMode);
    }

    [Fact]
    public void AllWritableGridsRelyOnTheSingleGlobalInstaller()
    {
        var code = File.ReadAllText(FindSource("GridClipboardBehavior.cs"));
        Assert.DoesNotContain("!grid.IsReadOnly", code);
        Assert.Contains("typeof(DataGrid)", code);
        Assert.Contains("FrameworkElement.LoadedEvent", code);
        Assert.Contains("GridClipboardBehavior.Install();",
            File.ReadAllText(FindSource("App.xaml.cs")));
        Assert.Contains("column.OnCopyingCellClipboardContent(item)", code);
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
