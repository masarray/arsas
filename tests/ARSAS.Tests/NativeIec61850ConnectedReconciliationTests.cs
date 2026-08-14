using AR.Iec61850.Discovery;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class NativeIec61850ConnectedReconciliationTests
{
    [Fact]
    public async Task DisconnectedNativeOwner_ReturnsEngineTransportFailure_NotAbsent()
    {
        await using var client = new NativeIec61850Client();
        var design = BuildModel(new TestAttribute(
            "IEDLD0/GGIO1.Ind1.stVal",
            "IEDLD0/GGIO1$ST$Ind1$stVal",
            "ST",
            "BOOLEAN",
            string.Empty));

        var result = await client.ReconcileDesignLiveAsync(
            design,
            EmptyLive(),
            new Iec61850DesignLiveReconciliationOptions
            {
                ProbeAllMissingDesignAttributes = true,
                ProbeKnownAlternateReferences = false,
                MaxProbeTargetCount = 1
            });

        var point = Assert.Single(result.Points);
        Assert.Equal(Iec61850DesignLiveStatus.TransportFailure, point.Status);
        Assert.Equal(Iec61850ExactProbeStatus.TransportFailure, point.Probe?.Status);
        Assert.NotEqual(Iec61850DesignLiveStatus.Absent, point.Status);
        Assert.Equal(0, result.AbsentCount);
    }

    [Fact]
    public async Task EndpointResolver_UsesExistingNativeOwner_WithoutCreatingAnotherSession()
    {
        const string ipAddress = "203.0.113.77";
        const int port = 65102;
        await using var client = new NativeIec61850Client();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        // NativeIec61850Client records the endpoint before entering the cancellable
        // association operation. An already-cancelled token therefore registers the
        // session owner deterministically without performing TCP/MMS network I/O.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ConnectAsync(ipAddress, port, cancelled.Token));

        var design = BuildModel(new TestAttribute(
            "IEDLD0/GGIO1.Ind1.stVal",
            "IEDLD0/GGIO1$ST$Ind1$stVal",
            "ST",
            "BOOLEAN",
            string.Empty));

        var result = await NativeIec61850Client.ReconcileConnectedAsync(
            ipAddress,
            port,
            design,
            EmptyLive(),
            new Iec61850DesignLiveReconciliationOptions
            {
                ProbeAllMissingDesignAttributes = true,
                ProbeKnownAlternateReferences = false,
                MaxProbeTargetCount = 1
            });

        var point = Assert.Single(result.Points);
        Assert.Equal(Iec61850DesignLiveStatus.TransportFailure, point.Status);
        Assert.Equal(Iec61850ExactProbeStatus.TransportFailure, point.Probe?.Status);
        Assert.Equal(0, result.AbsentCount);
    }

    [Fact]
    public async Task AlternateDiscovery_ThroughNativeOwner_RecoversWithoutNetworkProbe()
    {
        const string canonical = "IEDLD0/MMXU1$MX$TotW$mag$f";
        const string alternate = "IEDLD0/MMXU1$MX$TotW$instMag$f";
        await using var client = new NativeIec61850Client();
        var design = BuildModel(new TestAttribute(
            "IEDLD0/MMXU1.TotW.mag.f",
            canonical,
            "MX",
            "FLOAT32",
            string.Empty));
        var observed = BuildModel(
            new TestAttribute(
                "IEDLD0/MMXU1.TotW.instMag.f",
                alternate,
                "MX",
                "FLOAT32",
                "floating-point"),
            "LiveMmsDiscovery");

        var result = await client.ReconcileDesignLiveAsync(
            design,
            observed,
            new Iec61850DesignLiveReconciliationOptions
            {
                ProbeAllMissingDesignAttributes = true,
                MaxProbeTargetCount = 1
            });

        var point = Assert.Single(result.Points);
        Assert.Equal(Iec61850DesignLiveStatus.RecoveredByAlternateDiscovery, point.Status);
        Assert.Equal(canonical, point.CanonicalMmsReference);
        Assert.Equal(alternate, point.EffectiveMmsReference);
        Assert.Equal(alternate, point.ObservedMmsReference);
        Assert.Equal(
            Iec61850AlternateReferenceStrategyKind.MagnitudeInstantaneousSibling,
            point.AlternateStrategy);
        Assert.Empty(point.ProbeAttempts);
        Assert.Null(point.Probe);
        Assert.Equal(0, result.AbsentCount);
        Assert.Equal(0, result.LiveOnlyCount);
    }

    private static LiveIedModelDiscoveryDocument BuildModel(
        TestAttribute attribute,
        string source = "SclWorkspace")
        => BuildModel(new[] { attribute }, source);

    private static LiveIedModelDiscoveryDocument BuildModel(
        IReadOnlyCollection<TestAttribute> attributes,
        string source)
    {
        var models = attributes.Select((attribute, index) =>
        {
            var slash = attribute.MmsReference.IndexOf('/');
            var item = attribute.MmsReference[(slash + 1)..];
            var logicalNode = item.Split('$', StringSplitOptions.RemoveEmptyEntries)[0];
            var domain = attribute.MmsReference[..slash];
            return new
            {
                Domain = domain,
                LogicalNode = logicalNode,
                DataObject = new LiveIedDataObjectModel
                {
                    Reference = $"{domain}/{logicalNode}.DO{index + 1}",
                    Name = $"DO{index + 1}",
                    InferredCdc = "MV",
                    Attributes = new[]
                    {
                        new LiveIedDataAttributeModel
                        {
                            ObjectReference = attribute.ObjectReference,
                            AttributePath = attribute.ObjectReference[(attribute.ObjectReference.LastIndexOf('.') + 1)..],
                            FunctionalConstraint = attribute.FunctionalConstraint,
                            MmsReference = attribute.MmsReference,
                            MmsItemName = item,
                            SclBType = attribute.SclBType,
                            MmsType = attribute.MmsType,
                            Source = source
                        }
                    }
                }
            };
        }).ToArray();

        return new LiveIedModelDiscoveryDocument
        {
            Source = source,
            IedName = "IED",
            LogicalDevices = models
                .GroupBy(model => model.Domain, StringComparer.OrdinalIgnoreCase)
                .Select(domain => new LiveIedLogicalDeviceModel
                {
                    MmsDomain = domain.Key,
                    LogicalNodes = domain
                        .GroupBy(model => model.LogicalNode, StringComparer.OrdinalIgnoreCase)
                        .Select(node => new LiveIedLogicalNodeModel
                        {
                            Name = node.Key,
                            DataObjects = node.Select(model => model.DataObject).ToArray()
                        })
                        .ToArray()
                })
                .ToArray()
        };
    }

    private static LiveIedModelDiscoveryDocument EmptyLive()
        => new() { Source = "LiveMmsDiscovery", IedName = "IED" };

    private sealed record TestAttribute(
        string ObjectReference,
        string MmsReference,
        string FunctionalConstraint,
        string SclBType,
        string MmsType);
}
