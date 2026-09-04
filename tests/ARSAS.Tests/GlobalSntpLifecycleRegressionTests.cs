namespace ARSAS.Tests;

public sealed class GlobalSntpLifecycleRegressionTests
{
    [Fact]
    public void MainHeader_ExposesGlobalSntpToggleWithActualBoundServerIp()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.ClockSyncToggle.cs"));

        Assert.Contains("WorkflowNavShell.Parent is not Grid headerGrid", source, StringComparison.Ordinal);
        Assert.Contains("Name = \"GlobalSntpServerToggle\"", source, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(toggle, 2)", source, StringComparison.Ordinal);
        Assert.Contains("SNTP Server Active:", source, StringComparison.Ordinal);
        Assert.Contains("snapshot.Binding?.LocalAddress", source, StringComparison.Ordinal);
        Assert.Contains("Directed broadcast", source, StringComparison.Ordinal);
        Assert.Contains("continues outside FAT", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalLifecycle_FollowsConnectedIedsAndCannotRestartAfterToggleOff()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.ClockSync.cs"));

        Assert.Contains("Devices.CollectionChanged += ClockSyncDevices_CollectionChanged", source, StringComparison.Ordinal);
        Assert.Contains("device.PropertyChanged += ClockSyncDevice_PropertyChanged", source, StringComparison.Ordinal);
        Assert.Contains("EnsureStartedAsync(iedAddress", source, StringComparison.Ordinal);
        Assert.Contains("if (!_clockSyncEnabled || !device.IsConnected)", source, StringComparison.Ordinal);
        Assert.Contains("Closed += ClockSyncMainWindow_Closed", source, StringComparison.Ordinal);
        Assert.Contains("await _sntpClockService.DisposeAsync()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatWorkspace_IsPassiveConsumerOfGlobalSntpState()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.ClockSyncUx.cs"));

        Assert.Contains("Global SNTP is controlled from the ARSAS header", source, StringComparison.Ordinal);
        Assert.Contains("continues running when the FAT window closes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new CheckBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetClockSyncEnabledAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClockSyncCheckBox_Changed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalService_RemainsARealUdp123ServerAndMode5Broadcaster()
    {
        var source = File.ReadAllText(FindRepoFile("Services/SntpClockService.cs"));

        Assert.Contains("new IPEndPoint(binding.LocalAddress, 123)", source, StringComparison.Ordinal);
        Assert.Contains("SntpPacket.BuildServerReply", source, StringComparison.Ordinal);
        Assert.Contains("SntpPacket.BuildBroadcast", source, StringComparison.Ordinal);
        Assert.Contains("Mode 5 broadcast", source, StringComparison.Ordinal);
        Assert.Contains("SendBroadcastPacketAsync", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(64)", source, StringComparison.Ordinal);
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

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
