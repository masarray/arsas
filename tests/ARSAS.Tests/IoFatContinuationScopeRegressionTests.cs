namespace ARSAS.Tests;

public sealed class IoFatContinuationScopeRegressionTests
{
    [Fact]
    public void SelectedRows_AreCarriedExplicitlyWithoutMutatingOperatorSelection()
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
        var sessionStartIndex = source.IndexOf(
            "Session.Start(selectedIed, captureScope)",
            StringComparison.Ordinal);

        Assert.True(preflightIndex >= 0, "The real operator selection must be validated before capture scope creation.");
        Assert.True(scopeIndex > preflightIndex, "Capture scope must be created only after preflight succeeds.");
        Assert.True(preparationIndex > scopeIndex, "The same explicit scope must be used for live preparation.");
        Assert.True(sessionStartIndex > preparationIndex, "The same explicit scope must reach Session.Start.");
        Assert.DoesNotContain("point.TestEnabled = false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("point.TestEnabled = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("protectedPoints", source, StringComparison.Ordinal);
        Assert.Contains("point.IsIncludedInFat && point.TestEnabled && point.ImportReady", source, StringComparison.Ordinal);
        Assert.Contains("operator-snapshot rows expose ✓ Value 1 / Value 2 capture", source, StringComparison.Ordinal);
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