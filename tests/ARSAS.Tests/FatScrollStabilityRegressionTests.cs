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
    public void Recovery_UsesNonRecyclingVirtualizationBeforeFirstMeasureWithoutLiveSubscriptions()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatScrollStability.cs"));

        Assert.Contains("protected override void OnInitialized(EventArgs e)", source, StringComparison.Ordinal);
        Assert.Contains("ApplyFatScrollStabilityBeforeFirstMeasure();", source, StringComparison.Ordinal);
        Assert.Contains("if (grid.IsMeasureValid)", source, StringComparison.Ordinal);
        Assert.Contains("grid.EnableRowVirtualization = true;", source, StringComparison.Ordinal);
        Assert.Contains("grid.EnableColumnVirtualization = false;", source, StringComparison.Ordinal);
        Assert.Contains("VirtualizationMode.Standard", source, StringComparison.Ordinal);

        // VirtualizationMode is immutable after the ItemsHost has entered Measure. Never
        // regress to the late Loaded handler that caused the physical-bench UI exception.
        Assert.DoesNotContain("FrameworkElement.LoadedEvent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterClassHandler", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded +=", source, StringComparison.Ordinal);

        Assert.DoesNotContain("PropertyChanged +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectionChanged +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatcher.BeginInvoke", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatcher.Invoke", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Runtime.CurrentValue =", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_ScrollPatchCannotOwnFatMembershipSessionOrAcquisition()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatScrollStability.cs"));

        Assert.DoesNotContain("ItemsSource =", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Filter =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Columns.Clear", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartSession", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareIoTestIedForFatAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyStaticDataSetSelection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReportControl", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Iec61850", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Storage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Evidence", source, StringComparison.Ordinal);
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
