using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SmartIedOnboardingRegressionTests
{
    [Fact]
    public void BulkConnect_HidesForSingleIed()
    {
        var state = SmartIedBulkActionPolicy.Evaluate(
        [
            new Iec61850MonitorDevice { IpAddress = "192.0.2.10", Port = 102 }
        ]);

        Assert.False(state.ShowConnectAll);
        Assert.Equal(1, state.TotalDevices);
        Assert.Equal(1, state.CandidateCount);
    }

    [Fact]
    public void BulkConnect_ShowsOnlyUsefulCandidatesAcrossMultipleIeds()
    {
        var state = SmartIedBulkActionPolicy.Evaluate(
        [
            new Iec61850MonitorDevice { IpAddress = "192.0.2.10", Port = 102, IsMonitoring = true },
            new Iec61850MonitorDevice { IpAddress = "192.0.2.11", Port = 102 },
            new Iec61850MonitorDevice { IpAddress = string.Empty, Port = 102 }
        ]);

        Assert.True(state.ShowConnectAll);
        Assert.Equal(3, state.TotalDevices);
        Assert.Equal(1, state.CandidateCount);
        Assert.Equal("Connect 1 IED", state.Label);
    }

    [Fact]
    public void BulkConnect_HidesWhenNothingCanBeConnected()
    {
        var state = SmartIedBulkActionPolicy.Evaluate(
        [
            new Iec61850MonitorDevice { IpAddress = "192.0.2.10", Port = 102, IsMonitoring = true },
            new Iec61850MonitorDevice { IpAddress = "192.0.2.11", Port = 102, IsBusy = true },
            new Iec61850MonitorDevice { IpAddress = string.Empty, Port = 102 }
        ]);

        Assert.False(state.ShowConnectAll);
        Assert.Equal(0, state.CandidateCount);
    }

    [Fact]
    public void Onboarding_OffersSclFirstOrLiveIpDiscoveryWithoutOwningProtocolRuntime()
    {
        var behavior = Read("Services/SmartIedOnboardingBehavior.cs");
        var app = Read("App.xaml.cs");

        Assert.Contains("Open SCL / CID  ·  Recommended", behavior, StringComparison.Ordinal);
        Assert.Contains("Discover IED by IP", behavior, StringComparison.Ordinal);
        Assert.Contains("openScl.Visibility = Visibility.Collapsed", behavior, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumnSpan(button, 3)", behavior, StringComparison.Ordinal);
        Assert.Contains("snapshot.Length > 1 && candidateCount > 0", behavior, StringComparison.Ordinal);
        Assert.Contains("!device.IsBusy", behavior, StringComparison.Ordinal);
        Assert.Contains("!device.IsMonitoring", behavior, StringComparison.Ordinal);
        Assert.Contains("SmartIedOnboardingBehavior.Install(mainWindow)", app, StringComparison.Ordinal);

        Assert.DoesNotContain("DiscoverAsync(", behavior, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectUsingSclAsync", behavior, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteAsync", behavior, StringComparison.Ordinal);
        Assert.DoesNotContain("Operate", behavior, StringComparison.Ordinal);
    }

    [Fact]
    public void Onboarding_ModelEvents_MarshalVisualRefreshThroughMainWindowDispatcher()
    {
        var behavior = Read("Services/SmartIedOnboardingBehavior.cs");

        var propertyChangedStart = behavior.IndexOf(
            "private void Device_PropertyChanged",
            StringComparison.Ordinal);
        var refreshVisualsStart = behavior.IndexOf(
            "private void RefreshVisuals",
            StringComparison.Ordinal);
        Assert.True(propertyChangedStart >= 0 && refreshVisualsStart > propertyChangedStart);

        var propertyChangedBody = behavior[propertyChangedStart..refreshVisualsStart];
        Assert.Contains("DispatchUi(RefreshConnectAll)", propertyChangedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("{\n                RefreshConnectAll();\n            }", propertyChangedBody, StringComparison.Ordinal);

        Assert.Contains("private void DispatchUi(Action action", behavior, StringComparison.Ordinal);
        Assert.Contains("dispatcher.CheckAccess()", behavior, StringComparison.Ordinal);
        Assert.Contains("dispatcher.BeginInvoke(priority, action)", behavior, StringComparison.Ordinal);
        Assert.Contains("Smart IED onboarding visual refresh must run on the MainWindow dispatcher", behavior, StringComparison.Ordinal);
        Assert.Contains("Smart IED bulk action refresh must run on the MainWindow dispatcher", behavior, StringComparison.Ordinal);
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
