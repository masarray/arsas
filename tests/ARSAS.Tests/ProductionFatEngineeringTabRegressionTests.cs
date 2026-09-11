namespace ARSAS.Tests;

public sealed class ProductionFatEngineeringTabRegressionTests
{
    [Fact]
    public void EngineeringProjection_ReusesParsedSclWorkspaceWithoutOpeningXmlAgain()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatEngineeringWorkspaceProjectionService.cs"));

        Assert.Contains("device.SclWorkspace", source, StringComparison.Ordinal);
        Assert.Contains("FatSclWorkspaceImportService.Import(workspaceSources)", source, StringComparison.Ordinal);
        Assert.Contains("ENGINEERING_SCL_DATASET_AUTHORITY", source, StringComparison.Ordinal);
        Assert.Contains("IoFatSourceWorkspaceService.DescribeAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SclWorkspaceService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadScl", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionFatTab_AutoBootstrapsFromSelectedEngineeringStaticDataSet()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatEngineeringBootstrap.cs"));

        Assert.Contains("MainTabs.SelectedIndex != NativeFatWorkspaceIndex", source, StringComparison.Ordinal);
        Assert.Contains("selected?.SclWorkspace", source, StringComparison.Ordinal);
        Assert.Contains("DesignModel.DataSets.Sum", source, StringComparison.Ordinal);
        Assert.Contains("IoFatEngineeringWorkspaceProjectionService.BuildAsync", source, StringComparison.Ordinal);
        Assert.Contains("AdoptEngineeringRuntimeWorkspaces", source, StringComparison.Ordinal);
        Assert.Contains("IoTestWorkspaceBootstrapService.OpenSourcesAsync", source, StringComparison.Ordinal);
        Assert.Contains("SynchronizeImportedSclFatWithEngineering", source, StringComparison.Ordinal);
        Assert.Contains("ShowIoTestingWorkspaceAsync", source, StringComparison.Ordinal);
        Assert.Contains("no SCL re-import", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSclFatTesting_Click", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionFatTab_RegistersExactEngineeringRuntimeWorkspacesForSharedAcquisition()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatSclProjectImportService.cs"));

        Assert.Contains("AdoptEngineeringRuntimeWorkspaces", source, StringComparison.Ordinal);
        Assert.Contains("SetRuntimeWorkspaces(stable)", source, StringComparison.Ordinal);
        Assert.Contains("workspace.WorkspaceKey", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SeventhFatDestination_UsesSameSelectionPillAndActiveForegroundContract()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatNavigationParity.cs"));

        Assert.Contains("NavNativeFatButton", source, StringComparison.Ordinal);
        Assert.Contains("buttons.Length", source, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(tabs.SelectedIndex, 0, 6)", source, StringComparison.Ordinal);
        Assert.Contains("var cellWidth = contentWidth / 7d", source, StringComparison.Ordinal);
        Assert.Contains("pill.Width = Math.Max(1d, cellWidth - 2d)", source, StringComparison.Ordinal);
        Assert.Contains("button.Foreground = index == selectedIndex ? Brushes.White : muted", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ApplicationIdle", source, StringComparison.Ordinal);
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
