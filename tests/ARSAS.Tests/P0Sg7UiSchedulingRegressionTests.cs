namespace ARSAS.Tests;

/// <summary>
/// Source-path safety checks for the physical SG7 GUI scheduling failure.
/// WPF benchmarks still require an instrumented Windows/IED FAT; these guards
/// prevent accidentally restoring unbounded Dispatcher work in future merges.
/// </summary>
public sealed class P0Sg7UiSchedulingRegressionTests
{
    [Fact]
    public void SnapshotReportCoalescing_IsLosslessForEdgesAndUsesBoundedUiSlices()
    {
        var source = Read("MainWindow.xaml.cs");
        var ingress = source.Split("private void Runtime_PointUpdated(")[1]
            .Split("private void Runtime_EventRaised(")[0];
        Assert.Contains("_pendingPointSnapshots.TryAdd(key,", ingress);
        Assert.Contains("_pendingPointSnapshots.TryUpdate(key, merged, prior)", ingress);
        Assert.Contains("CanonicalPointSnapshotAuthority.Merge(prior, snapshot)", ingress);
        Assert.Contains("_pendingPointUiKeys.Enqueue(key)", ingress);

        var flush = source.Split("private void UiFlushTimer_Tick(")[1]
            .Split("private void Signal_PropertyChanged(")[0];
        Assert.Contains("processedPoints < UiPointBatchLimit", flush);
        Assert.Contains("Stopwatch.GetTimestamp() < uiDeadlineTicks", flush);
        Assert.Contains("_priorityPointUiKeys.TryDequeue(out var pointKey)", flush);
        Assert.Contains("_pendingPointSnapshots.TryRemove(pointKey, out var pending)", flush);
        Assert.DoesNotContain("_pendingPointSnapshots.Keys.ToArray()", flush);
        Assert.Contains("CanonicalPointSnapshotAuthority.Project(", flush);
        Assert.Contains("observedOwner.LiveValues.PublishReport(", flush);
        Assert.Contains("eventBatch.Count < UiEventBatchLimit", flush);
        Assert.Contains("diagnosticBatch.Count < UiDiagnosticBatchLimit", flush);
        Assert.Contains("Events.AddRange(eventBatch)", flush); // FIFO SOE not latest-wins
        Assert.Contains("Logs.AddRange(diagnosticBatch)", flush);
        Assert.DoesNotContain("_pendingEvents.Clear()", flush);
    }

    [Fact]
    public void ExplorerSearch_HasOneFilterOwner_NoPerRowStringJoinOrDoubleRefresh()
    {
        var primary = Read("MainWindow.LiveSearch.cs");
        var dynamic = Read("MainWindow.LiveSignalSearch.cs");
        var sorting = Read("MainWindow.IecNaturalSort.cs");
        Assert.Contains("if (_liveSignalSearchInstalled", primary);
        Assert.Contains("ApplyLiveSignalSearch();", primary);
        Assert.Contains("_unifiedExplorerSearchText", dynamic);
        Assert.Contains("_unifiedLiveSearchTokens", dynamic);
        Assert.Contains("LiveSignalSearchContains(", dynamic);
        Assert.DoesNotContain("_liveSignalSearchView.Refresh()", dynamic);
        Assert.DoesNotContain("string.Join('\\n', new[]", dynamic);
        Assert.Contains("sourceCollection.Count <= 2048", dynamic);
        Assert.Contains("large.Count > 2048", sorting);
        Assert.Contains("grid.Name == \"ExplorerLiveValuesGrid\"", sorting);
    }

    [Fact]
    public void GooseUiBatch_AvoidsWholeDictionaryAllocation()
    {
        var goose = Read("MainWindow.GooseSubscriber.cs");
        var flush = goose.Split("private void FlushGooseSubscriberUi()")[1]
            .Split("private GooseBindingCatalog BuildGooseBindingCatalog()")[0];
        Assert.Contains("foreach (var pair in _pendingGooseFrames)", flush);
        Assert.DoesNotContain("_pendingGooseFrames.ToArray()", flush);
        Assert.Contains("processed >= 64", flush);
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
