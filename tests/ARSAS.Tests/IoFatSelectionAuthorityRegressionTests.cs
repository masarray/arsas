namespace ARSAS.Tests;

public sealed class IoFatSelectionAuthorityRegressionTests
{
    [Fact]
    public void SnapshotAndPackageRestore_DoNotAutoDisableCompletedRows()
    {
        var source = Read("Services/IoTesting/IoTestWorkspaceBootstrapService.cs");

        Assert.DoesNotContain("ExcludeCompletedFromNextSession", source, StringComparison.Ordinal);
        Assert.DoesNotContain("point.TestEnabled = false", source, StringComparison.Ordinal);
        Assert.Contains("point.TestEnabled = enabled.GetBoolean();", source, StringComparison.Ordinal);
        Assert.Contains("WorkspaceSelected", source, StringComparison.Ordinal);
        Assert.Contains("FAT TEST scope, and FAT disposition", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanRetest_ClearsEvidenceButNeverChangesOperatorSelection()
    {
        var source = Read("Services/IoTesting/IoFatCleanSessionService.cs");

        Assert.DoesNotContain("point.TestEnabled =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("point.WorkspaceSelected =", source, StringComparison.Ordinal);
        Assert.Contains("runtime.OnEvidence = null;", source, StringComparison.Ordinal);
        Assert.Contains("runtime.OffEvidence = null;", source, StringComparison.Ordinal);
        Assert.Contains("Selection belongs to the operator", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StartAndContinuation_NeverRewriteCheckboxStateAndArmOnlyLiveRows()
    {
        var source = Read("IoListTestingWindow.ContextUx.cs");

        Assert.DoesNotContain("point.TestEnabled = false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("point.TestEnabled = true", source, StringComparison.Ordinal);
        Assert.Contains("point.WorkspaceSelected && point.IsIncludedInFat && point.TestEnabled && point.ImportReady", source, StringComparison.Ordinal);
        Assert.Contains("point.LiveBindingState == IoTestLiveBindingState.LivePointReady", source, StringComparison.Ordinal);
        Assert.Contains("Session.Start(selectedIed, liveCaptureScope)", source, StringComparison.Ordinal);
        Assert.Contains("checkbox/disposition unchanged", source, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(FindRepoFile(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

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
