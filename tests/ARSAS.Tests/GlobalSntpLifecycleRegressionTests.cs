using System.Net;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class GlobalSntpLifecycleRegressionTests
{
    [Fact]
    public void StandaloneUi_IsOnlyToggleLocalIpAndTrafficIndicator()
    {
        var ui = Read("MainWindow.ClockSyncToggle.cs");
        Assert.Contains("Name = \"GlobalSntpServerToggle\"", ui, StringComparison.Ordinal);
        Assert.Contains("Name = \"GlobalSntpPcIpPicker\"", ui, StringComparison.Ordinal);
        Assert.Contains("Name = \"GlobalSntpTrafficIndicator\"", ui, StringComparison.Ordinal);
        Assert.Contains("Text = \"NTP Server\"", ui, StringComparison.Ordinal);
        Assert.Contains("FrameworkElementFactory(typeof(Ellipse))", ui, StringComparison.Ordinal);
        Assert.Contains("SntpNetworkRouteResolver.GetLocalBindings()", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("waiting for IED", ui, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MinWidth = 282", ui, StringComparison.Ordinal);
    }

    [Fact]
    public void StandaloneLifecycle_IsIndependentOfAllIedConnectionEvents()
    {
        var source = Read("MainWindow.ClockSync.cs");
        var ui = Read("MainWindow.ClockSyncToggle.cs");
        Assert.Contains("ReconcileStandaloneClockAsync()", source, StringComparison.Ordinal);
        Assert.Contains("StartOnLocalAddressAsync(", source, StringComparison.Ordinal);
        Assert.Contains("SntpNetworkRouteResolver.ResolveForLocal(", source, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Increment(ref _clockSyncDesiredVersion)", source, StringComparison.Ordinal);
        Assert.Contains("await _sntpClockService.StopAsync()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClockSyncDevices_CollectionChanged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClockSyncDevice_PropertyChanged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduleClockSyncReconcile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureStartedAsync(iedAddress", source, StringComparison.Ordinal);
        Assert.Contains("private bool _clockSyncEnabled;", ui, StringComparison.Ordinal);
        Assert.Contains("SetClockSyncEnabledAsync(bool enabled)", ui, StringComparison.Ordinal);
        Assert.Contains("if (_clockSyncEnabled && selected is null)", ui, StringComparison.Ordinal);
        Assert.Contains("Closed += ClockSyncMainWindow_Closed", source, StringComparison.Ordinal);
        Assert.Contains("await _sntpClockService.DisposeAsync()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalSelection_MustBeLiveAndUnambiguous()
    {
        var resolver = Read("Services/SntpNetworkRouteResolver.cs");
        Assert.Contains("GetLocalBindings()", resolver, StringComparison.Ordinal);
        Assert.Contains("ResolveForLocal(IPAddress localAddress, string? interfaceId = null)", resolver, StringComparison.Ordinal);
        Assert.Contains("matches.Length != 1", resolver, StringComparison.Ordinal);
        Assert.Contains("OperationalStatus.Up", resolver, StringComparison.Ordinal);
        Assert.Contains("NetworkInterfaceType.Loopback", resolver, StringComparison.Ordinal);
        Assert.Contains("UnicastAddresses", resolver, StringComparison.Ordinal);
        Assert.Contains("item.InterfaceId.Equals(interfaceId", resolver, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingTransport_RemainsUdp123WithReadOnlyFallbackAndClockGuard()
    {
        var source = Read("Services/SntpClockService.cs");
        Assert.Contains("StartOnLocalAddressAsync(", source, StringComparison.Ordinal);
        Assert.Contains("new IPEndPoint(binding.LocalAddress, 123)", source, StringComparison.Ordinal);
        Assert.Contains("SntpPacket.BuildServerReply", source, StringComparison.Ordinal);
        Assert.Contains("SntpPacket.BuildBroadcast", source, StringComparison.Ordinal);
        Assert.Contains("StartRawFallbackAsync(", source, StringComparison.Ordinal);
        Assert.Contains("RequestImmediateBroadcast()", source, StringComparison.Ordinal);
        Assert.Contains("ClockHealthSample", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatWorkspace_RemainsPassiveConsumer()
    {
        var source = Read("IoListTestingWindow.ClockSyncUx.cs");
        Assert.Contains("Global SNTP is controlled from the ARSAS header", source, StringComparison.Ordinal);
        Assert.Contains("continues running when the FAT window closes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetClockSyncEnabledAsync", source, StringComparison.Ordinal);
    }

    private static string Read(string path)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, path);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
