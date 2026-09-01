namespace ARSAS.Tests;

public sealed class IoFatContinuationScopeRegressionTests
{
    [Fact]
    public void SelectedRows_ArePreparedExplicitlyAndOnlyLiveSubsetIsArmedWithoutMutatingOperatorSelection()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.ContextUx.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var preflightIndex = source.IndexOf(
            "IoTestSessionPreflight.Validate(selectedIed)",
            StringComparison.Ordinal);
        var scopeIndex = source.IndexOf(
            "var captureScope = selectedIed.TestPoints",
            StringComparison.Ordinal);
        var preparationIndex = source.IndexOf(
            "progress,\n                    captureScope);",
            StringComparison.Ordinal);
        var liveScopeIndex = source.IndexOf(
            "var liveCaptureScope = captureScope",
            StringComparison.Ordinal);
        var liveReadyIndex = source.IndexOf(
            "point.LiveBindingState == IoTestLiveBindingState.LivePointReady",
            StringComparison.Ordinal);
        var sessionStartIndex = source.IndexOf(
            "Session.Start(selectedIed, liveCaptureScope)",
            StringComparison.Ordinal);

        Assert.True(preflightIndex >= 0, "The real operator selection must be validated before capture scope creation.");
        Assert.True(scopeIndex > preflightIndex, "Capture scope must be created only after preflight succeeds.");
        Assert.True(preparationIndex > scopeIndex, "The complete operator-selected scope must be used for live preparation.");
        Assert.True(liveScopeIndex > preparationIndex, "Live evidence scope must be derived only after preparation has produced binding evidence.");
        Assert.True(liveReadyIndex >= liveScopeIndex, "Active evidence scope must require LivePointReady rows.");
        Assert.True(sessionStartIndex > liveScopeIndex, "Only the proven live subset may reach Session.Start.");
        Assert.DoesNotContain("point.TestEnabled = false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("point.TestEnabled = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("protectedPoints", source, StringComparison.Ordinal);
        Assert.Contains("point.IsIncludedInFat && point.TestEnabled && point.ImportReady", source, StringComparison.Ordinal);
        Assert.Contains("checkbox/disposition unchanged", source, StringComparison.Ordinal);
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
