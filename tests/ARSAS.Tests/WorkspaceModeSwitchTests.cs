namespace ARSAS.Tests;

public sealed class WorkspaceModeSwitchTests
{
    [Fact]
    public void MainWindow_AlwaysExposesEngineeringAndPersistentIoFatWorkspaceModes()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.WorkspaceModeSwitch.cs"));

        Assert.Contains("ENGINEERING", source, StringComparison.Ordinal);
        Assert.Contains("IO LIST FAT", source, StringComparison.Ordinal);
        Assert.Contains("IO LIST FAT · LOADED", source, StringComparison.Ordinal);
        Assert.Contains("_loadedIoFatWindow", source, StringComparison.Ordinal);
        Assert.Contains("ShowLoadedIoFatWorkspace", source, StringComparison.Ordinal);
        Assert.Contains("Continue loaded FAT project", source, StringComparison.Ordinal);
        Assert.Contains("Import another IO List Excel workbook", source, StringComparison.Ordinal);
        Assert.Contains("Open another portable .arsas project", source, StringComparison.Ordinal);
        Assert.Contains("QueueIoFatWorkspaceReplacement", source, StringComparison.Ordinal);
        Assert.Contains("FrameworkElement.LoadedEvent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatWorkspace_ReturnsToEngineeringByHideAndShowWithoutUnloadingProject()
    {
        var switchSource = File.ReadAllText(FindRepoFile("IoListTestingWindow.WorkspaceModeSwitch.cs"));
        var hostSource = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.cs"));

        Assert.Contains("IO LIST FAT · LOADED", switchSource, StringComparison.Ordinal);
        Assert.Contains("Engineering Workspace", switchSource, StringComparison.Ordinal);
        Assert.Contains("Return to Engineering without unloading this FAT project", switchSource, StringComparison.Ordinal);
        Assert.Contains("owner.ShowEngineeringWorkspaceFromFat(this)", switchSource, StringComparison.Ordinal);
        Assert.Contains("Storage?.ScheduleSave()", switchSource, StringComparison.Ordinal);
        Assert.Contains("Hide();", switchSource, StringComparison.Ordinal);

        Assert.Contains("RegisterLoadedIoFatWindow(window)", hostSource, StringComparison.Ordinal);
        Assert.Contains("window.Show();", hostSource, StringComparison.Ordinal);
        Assert.Contains("_activeIoTestSessionController = controller", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("window.ShowDialog();", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("using var controller = launch.Session", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("using var persistence = launch.Workspace", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadingAnotherFatProject_IsExplicitAndClosesTheLoadedWorkspaceFirst()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.WorkspaceModeSwitch.cs"));
        var hostSource = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.cs"));

        Assert.Contains("Save and close that workspace before loading another project?", source, StringComparison.Ordinal);
        Assert.Contains("loaded.Close();", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke(openReplacement", source, StringComparison.Ordinal);
        Assert.Contains("QueueIoFatWorkspaceReplacement(() => OpenIoListTesting_Click", hostSource, StringComparison.Ordinal);
        Assert.Contains("QueueIoFatWorkspaceReplacement(() => OpenIoListPackage_Click", hostSource, StringComparison.Ordinal);
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
