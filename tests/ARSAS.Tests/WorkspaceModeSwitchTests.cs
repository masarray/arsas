namespace ARSAS.Tests;

public sealed class WorkspaceModeSwitchTests
{
    [Fact]
    public void MainWindow_AlwaysExposesEngineeringAndIoFatWorkspaceModes()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.WorkspaceModeSwitch.cs"));

        Assert.Contains("ENGINEERING", source, StringComparison.Ordinal);
        Assert.Contains("IO LIST FAT", source, StringComparison.Ordinal);
        Assert.Contains("Import IO List Excel workbook", source, StringComparison.Ordinal);
        Assert.Contains("Open portable .arsas project", source, StringComparison.Ordinal);
        Assert.Contains("OpenIoListTesting_Click", source, StringComparison.Ordinal);
        Assert.Contains("OpenIoListPackage_Click", source, StringComparison.Ordinal);
        Assert.Contains("FrameworkElement.LoadedEvent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatWorkspace_ShowsSelectedModeAndSafeReturnToEngineering()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.WorkspaceModeSwitch.cs"));
        var windowSource = File.ReadAllText(FindRepoFile("IoListTestingWindow.xaml.cs"));

        Assert.Contains("IO LIST FAT", source, StringComparison.Ordinal);
        Assert.Contains("Engineering Workspace", source, StringComparison.Ordinal);
        Assert.Contains("Save this FAT workspace", source, StringComparison.Ordinal);
        Assert.Contains("private void ReturnToEngineering_Click", windowSource, StringComparison.Ordinal);
        Assert.Contains("Storage?.SaveNow()", windowSource, StringComparison.Ordinal);
        Assert.Contains("Session.Stop", windowSource, StringComparison.Ordinal);
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

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
