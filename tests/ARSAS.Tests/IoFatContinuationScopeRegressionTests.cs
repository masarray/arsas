namespace ARSAS.Tests;

public sealed class IoFatContinuationScopeRegressionTests
{
    [Fact]
    public void CompletedRows_AreExcludedBeforePreflightPreparationAndSessionStart()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.ContextUx.cs"));

        var disableIndex = source.IndexOf(
            "foreach (var point in protectedPoints)\n            point.TestEnabled = false;",
            StringComparison.Ordinal);
        var preflightIndex = source.IndexOf(
            "IoTestSessionPreflight.Validate(selectedIed)",
            StringComparison.Ordinal);
        var preparationIndex = source.IndexOf(
            "PrepareIoTestIedForFatAsync(",
            StringComparison.Ordinal);
        var sessionStartIndex = source.IndexOf(
            "Session.Start(selectedIed)",
            StringComparison.Ordinal);
        var restoreIndex = source.LastIndexOf(
            "foreach (var point in protectedPoints)\n                point.TestEnabled = true;",
            StringComparison.Ordinal);

        Assert.True(disableIndex >= 0, "Completed evidence rows must be disabled for continuation scope.");
        Assert.True(preflightIndex > disableIndex, "Protected rows must be excluded before preflight.");
        Assert.True(preparationIndex > preflightIndex, "Protected rows must remain excluded during live preparation.");
        Assert.True(sessionStartIndex > preparationIndex, "Protected rows must remain excluded through Session.Start.");
        Assert.True(restoreIndex > sessionStartIndex, "Original TestEnabled flags must be restored only after the workflow ends.");
        Assert.Contains("outer finally", source, StringComparison.Ordinal);
        Assert.Contains("otherwise-valid", source, StringComparison.Ordinal);
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
