namespace ARSAS.Tests;

public sealed class IoFatBuild1888LiveValueMinimalFixRegressionTests
{
    [Fact]
    public void Build1888FatContract_RemainsValue1Value2WithEngineeringReturnAndCommandPanel()
    {
        var fatV2 = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatV2Ux.cs"));
        var xaml = File.ReadAllText(FindRepoFile("IoListTestingWindow.xaml"));
        var commandPanel = File.ReadAllText(FindRepoFile("IoListTestingWindow.CommandPanel.cs"));

        Assert.Contains("\"VALUE 1\"", fatV2, StringComparison.Ordinal);
        Assert.Contains("\"VALUE 2\"", fatV2, StringComparison.Ordinal);
        Assert.Contains("ReturnToEngineering_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("IED COMMAND PANEL", commandPanel, StringComparison.Ordinal);
    }

    [Fact]
    public void FatLiveMirror_UsesEngineeringUiFlushAndChangesPresentationOnly()
    {
        var mainMirror = File.ReadAllText(FindRepoFile("MainWindow.IoFatLiveValueMirror.cs"));
        var fatMirror = File.ReadAllText(FindRepoFile("IoListTestingWindow.LiveValueMirror.cs"));

        Assert.Contains("_uiFlushTimer.Tick", mainMirror, StringComparison.Ordinal);
        Assert.Contains("RefreshEngineeringLiveMirror(Devices)", mainMirror, StringComparison.Ordinal);
        Assert.Contains("LiveDeviceId", fatMirror, StringComparison.Ordinal);
        Assert.Contains("LiveSignalReference", fatMirror, StringComparison.Ordinal);
        Assert.Contains("Runtime.CurrentValue", fatMirror, StringComparison.Ordinal);
        Assert.Contains("Runtime.CurrentQuality", fatMirror, StringComparison.Ordinal);
        Assert.Contains("Runtime.CurrentSource", fatMirror, StringComparison.Ordinal);

        Assert.DoesNotContain("Value1Evidence =", fatMirror, StringComparison.Ordinal);
        Assert.DoesNotContain("Value2Evidence =", fatMirror, StringComparison.Ordinal);
        Assert.DoesNotContain("SetFatValueEvidence", fatMirror, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteControl", fatMirror, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadValue", fatMirror, StringComparison.Ordinal);
        Assert.DoesNotContain("PropertyChanged +=", fatMirror, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectionChanged +=", fatMirror, StringComparison.Ordinal);
    }

    [Fact]
    public void FatScrollFix_KeepsVirtualizationButDisablesRowRecyclingOnly()
    {
        var fatMirror = File.ReadAllText(FindRepoFile("IoListTestingWindow.LiveValueMirror.cs"));

        Assert.Contains("VirtualizingPanel.SetIsVirtualizing(grid, true)", fatMirror, StringComparison.Ordinal);
        Assert.Contains("VirtualizationMode.Standard", fatMirror, StringComparison.Ordinal);
        Assert.Contains("grid.EnableRowVirtualization = true", fatMirror, StringComparison.Ordinal);
        Assert.DoesNotContain("VirtualizationMode.Recycling", fatMirror, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableColumnVirtualization = false", fatMirror, StringComparison.Ordinal);
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
