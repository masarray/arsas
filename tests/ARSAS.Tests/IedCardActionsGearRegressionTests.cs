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
        Assert.Contains("actionBar.Children.Add(CreateGearButton(device))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Gear_AndExistingActions_AreForcedInsideCompactCardWidth()
    {
        var source = File.ReadAllText(FindRepoFile("IedCardActionsGearPolicy.cs"));

        Assert.Contains("DispatcherPriority.ContextIdle", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeActionBar(actionBar)", source, StringComparison.Ordinal);
        Assert.Contains("actionBar.Columns = buttons.Length", source, StringComparison.Ordinal);
        Assert.Contains("button.Width = double.NaN", source, StringComparison.Ordinal);
        Assert.Contains("button.MinWidth = 0", source, StringComparison.Ordinal);
        Assert.Contains("button.HorizontalAlignment = HorizontalAlignment.Stretch", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Width = 27", source, StringComparison.Ordinal);
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
