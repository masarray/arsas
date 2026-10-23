namespace ARSAS.Tests;

public sealed class ProductionFatFieldPerformanceRegressionTests
{
    [Fact]
    public void EmbeddedStaticDataSetFat_ReleasesLegacy250msPollingOverride()
    {
        var source = Read("MainWindow.ProductionFatFieldPerformance.cs");

        Assert.Contains("ReleaseLegacyPollingOverrideForStaticEmbeddedFat", source, StringComparison.Ordinal);
        Assert.Contains("IsSharedStaticDataSetAuthority", source, StringComparison.Ordinal);
        Assert.Contains("PollingIntervalMs = _pollingIntervalBeforeIoFat.Value;", source, StringComparison.Ordinal);
        Assert.Contains("_pollingIntervalBeforeIoFat = null;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseHybrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MMS polling", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManualOrWorkbookFat_DoesNotLoseLegacyPollingCadence()
    {
        var source = Read("MainWindow.ProductionFatFieldPerformance.cs");

        Assert.Contains(
            "liveDevices.Any(device => !IsSharedStaticDataSetAuthority(device!))",
            source,
            StringComparison.Ordinal);
        Assert.Contains("return;", source, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests", "ARSAS.Tests")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
