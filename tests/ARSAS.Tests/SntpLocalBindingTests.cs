using System.Net;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SntpLocalBindingTests
{
    private static readonly IPAddress Mask = IPAddress.Parse("255.255.255.0");

    private static SntpNetworkBinding Binding(string ip, string nic) =>
        new(IPAddress.Parse(ip), Mask, null, nic, nic);

    [Fact]
    public void SelectedIp_ResolvesAliasOnSameAdapterWithoutGuessingFirstAddress()
    {
        var bindings = new[]
        {
            Binding("192.16.1.33", "LAN"),
            Binding("192.16.1.115", "LAN"),
            Binding("192.168.1.7", "WiFi")
        };
        var chosen = SntpNetworkRouteResolver.SelectLocalBinding(
            IPAddress.Parse("192.16.1.115"), "LAN", bindings);
        Assert.Equal("192.16.1.115", chosen.LocalAddress.ToString());
        Assert.Equal("LAN", chosen.InterfaceId);
    }

    [Fact]
    public void MissingOrStaleIp_IsRejectedWithoutRebindingToDifferentNic()
    {
        var bindings = new[] { Binding("192.16.1.33", "LAN") };
        Assert.Throws<InvalidOperationException>(() =>
            SntpNetworkRouteResolver.SelectLocalBinding(
                IPAddress.Parse("192.16.1.115"), "LAN", bindings));
        Assert.Throws<InvalidOperationException>(() =>
            SntpNetworkRouteResolver.SelectLocalBinding(
                IPAddress.Parse("192.16.1.33"), "WiFi", bindings));
    }

    [Fact]
    public void DuplicateIpAcrossAdapters_FailsClosedWithoutExplicitNic()
    {
        var bindings = new[] { Binding("192.16.1.33", "LAN"), Binding("192.16.1.33", "USB") };
        Assert.Throws<InvalidOperationException>(() =>
            SntpNetworkRouteResolver.SelectLocalBinding(
                IPAddress.Parse("192.16.1.33"), null, bindings));
        Assert.Equal("USB", SntpNetworkRouteResolver.SelectLocalBinding(
            IPAddress.Parse("192.16.1.33"), "USB", bindings).InterfaceId);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    public void UnsafeListenAddresses_AreRejected(string ip)
    {
        Assert.Throws<ArgumentException>(() =>
            SntpNetworkRouteResolver.SelectLocalBinding(
                IPAddress.Parse(ip), null, new[] { Binding("192.16.1.33", "LAN") }));
    }

    [Fact]
    public void NoNetworkCandidates_ReturnsEmptyListWithoutThrowing()
    {
        var resolver = File.ReadAllText(Find("Services/SntpNetworkRouteResolver.cs"));
        Assert.Contains("return candidates;", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("No active IPv4 station-bus network adapter is available.", resolver, StringComparison.Ordinal);
    }

    private static string Find(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, name);
            if (File.Exists(path)) return path;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(name);
    }
}
