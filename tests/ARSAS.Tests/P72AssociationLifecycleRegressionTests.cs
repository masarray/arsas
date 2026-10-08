namespace ARSAS.Tests;

public sealed class P72AssociationLifecycleRegressionTests
{
    [Fact]
    public void UiShell_DoesNotOwnIcmpDeathOrSecondReconnectStateMachine()
    {
        var source = Read("MainWindow.P0AssociationLiveness.cs");

        Assert.Contains(
            "Association liveness belongs to the per-IED MMS runtime",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("System.Net.NetworkInformation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Ping(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SendPingAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryPingEndpointAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkAssociationOfflineAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_associationReconnectWanted", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StopDeviceConnectionAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_ClearsReportTrafficProofWhenAssociationIsLostOrReplaced()
    {
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");

        Assert.Contains(
            "session.Device.HasReportStream = false;",
            runtime,
            StringComparison.Ordinal);
        Assert.Contains(
            "InformationReport proof is association-scoped",
            runtime,
            StringComparison.Ordinal);
        Assert.Contains(
            "private static void ResetAssociationReportEvidence",
            runtime,
            StringComparison.Ordinal);
        Assert.Contains(
            "private void MarkSessionOffline",
            runtime,
            StringComparison.Ordinal);

        Assert.True(
            Count(runtime, "session.Device.HasReportStream = false;") >= 2,
            "Traffic proof must be cleared both when the association goes offline and when a replacement association is installed.");
    }

    [Fact]
    public void StaticTrafficProof_WaitsForPropertyChangeInsteadOfFixedSleep()
    {
        var source = Read("MainWindow.IoTesting.AutoConnect.cs");

        Assert.Contains("WaitForReportStreamAsync", source, StringComparison.Ordinal);
        Assert.Contains("device.PropertyChanged += handler;", source, StringComparison.Ordinal);
        Assert.Contains(".WaitAsync(timeout, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("firstReportLatency.TotalMilliseconds", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "await Task.Delay(proofWindow",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StaticReportOnlyReconnect_DoesNotClaimPollingResumed()
    {
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");

        Assert.Contains(
            "Static DataSet process polling remains disabled; configured RCB re-arm",
            runtime,
            StringComparison.Ordinal);
        Assert.Contains(
            "session.StaticDataSetReportOnly",
            runtime,
            StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string Read(string relativePath)
        => File.ReadAllText(FindRepoFile(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
