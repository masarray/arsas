using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>Field P0 reproductions against exact IEC owner/FC; never fake IED observations.</summary>
public sealed class P0IedScoutFieldSemanticParityTests
{
    private static readonly string Phase = "LD/MMXU1.PPV.phsAB";

    [Theory]
    [InlineData("LD/MMXU1.PPV.phsAB.cVal.ang")]
    [InlineData("LD/MMXU1.PPV.phsAB.cVal.ang.f")]
    [InlineData("LD/MMXU1.PhV.phsA.cVal.ang")]
    [InlineData("LD/MMXU1.A.phsA.cVal.ang")]
    public void Ang01_Ang02_VectorAngleParentsAndLeavesCannotInheritMagnitudeUnits(string path)
    {
        var owner = path[..path.IndexOf(".cVal.ang", StringComparison.Ordinal)];
        var unitIndex = new Dictionary<string, string?>
        {
            [owner + "|MX"] = owner.Contains(".A.", StringComparison.Ordinal) ? "kA" : "kV"
        };
        var role = IedBrowserEngineeringValuePresentation.ResolveUnit(unitIndex, path, "MX");
        Assert.Equal("°", role);
        Assert.Equal("0°", IedBrowserEngineeringValuePresentation.Format("0", role));
        Assert.Equal(unitIndex[owner + "|MX"],
            IedBrowserEngineeringValuePresentation.ResolveUnit(
                unitIndex, owner + ".cVal.mag.f", "MX"));
    }

    [Fact]
    public void Phv01_ExactLiveCfPairWinsOverConflictingSignalMagnitudeMetadata()
    {
        var owner = "LD/MMXU1.PhV.phsA";
        LiveIedDataAttributeModel Attr(string path, string fc) => new()
        {
            ObjectReference = "LD/MMXU1.PhV",
            AttributePath = "PhV.phsA." + path,
            MmsReference = "LD/MMXU1$" + fc + "$PhV$phsA$" + path.Replace('.', '$'),
            FunctionalConstraint = fc
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    DataObjects = [new LiveIedDataObjectModel
                    {
                        Name = "PhV", Reference = "LD/MMXU1.PhV",
                        InferredCdc = "WYE",
                        Attributes = [Attr("cVal.mag.f", "MX"),
                            Attr("cVal.ang.f", "MX"),
                            Attr("units.SIUnit", "CF"), Attr("units.multiplier", "CF")]
                    }]
                }]
            }]
        };
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        ledger.PublishForAssociation(association, owner + ".units.SIUnit", "CF",
            "29", "", "", "MMS Read");
        ledger.PublishForAssociation(association, owner + ".units.multiplier", "CF",
            "3", "", "", "MMS Read");
        var signals = new [] { new SignalDefinition
        {
            ObjectReference = owner + ".cVal.mag.f", FunctionalConstraint = "MX", Unit = "V"
        }};
        var units = new IedBrowserCfEvidencePresentation(ledger, model, null)
            .BuildVerifiedUnits(signals, model);
        Assert.Equal("kV", IedBrowserEngineeringValuePresentation.ResolveUnit(
            units, owner + ".cVal.mag.f", "MX"));
        Assert.Equal("°", IedBrowserEngineeringValuePresentation.ResolveUnit(
            units, owner + ".cVal.ang", "MX"));
    }

    [Fact]
    public void Xcbr01_CbOpCapEnumRequiresExactMatchingEngDeclaration()
    {
        const string owner = "LD/XCBR1.CBOpCap";
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "XCBR1", LnClass = "XCBR",
                    DataObjects = [new LiveIedDataObjectModel
                    {
                        Name = "CBOpCap", Reference = owner, InferredCdc = "ENG",
                        Attributes = [new LiveIedDataAttributeModel
                        {
                            ObjectReference = owner, FunctionalConstraint = "ST",
                            AttributePath = "CBOpCap.stVal",
                            MmsReference = "LD/XCBR1$ST$CBOpCap$stVal",
                            SclBType = "Enum"
                        }]
                    }]
                }]
            }]
        };
        var schema = new IedBrowserSchemaIndex(model);
        Assert.Equal("None", IedBrowserStandardStatusPresentation.Format(
            owner + ".stVal", "ST", "value1", schema));
        Assert.Equal("None", IedBrowserStandardStatusPresentation.Format(
            owner + ".stVal", "ST", "1", schema));
        Assert.Equal("7", IedBrowserStandardStatusPresentation.Format(
            owner + ".stVal", "ST", "7", schema));
        Assert.Equal("value1", IedBrowserStandardStatusPresentation.Format(
            owner + ".stVal", "MX", "value1", schema));
        Assert.Equal("value1", IedBrowserStandardStatusPresentation.Format(
            "LD/XCBR1.NotDeclared.stVal", "ST", "value1", schema));
    }
}
