using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;
using ArIED61850Tester.Protocol.Mms;

namespace ARSAS.Tests;

public sealed class P0LiveAcceptanceCoverageTests
{
    private static LiveIedModelDiscoveryDocument Model()
    {
        LiveIedDataAttributeModel Attr(string owner, string leaf, string fc,
            string type) => new()
        {
            ObjectReference = owner,
            AttributePath = owner[(owner.IndexOf('.') + 1)..] + "." + leaf,
            MmsReference = owner + "." + leaf,
            FunctionalConstraint = fc,
            SclBType = type,
            MmsType = type
        };
        var phase = "LD/MMXU1.PhV";
        var setting = "LD/PIOC1.InMagTyp";
        return new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "MMXU1", LnClass = "MMXU",
                    DataObjects = [
                        new LiveIedDataObjectModel
                        {
                            Reference = phase, Name = "PhV",
                            Attributes = [
                                Attr(phase, "mag.f", "MX", "FLOAT32"),
                                Attr(phase, "q", "MX", "Quality"),
                                Attr(phase, "t", "MX", "Timestamp"),
                                Attr(phase, "units.SIUnit", "CF", "Enum"),
                                Attr(phase, "units.multiplier", "CF", "Enum")
                            ]
                        },new LiveIedDataObjectModel
                        {
                            Reference = setting, Name = "InMagTyp",
                            Attributes = [Attr(setting, "setVal", "SG", "Enum")]
                        }
                    ]
                }]
            }]
        };
    }

    [Fact]
    public void Metrics01_ExactAssociationAndFcCountOnlyObservedNoGuessedDecoding()
    {
        var model = Model();
        var ledger = new IedBrowserLiveValues();
        var token = ledger.BeginAssociation();
        ledger.PublishForAssociation(token, "LD/MMXU1.PhV.mag.f", "MX",
            "12.75", "", "", "MMS Read");
        ledger.PublishForAssociation(token, "LD/MMXU1.PhV.q", "MX",
            "good", "", "", "Report quality");
        ledger.PublishForAssociation(token, "LD/MMXU1.PhV.t", "MX",
            "2026-10-18T00:00:00Z", "", "", "Report timestamp");
        ledger.PublishForAssociation(token, "LD/MMXU1.PhV.units.SIUnit", "CF",
            "29", "", "", "MMS Read");
        ledger.PublishForAssociation(token, "LD/MMXU1.PhV.units.multiplier", "CF",
            "3", "", "", "MMS Read");
        ledger.PublishForAssociation(token, "LD/PIOC1.InMagTyp.setVal", "SG",
            "0", "", "", "MMS Read");

        var before = IedBrowserAcceptanceMetrics.Capture(model, ledger);
        Assert.Equal(6, before.ReadableAttributes);
        Assert.Equal(6, before.FreshObserved);
        Assert.Equal(0, before.MissingOrDeferred);
        Assert.Equal(1, before.CfPairOwners);
        Assert.Equal(1, before.CfPairsProven);
        Assert.Equal(1, before.SgEnumObserved);
        Assert.Equal(0, before.SgEnumDecoded);
        Assert.Equal(1, before.QualityObserved);
        Assert.Equal(0, before.QualityDecoded);
        Assert.Equal(1, before.TimestampObserved);
        Assert.Equal(0, before.TimestampDecoded);
        Assert.Null(before.ConsumerMismatchCount);

        Assert.True(MmsQualityDecoder.TryDecode(
            MmsDataValue.BitString(3, [0, 0]).RawValue.ToArray(), out var q));
        Assert.True(ledger.PublishForAssociation(token, "LD/MMXU1.PhV.q", "MX",
            q.Summary, "", "", "Report quality · raw MMS",
            qualityEvidence: q));
        var after = IedBrowserAcceptanceMetrics.Capture(model, ledger);
        Assert.Equal(1, after.QualityDecoded);
        Assert.Equal(0, after.TimestampDecoded);
        Assert.Null(after.ConsumerMismatchCount);

        ledger.BeginAssociation();
        Assert.False(ledger.PublishForAssociation(token, "LD/MMXU1.PhV.mag.f",
            "MX", "99", "", "", "MMS Read"));
        var reset = IedBrowserAcceptanceMetrics.Capture(model, ledger);
        Assert.Equal(0, reset.FreshObserved);
        Assert.Equal(6, reset.MissingOrDeferred);
        Assert.Equal(0, reset.CfPairsProven);
    }

    [Fact]
    public void Metrics02_NoSiblingCfPairingAndFcMismatchCanInflateProof()
    {
        var model = Model();
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        ledger.PublishForAssociation(association, "LD/MMXU1.PhV.units.SIUnit",
            "CF", "29", "", "", "MMS Read");
        ledger.PublishForAssociation(association, "LD/MMXU1.Other.units.multiplier",
            "CF", "3", "", "", "MMS Read");
        ledger.PublishForAssociation(association, "LD/MMXU1.PhV.units.multiplier",
            "SG", "3", "", "", "MMS Read");
        var m = IedBrowserAcceptanceMetrics.Capture(model, ledger,
            failures: [new IedBrowserReadFailureSample("LD/MMXU1.PhV.mag.f", "MX",
                "MMS timeout")]);
        Assert.Equal(1, m.CfPairOwners);
        Assert.Equal(0, m.CfPairsProven);
        Assert.Equal(1, m.FailedReadSamples);
        Assert.Equal(5, m.MissingOrDeferred);
    }
}
