using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M217USgAnalogueSettingUnitParityTests
{
    private const string Owner = "LD/SGgndPIOC1.StrVal";
    private static LiveIedDataAttributeModel Attribute(string path, string fc)
        => new()
        {
            ObjectReference = Owner,
            AttributePath = "StrVal." + path,
            MmsReference = "LD/SGgndPIOC1$" + fc + "$StrVal$" +
                path.Replace('.', '$'),
            FunctionalConstraint = fc,
            SclBType = "FLOAT32"
        };

    private static (LiveIedModelDiscoveryDocument Model, IedBrowserTreeRow Page)
        ModelAndPage()
    {
        var block = new LiveIedControlBlockModel
        {
            Domain = "LD"
        };
        var model = new LiveIedModelDiscoveryDocument
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
                            Name = "SGgndPIOC1",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Name = "StrVal", Reference = Owner,
                                    Attributes =
                                    [
                                        Attribute("setMag.f", "SG"),
                                        Attribute("units.SIUnit", "CF"),
                                        Attribute("units.multiplier", "CF"),
                                        Attribute("setVal", "SE")
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        return (model, new IedBrowserTreeRow(
            "sg/LD/LLN0.SGCB", "SGCB", "SGCB", 3, block));
    }

    [Fact]
    public void ActualSettingMagnitudeUsesVerifiedCfUnitNotVendorGuess()
    {
        var (model, page) = ModelAndPage();
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        var candidates = IedBrowserMeasurementMetadataReadPlan.BuildForSettingGroup(
            model, page, values);
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, t => Assert.Equal("CF", t.FunctionalConstraint));
        values.PublishForAssociation(association, Owner + ".setMag.f",
            "SG", "0", "", "", "MMS Read");
        values.PublishForAssociation(association, Owner + ".units.SIUnit",
            "CF", "5", "", "", "MMS Read");
        values.PublishForAssociation(association, Owner + ".units.multiplier",
            "CF", "3", "", "", "MMS Read");
        var cf = new IedBrowserCfEvidencePresentation(values, model, null);
        var units = cf.BuildVerifiedUnits([], model);
        Assert.Equal("kA", IedBrowserEngineeringValuePresentation.ResolveUnit(
            units, Owner + ".setMag.f", "SG"));
        Assert.Equal("0 kA", IedBrowserEngineeringValuePresentation.Format("0",
            IedBrowserEngineeringValuePresentation.ResolveUnit(
                units, Owner + ".setMag.f", "SG")));
        Assert.Equal("", IedBrowserEngineeringValuePresentation.ResolveUnit(
            units, Owner + ".setVal", "SE"));
        Assert.Empty(IedBrowserMeasurementMetadataReadPlan.BuildForSettingGroup(
            model, page, values));
    }

    [Fact]
    public void OnlyExactSgDomainAndSchemaCfPairsMayAuthorizeMetadataRead()
    {
        var (model, page) = ModelAndPage();
        var values = new IedBrowserLiveValues();
        values.BeginAssociation();
        Assert.Empty(IedBrowserMeasurementMetadataReadPlan.BuildForSettingGroup(
            model, new IedBrowserTreeRow("other", "other", "DataSet", 2), values));
        Assert.Empty(IedBrowserMeasurementMetadataReadPlan.BuildForSettingGroup(
            null, page, values));
        Assert.Equal(1, IedBrowserMeasurementMetadataReadPlan.BuildForSettingGroup(
            model, page, values, budget: 1).Count);
    }
}
