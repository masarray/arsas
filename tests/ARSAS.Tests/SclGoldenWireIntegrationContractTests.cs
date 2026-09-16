using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SclGoldenWireIntegrationContractTests
{
    [Fact]
    public void Runtime_FastPlay_PrefersVerifiedSclWithoutDiscoveryFallback()
    {
        var source = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));
        var start = source.IndexOf("public async Task ConnectUsingCachedModelAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<IReadOnlyList<Iec61850MonitorPoint>> StartMonitoringAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var connect = source[start..end];

        Assert.Contains("Iec61850ConnectionPathPolicy.SelectForFastConnect", connect, StringComparison.Ordinal);
        Assert.Contains("VerifiedSclSourceLoader.LoadAsync", connect, StringComparison.Ordinal);
        Assert.Contains("ConnectUsingSclAsync", connect, StringComparison.Ordinal);
        Assert.Contains("allowCachedRetry: true", connect, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectAndDiscoverAsync", connect, StringComparison.Ordinal);
        Assert.DoesNotContain("DiscoverSignalsAsync", connect, StringComparison.Ordinal);
        Assert.True(
            connect.IndexOf("VerifiedSclSourceLoader.LoadAsync", StringComparison.Ordinal) <
            connect.IndexOf("ConnectUsingSclAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void Runtime_TrustedSclStaticReporting_BypassesHybridAndLegacyStartPaths()
    {
        var source = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));
        Assert.Contains("session.StaticDataSetReportOnly && session.Client.HasTrustedSclOnlineAuthority", source, StringComparison.Ordinal);
        Assert.Contains("StartTrustedSclStaticReportMonitorAsync", source, StringComparison.Ordinal);
        Assert.Contains("DataSet membership and RCB identity remain SCL-authoritative", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedSclStaticAdapter_HasGoldenWireSafetyContract()
    {
        var source = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.TrustedSclStaticReporting.cs"));
        Assert.Contains("TryGetTrustedSclDataSetDirectory", source, StringComparison.Ordinal);
        Assert.Contains("StartStaticSclReportMonitorAsync", source, StringComparison.Ordinal);
        Assert.Contains("triggerGeneralInterrogation: true", source, StringComparison.Ordinal);
        Assert.Contains("one explicit GI=true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("triggerGeneralInterrogation: false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDataSetDirectoriesAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefineNamedVariableList", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitorAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_Reconnect_ReusesSelectedAuthorityInsteadOfSilentlyDowngrading()
    {
        var source = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));
        var start = source.IndexOf("private async Task TryReconnectAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private void ScheduleReconnectRetry", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var reconnect = source[start..end];

        Assert.Contains("ConnectUsingSelectedFastPathAsync", reconnect, StringComparison.Ordinal);
        Assert.Contains("no cached-association or discovery fallback was attempted", reconnect, StringComparison.Ordinal);
        Assert.DoesNotContain("replacement.ConnectAsync", reconnect, StringComparison.Ordinal);
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
        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
