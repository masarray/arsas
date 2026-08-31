namespace ARSAS.Tests;

public sealed class IoFatSelectionAuthorityRegressionTests
{
    [Fact]
    public void SnapshotAndPackageRestore_DoNotAutoDisableCompletedRows()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoTestWorkspaceBootstrapService.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain("ExcludeCompletedFromNextSession", source, StringComparison.Ordinal);
        Assert.DoesNotContain("point.TestEnabled = false", source, StringComparison.Ordinal);
        Assert.Contains("point.TestEnabled = enabled.GetBoolean();", source, StringComparison.Ordinal);
        Assert.Contains("Persisted TestEnabled is operator-authored state", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanRetest_ClearsEvidenceButNeverChangesOperatorSelection()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatCleanSessionService.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain("point.TestEnabled =", source, StringComparison.Ordinal);
        Assert.Contains("runtime.OnEvidence = null;", source, StringComparison.Ordinal);
        Assert.Contains("runtime.OffEvidence = null;", source, StringComparison.Ordinal);
        Assert.Contains("Selection belongs to the operator", source, StringComparison.Ordinal);
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
