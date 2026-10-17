using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M217TDataSetReportCanonicalUnitHydrationTests
{
    private const string SetRef = "LD/LLN0.Analog";
    private const string Owner = "LD/MMXU1.PPV";

    private static LiveIedDataAttributeModel Attribute(string name, string fc, string tail)
    {
        var obj = "LD/MMXU1." + name;
        return new LiveIedDataAttributeModel
        {
            ObjectReference = obj,
            AttributePath = name + "." + tail,
            MmsReference = "LD/MMXU1$" + fc + "$" + name + "$" +
                tail.Replace('.', '$'),
            FunctionalConstraint = fc,
            SclBType = tail.EndsWith("SIUnit", StringComparison.Ordinal)
                ? "Enum" : "FLOAT32"
        };
    }

    private static LiveIedModelDiscoveryDocument Fixture(bool includeUnitPair = true)
    {
        var ppv = new List<LiveIedDataAttributeModel>();
        foreach (var phase in new[] { "phsAB", "phsBC", "phsCA" })
        {
            ppv.Add(Attribute("PPV", "MX", phase + ".cVal.mag.f"));
            ppv.Add(Attribute("PPV", "MX", phase + ".cVal.ang.f"));
            ppv.Add(Attribute("PPV", "CF", phase + ".units.SIUnit"));
            if (includeUnitPair)
                ppv.Add(Attribute("PPV", "CF", phase + ".units.multiplier"));
        }
        return new LiveIedModelDiscoveryDocument
        {
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "LD",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MMXU1",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Name = "PPV", Reference = Owner,
                                    InferredCdc = "DEL", Attributes = ppv
                                },
                                new LiveIedDataObjectModel
                                {
                                    Name = "PhV", Reference = "LD/MMXU1.PhV",
                                    Attributes =
                                    [
                                        Attribute("PhV", "MX", "phsA.cVal.mag.f"),
                                        Attribute("PhV", "CF", "phsA.units.SIUnit"),
                                        Attribute("PhV", "CF", "phsA.units.multiplier")
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ],
            DataSets =
            [
                new LiveIedDataSetModel
                {
                    Domain = "LD", LogicalNode = "LLN0",
                    Name = "Analog", Reference = SetRef,
                    Members =
                    [
                        new LiveIedDataSetMemberModel
                        {
                            Reference = Owner, FunctionalConstraint = "MX"
                        }
                    ]
                }
            ]
        };
    }

    [Fact]
    public void OnlyExactLiveDataSetPhaseUnitsAreReadNotUnrelatedSiblings()
    {
        var model = Fixture();
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        var targets = IedBrowserMeasurementMetadataReadPlan.Build(
            model, SetRef, values);
        Assert.Equal(6, targets.Count);
        Assert.All(targets, target =>
        {
            Assert.Equal("CF", target.FunctionalConstraint);
            Assert.StartsWith(Owner + ".", IedBrowserLiveValues.CanonicalReference(
                target.Reference));
        });
        Assert.DoesNotContain(targets, target => target.Reference.Contains(
            "PhV", StringComparison.Ordinal));
        values.PublishForAssociation(association, Owner +
            ".phsAB.units.SIUnit", "CF", "5", "", "", "MMS Read");
        var pending = IedBrowserMeasurementMetadataReadPlan.Build(
            model, SetRef, values);
        Assert.Equal(5, pending.Count);
        Assert.DoesNotContain(pending, target =>
            IedBrowserLiveValues.CanonicalReference(target.Reference) ==
            Owner + ".phsAB.units.SIUnit");
        Assert.Empty(IedBrowserMeasurementMetadataReadPlan.Build(
            model, "LD/LLN0.Unknown", values));
        Assert.Equal(2, IedBrowserMeasurementMetadataReadPlan.Build(
            model, SetRef, values, budget: 2).Count);
    }

    [Fact]
    public void FocusedDataSetHydratesCfBeforeMxAndResolvesSharedUnitForReportRows()
    {
        var model = Fixture();
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        var page = new IedBrowserTreeRow(
            "datasets/set/" + SetRef, "LLN0.Analog", "DataSet", 3,
            model.DataSets.Single());
        var focused = IedBrowserReadPlan.BuildFocused(model, page, values);
        Assert.True(focused.Count >= 6);
        Assert.All(focused.Take(6), target =>
            Assert.Equal("CF", target.FunctionalConstraint));

        foreach (var phase in new[] { "phsAB", "phsBC", "phsCA" })
        {
            values.PublishForAssociation(association,
                Owner + "." + phase + ".units.SIUnit",
                "CF", "V", "", "", "MMS Read");
            values.PublishForAssociation(association,
                Owner + "." + phase + ".units.multiplier",
                "CF", "3", "", "", "MMS Read");
            values.PublishForAssociation(association,
                Owner + "." + phase + ".cVal.mag.f",
                "MX", "0", "", "sample-1", "Report");
            values.PublishForAssociation(association,
                Owner + "." + phase + ".cVal.ang.f",
                "MX", "0", "", "sample-1", "Report");
        }
        var cf = new IedBrowserCfEvidencePresentation(values, model, null);
        var units = cf.BuildVerifiedUnits([], model);
        var dataSetRow = IedBrowserStructuredDetails.Member(
            model.DataSets.Single().Members.Single(), model);
        Assert.True(IedBrowserEngineeringComposite.TryCompose(dataSetRow,
            values, units, true, out var report));
        Assert.Equal("phsAB: 0 kV ∠ 0°; phsBC: 0 kV ∠ 0°; phsCA: 0 kV ∠ 0°",
            report.Text);
        Assert.Empty(IedBrowserMeasurementMetadataReadPlan.Build(
            model, SetRef, values));
    }

    [Fact]
    public void MissingLiveUnitPairIsNeverReplacedByGuessedSclUnit()
    {
        var model = Fixture(includeUnitPair: false);
        var values = new IedBrowserLiveValues();
        values.BeginAssociation();
        Assert.Empty(IedBrowserMeasurementMetadataReadPlan.Build(
            model, SetRef, values));
    }
}
