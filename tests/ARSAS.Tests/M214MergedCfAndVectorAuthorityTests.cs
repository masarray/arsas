using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Integrates Thread A's CF authority with Thread B's normalized scalar/vector
/// projections on the current, already-validated Browser stack.
/// </summary>
public sealed class M214MergedCfAndVectorAuthorityTests
{
    private const string Owner = "LD/MMXU1.PhV";

    private static LiveIedDataAttributeModel Leaf(string phase, string suffix) => new()
    {
        ObjectReference = Owner, AttributePath = "PhV." + phase + ".cVal." + suffix,
        FunctionalConstraint = "MX",
        MmsReference = "LD/MMXU1$MX$PhV$" + phase + "$cVal$" +
            suffix.Replace('.', '$')
    };

    [Fact]
    public void NeutralExtension_UsesOneComposerAndRefusesMismatchedAngleTime()
    {
        var model = new LiveIedDataObjectModel
        {
            Name = "PhV", Reference = Owner, InferredCdc = "WYE",
            Attributes = new[] { "phsA", "phsB", "phsC", "neut" }
                .SelectMany(p => new[] { Leaf(p, "mag.f"), Leaf(p, "ang.f") })
                .ToArray()
        };
        var row = IedBrowserStructuredDetails.DataObject(model, "MX");
        Assert.Equal(4, row.VectorComponents.Count);
        Assert.Equal(3, row.PhasePlans.Count);
        var ledger = new IedBrowserLiveValues();
        var session = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(session,
            Owner + ".phsA.cVal.mag.f", "MX", "10", "", "t1", "Report"));
        Assert.True(ledger.PublishForAssociation(session,
            Owner + ".phsA.cVal.ang.f", "MX", "30", "", "t2", "Report"));
        Assert.True(ledger.PublishForAssociation(session,
            Owner + ".neut.cVal.mag.f", "MX", "3", "", "t1", "Report"));
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            row, ledger, new Dictionary<string, string?>(), true, out var unpaired));
        Assert.Contains("phsA: 10", unpaired.Text);
        Assert.DoesNotContain("angle 30", unpaired.Text);
        Assert.DoesNotContain("neut: 3", unpaired.Text);
        Assert.Contains("∠ 30", unpaired.Text);
        Assert.Contains("other model-proven components on expansion",
            unpaired.Provenance);
        // The complete independently observed vector remains accessible
        // through an explicitly expanded inspection row, never thrown away.
        var expanded = row with { IsDataObject = false };
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            expanded, ledger, new Dictionary<string, string?>(), true, out var all));
        Assert.Equal("Report", all.Source);
        Assert.Contains("∠", all.Text);
        Assert.Contains("angle 30", all.Provenance);
        Assert.DoesNotContain("independent, not paired", all.Text);
        Assert.Contains("not paired with magnitude", all.Provenance);
        Assert.Contains("neut: 3", all.Text);

        Assert.True(ledger.PublishForAssociation(session,
            Owner + ".phsA.cVal.ang.f", "MX", "30", "", "t1", "Report"));
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            row, ledger, new Dictionary<string, string?>(), true, out var paired));
        Assert.Contains("phsA: 10 ∠ 30", paired.Text);
    }

    [Fact]
    public void CfUnitHistoryStalenessAndReconnect_AreNeverPresentedAsFresh()
    {
        var owner = Owner + ".phsA";
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
        var values = new IedBrowserLiveValues();
        var projection = new IedBrowserCfEvidencePresentation(values, model, canonical);
        Assert.True(projection.TryFormatUnitsParent(owner + ".units", "CF",
            out var historical, out _, out var historicalAuthority));
        Assert.Equal("kV", historical);
        Assert.Equal("Historical discovery CF", historicalAuthority);
        var session = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(session, owner + ".units.SIUnit",
            "CF", "29", "", "", "MMS Read"));
        Assert.True(values.PublishForAssociation(session, owner + ".units.multiplier",
            "CF", "3", "", "", "MMS Read"));
        Assert.True(projection.TryFormatUnitsParent(owner + ".units", "CF",
            out var verified, out _, out var verifiedAuthority));
        Assert.Equal("kV", verified);
        Assert.Equal("Verified IEC CF configuration", verifiedAuthority);
        values.MarkOffline();
        Assert.True(projection.TryFormatUnitsParent(owner + ".units", "CF",
            out _, out _, out var staleAuthority));
        Assert.Equal("Stale IEC CF configuration", staleAuthority);
        values.BeginAssociation();
        Assert.True(projection.TryFormatUnitsParent(owner + ".units", "CF",
            out _, out _, out var postReconnectAuthority));
        Assert.Equal("Historical discovery CF", postReconnectAuthority);
    }

    [Fact]
    public void ScalarMvStillCannotEnterVectorFallback_DespiteSharedEntryPoint()
    {
        const string owner = "LD/MMXU1.Hz";
        var scalar = new LiveIedDataObjectModel
        {
            Reference = owner, Name = "Hz", InferredCdc = "MV",
            Attributes = [new LiveIedDataAttributeModel
            {
                ObjectReference = owner, AttributePath = "Hz.mag.f",
                FunctionalConstraint = "MX",
                MmsReference = "LD/MMXU1$MX$Hz$mag$f"
            }]
        };
        var row = IedBrowserStructuredDetails.DataObject(scalar, "MX");
        Assert.Empty(row.VectorComponents);
        Assert.Empty(row.PhasePlans);
        Assert.Null(row.PolarPlan);
        var ledger = new IedBrowserLiveValues();
        var session = ledger.BeginAssociation();
        ledger.PublishForAssociation(session, row.ValueReference,
            "MX", "50", "", "", "MMS Read");
        Assert.False(IedBrowserEngineeringComposite.TryCompose(
            row, ledger, new Dictionary<string, string?>(), false, out _));
        Assert.True(ledger.TryGet(row.ValueReference, "MX", out var trueScalar));
        Assert.Equal("50", trueScalar.Value);
    }
}
