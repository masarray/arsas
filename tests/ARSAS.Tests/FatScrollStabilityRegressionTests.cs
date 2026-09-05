namespace ARSAS.Tests;

public sealed class FatScrollStabilityRegressionTests
{
    [Fact]
    public void Recovery_KeepsBuild1868Value1Value2GridContract()
    {
        var ux = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatV2Ux.cs"));

        Assert.Contains("Header = \"TEST\"", ux, StringComparison.Ordinal);
        Assert.Contains("TextColumn(\"SIGNAL\"", ux, StringComparison.Ordinal);
        Assert.Contains("TextColumn(\"IEC REFERENCE\"", ux, StringComparison.Ordinal);
        Assert.Contains("TextColumn(\"TYPE\"", ux, StringComparison.Ordinal);
        Assert.Contains("Header = \"LIVE VALUE\"", ux, StringComparison.Ordinal);
        Assert.Contains("Header = slot == FatValueSlot.Value1 ? \"VALUE 1\" : \"VALUE 2\"", ux, StringComparison.Ordinal);
        Assert.Contains("TextColumn(\"STATUS\"", ux, StringComparison.Ordinal);
        Assert.Contains("TextColumn(\"RESULT\"", ux, StringComparison.Ordinal);
        Assert.DoesNotContain("ON · RELAY TIME", ux, StringComparison.Ordinal);
        Assert.DoesNotContain("OFF · RELAY TIME", ux, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_RestoresGoldenWpfVirtualizationAndForbidsRuntimeModeMutation()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "IoListTestingWindow.xaml"));

        // The physical-bench failure proved that VirtualizationMode must never be mutated
        // from Loaded/OnInitialized/runtime code. Restore the Build #1868 XAML-owned policy.
        Assert.Contains("EnableRowVirtualization=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("EnableColumnVirtualization=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", xaml, StringComparison.Ordinal);

        Assert.False(
            File.Exists(Path.Combine(root, "IoListTestingWindow.FatScrollStability.cs")),
            "Do not reintroduce a runtime FAT virtualization patch. WPF throws if VirtualizationMode is changed after the ItemsHost has entered Measure.");

        foreach (var file in Directory.EnumerateFiles(root, "IoListTestingWindow*.cs", SearchOption.TopDirectoryOnly))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("VirtualizingPanel.SetVirtualizationMode", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Recovery_KeepsGoldenFatMembershipAndEngineeringReturnPath()
    {
        var fatUx = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatV2Ux.cs"));
        var fatModeSwitch = File.ReadAllText(FindRepoFile("IoListTestingWindow.WorkspaceModeSwitch.cs"));
        var mainModeSwitch = File.ReadAllText(FindRepoFile("MainWindow.WorkspaceModeSwitch.cs"));
        var sharedScl = File.ReadAllText(FindRepoFile("MainWindow.SharedSclWorkspace.cs"));
        var fatHost = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.cs"));

        Assert.Contains("point.WorkspaceSelected", fatUx, StringComparison.Ordinal);
        Assert.Contains("point.IsIncludedInFat", fatUx, StringComparison.Ordinal);

        Assert.Contains("Engineering Workspace", fatModeSwitch, StringComparison.Ordinal);
        Assert.Contains("Return to Engineering without unloading this FAT project", fatModeSwitch, StringComparison.Ordinal);
        Assert.Contains("owner.ShowEngineeringWorkspaceFromFat(this)", fatModeSwitch, StringComparison.Ordinal);
        Assert.Contains("Hide();", fatModeSwitch, StringComparison.Ordinal);

        Assert.Contains("_loadedIoFatWindow", mainModeSwitch, StringComparison.Ordinal);
        Assert.Contains("ShowLoadedIoFatWorkspace", mainModeSwitch, StringComparison.Ordinal);
        Assert.Contains("CurrentEngineeringSclSourcePaths", mainModeSwitch, StringComparison.Ordinal);
        Assert.Contains("OpenSclFatSourcesAsync(sharedSources, selectionMode: null)", mainModeSwitch, StringComparison.Ordinal);

        Assert.Contains("_sharedSclSelectionAuthorityDeviceIds", sharedScl, StringComparison.Ordinal);
        Assert.Contains("RegisterLoadedIoFatWindow(window)", fatHost, StringComparison.Ordinal);
        Assert.DoesNotContain("window.ShowDialog();", fatHost, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_DoesNotReintroducePerCellLiveValueAuthorityPatch()
    {
        var root = FindRepoRoot();
        var forbiddenPath = Path.Combine(root, "IoListTestingWindow.LiveValueAuthority.cs");

        Assert.False(
            File.Exists(forbiddenPath),
            "The per-cell LIVE VALUE authority patch is forbidden on the Build #1868 recovery branch because it can fan out UI-thread subscriptions and regress FAT lifecycle behavior.");
    }

    private static string FindRepoFile(string relativePath)
        => Path.Combine(FindRepoRoot(), relativePath);

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IoListTestingWindow.FatV2Ux.cs")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repository root from '{AppContext.BaseDirectory}'.");
    }
}
