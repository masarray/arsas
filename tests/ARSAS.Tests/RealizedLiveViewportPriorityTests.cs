using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Real data grids own only viewport hints. No acquisition snapshot or event is
/// discarded: every point retains its normal FIFO key independent of priority.
/// </summary>
public sealed class RealizedLiveViewportPriorityTests
{
    [Fact]
    public void ConcurrentTwoGridHints_PreserveSameExactReferenceWithoutCrossGridRemoval()
    {
        var viewport = new LivePointViewportPriorityTracker();
        const string key = "SG7|LD/MMXU1.A.phsA.cVal.mag.f|MX";
        const string distinctFc = "SG7|LD/MMXU1.A.phsA.cVal.mag.f|ST";

        Assert.False(viewport.IsRealized(key));
        Assert.True(viewport.Realize(LivePointViewportKind.Explorer, key));
        Assert.False(viewport.Realize(LivePointViewportKind.Explorer, key));
        Assert.True(viewport.Realize(LivePointViewportKind.Global, key));
        Assert.Equal(2, viewport.RealizedCount);
        viewport.Unrealize(LivePointViewportKind.Explorer, key);
        Assert.True(viewport.IsRealized(key));
        Assert.False(viewport.IsRealized(distinctFc));
        viewport.Clear(LivePointViewportKind.Global);
        Assert.False(viewport.IsRealized(key));
        Assert.Equal(0, viewport.RealizedCount);
    }

    [Fact]
    public void ScrollRecycling_OnlyActualRowsRemainPriorityHints()
    {
        var viewport = new LivePointViewportPriorityTracker();
        const int modelSize = 32_900;
        var keys = Enumerable.Range(0, modelSize)
            .Select(n => $"SG7|LD/MMXU1.Do{n}.stVal|ST").ToArray();
        foreach (var index in Enumerable.Range(10200, 55))
            Assert.True(viewport.Realize(LivePointViewportKind.Explorer, keys[index]));
        Assert.Equal(55, viewport.RealizedCount);
        Assert.True(viewport.IsRealized(keys[10223]));
        Assert.False(viewport.IsRealized(keys[0]));

        foreach (var index in Enumerable.Range(10200, 55))
            viewport.Unrealize(LivePointViewportKind.Explorer, keys[index]);
        foreach (var index in Enumerable.Range(24000, 42))
            viewport.Realize(LivePointViewportKind.Explorer, keys[index]);
        Assert.Equal(42, viewport.RealizedCount);
        Assert.False(viewport.IsRealized(keys[10223]));
        Assert.True(viewport.IsRealized(keys[24001]));

        viewport.Clear(LivePointViewportKind.Explorer);
        Assert.Equal(0, viewport.RealizedCount);
    }

    [Fact]
    public void BackgroundAcquisitionThread_CanSafelyInspectVisibleSnapshotHints()
    {
        var viewport = new LivePointViewportPriorityTracker();
        const string key = "SG7|LD/XCBR1.Pos.stVal|ST";
        var tasks = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            for (var iteration = 0; iteration < 2000; iteration++)
            {
                if (worker == 0)
                {
                    viewport.Realize(LivePointViewportKind.Explorer, key);
                    viewport.Unrealize(LivePointViewportKind.Explorer, key);
                }
                else
                {
                    _ = viewport.IsRealized(key);
                }
            }
        }));
        Task.WaitAll(tasks.ToArray());
        Assert.False(viewport.IsRealized(key));
    }

    [Fact]
    public void WpfContract_UsesActuallyRealizedRowsAndStillProjectsEveryQueuedPoint()
    {
        var partial = Read("MainWindow.LivePointViewportPriority.cs");
        var loaded = Read("MainWindow.IecNaturalSort.cs");
        var main = Read("MainWindow.xaml.cs");
        var flush = main.Split("private void UiFlushTimer_Tick(")[1]
            .Split("private void Signal_PropertyChanged(")[0];
        var ingress = main.Split("private void Runtime_PointUpdated(")[1]
            .Split("private void Runtime_EventRaised(")[0];

        Assert.Contains("grid.LoadingRow += LivePointViewport_LoadingRow", partial);
        Assert.Contains("grid.UnloadingRow += LivePointViewport_UnloadingRow", partial);
        Assert.Contains("_livePointViewport.Realize(", partial);
        Assert.Contains("_livePointViewport.Unrealize(", partial);
        Assert.Contains("_livePointViewport.Clear(", partial);
        Assert.Contains("AttachLivePointViewportPriority(sender as DataGrid)", loaded);
        Assert.Contains("Devices.CollectionChanged += ", main);
        Assert.Contains("UiPointOwners()", flush);
        Assert.DoesNotContain("Devices.GroupBy(", flush);
        Assert.Contains("_pendingPointUiKeys.Enqueue(key)", ingress);
        Assert.Contains("_livePointViewport.IsRealized(key)", ingress);
        Assert.Contains("_priorityPointUiKeys.Enqueue(key)", ingress);
        Assert.Contains("_pendingPointSnapshots.TryRemove(pointKey, out var pending)", flush);
        Assert.Contains("CanonicalPointSnapshotAuthority.Project(", flush);
        Assert.Contains("snapshot.ObservationAssociation != observedOwner.LiveValues.Association", flush);
        Assert.Contains("Events.AddRange(eventBatch)", flush);
        Assert.DoesNotContain("priorityDevice.Points.Take(48)", main);
        Assert.DoesNotContain("_pendingPointSnapshots.Clear()", flush);
    }

    private static string Read(string name)
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory);
             folder is not null; folder = folder.Parent)
        {
            var path = Path.Combine(folder.FullName, name);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(name);
    }
}
