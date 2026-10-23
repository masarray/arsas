using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;
using ArIED61850Tester;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Regression gates for SG7-size WPF search/sort scheduling. This is a
/// presentation test: no MMS acquisition, SOE or command operation is mocked.
/// </summary>
[Collection("WpfStaIsolation")]
public sealed class SsotV2LargeGridSearchSortBudgetTests
{
    [Fact]
    public void FatGlobalLiveGrid_DropsOnlyOurCustomSort_KeepingSourceOrderUnchanged()
    {
        RunInSta(() =>
        {
            var points = new ObservableCollection<Iec61850MonitorPoint>(
                Enumerable.Range(0, 2500).Select(n => new Iec61850MonitorPoint
                {
                    DeviceId = "SG7",
                    IecReference = $"SG7LD/MMXU1.A{n}.stVal",
                    FunctionalConstraint = "ST"
                }));
            var expectedFirst = points[0];
            var grid = new DataGrid { Name = "GlobalLiveGrid", ItemsSource = points, CanUserAddRows = false };
            var view = Assert.IsType<ListCollectionView>(
                CollectionViewSource.GetDefaultView(points));
            view.CustomSort = IecNaturalGlobalMonitorSort.Instance;
            ApplyNaturalSort(grid, IecNaturalGlobalMonitorSort.Instance);

            Assert.Null(view.CustomSort);
            Assert.Same(expectedFirst, points[0]);
            Assert.Equal(2500, view.Count);
        });
    }

    [Fact]
    public void SmallGlobalGrid_KeepsNumericNaturalSort()
    {
        RunInSta(() =>
        {
            var points = new ObservableCollection<Iec61850MonitorPoint>(
            [
                new() { DeviceName = "GE", IecReference = "LD/CSWI10.Pos.stVal" },
                new() { DeviceName = "GE", IecReference = "LD/CSWI2.Pos.stVal" }
            ]);
            var grid = new DataGrid { Name = "GlobalLiveGrid", ItemsSource = points, CanUserAddRows = false };
            var view = Assert.IsType<ListCollectionView>(
                CollectionViewSource.GetDefaultView(points));

            ApplyNaturalSort(grid, IecNaturalGlobalMonitorSort.Instance);

            Assert.Same(IecNaturalGlobalMonitorSort.Instance, view.CustomSort);
            Assert.Same(points[1], view.Cast<Iec61850MonitorPoint>().First());
        });
    }

    [Fact]
    public void FatExplorerGrid_IsProtectedButCommandGridRetainsVerifiedOrder()
    {
        RunInSta(() =>
        {
            var points = new ObservableCollection<Iec61850MonitorPoint>(
                Enumerable.Range(0, 2100).Select(n => new Iec61850MonitorPoint
                {
                    DeviceId = "SG7",
                    IecReference = $"SG7LD/MMXU1.Freq{n}.stVal",
                    FunctionalConstraint = "ST"
                }));
            var explorer = new DataGrid
            {
                Name = "ExplorerLiveValuesGrid",
                ItemsSource = points
            };
            ApplyNaturalSort(explorer, IecNaturalLiveMonitorSort.Instance);
            var view = Assert.IsType<ListCollectionView>(
                CollectionViewSource.GetDefaultView(points));
            Assert.Null(view.CustomSort);

            var commands = new ObservableCollection<SignalDefinition>(
            [
                new() { ObjectReference = "SG7/GGIO1.DPCSO10" },
                new() { ObjectReference = "SG7/GGIO1.DPCSO2" }
            ]);
            var commandGrid = new DataGrid
            {
                Name = "EquipmentCommandGrid", ItemsSource = commands
            };
            ApplyNaturalSort(commandGrid, IecNaturalCommandSort.Instance);
            Assert.Same(IecNaturalCommandSort.Instance,
                CollectionViewSource.GetDefaultView(commands)
                    is ListCollectionView commandView ? commandView.CustomSort : null);
        });
    }

    [Fact]
    public void ExplorerHeavySearch_IsDebouncedButSwitchClearAndEnterApplyImmediately()
    {
        var dynamicSource = Read("MainWindow.LiveSignalSearch.cs");
        var nativeSource = Read("MainWindow.LiveSearch.cs");
        var globalSort = Read("MainWindow.IecNaturalSort.cs");

        Assert.Contains("=> ScheduleUnifiedLiveSignalSearch()", nativeSource);
        Assert.Contains("=> ScheduleUnifiedLiveSignalSearch()", dynamicSource);
        Assert.Contains("Count: > 2048", dynamicSource);
        Assert.Contains("TimeSpan.FromMilliseconds(220)", dynamicSource);
        Assert.Contains("_liveSignalFilterDebounce.Stop();", dynamicSource);
        Assert.Contains("if (e.Key == Key.Enter)", dynamicSource);
        Assert.Contains("AttachLiveSignalSearchSource()", dynamicSource);
        Assert.Contains("ApplyLiveSignalSearch();", dynamicSource);
        Assert.Contains("GlobalPoints.CollectionChanged +=", globalSort);
        Assert.Contains("_globalNaturalSortLargeMode", globalSort);
        Assert.Contains("view.CustomSort = null", globalSort);
        Assert.Contains("grid.Name is \"ExplorerLiveValuesGrid\" or \"GlobalLiveGrid\"", globalSort);
        Assert.Contains("IecNaturalCommandSort.Instance", globalSort);
    }

    private static void ApplyNaturalSort(DataGrid grid, IComparer comparer)
    {
        var method = typeof(MainWindow).GetMethod("ApplyIecNaturalSort",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method!.Invoke(null, [grid, comparer]);
    }

    private static void RunInSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); } catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "WPF sort test timed out");
        if (error is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static string Read(string file)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory);
             dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, file);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(file);
    }
}
