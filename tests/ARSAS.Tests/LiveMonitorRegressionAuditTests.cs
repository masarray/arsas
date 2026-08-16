using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class LiveMonitorRegressionAuditTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void UnverifiedReporting_KeepsConfiguredMmsCadence(bool assigned, bool traffic, bool verified)
    {
        var interval = ReportVerificationPollingPolicy.GetIntervalMs(1000, true, assigned, traffic, verified);
        Assert.Equal(1000, interval);
    }

    [Fact]
    public void VerifiedReporting_CanSlowMmsVerification()
    {
        var interval = ReportVerificationPollingPolicy.GetIntervalMs(1000, true, true, true, true);
        Assert.Equal(15000, interval);
    }

    [Fact]
    public void ExplorerLiveGrid_HasPresentationSearch()
    {
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        var code = File.ReadAllText(FindRepoFile("MainWindow.LiveSearch.cs"));

        Assert.Contains("ExplorerLiveSearchBox", xaml, StringComparison.Ordinal);
        Assert.Contains("ExplorerLiveSearch_TextChanged", xaml, StringComparison.Ordinal);
        Assert.Contains("ExplorerLiveSearchClear_Click", xaml, StringComparison.Ordinal);
        var app = File.ReadAllText(FindRepoFile("App.xaml"));
        Assert.Contains("x:Key=\"LucideSearch\"", app, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SelectedDevice.Points}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.GetDefaultView(device.Points)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_UsesFailSafeVerificationPolicy()
    {
        var source = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));
        Assert.Contains("ReportVerificationPollingPolicy.GetIntervalMs", source, StringComparison.Ordinal);
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
