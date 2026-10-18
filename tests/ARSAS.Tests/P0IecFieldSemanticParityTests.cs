using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Protocol.Mms;
using ArIED61850Tester.Services;
using AR.Iec61850.Mms;

namespace ARSAS.Tests;

/// <summary>Field P0 reproductions against exact IEC owner/FC; never fake IED observations.</summary>
public sealed class P0IecFieldSemanticParityTests
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
    [Fact]
    public void Qt01_OnExpandTargetsOnlyExactUnprovenPhaseQualityAndUsesTypedWire()
    {
        const string q = "LD/MMXU1.PPV.phsAB.q";
        var ln = new LiveIedLogicalNodeModel
        {
            Name = "MMXU1", LnClass = "MMXU",
            DataObjects = [new LiveIedDataObjectModel
            {
                Name = "PPV", Reference = "LD/MMXU1.PPV", InferredCdc = "WYE",
                Attributes = [new LiveIedDataAttributeModel
                {
                    ObjectReference = "LD/MMXU1.PPV",
                    AttributePath = "PPV.phsAB.q",
                    MmsReference = "LD/MMXU1$MX$PPV$phsAB$q",
                    FunctionalConstraint = "MX", SclBType = "Quality",
                    MmsType = "bit-string"
                }]
            }]
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD", LogicalNodes = [ln]
            }]
        };
        var page = new IedBrowserTreeRow("model/ln/LD/MMXU1",
            "MMXU1", "LN", 2, ln);
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        ledger.PublishForAssociation(association, q, "MX", "good",
            "", "", "Report quality");
        Assert.Single(IedBrowserExactCompanionReadRequest.Build(
            model, page, ledger, q, "MX", "Quality"));
        Assert.Empty(IedBrowserExactCompanionReadRequest.Build(
            model, page, ledger, q, "ST", "Quality"));
        Assert.Empty(IedBrowserExactCompanionReadRequest.Build(
            model, page, ledger, "LD/MMXU1.PPV.phsBC.q", "MX", "Quality"));
        Assert.Empty(IedBrowserExactCompanionReadRequest.Build(
            model, page, ledger, q, "CO", "Quality"));

        // BitString is the typed MMS payload, not the textual "good" string.
        var frame = new MmsReportFrame
        {
            Values = [new MmsReportValue
            {
                Member = new MmsDataSetDirectoryMember
                {
                    UserReference = q, FunctionalConstraint = "MX"
                },
                Value = MmsDataValue.BitString(3, [0, 0])
            }]
        };
        var raw = Assert.Single(IedBrowserReportWireEvidence.Extract(frame));
        Assert.Equal(q, raw.CarrierReference);
        Assert.True(ledger.PublishForAssociation(association, q, "MX",
            raw.QualityBits!.Summary, "", "", "MMS Read",
            qualityEvidence: raw.QualityBits));
        Assert.True(ledger.TryGet(q, "MX", out var typed,
            requiredEvidence: "Quality"));
        Assert.Equal("false", typed.QualityBits?.FormatField("Overflow"));
        Assert.Empty(IedBrowserExactCompanionReadRequest.Build(
            model, page, ledger, q, "MX", "Quality"));
        ledger.BeginAssociation();
        Assert.False(ledger.TryGet(q, "MX", out _));
        Assert.Single(IedBrowserExactCompanionReadRequest.Build(
            model, page, ledger, q, "MX", "Quality"));
    }

}
