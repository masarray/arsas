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
    [InlineData(false, true, false, P1CommandFreshnessDecision.Suppress)]
    [InlineData(false, true, true, P1CommandFreshnessDecision.ConfirmAndPublish)]
    [InlineData(true, true, false, P1CommandFreshnessDecision.ReleaseAndPublish)]
    [InlineData(false, false, false, P1CommandFreshnessDecision.Suppress)]
    [InlineData(false, false, true, P1CommandFreshnessDecision.Publish)]
    public void CommandFreshness_FirstContradictoryReportCannotRollbackConfirmedPosition(
        bool matchingReportSeen,
        bool reportTraffic,
        bool matchesExpected,
        P1CommandFreshnessDecision expected)
    {
        Assert.Equal(
            expected,
            MainWindow.P1DecideCommandFreshnessForTest(
                matchingReportSeen,
                reportTraffic,
                matchesExpected));
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
    public void RelayFascia_UsesUploadedSvgArtworkAsVectorSource()
    {
        var fascia = Read("Resources/ArvrelMiniIedFascia.xaml");
        var svg = Read("Assets/RelayFascia.svg");

        Assert.Contains("direct WPF vector transcription", fascia, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("x:Name=\"RelayFasciaArtwork\"", fascia, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RelayStateRail\"", fascia, StringComparison.Ordinal);
        Assert.Contains("#C0C0C0", fascia, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#FF0000", fascia, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"svg_1\"", svg, StringComparison.Ordinal);
        Assert.Contains("id=\"svg_44\"", svg, StringComparison.Ordinal);
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
