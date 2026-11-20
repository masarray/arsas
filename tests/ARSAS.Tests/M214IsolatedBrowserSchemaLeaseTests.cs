using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class M214IsolatedBrowserSchemaLeaseTests
{
    [Fact]
    public void VerifiedLiveSchema_CanBeReusedWithoutMmsDiscoveryOrNewModel()
    {
        var device = new Iec61850MonitorDevice
        {
            LiveDiscoveryModel = new LiveIedModelDiscoveryDocument(),
            IsConnected = true, IsMonitoring = true
        };
        var lease = Assert.IsType<IedBrowserIsolatedReadSchemaLease>(
            IedBrowserIsolatedReadSchemaLease.Capture(device));
        Assert.Same(device.LiveDiscoveryModel, lease.Model);
        Assert.True(lease.IsCurrent(device));
    }

    [Fact]
    public void SclOnlyOrInactiveMonitor_CannotSupplySecondaryReadSchema()
    {
        var device = new Iec61850MonitorDevice { IsConnected = true, IsMonitoring = true };
        Assert.Null(IedBrowserIsolatedReadSchemaLease.Capture(device));
        device.LiveDiscoveryModel = new LiveIedModelDiscoveryDocument();
        device.IsMonitoring = false;
        Assert.Null(IedBrowserIsolatedReadSchemaLease.Capture(device));
        device.IsMonitoring = true;
        device.IsConnected = false;
        Assert.Null(IedBrowserIsolatedReadSchemaLease.Capture(device));
    }

    [Fact]
    public void AssociationCatalogAndEndpointChanges_InvalidateSchemaLease()
    {
        var device = new Iec61850MonitorDevice
        {
            LiveDiscoveryModel = new LiveIedModelDiscoveryDocument(),
            IsConnected = true, IsMonitoring = true
        };
        var lease = Assert.IsType<IedBrowserIsolatedReadSchemaLease>(
            IedBrowserIsolatedReadSchemaLease.Capture(device));
        device.LiveDiscoveryModel = new LiveIedModelDiscoveryDocument();
        Assert.False(lease.IsCurrent(device));
        var other = Assert.IsType<IedBrowserIsolatedReadSchemaLease>(
            IedBrowserIsolatedReadSchemaLease.Capture(device));
        device.IpAddress = "192.0.2.9";
        Assert.False(other.IsCurrent(device));
        var endpoint = Assert.IsType<IedBrowserIsolatedReadSchemaLease>(
            IedBrowserIsolatedReadSchemaLease.Capture(device));
        device.LiveValues.BeginAssociation();
        Assert.False(endpoint.IsCurrent(device));
    }

    [Fact]
    public async Task SecondaryClientRefusesOfflineSchemaInjection()
    {
        var client = new NativeIec61850Client();
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                client.BindIsolatedBrowserReadSchema(new LiveIedModelDiscoveryDocument()));
        }
        finally { await client.DisposeAsync(); }
    }
}
