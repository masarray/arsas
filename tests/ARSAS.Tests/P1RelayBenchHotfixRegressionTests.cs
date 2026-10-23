using ArIED61850Tester;

namespace ARSAS.Tests;

public sealed class P1RelayBenchHotfixRegressionTests
{
    [Fact]
    public void LegacyRcbFilter_RowAndCheckboxUseIndependentMultiSelectionAndMultiExport()
    {
        var source = Read("RcbExportFilterWindow.P1RelayBench.cs");
        var bridge = Read("MainWindow.P1LegacyRcbMultiExport.cs");

        Assert.Contains("MainWindow.ApplyRcbSelectionForTest", source, StringComparison.Ordinal);
        Assert.Contains("row => row.IsSelected && row.IsSelectable", source, StringComparison.Ordinal);
        Assert.Contains("P1LegacyRcbGrid_PreviewMouseLeftButtonDown", source, StringComparison.Ordinal);
        Assert.Contains("P1LegacyRcbGrid_PreviewKeyDown", source, StringComparison.Ordinal);
        Assert.Contains("ModifierKeys.Shift", source, StringComparison.Ordinal);
        Assert.Contains("ButtonBase.ClickEvent", source, StringComparison.Ordinal);
        Assert.Contains("ExportP1LegacyMultiRcbAsync", source, StringComparison.Ordinal);
        Assert.Contains("ExportGenericMultiRcbAsync", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectOnly(row)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandSafetyDefaults_AreInstalledByModuleInitializer()
    {
        var source = Read("MainWindow.P1CommandSafetyDefaults.cs");
        var defaults = Read("MainWindow.P0CommandDefaults.cs");

        Assert.Contains("[ModuleInitializer]", source, StringComparison.Ordinal);
        Assert.Contains("window.AttachP0CommandDefaults()", source, StringComparison.Ordinal);
        Assert.Contains("signal.ControlInterlockCheck = true", defaults, StringComparison.Ordinal);
        Assert.Contains("signal.ControlSynchroCheck = true", defaults, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true, false, "Suppress")]
    [InlineData(false, true, true, "ConfirmAndPublish")]
    [InlineData(true, true, false, "ReleaseAndPublish")]
    [InlineData(false, false, false, "Suppress")]
    [InlineData(false, false, true, "Publish")]
    public void CommandFreshness_FirstContradictoryReportCannotRollbackConfirmedPosition(
        bool matchingReportSeen,
        bool reportTraffic,
        bool matchesExpected,
        string expected)
    {
        Assert.Equal(
            expected,
            MainWindow.P1DecideCommandFreshnessForTest(
                    matchingReportSeen,
                    reportTraffic,
                    matchesExpected)
                .ToString());
    }

    [Fact]
    public void CommandFreshness_FiltersBeforeEngineeringFatAndSoeQueues()
    {
        var source = Read("MainWindow.P1CommandLiveFreshness.cs");

        Assert.Contains("_runtime.PointUpdated -= Runtime_PointUpdated", source, StringComparison.Ordinal);
        Assert.Contains("_runtime.PointUpdated -= P0FatRuntimePointUpdated", source, StringComparison.Ordinal);
        Assert.Contains("_runtime.PointUpdated += P1CommandLiveFreshness_PointUpdated", source, StringComparison.Ordinal);
        Assert.Contains("Runtime_PointUpdated(snapshot)", source, StringComparison.Ordinal);
        Assert.Contains("P0FatRuntimePointUpdated(snapshot)", source, StringComparison.Ordinal);
        Assert.Contains("_runtime.EventRaised -= Runtime_EventRaised", source, StringComparison.Ordinal);
        Assert.Contains("P1SuppressedCommandEvents", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MatchingReportSeen", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RelayFascia_UsesBlackSvgArtworkAsVectorSource()
    {
        var fascia = Read("Resources/ArvrelMiniIedFascia.xaml");
        var svg = Read("Assets/black-fascia-ied.svg");

        Assert.Contains("Runtime WPF transcription", fascia, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("black-fascia-ied.svg", fascia, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("x:Key=\"ArsasIedConnectionLed\"", fascia, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RelayStateRail\"", fascia, StringComparison.Ordinal);
        Assert.Contains("#FF5538", fascia, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#2DE57A", fascia, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ARSAS Premium IED Icon", svg, StringComparison.Ordinal);
        Assert.Contains("id=\"LED1\"", svg, StringComparison.Ordinal);
        Assert.Contains("id=\"LED2\"", svg, StringComparison.Ordinal);
        Assert.Contains("id=\"LED3\"", svg, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
