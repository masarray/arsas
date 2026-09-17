namespace ARSAS.Tests;

public sealed class RcbExportEvidenceAcquisitionRegressionTests
{
    [Fact]
    public void LiveExport_SelfAcquiresFcdaEvidence_WhenBoundDataSetIsIncomplete()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.RcbExport.cs"));

        Assert.Contains("FCDA evidence missing", source, StringComparison.Ordinal);
        Assert.Contains("_rcbAvailabilityProbe", source, StringComparison.Ordinal);
        Assert.Contains(".CheckAsync(device, evidenceTimeout.Token)", source, StringComparison.Ordinal);
        Assert.Contains("evidenceTimeout.CancelAfter(TimeSpan.FromSeconds(30))", source, StringComparison.Ordinal);
        Assert.Contains("MergeSelectedDataSetDirectory", source, StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Run Check Availability to browse DataSet directories, then export again.",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LiveExport_ReusesExistingAvailability_BeforeOpeningFallbackAudit()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.RcbExport.cs"));

        var reuseIndex = source.IndexOf(
            "if (effectiveAvailability != null)",
            StringComparison.Ordinal);
        var fallbackProbeIndex = source.IndexOf(
            "effectiveAvailability = await _rcbAvailabilityProbe",
            StringComparison.Ordinal);

        Assert.True(reuseIndex >= 0, "Existing availability evidence must be considered first.");
        Assert.True(fallbackProbeIndex > reuseIndex,
            "Fallback MMS audit must only occur after existing availability evidence has been considered.");
    }

    [Fact]
    public void LiveExport_RefusesSerialization_WhenAuthoritativeDirectoryStillHasNoFcdaMembers()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.RcbExport.cs"));

        var rejectionIndex = source.IndexOf(
            "authoritative read-only MMS audit did not return any FCDA members. The CID was not written.",
            StringComparison.Ordinal);
        var serializationIndex = source.IndexOf(
            "AuthoritativeLiveIedSclExporter.WriteFiles",
            StringComparison.Ordinal);

        Assert.True(rejectionIndex >= 0, "Incomplete FCDA evidence must have an explicit export rejection gate.");
        Assert.True(serializationIndex > rejectionIndex,
            "The incomplete-evidence rejection gate must execute before SCL serialization.");
    }

    [Fact]
    public void LiveExport_ReResolvesDataSetReference_AfterLiveRcbEvidenceMerge()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.RcbExport.cs"));

        var mergeIndex = source.IndexOf(
            "MergeSelectedReportControlEvidence",
            StringComparison.Ordinal);
        var resolveIndex = source.IndexOf(
            "ResolveExportDataSetReference(exportModel, row)",
            StringComparison.Ordinal);

        Assert.True(mergeIndex >= 0, "Live RCB evidence must be merged into the export model.");
        Assert.True(resolveIndex > mergeIndex,
            "The effective DataSet reference must be resolved from the merged live model, not stale UI evidence.");
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

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
