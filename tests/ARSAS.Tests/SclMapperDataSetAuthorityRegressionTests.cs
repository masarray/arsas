using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SclMapperDataSetAuthorityRegressionTests
{
    [Fact]
    public void SclMapper_DoesNotDeduplicateRuntimeReferencesAfterAuthoritativeDataSetMerge()
    {
        var source = File.ReadAllText(FindRepoFile("Services/SclWorkspaceSignalMapper.cs"));
        var merge = source.IndexOf(
            "Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(",
            StringComparison.Ordinal);
        var finalReturn = source.IndexOf("return visibleSignals", merge, StringComparison.Ordinal);
        var nextMethod = source.IndexOf("private static void AddRuntimeSignals", finalReturn, StringComparison.Ordinal);

        Assert.True(merge >= 0, "The ARIEC DataSet authority merge must remain in the SCL mapper.");
        Assert.True(finalReturn > merge, "The mapper must return the authority-merged inventory.");
        Assert.True(nextMethod > finalReturn);

        var postMergeProjection = source[merge..nextMethod];
        Assert.DoesNotContain(
            ".GroupBy(signal => NormalizePresentationReference(signal.ObjectReference)",
            postMergeProjection,
            StringComparison.Ordinal);
        Assert.Contains("return visibleSignals", postMergeProjection, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(signal => signal.SortPriority)", postMergeProjection, StringComparison.Ordinal);
    }

    [Fact]
    public void SclMapper_KeepsGenericRuntimeDedupBeforeAuthorityMerge()
    {
        var source = File.ReadAllText(FindRepoFile("Services/SclWorkspaceSignalMapper.cs"));
        var merge = source.IndexOf(
            "Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(",
            StringComparison.Ordinal);
        var preMergeProjection = source[..merge];

        Assert.Contains(
            ".GroupBy(signal => NormalizePresentationReference(signal.ObjectReference)",
            preMergeProjection,
            StringComparison.Ordinal);
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
