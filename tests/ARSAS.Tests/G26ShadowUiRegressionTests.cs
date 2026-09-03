namespace ARSAS.Tests;

public sealed class G26ShadowUiRegressionTests
{
    [Fact]
    public void Shadow_HasDedicatedCtrlShiftSActionSeparateFromA21AndA3()
    {
        var ui = Read("DynamicReportCommandBoundWitnessUiBehavior.cs");

        Assert.Contains("e.Key != Key.F && e.Key != Key.A && e.Key != Key.S", ui, StringComparison.Ordinal);
        Assert.Contains("var shadow = e.Key == Key.S", ui, StringComparison.Ordinal);
        Assert.Contains("RunPhysicalShadowAsync", ui, StringComparison.Ordinal);
        Assert.Contains("DynamicReportShadowVerificationCommissioningService", ui, StringComparison.Ordinal);
        Assert.Contains("Ctrl+Shift+S issues ZERO automatic control commands", ui, StringComparison.Ordinal);
        Assert.Contains("controlRegressionPassed: false", ui, StringComparison.Ordinal);
        Assert.Contains("staticReportingRegressionPassed: false", ui, StringComparison.Ordinal);
    }

    [Fact]
    public void Shadow_UiRequiresTwoExplicitReadyMarkersAndShowsEvidenceWindow()
    {
        var ui = Read("DynamicReportCommandBoundWitnessUiBehavior.cs");
        var service = Read("Services/DynamicReportShadowVerificationCommissioningService.cs");
        var window = Read("DynamicReportQualificationResultWindow.G26Shadow.cs");

        Assert.Contains("G2.6 SHADOW PHASE 1 READY — CAUSE ONE SAFE CHANGE", ui, StringComparison.Ordinal);
        Assert.Contains("Phase1ReadyMarker", service, StringComparison.Ordinal);
        Assert.Contains("Phase2ReadyMarker", service, StringComparison.Ordinal);
        Assert.Contains("new DynamicReportQualificationResultWindow(result)", ui, StringComparison.Ordinal);
        Assert.Contains("Shadow PASS != ProductionEligible", window, StringComparison.Ordinal);
        Assert.Contains("production automatic dynamic reporting remains OFF", window, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(FindRepoFile(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

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
