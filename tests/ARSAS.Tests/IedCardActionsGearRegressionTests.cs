namespace ARSAS.Tests;

public sealed class IedCardActionsGearRegressionTests
{
    [Fact]
    public void IedCard_HasDedicatedGearEntryPointForReusableActions()
    {
        var source = File.ReadAllText(FindRepoFile("IedCardActionsGearPolicy.cs"));

        Assert.Contains("GearUid = \"ARSAS.IedActionsGear\"", source, StringComparison.Ordinal);
        Assert.Contains("IED Actions — Static DataSet, Select Signals, RCB Engineering, COMTRADE, Browse Offline", source, StringComparison.Ordinal);
        Assert.Contains("OpenIedWorkspaceActionsAsync(device)", source, StringComparison.Ordinal);
        Assert.Contains("actionBar.Children.Add(button)", source, StringComparison.Ordinal);
        Assert.Contains("actionBar.Columns = Math.Max(actionBar.Columns, actionBar.Children.Count)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Gear_IsSeparateFromExistingEditSignalsShortcut()
    {
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        var source = File.ReadAllText(FindRepoFile("IedCardActionsGearPolicy.cs"));

        Assert.Contains("Click=\"IedConfigureSignals_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LucidePencilLine", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IedConfigureSignals_Click", source, StringComparison.Ordinal);
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

        throw new FileNotFoundException(relativePath);
    }
}
