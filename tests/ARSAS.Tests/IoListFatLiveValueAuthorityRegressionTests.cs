namespace ARSAS.Tests;

public sealed class IoListFatLiveValueAuthorityRegressionTests
{
    [Fact]
    public void LiveValueColumn_UsesSharedEngineeringAuthorityInsteadOfOnlyRuntimeCopy()
    {
        var legacyUx = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatV2Ux.cs"));
        var authority = File.ReadAllText(FindRepoFile("IoListTestingWindow.LiveValueAuthority.cs"));
        var binding = File.ReadAllText(FindRepoFile("Services/IoTesting/IoTestLiveBindingService.cs"));

        // Root cause guard: FAT v2 originally rendered the copied runtime image, while
        // live binding copied Iec61850MonitorPoint only at binding time.
        Assert.Contains("new Binding(\"Runtime.CurrentValue\")", legacyUx, StringComparison.Ordinal);
        Assert.Contains("point.Runtime.CurrentValue = binding.LivePoint.Value;", binding, StringComparison.Ordinal);

        // The installed field cell must instead subscribe to the shared Engineering
        // monitor/control objects. Runtime remains only a display fallback and evidence
        // state; the UI authority must not depend on another protocol read.
        Assert.Contains("FatAuthoritativeLiveValueCell", authority, StringComparison.Ordinal);
        Assert.Contains("_livePoint.PropertyChanged += LivePoint_PropertyChanged;", authority, StringComparison.Ordinal);
        Assert.Contains("nameof(Iec61850MonitorPoint.Value)", authority, StringComparison.Ordinal);
        Assert.Contains("nameof(SignalDefinition.ControlCurrentValue)", authority, StringComparison.Ordinal);
        Assert.Contains("_plan!.Runtime.CurrentValue", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshIoFatCommandValuesAsync", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadObject", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadAsync", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveValueGrid_DoesNotRecyclePreviousRowContentDuringScroll()
    {
        var authority = File.ReadAllText(FindRepoFile("IoListTestingWindow.LiveValueAuthority.cs"));

        Assert.Contains("VirtualizingPanel.SetIsVirtualizing(grid, true);", authority, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Standard);", authority, StringComparison.Ordinal);
        Assert.Contains("_valueText.Text = \"—\";", authority, StringComparison.Ordinal);
        Assert.Contains("_qualityText.Text = \"Unknown\";", authority, StringComparison.Ordinal);
        Assert.Contains("Cell_DataContextChanged", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveValuePresentation_DoesNotMutateFatEvidenceOrAcquisitionPolicy()
    {
        var authority = File.ReadAllText(FindRepoFile("IoListTestingWindow.LiveValueAuthority.cs"));

        Assert.DoesNotContain("Value1Evidence =", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("Value2Evidence =", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("SetFatValueEvidence", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicDataSetWrites", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("PollingInterval", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("MMS polling", authority, StringComparison.OrdinalIgnoreCase);
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
