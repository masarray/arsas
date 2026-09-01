namespace ARSAS.Tests;

public sealed class IoFatV2UiLockRegressionTests
{
    [Fact]
    public void RuntimeGrid_LocksPlanMutationButKeepsOperatorCaptureAvailable()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatV2Ux.cs"));

        Assert.Contains("DataContext.CanEditPlan", source, StringComparison.Ordinal);
        Assert.Contains("point.RemoveFromFat();", source, StringComparison.Ordinal);
        Assert.Contains("if (!CanEditPlan)", source, StringComparison.Ordinal);
        Assert.Contains("CanCaptureOperatorSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("Session.CaptureOperatorSnapshot(point, slot)", source, StringComparison.Ordinal);
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