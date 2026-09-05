namespace ARSAS.Tests;

public sealed class IoListFatLiveValueAuthorityRegressionTests
{
    [Fact]
    public void LiveValueColumn_UsesSharedEngineeringAuthorityInsteadOfOnlyRuntimeCopy()
    {
        var legacyUx = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatV2Ux.cs"));
        var authority = File.ReadAllText(FindRepoFile("IoListTestingWindow.LiveValueAuthority.cs"));
        var binding = File.ReadAllText(FindRepoFile("Services/IoTesting/IoTestLiveBindingService.cs"));

        Assert.Contains("new Binding(\"Runtime.CurrentValue\")", legacyUx, StringComparison.Ordinal);
        Assert.Contains("point.Runtime.CurrentValue = binding.LivePoint.Value;", binding, StringComparison.Ordinal);

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
    public void LiveValuePresentation_PrefersActualMonitorPointOverControlCache()
    {
        var authority = File.ReadAllText(FindRepoFile("IoListTestingWindow.LiveValueAuthority.cs"));

        Assert.Contains("var liveValue = _livePoint?.Value;", authority, StringComparison.Ordinal);
        Assert.Contains("var controlValue = _controlSignal?.ControlCurrentValue;", authority, StringComparison.Ordinal);
        Assert.Contains("var rawValue = IsInitialized(liveValue)", authority, StringComparison.Ordinal);
        Assert.Contains("? liveValue!", authority, StringComparison.Ordinal);
        Assert.Contains(": IsInitialized(controlValue)", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveValueGrid_NeverMutatesVirtualizationModeAfterLayout()
    {
        var authority = File.ReadAllText(FindRepoFile("IoListTestingWindow.LiveValueAuthority.cs"));

        Assert.DoesNotContain("VirtualizingPanel.SetVirtualizationMode(", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("VirtualizingPanel.SetIsVirtualizing(", authority, StringComparison.Ordinal);
        Assert.Contains("Cell_DataContextChanged", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveValuePresentation_NormalizesBooleanTextAndSkipsRedundantPaints()
    {
        var authority = File.ReadAllText(FindRepoFile("IoListTestingWindow.LiveValueAuthority.cs"));

        Assert.Contains("bool.TryParse(text, out var booleanValue)", authority, StringComparison.Ordinal);
        Assert.Contains("booleanValue ? bool.TrueString : bool.FalseString", authority, StringComparison.Ordinal);
        Assert.Contains("if (!string.Equals(target.Text, value, StringComparison.Ordinal))", authority, StringComparison.Ordinal);
        Assert.Contains("SetTextIfChanged(_valueText, NormalizeDisplayValue(rawValue, \"—\"));", authority, StringComparison.Ordinal);
        Assert.Contains("SetTextIfChanged(_qualityText, NormalizeDisplayValue(rawQuality, \"Unknown\"));", authority, StringComparison.Ordinal);
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
