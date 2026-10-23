namespace ARSAS.Tests;

public sealed class IoFatContinuationScopeRegressionTests
{
    [Fact]
    public void SelectedRows_DefineEvidenceScopeWhileFullIedScopeOwnsLivePreparation()
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
            "var preparation = await engineeringWindow.PrepareIoTestIedForFatAsync(\n                    Project,\n                    selectedIed,\n                    progress);",
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

        Assert.True(preflightIndex >= 0, "The operator-selected evidence scope must be validated before capture scope creation.");
        Assert.True(scopeIndex > preflightIndex, "Capture scope must be created only after preflight succeeds.");
        Assert.True(preparationIndex > scopeIndex, "P1 live preparation must use the full included IED acquisition scope, not the TEST subset.");
        Assert.True(liveScopeIndex > preparationIndex, "Evidence scope must be filtered to proven live rows only after full IED preparation.");
        Assert.True(liveReadyIndex >= liveScopeIndex, "Active evidence scope must require LivePointReady rows.");
        Assert.True(sessionStartIndex > liveScopeIndex, "Only the proven selected live subset may reach Session.Start.");
        Assert.DoesNotContain("progress,\n                    captureScope);", source, StringComparison.Ordinal);
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
