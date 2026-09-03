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
        Assert.Contains("CurrentEngineeringSclSourcePaths", source, StringComparison.Ordinal);
        Assert.Contains("OpenSclFatSourcesAsync(sharedSources, selectionMode: null)", source, StringComparison.Ordinal);
        Assert.Contains("Continue loaded FAT project", source, StringComparison.Ordinal);
        Assert.Contains("Import SCL / CID files", source, StringComparison.Ordinal);
        Assert.Contains("Add SCL / CID to loaded FAT workspace", source, StringComparison.Ordinal);
        Assert.Contains("OpenSclForLoadedFatAppendAsync(loaded)", source, StringComparison.Ordinal);
        Assert.Contains("OpenSclFatTesting_Click", source, StringComparison.Ordinal);
        Assert.Contains("Import another IO List Excel workbook", source, StringComparison.Ordinal);
        Assert.Contains("Open another portable .arsas project", source, StringComparison.Ordinal);
        Assert.Contains("QueueIoFatWorkspaceReplacement", source, StringComparison.Ordinal);
        Assert.Contains("FrameworkElement.LoadedEvent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SclImport_UsesOneSelectionAuthorityAcrossEngineeringAndFat()
    {
        var workflow = File.ReadAllText(FindRepoFile("MainWindow.SharedSclWorkspace.cs"));
        var selectionWindow = File.ReadAllText(FindRepoFile("SclSignalSelectionModeWindow.xaml"));
        var engineering = File.ReadAllText(FindRepoFile("MainWindow.xaml.cs"));
        var fat = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.cs"));
        var fatProjection = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatV2Ux.cs"));

        Assert.Contains("new SclSignalSelectionModeWindow", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox.Show", workflow, StringComparison.Ordinal);
        Assert.Contains("Use Static DataSet", selectionWindow, StringComparison.Ordinal);
        Assert.Contains("Choose Signals Manually", selectionWindow, StringComparison.Ordinal);
        Assert.Contains("SelectionCard", selectionWindow, StringComparison.Ordinal);
        Assert.Contains("CardShadow", selectionWindow, StringComparison.Ordinal);
        Assert.Contains("PrimaryButton", selectionWindow, StringComparison.Ordinal);
        Assert.Contains("SoftButton", selectionWindow, StringComparison.Ordinal);
        Assert.Contains("_sharedSclSelectionAuthorityDeviceIds", workflow, StringComparison.Ordinal);
        Assert.Contains("ApplyStaticDataSetSelection", engineering, StringComparison.Ordinal);
        Assert.Contains("selectionAlreadyApplied: true", engineering, StringComparison.Ordinal);
        Assert.Contains("ApplyManualSelectionToFatProjectAsync", fat, StringComparison.Ordinal);
        Assert.Contains("promptForSelection: true", fat, StringComparison.Ordinal);
        Assert.Contains("PromptSclSignalSelectionMode(this, import.Project.Ieds.Count)", fat, StringComparison.Ordinal);
        Assert.Contains("point.IsIncludedInFat &&", fatProjection, StringComparison.Ordinal);
        Assert.Contains("point.TestEnabled", fatProjection, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringSclImport_IsAdditiveAndSupportsMultipleFiles()
    {
        var engineering = File.ReadAllText(FindRepoFile("MainWindow.xaml.cs"));

        Assert.Contains("Title = \"Open one or more IEC 61850 SCL files\"", engineering, StringComparison.Ordinal);
        Assert.Contains("Multiselect = true", engineering, StringComparison.Ordinal);
        Assert.Contains("foreach (var sourcePath in dialog.FileNames.Distinct", engineering, StringComparison.Ordinal);
        Assert.Contains("var importedSources = new List<(string Path, string Sha256)>()", engineering, StringComparison.Ordinal);
        Assert.Contains("newFatSourcePaths", engineering, StringComparison.Ordinal);
        Assert.Contains("AppendSclIedsToLoadedFatAsync", engineering, StringComparison.Ordinal);
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
    public void LoadingAnotherFatProject_IsExplicitWhileLoadedSclImportIsAdditive()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.WorkspaceModeSwitch.cs"));
        var hostSource = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.cs"));

        Assert.Contains("Save and close that workspace before loading another project?", source, StringComparison.Ordinal);
        Assert.Contains("loaded.Close();", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke(openReplacement", source, StringComparison.Ordinal);

        // P0.4 keeps SCL additive while workbook/project opens remain explicit replacement.
        Assert.Contains("OpenSclForLoadedFatAppendAsync(loaded)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "QueueIoFatWorkspaceReplacement(\n            () => OpenSclFatTesting_Click",
            source,
            StringComparison.Ordinal);
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
