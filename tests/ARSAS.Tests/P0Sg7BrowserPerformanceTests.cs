using ArIED61850Tester.Models;

namespace ARSAS.Tests;

[CollectionDefinition("SG7-Unit-Metadata-Serial", DisableParallelization = true)]
public sealed class P0Sg7BrowserPerformanceTestCollection;

[Collection("SG7-Unit-Metadata-Serial")]
public sealed class P0Sg7BrowserPerformanceTests
{
    [Fact]
    public void ConfigurationRevision_DoesNotAdvanceForOrdinarySTMXOrDC_ReportValues()
    {
        var ledger = new IedBrowserLiveValues();
        var firstAssociation = ledger.BeginAssociation();
        var initial = ledger.ConfigurationVersion;

        Assert.True(ledger.PublishForAssociation(firstAssociation,
            "SG7LD/MMXU1.A.phsA.cVal.mag.f", "MX", "123.4", "", "", "Report"));
        Assert.True(ledger.PublishForAssociation(firstAssociation,
            "SG7LD/XCBR1.Pos.stVal", "ST", "Open", "", "", "Report"));
        Assert.True(ledger.PublishForAssociation(firstAssociation,
            "SG7LD/LLN0.NamPlt.d", "DC", "BCU", "", "", "Report"));
        Assert.Equal(initial, ledger.ConfigurationVersion);

        Assert.True(ledger.PublishForAssociation(firstAssociation,
            "SG7LD/MMXU1.A.phsA.units.SIUnit", "CF", "A", "", "", "Report"));
        Assert.Equal(initial + 1, ledger.ConfigurationVersion);

        ledger.MarkOffline();
        Assert.Equal(initial + 2, ledger.ConfigurationVersion);
        Assert.False(ledger.PublishForAssociation(firstAssociation,
            "SG7LD/MMXU1.A.phsA.units.multiplier", "CF", "0", "", "", "Report"));
        Assert.Equal(initial + 2, ledger.ConfigurationVersion);
        ledger.BeginAssociation();
        Assert.Equal(initial + 3, ledger.ConfigurationVersion);
    }

    [Fact]
    public void EngineeringUnitProjection_ReusesCacheForValueUpdates_ButInvalidatesMetadata()
    {
        var device = new Iec61850MonitorDevice();
        var unit = new SignalDefinition
        {
            ObjectReference = "SG7LD/MMXU1.A.phsA.cVal.mag.f",
            FunctionalConstraint = "MX",
            Unit = "A"
        };
        device.Signals.Add(unit);
        var cache = new IedBrowserUnitProjectionCache();
        var evidence = new IedBrowserCfEvidencePresentation(device.LiveValues, null, null);

        var initial = cache.Resolve(device, null, evidence);
        Assert.Equal("A", initial["SG7LD/MMXU1.A.phsA.cVal.mag.f|MX"]);
        Assert.Same(initial, cache.Resolve(device, null, evidence));

        var association = device.LiveValues.BeginAssociation();
        var newAssociation = cache.Resolve(device, null, evidence);
        Assert.NotSame(initial, newAssociation);
        Assert.True(device.LiveValues.PublishForAssociation(association,
            "SG7LD/MMXU1.A.phsA.cVal.mag.f", "MX", "23", "", "", "Report"));
        Assert.Same(newAssociation, cache.Resolve(device, null, evidence));

        unit.Unit = "kA";
        var changedUnit = cache.Resolve(device, null, evidence);
        Assert.NotSame(newAssociation, changedUnit);
        Assert.Equal("kA", changedUnit["SG7LD/MMXU1.A.phsA.cVal.mag.f|MX"]);

        Assert.True(device.LiveValues.PublishForAssociation(association,
            "SG7LD/MMXU1.A.phsA.units.SIUnit", "CF", "A", "", "", "Report"));
        var changedCf = cache.Resolve(device, null, evidence);
        Assert.NotSame(changedUnit, changedCf);

        device.Signals.Clear();
        var afterClear = cache.Resolve(device, null, evidence);
        Assert.NotSame(changedCf, afterClear);
        Assert.Empty(afterClear);
    }

    [Fact]
    public void UnitMetadataRevision_OnlyChangesWhenUnitChanges()
    {
        var signal = new SignalDefinition();
        var before = SignalDefinition.UnitMetadataRevision;
        signal.Unit = "kV";
        Assert.True(SignalDefinition.UnitMetadataRevision > before);
        var after = SignalDefinition.UnitMetadataRevision;
        signal.Unit = "kV";
        Assert.Equal(after, SignalDefinition.UnitMetadataRevision);
    }

    [Fact]
    public void BrowserPerformanceSource_ExistsWithoutRequiringAUiOrNetworkSession()
    {
        var type = typeof(IedBrowserUnitProjectionCache).Assembly.GetType(
            "ArIED61850Tester.Services.IedBrowserPerformanceEvents");
        Assert.NotNull(type);
        var attribute = Assert.Single(type.GetCustomAttributes(
            typeof(System.Diagnostics.Tracing.EventSourceAttribute), false));
        Assert.Equal("ARSAS-IEDBrowser-Performance",
            ((System.Diagnostics.Tracing.EventSourceAttribute)attribute).Name);
    }
}
