using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// A+B integration gate: no second live display path can promote independent
/// component evidence to a phasor; CF history must never claim fresh authority.
/// </summary>
public sealed class M213J_M214CrossThreadCompositionTests
{
    private const string Owner = "LD/MMXU1.PhV";

    private static LiveIedDataAttributeModel Leaf(string phase, string suffix)
        => new()
        {
            ObjectReference = Owner,
            AttributePath = "PhV." + phase + ".cVal." + suffix,
            FunctionalConstraint = "MX",
            MmsReference = "LD/MMXU1$MX$PhV$" + phase + "$cVal$" +
                suffix.Replace('.', '$')
        };

    private static LiveIedDataObjectModel FourPhaseModel()
        => new()
        {
            Name = "PhV",
            Reference = Owner,
            Attributes = ["phsA", "phsB", "phsC", "neut"]
                .SelectMany(phase => new[]
                {
                    Leaf(phase, "mag.f"), Leaf(phase, "ang.f")
                }).ToArray()
        };

    [Fact]
    public void ExtraNeutralCannotBypassTimestampAuthorityOnRoot()
    {
        var row = IedBrowserStructuredDetails.DataObject(FourPhaseModel(), "MX");
        Assert.Equal(4, row.VectorComponents.Count);
        Assert.Equal(3, row.PhasePlans.Count);
        var ledger = new IedBrowserLiveValues();
        var session = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(session,
            Owner + ".phsA.cVal.mag.f", "MX", "10", "", "deviceT1", "Report"));
        Assert.True(ledger.PublishForAssociation(session,
            Owner + ".phsA.cVal.ang.f", "MX", "30", "", "deviceT2", "Report"));
        Assert.True(ledger.PublishForAssociation(session,
            Owner + ".neut.cVal.mag.f", "MX", "3", "", "deviceT1", "Report"));
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            row, ledger, new Dictionary<string, string?>(), true, out var display));
        Assert.Contains("phsA: 10", display.Text);
        Assert.Contains("angle 30 (observed separately)", display.Text);
        Assert.Contains("neut: 3", display.Text);
        Assert.DoesNotContain("∠ 30", display.Text);
        Assert.StartsWith("Independent", display.Source);

        Assert.True(ledger.PublishForAssociation(session,
            Owner + ".phsA.cVal.ang.f", "MX", "30", "", "deviceT1", "Report"));
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            row, ledger, new Dictionary<string, string?>(), true, out var matched));
        Assert.Contains("phsA: 10 ∠ 30", matched.Text);
        Assert.DoesNotContain("angle 30 (observed separately)", matched.Text);
    }

    [Fact]
    public void ThreePhaseCompositeUsesSameStrictTimestampPolicy()
    {
        var model = FourPhaseModel();
        var attributes = model.Attributes.Where(a =>
            !a.AttributePath.Contains(".neut.", StringComparison.Ordinal)).ToArray();
        var row = IedBrowserStructuredDetails.DataObject(
            new LiveIedDataObjectModel
            {
                Name = "PhV", Reference = Owner, Attributes = attributes
            }, "MX");
        Assert.Equal(3, row.PhasePlans.Count);
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association,
            Owner + ".phsA.cVal.mag.f", "MX", "10", "", "t1", "Report"));
        Assert.True(ledger.PublishForAssociation(association,
            Owner + ".phsA.cVal.ang.f", "MX", "20", "", "t2", "Report"));
        Assert.True(IedBrowserEngineeringComposite.TryCompose(row, ledger,
            new Dictionary<string, string?>(), true, out var output));
        Assert.Contains("angle not verified", output.Text);
        Assert.DoesNotContain("∠ 20", output.Text);
        Assert.DoesNotContain("Independent", output.Source);
    }

    [Fact]
    public void StaleAndHistoricalConfigurationAreNeverCalledFresh()
    {
        var owner = "LD/MMXU1.PhV.phsA";
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    DataObjects = [new LiveIedDataObjectModel
                    {
                        Name = "PhV", Reference = Owner,
                        Attributes =
                        [
                            new LiveIedDataAttributeModel
                            {
                                FunctionalConstraint = "CF",
                                MmsReference = "LD/MMXU1$CF$PhV$phsA$units$SIUnit"
                            },
                            new LiveIedDataAttributeModel
                            {
                                FunctionalConstraint = "CF",
                                MmsReference = "LD/MMXU1$CF$PhV$phsA$units$multiplier"
                            }
                        ]
                    }]
                }]
            }]
        };
        var canonical = new LiveIedCanonicalModel
        {
            Discovery = model,
            InstanceValues =
            [
                new LiveIedInstanceValueEvidence
                {
                    Domain = "LD", LogicalNode = "MMXU1",
                    DataObject = "PhV", AttributePath = "phsA.units.SIUnit",
                    FunctionalConstraint = "CF", Value = MmsDataValue.Integer(29)
                },
                new LiveIedInstanceValueEvidence
                {
                    Domain = "LD", LogicalNode = "MMXU1",
                    DataObject = "PhV", AttributePath = "phsA.units.multiplier",
                    FunctionalConstraint = "CF", Value = MmsDataValue.Integer(3)
                }
            ]
        };
        var ledger = new IedBrowserLiveValues();
        var historical = new IedBrowserCfEvidencePresentation(ledger, model, canonical);
        Assert.True(historical.TryFormatUnitsParent(owner + ".units", "CF",
            out var historicUnit, out _, out var historicAuthority));
        Assert.Equal("kV", historicUnit);
        Assert.Equal("Historical discovery CF", historicAuthority);

        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association, owner + ".units.SIUnit",
            "CF", "29", "", "", "MMS Read"));
        Assert.True(ledger.PublishForAssociation(association, owner + ".units.multiplier",
            "CF", "3", "", "", "MMS Read"));
        Assert.True(historical.TryFormatUnitsParent(owner + ".units", "CF",
            out var currentUnit, out _, out var currentAuthority));
        Assert.Equal("kV", currentUnit);
        Assert.Equal("Verified IEC CF configuration", currentAuthority);
        ledger.MarkOffline();
        Assert.True(historical.TryFormatUnitsParent(owner + ".units", "CF",
            out _, out _, out var staleAuthority));
        Assert.Equal("Stale IEC CF configuration", staleAuthority);
        ledger.BeginAssociation();
        Assert.True(historical.TryFormatUnitsParent(owner + ".units", "CF",
            out _, out _, out var afterReconnect));
        Assert.Equal("Historical discovery CF", afterReconnect);
    }
}
