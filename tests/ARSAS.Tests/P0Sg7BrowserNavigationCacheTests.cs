using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>Protect the fast return-to-SG7 navigation path without physical IEC traffic.</summary>
public sealed class P0Sg7BrowserNavigationCacheTests
{
    [Fact]
    public void CompletedSclFlights_AreReusedAcrossInterleavedIeds_WithinFiniteCapacity()
    {
        var parseCalls = 0;
        var tasks = new BoundedFifoCache<string, Task<int>>(2, StringComparer.Ordinal);
        Task<int> GetOrParse(string key)
        {
            if (!tasks.TryGetValue(key, out var flight))
            {
                flight = Task.FromResult(++parseCalls);
                tasks.Set(key, flight);
            }
            return flight;
        }

        var sg7 = GetOrParse("SG7|SCL-hash|IED|AP");
        var bcu = GetOrParse("BCUGE|SCL-hash|IED|AP");
        Assert.Same(sg7, GetOrParse("SG7|SCL-hash|IED|AP"));
        Assert.Same(bcu, GetOrParse("BCUGE|SCL-hash|IED|AP"));
        Assert.Equal(2, parseCalls);

        // Capacity is finite: evict one oldest entry rather than pin every SCL forever.
        GetOrParse("THIRD|SCL-hash|IED|AP");
        Assert.Equal(2, tasks.Count);
        Assert.NotSame(sg7, GetOrParse("SG7|SCL-hash|IED|AP"));
        Assert.Equal(4, parseCalls);
        Assert.Equal(2, tasks.Count);
    }

    [Fact]
    public void BrowserSelectionPath_UsesBoundedSclFlights_AndLatestMetricsHandoff()
    {
        var perf = Read("MainWindow.IedBrowser.Performance.cs");
        var browser = Read("MainWindow.IedBrowser.cs");

        Assert.Contains("new(8, StringComparer.Ordinal)", perf);
        Assert.Contains("_iedBrowserInventoryFlights.TryGetValue(key, out var task)", perf);
        Assert.Contains("_iedBrowserInventoryFlights.Set(key, task)", perf);
        Assert.DoesNotContain("task.IsFaulted || task.IsCanceled", perf);
        Assert.Contains("Cache failed tasks too", perf);
        Assert.Contains("QueuePendingIedBrowserMetricsAfter(flight, device, model, association)", perf);
        Assert.Contains("_iedBrowserMetricsStartedUtc = DateTime.MinValue", perf);
        Assert.Contains("ReferenceEquals(completedDevice, current)", perf);
        Assert.Contains("device.LiveValues.Association != association", perf);
        Assert.Contains("_iedBrowserInventoryFlights.Clear()", browser);
        Assert.DoesNotContain("SclReportControlFilter.InspectFile(", browser);
    }

    [Fact]
    public void ExplorerSwitchWithEmptySearch_SkipsRedundantWpfCollectionRefresh()
    {
        var source = Read("MainWindow.LiveSearch.cs");
        Assert.Contains("if (view.Filter is not null)", source);
        Assert.Contains("view.Filter = item =>", source);
        Assert.Contains("IedBrowserPerformanceProbe.Measure(", source);
        Assert.DoesNotContain("view.Refresh();", source);
        // WPF's Filter setter already RefreshOrDefer's this same CollectionView.
        // Never change exact IEC reference or selected report membership.
    }

    private static string Read(string path)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        throw new FileNotFoundException(path);
    }
}
