namespace ARSAS.Tests;

public sealed class FirstRunLauncherRegressionTests
{
    [Fact]
    public void FirstRunLauncher_SelectsOperationalCardWithoutAssumingSingleBorder()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.cs"));

        Assert.DoesNotContain("heroGrid.Children.OfType<Border>().SingleOrDefault()", source, StringComparison.Ordinal);
        Assert.Contains("P2IndustrialHeroTint", source, StringComparison.Ordinal);
        Assert.Contains("border.Child is StackPanel", source, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefault(border =>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstRunLauncher_RepairsP2OverlayBeforeRebuildingOperationalCards()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.FirstRunLauncherRepair.cs"));

        var findTint = source.IndexOf("P2IndustrialHeroTint", StringComparison.Ordinal);
        var removeTint = source.IndexOf("heroGrid.Children.Remove(tint)", StringComparison.Ordinal);
        var installLauncher = source.IndexOf("InstallFirstRunTestingChoices();", StringComparison.Ordinal);
        var reapplyP2 = source.IndexOf("P2IndustrialWorkstationUx.Apply(this)", StringComparison.Ordinal);

        Assert.True(findTint >= 0);
        Assert.True(removeTint > findTint);
        Assert.True(installLauncher > removeTint);
        Assert.True(reapplyP2 > installLauncher);
        Assert.Contains("Panel.SetZIndex(chooser, 2)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstRunLauncher_ExposesSclIpAndExcelAsPrimaryOperatorPaths()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.FirstRunLauncherRepair.cs"));
        var baseLauncher = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.cs"));

        Assert.Contains("Add IED by SCL or IP address", source, StringComparison.Ordinal);
        Assert.Contains("\"Open SCL\"", source, StringComparison.Ordinal);
        Assert.Contains("OpenScl_Click", source, StringComparison.Ordinal);
        Assert.Contains("\"Add IED by IP\"", source, StringComparison.Ordinal);
        Assert.Contains("AddRelay_Click", source, StringComparison.Ordinal);
        Assert.Contains("Import Excel IO List", source, StringComparison.Ordinal);
        Assert.Contains("Import the ARSAS IO List FAT Excel workbook (.xlsx)", source, StringComparison.Ordinal);

        // The repair must preserve the original two-card launcher and Excel import engine,
        // not replace either workflow with a new copy of project state.
        Assert.Contains("InstallFirstRunTestingChoices", baseLauncher, StringComparison.Ordinal);
        Assert.Contains("CreateIoListTestingCard", baseLauncher, StringComparison.Ordinal);
        Assert.Contains("OpenIoListTesting_Click", baseLauncher, StringComparison.Ordinal);
        Assert.Contains("_ioListExcelImportService.ImportAsync", baseLauncher, StringComparison.Ordinal);
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
