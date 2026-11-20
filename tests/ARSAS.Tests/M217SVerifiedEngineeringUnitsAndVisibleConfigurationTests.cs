using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M217SVerifiedEngineeringUnitsAndVisibleConfigurationTests
{
    private const string Owner = "LD/MMXU1.A";
    private const string Phase = Owner + ".phsA";
    private static LiveIedDataAttributeModel Attr(string fc, string subpath)
        => new()
        {
            ObjectReference = Owner,
            AttributePath = "A." + subpath,
            MmsReference = "LD/MMXU1$" + fc + "$A$" +
                subpath.Replace('.', '$'),
            FunctionalConstraint = fc,
            SclBType = subpath.Contains("SIUnit", StringComparison.Ordinal)
                ? "Enum" : "FLOAT32"
        };

    private static (LiveIedModelDiscoveryDocument Model, IedBrowserTreeRow Ln)
        Fixture()
    {
        var ln = new LiveIedLogicalNodeModel
        {
            Name = "MMXU1",
            DataObjects = [new LiveIedDataObjectModel
            {
                Name = "A", Reference = Owner, InferredCdc = "WYE",
                Attributes =
                [
                    Attr("MX", "phsA.cVal.mag.f"),
                    Attr("MX", "phsA.cVal.ang.f"),
                    Attr("CF", "phsA.units.SIUnit"),
                    Attr("CF", "phsA.units.multiplier"),
                    Attr("CF", "phsA.db"),
                    Attr("CF", "phsA.dbAng"),
                    Attr("CF", "phsA.rangeC"),
                    Attr("DC", "phsA.d"),
                    Attr("CF", "phsB.units.SIUnit"),
                    Attr("CF", "phsB.units.multiplier")
                ]
            }]
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD", LogicalNodes = [ln]
            }]
        };
        return (model, new IedBrowserTreeRow(
            "model/ln/LD/MMXU1", "MMXU1", "LN", 2, ln));
    }

    [Fact]
    public void LiveVerifiedCfOverridesConflictingLeafMetadataOnlyInItsOwnPhase()
    {
        var (model, _) = Fixture();
        var ledger = new IedBrowserLiveValues();
        var session = ledger.BeginAssociation();
        ledger.PublishForAssociation(session, Phase + ".units.SIUnit",
            "CF", "5", "", "", "MMS Read");
        ledger.PublishForAssociation(session, Phase + ".units.multiplier",
            "CF", "3", "", "", "MMS Read");
        var signals = new[]
        {
            new SignalDefinition
            {
                ObjectReference = Phase + ".cVal.mag.f",
                FunctionalConstraint = "MX", Unit = "A"
            },
            new SignalDefinition
            {
                ObjectReference = Owner + ".phsB.cVal.mag.f",
                FunctionalConstraint = "MX", Unit = "V"
            }
        };
        var cf = new IedBrowserCfEvidencePresentation(ledger, model, null);
        var index = cf.BuildVerifiedUnits(signals, model);
        Assert.True(cf.TryFormatUnitsParent(Phase + ".units", "CF",
            out var unit, out _));
        Assert.Equal("kA", unit);
        Assert.Equal("kA", IedBrowserEngineeringValuePresentation.ResolveUnit(
            index, Phase + ".cVal.mag.f", "MX"));
        Assert.Equal("V", IedBrowserEngineeringValuePresentation.ResolveUnit(
            index, Owner + ".phsB.cVal.mag.f", "MX"));
        Assert.Equal("0 kA", IedBrowserEngineeringValuePresentation.Format("0",
            IedBrowserEngineeringValuePresentation.ResolveUnit(
                index, Phase + ".cVal.mag.f", "MX")));
    }

    [Fact]
    public void ExpandedPhaseReadsOnlyItsMissingVerifiedCfAndDcLeaves()
    {
        var (model, ln) = Fixture();
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        var targets = IedBrowserVisibleConfigurationReadRequest.Build(
            model, ln, Phase, ledger);
        Assert.Equal(6, targets.Count);
        Assert.All(targets, target =>
        {
            Assert.StartsWith(Phase + ".",
                IedBrowserLiveValues.CanonicalReference(target.Reference));
            Assert.Contains(target.FunctionalConstraint, new[] { "CF", "DC" });
        });
        Assert.Contains(targets, x => IedBrowserLiveValues.CanonicalReference(x.Reference)
            .EndsWith(".dbAng", StringComparison.Ordinal));
        Assert.Contains(targets, x => IedBrowserLiveValues.CanonicalReference(x.Reference)
            .EndsWith(".d", StringComparison.Ordinal));
        ledger.PublishForAssociation(ledger.Association, Phase + ".db",
            "CF", "10", "", "", "MMS Read");
        var remaining = IedBrowserVisibleConfigurationReadRequest.Build(
            model, ln, Phase, ledger);
        Assert.Equal(5, remaining.Count);
        Assert.DoesNotContain(remaining, x =>
            IedBrowserLiveValues.CanonicalReference(x.Reference).EndsWith(
                ".db", StringComparison.Ordinal));
        Assert.Empty(IedBrowserVisibleConfigurationReadRequest.Build(
            model, ln, Owner + ".phsC", ledger));
        Assert.Empty(IedBrowserVisibleConfigurationReadRequest.Build(
            null, ln, Phase, ledger));
    }
}
