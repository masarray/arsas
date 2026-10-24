using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0XcbrNumericCounterUnitTests
{
    private static LiveIedDataObjectModel Counter(string name, string type = "INT32U")
    {
        var owner = "LD/XCBR1." + name;
        LiveIedDataAttributeModel Attr(string path, string fc, string btype) => new()
        {
            ObjectReference = owner, AttributePath = name + "." + path,
            MmsReference = "LD/XCBR1$" + fc + "$" + name + "$" +
                path.Replace('.', '$'),
            FunctionalConstraint = fc, SclBType = btype
        };
        return new LiveIedDataObjectModel
        {
            Reference = owner, Name = name, InferredCdc = "INS",
            Attributes =
            [
                Attr("stVal", "ST", type),
                Attr("units.SIUnit", "CF", "Enum"),
                Attr("units.multiplier", "CF", "Enum")
            ]
        };
    }

    private static LiveIedModelDiscoveryDocument Model(
        params LiveIedDataObjectModel[] dataObjects)
        => new()
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "XCBR1", LnClass = "XCBR", DataObjects = dataObjects
                }]
            }]
        };

    private static void PublishUnit(
        IedBrowserLiveValues values, long association,
        string owner, string unitOrd, string multiplier)
    {
        Assert.True(values.PublishForAssociation(association,
            owner + ".units.SIUnit", "CF", unitOrd, "", "", "MMS Read"));
        Assert.True(values.PublishForAssociation(association,
            owner + ".units.multiplier", "CF", multiplier, "", "", "MMS Read"));
    }

    [Fact]
    public void Xcbr02_ExactIntegerCounterCfSupportsAmperesSquaredTimeAndSeconds()
    {
        var model = Model(Counter("SumSwARs"), Counter("SumSwATmms"),
            Counter("ThAlSwA"));
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        PublishUnit(ledger, association, "LD/XCBR1.SumSwARs", "70", "3");
        PublishUnit(ledger, association, "LD/XCBR1.SumSwATmms", "4", "0");
        PublishUnit(ledger, association, "LD/XCBR1.ThAlSwA", "70", "3");
        var cf = new IedBrowserCfEvidencePresentation(ledger, model, null);

        foreach (var (name, expected) in new[]
        {
            ("SumSwARs", "0 kA²t"),
            ("SumSwATmms", "0 s"),
            ("ThAlSwA", "0 kA²t")
        })
        {
            Assert.True(cf.TryFormatVerifiedNumericStatusUnit(
                "LD/XCBR1." + name + ".stVal", "ST", out var unit, out var proof));
            Assert.NotEmpty(proof);
            Assert.Equal(expected,
                IedBrowserEngineeringValuePresentation.Format("0", unit));
            var reference = "LD/XCBR1." + name + ".stVal";
            var units = cf.BuildVerifiedUnits([], model);
            // Same unit for a DO parent, scalar leaf, or a DataSet/Report
            // row resolving that exact canonical ST stVal.
            var parentUnit = IedBrowserEngineeringValuePresentation.ResolveObservedUnit(
                units, cf, reference, reference, "ST", isEngineeringValue: true);
            var leafUnit = IedBrowserEngineeringValuePresentation.ResolveObservedUnit(
                units, cf, reference, reference, "ST", isEngineeringValue: false);
            Assert.Equal(unit, parentUnit);
            Assert.Equal(unit, leafUnit);
        }
        Assert.False(cf.TryFormatVerifiedNumericStatusUnit(
            "LD/XCBR1.SumSwARs.stVal", "MX", out _, out _));
        Assert.False(cf.TryFormatVerifiedNumericStatusUnit(
            "LD/XCBR1.Other.stVal", "ST", out _, out _));
    }

    [Fact]
    public void Xcbr03_UntypedEnumAndMissingOrStaleCfMustNotGainCounterUnit()
    {
        var model = Model(Counter("SumSwARs"), Counter("CBOpCap", "Enum"),
            Counter("Missing"), Counter("BooleanObject", "BOOLEAN"));
        var ledger = new IedBrowserLiveValues();
        var oldAssociation = ledger.BeginAssociation();
        PublishUnit(ledger, oldAssociation, "LD/XCBR1.SumSwARs", "70", "3");
        PublishUnit(ledger, oldAssociation, "LD/XCBR1.CBOpCap", "70", "3");
        PublishUnit(ledger, oldAssociation, "LD/XCBR1.BooleanObject", "70", "3");
        var cf = new IedBrowserCfEvidencePresentation(ledger, model, null);
        Assert.False(cf.TryFormatVerifiedNumericStatusUnit(
            "LD/XCBR1.CBOpCap.stVal", "ST", out _, out _));
        Assert.False(cf.TryFormatVerifiedNumericStatusUnit(
            "LD/XCBR1.BooleanObject.stVal", "ST", out _, out _));
        Assert.False(cf.TryFormatVerifiedNumericStatusUnit(
            "LD/XCBR1.Missing.stVal", "ST", out _, out _));

        ledger.BeginAssociation();
        Assert.False(cf.TryFormatVerifiedNumericStatusUnit(
            "LD/XCBR1.SumSwARs.stVal", "ST", out _, out _));
        Assert.False(ledger.PublishForAssociation(oldAssociation,
            "LD/XCBR1.SumSwARs.units.SIUnit", "CF", "70",
            "", "", "MMS Read"));
    }
}
