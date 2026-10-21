using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class M217TypedDoublePointLiveModelTests
{
    private static LiveIedDataAttributeModel Attr(
        string owner, string path, string fc = "ST", string bType = "Enum") => new()
    {
        ObjectReference = owner,
        AttributePath = owner[(owner.LastIndexOf('.') + 1)..] + "." + path,
        MmsReference = owner[..owner.IndexOf('.')] + "$" + fc + "$" +
            owner[(owner.IndexOf('.') + 1)..].Replace('.', '$') + "$" + path,
        FunctionalConstraint = fc,
        SclBType = bType,
        TypeSource = "LiveTypeSpecification"
    };

    private static LiveIedDataObjectModel Obj(string owner, string cdc,
        string fc = "ST", string bType = "Enum") => new()
    {
        Reference = owner,
        Name = owner[(owner.LastIndexOf('.') + 1)..],
        InferredCdc = cdc,
        Attributes = [Attr(owner, "stVal", fc, bType)]
    };

    private static IedBrowserSchemaIndex Index(params LiveIedDataObjectModel[] objects) =>
        new(new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                LogicalNodes = [new LiveIedLogicalNodeModel { DataObjects = objects }]
            }]
        });

    [Theory]
    [InlineData("0", "Intermediate [00]")]
    [InlineData("1", "Open [01]")]
    [InlineData("2", "Close [10]")]
    [InlineData("3", "Bad state [11]")]
    [InlineData("00", "Intermediate [00]")]
    [InlineData("01", "Open [01]")]
    [InlineData("10", "Close [10]")]
    [InlineData("11", "Bad state [11]")]
    [InlineData("intermediate-state", "Intermediate [00]")]
    [InlineData("off", "Open [01]")]
    [InlineData("on", "Close [10]")]
    [InlineData("bits(00,unused=6)", "Intermediate [00]")]
    [InlineData("bits(40,unused=6)", "Open [01]")]
    [InlineData("bits(80,unused=6)", "Close [10]")]
    [InlineData("bits(c0,unused=6)", "Bad state [11]")]
    public void ExistingIecFormatter_PreservesAllFourDpsCodes(string input, string expected)
    {
        Assert.Equal(expected, Iec61850ValueFormatter.FormatReportProcessValue(
            input, "Dbpos", "", "Position", "LD/XCBR1.Pos.stVal"));
    }

    [Theory]
    [InlineData("XCBR1")]
    [InlineData("XSWI2")]
    [InlineData("CSWI")]
    public void StandardSwitchPos_IsTypedWithExactDeclaredStValueEvenIfCdcUnknown(
        string ln)
    {
        var owner = "LD/" + ln + ".Pos";
        var schema = Index(Obj(owner, ""));
        Assert.True(schema.IsDeclaredDoublePointValue(owner, "ST"));
        Assert.True(schema.IsDeclaredDoublePointValue(owner + ".stVal", "ST"));
        Assert.False(schema.IsDeclaredDoublePointValue(owner + ".q", "ST"));
        Assert.False(schema.IsDeclaredDoublePointValue(owner + ".stVal", "MX"));
        Assert.False(schema.IsDeclaredDoublePointValue("LD/" + ln + "2.Pos.stVal", "ST"));
        Assert.Equal("Open [01]", IedBrowserDoublePointPresentation.Format(
            "off", owner + ".stVal", owner, "ST", schema));
        Assert.Equal("Close [10]", IedBrowserDoublePointPresentation.Format(
            "on", owner, owner, "ST", schema));
        Assert.Equal("Intermediate [00]", IedBrowserDoublePointPresentation.Format(
            "00", owner + ".stVal", owner, "ST", schema));
        Assert.Equal("Bad state [11]", IedBrowserDoublePointPresentation.Format(
            "11", owner + ".stVal", owner, "ST", schema));
    }

    [Fact]
    public void ExplicitDpcCdcOnVendorLn_RemainsSupported()
    {
        const string owner = "LD/VENDOR1.SwitchPosition";
        var schema = Index(Obj(owner, "DPC"));
        Assert.Equal("Open [01]", IedBrowserDoublePointPresentation.Format(
            "1", owner + ".stVal", owner, "ST", schema));
    }

    [Fact]
    public void ExactDbposBTypeOnVendorLn_RemainsSupported()
    {
        const string owner = "LD/VENDOR1.SwitchPosition";
        var schema = Index(Obj(owner, "", "ST", "Dbpos"));
        Assert.Equal("Close [10]", IedBrowserDoublePointPresentation.Format(
            "2", owner + ".stVal", owner, "ST", schema));
    }

    [Fact]
    public void DataModelAndDataSet_ViewSameRawLiveValueWithoutMutatingEvidence()
    {
        const string owner = "LD/XCBR1.Pos";
        var schema = Index(Obj(owner, "DPC"));
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(
            association, owner + ".stVal", "ST", "bits(40,unused=6)",
            "Good", "2026-10-16T03:00:00Z", "Report"));
        Assert.True(ledger.TryGet(owner + ".stVal", "ST", out var raw));
        var modelDisplay = IedBrowserDoublePointPresentation.Format(
            raw.Value, owner + ".stVal", owner, "ST", schema);
        var datasetDisplay = IedBrowserDoublePointPresentation.Format(
            raw.Value, owner, owner + ".stVal", "ST", schema);
        Assert.Equal("Open [01]", modelDisplay);
        Assert.Equal(modelDisplay, datasetDisplay);
        Assert.True(ledger.TryGet(owner + ".stVal", "ST", out var unchanged));
        Assert.Equal("bits(40,unused=6)", unchanged.Value);
        Assert.Equal("Good", unchanged.Quality);
        Assert.Equal(association, unchanged.Association);
    }

    [Theory]
    [InlineData("LD/CSWI1.Mod", "on")]
    [InlineData("LD/CSWI1.Beh", "off")]
    [InlineData("LD/XCBR1.Pos", "on")]
    [InlineData("LD/VENDOR1.Pos", "on")]
    public void UntypedNonPositionValuesRemainUnchanged(string owner, string raw)
    {
        // Name Pos alone is NOT enough, unless it belongs to a standard
        // switching LN with a verified ST stVal directory entry.
        var objectModel = owner.EndsWith(".Pos", StringComparison.Ordinal)
            ? Obj(owner, "", "MX")
            : Obj(owner, "ENS");
        var schema = Index(objectModel);
        Assert.Equal(raw, IedBrowserDoublePointPresentation.Format(
            raw, owner + ".stVal", owner, "ST", schema));
    }

    [Fact]
    public void ContradictoryExplicitCdc_NeverOverriddenByStandardLnFallback()
    {
        const string owner = "LD/XCBR1.Pos";
        var schema = Index(Obj(owner, "SPS"));
        Assert.False(schema.IsDeclaredDoublePointValue(owner + ".stVal", "ST"));
        Assert.Equal("off", IedBrowserDoublePointPresentation.Format(
            "off", owner + ".stVal", owner, "ST", schema));
    }

    [Fact]
    public void GenericEnumsMustNotBeMisrepresentedAsBreakerPosition()
    {
        Assert.Equal("1", Iec61850ValueFormatter.Format(1, "Enum", ""));
        Assert.Equal("2", Iec61850ValueFormatter.Format(2, "Enum", ""));
        Assert.Equal("on", IedBrowserDoublePointPresentation.Format(
            "on", "LD/CSWI1.Mod.stVal", "LD/CSWI1.Mod", "ST",
            Index(Obj("LD/CSWI1.Mod", "ENS"))));
    }

    [Theory]
    [InlineData("bits(00,unused=6)", "Intermediate [00]")]
    [InlineData("bits(40,unused=6)", "Open [01]")]
    [InlineData("bits(80,unused=6)", "Close [10]")]
    [InlineData("bits(C0,unused=6)", "Bad state [11]")]
    public void VerifiedDpcSubstitutionUsesTwoBitCodeWithoutMutatingWire(
        string raw, string expected)
    {
        const string owner = "LD/XCBR1.Pos";
        var obj = new LiveIedDataObjectModel
        {
            Name = "Pos", Reference = owner, InferredCdc = "",
            Attributes =
            [
                Attr(owner, "stVal", "ST", "Check"),
                Attr(owner, "subVal", "SV", "Check"),
                Attr(owner, "subQ", "SV", "Check")
            ]
        };
        var schema = Index(obj);
        Assert.True(schema.IsDeclaredDoublePointValue(owner + ".subVal", "SV"));
        var ledger = new IedBrowserLiveValues();
        var assoc = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(assoc, owner + ".subVal", "SV",
            raw, "", "", "MMS Read"));
        Assert.Equal(expected, IedBrowserDoublePointPresentation.Format(
            raw, owner + ".subVal", owner + ".subVal", "SV", schema));
        Assert.True(ledger.TryGet(owner + ".subVal", "SV", out var original));
        Assert.Equal(raw, original.Value);
    }

    [Fact]
    public void VerifiedDpcSubQUses13BitQualityNotDbpos()
    {
        const string owner = "LD/XCBR1.Pos";
        var schema = Index(new LiveIedDataObjectModel
        {
            Name = "Pos", Reference = owner, InferredCdc = "DPC",
            Attributes = [
                Attr(owner, "stVal", "ST", "Check"),
                Attr(owner, "subVal", "SV", "Check"),
                Attr(owner, "subQ", "SV", "Check")
            ]
        });
        Assert.Equal("Good", IedBrowserDoublePointPresentation.FormatSubstitutionQuality(
            "bits(0000, unused=3)", owner + ".subQ", "SV", schema));
        Assert.Equal("Invalid", IedBrowserDoublePointPresentation.FormatSubstitutionQuality(
            "bits(4000, unused=3)", owner + ".subQ", "SV", schema));
        Assert.Equal("bits(00,unused=6)",
            IedBrowserDoublePointPresentation.FormatSubstitutionQuality(
                "bits(00,unused=6)", owner + ".subQ", "SV", schema));
        Assert.Equal("bits(0000,unused=3)",
            IedBrowserDoublePointPresentation.FormatSubstitutionQuality(
                "bits(0000,unused=3)", owner + ".subQ", "ST", schema));
    }

    [Fact]
    public void SubstitutionWithoutExactDpcProofFailsClosed()
    {
        const string owner = "LD/VENDOR1.Pos";
        var unknown = Index(new LiveIedDataObjectModel {
            Name = "Pos", Reference = owner, InferredCdc = "SPS",
            Attributes = [Attr(owner, "stVal", "ST", "Check"),
                          Attr(owner, "subVal", "SV", "Check")]
        });
        Assert.False(unknown.IsDeclaredDoublePointValue(owner + ".subVal", "SV"));
        Assert.Equal("bits(00,unused=6)", IedBrowserDoublePointPresentation.Format(
            "bits(00,unused=6)", owner + ".subVal", owner + ".subVal",
            "SV", unknown));
        Assert.Equal("bits(0000,unused=3)",
            IedBrowserDoublePointPresentation.FormatSubstitutionQuality(
                "bits(0000,unused=3)", owner + ".subQ", "SV", unknown));
    }

    [Fact]
    public void SubQDetailsExposeVerifiedQualityFieldsWithoutTreatingItAsStatus()
    {
        const string owner = "LD/XCBR1.Pos";
        var attrs = new[] {
            Attr(owner, "subVal", "SV", "Check"),
            Attr(owner, "subQ", "SV", "Check"),
            Attr(owner, "subID", "SV", "VisString255")
        };
        var rows = IedBrowserStructuredDetails.AttributeRows(attrs, "Pos");
        var q = Assert.Single(rows, r => r.Name == "subQ [SV]");
        Assert.Equal("Quality", q.EvidenceKind);
        Assert.Equal("SV", q.FunctionalConstraint);
        var fields = Assert.IsAssignableFrom<IReadOnlyList<IedBrowserDetailRow>>(
            q.LoadChildren!());
        Assert.Contains(fields, row => row.Name == "Validity" &&
            row.EvidenceField == "Validity" && row.EvidenceKind == "Quality");
        Assert.Contains(fields, row => row.Name == "Source");
        Assert.Contains(fields, row => row.Name == "Test");
        Assert.Contains(fields, row => row.Name == "OperatorBlocked");
        Assert.Contains(fields, row => row.Name == "Quality Details");
        Assert.DoesNotContain(rows, row => row.Name == "subVal [SV]" &&
            row.EvidenceKind == "Quality");
    }

    private static LiveIedDataObjectModel RawTwoBitStatusModel(
        string owner, string cdc = "", bool hasQ = true,
        string mmsType = "Check", string fc = "ST")
    {
        var leaf = new LiveIedDataAttributeModel
        {
            ObjectReference = owner,
            AttributePath = owner[(owner.LastIndexOf('.') + 1)..] + ".stVal",
            MmsReference = owner[..owner.IndexOf('.')] + "$" + fc + "$" +
                owner[(owner.IndexOf('.') + 1)..].Replace('.', '$') + "$stVal",
            FunctionalConstraint = fc,
            SclBType = "Check",
            MmsType = mmsType,
            TypeSource = "LiveTypeSpecification"
        };
        var quality = new LiveIedDataAttributeModel
        {
            ObjectReference = owner,
            AttributePath = owner[(owner.LastIndexOf('.') + 1)..] + ".q",
            MmsReference = owner[..owner.IndexOf('.')] + "$ST$" +
                owner[(owner.IndexOf('.') + 1)..].Replace('.', '$') + "$q",
            FunctionalConstraint = "ST",
            SclBType = "Check",
            MmsType = "Check",
            TypeSource = "LiveTypeSpecification"
        };
        return new LiveIedDataObjectModel
        {
            Reference = owner,
            Name = owner[(owner.LastIndexOf('.') + 1)..],
            InferredCdc = cdc,
            Attributes = hasQ ? [leaf, quality] : [leaf]
        };
    }

    [Theory]
    [InlineData("bits(00, unused=6)", "Intermediate [00]")]
    [InlineData("bits(40, unused=6)", "Open [01]")]
    [InlineData("bits(80, unused=6)", "Close [10]")]
    [InlineData("bits(C0, unused=6)", "Bad state [11]")]
    public void UnknownCdcWithExactTwoBitMmsStValAndQ_PresentsIECStatusOnAnyIED(
        string raw, string expected)
    {
        foreach (var owner in new[] {
            "ZIVAVRLD1/ATCC1.ParOp",
            "SIEBCUCTRL/XCBR1.ParOp",
            "OTHERLD1/VENDOR1.CustomStatus" })
        {
            var schema = Index(RawTwoBitStatusModel(owner));
            Assert.True(schema.IsTwoBitStatusCandidate(owner + ".stVal", "ST"));
            Assert.Equal(expected, IedBrowserDoublePointPresentation.Format(
                raw, owner + ".stVal", owner, "ST", schema));
            var ledger = new IedBrowserLiveValues();
            var association = ledger.BeginAssociation();
            Assert.True(ledger.PublishForAssociation(
                association, owner + ".stVal", "ST",
                raw, "", "", "MMS Read"));
            Assert.True(ledger.TryGet(owner + ".stVal", "ST", out var evidence));
            Assert.Equal(raw, evidence.Value);
        }
    }

    [Theory]
    [InlineData("bits(00,unused=3)")]
    [InlineData("bits(0000,unused=3)")]
    [InlineData("bits(00,unused=5)")]
    [InlineData("bits(01,unused=6)")]
    [InlineData("bits(41,unused=6)")]
    [InlineData("prefix bits(00,unused=6)")]
    [InlineData("bits(00,unused=6) suffix")]
    [InlineData("2")]
    [InlineData("False")]
    [InlineData("open")]
    public void UnknownCdcStatusRejectsWrongWireWidthPaddingOrUnverifiedText(
        string raw)
    {
        var owner = "LD/VENDOR1.ParOp";
        var schema = Index(RawTwoBitStatusModel(owner));
        Assert.Equal(raw, IedBrowserDoublePointPresentation.Format(
            raw, owner + ".stVal", owner, "ST", schema));
    }

    [Theory]
    [InlineData("SPS", "ST", "Check", true)]
    [InlineData("ENS", "ST", "Check", true)]
    [InlineData("Boolean", "ST", "Check", true)]
    [InlineData("", "MX", "Check", true)]
    [InlineData("", "ST", "Enum", true)]
    [InlineData("", "ST", "Check", false)]
    public void UnknownCdcProofNeverOverridesContradictoryTypeOrMissingQuality(
        string cdc, string fc, string mmsType, bool hasQuality)
    {
        var owner = "LD/VENDOR1.ParOp";
        var schema = Index(RawTwoBitStatusModel(
            owner, cdc, hasQuality, mmsType, fc));
        Assert.False(schema.IsTwoBitStatusCandidate(owner + ".stVal", fc));
        Assert.Equal("bits(40,unused=6)", IedBrowserDoublePointPresentation.Format(
            "bits(40,unused=6)", owner + ".stVal", owner,
            fc, schema));
    }

    [Fact]
    public void RawTwoBitStatusNeverTurnsThirteenBitQualityOrVendorDcIntoDbpos()
    {
        var owner = "LD/ATCC1.ParOp";
        var schema = Index(RawTwoBitStatusModel(owner));
        Assert.False(schema.IsTwoBitStatusCandidate(owner + ".q", "ST"));
        Assert.False(schema.IsTwoBitStatusCandidate(owner + ".stVal", "SV"));
        Assert.False(schema.IsTwoBitStatusCandidate("LD/ATCC1.ParOpX.stVal", "ST"));
        Assert.Equal("bits(0000,unused=3)", IedBrowserDoublePointPresentation.Format(
            "bits(0000,unused=3)", owner + ".q", owner + ".q", "ST", schema));
        Assert.Equal("bits(40,unused=6)", IedBrowserDoublePointPresentation.Format(
            "bits(40,unused=6)", owner + ".stVal", owner, "CF", schema));
    }

    [Fact]
    public void TwoBitWireDecoderRejectsMalformedPaddingAndNonBitString()
    {
        foreach (var wrong in new[] {
            "bits(01,unused=6)", "bits(41,unused=6)", "bits(80,unused=0)",
            "bits(80,unused=7)", "bits(0000,unused=3)", "false", "1",
            "bits(40,unused=6) trash" })
            Assert.False(Iec61850ValueFormatter.TryNormalizeTwoBitWireStatus(
                wrong, out _), wrong);
        Assert.False(Iec61850ValueFormatter.TryNormalizeTwoBitWireStatus(
            new string(' ', 100000) + "bits(80, unused=6)", out _));
        Assert.True(Iec61850ValueFormatter.TryNormalizeTwoBitWireStatus(
            "bits(80, unused=6)", out var code));
        Assert.Equal(2, code);
    }

    [Theory]
    [InlineData("DPC")]
    [InlineData("DPS")]
    public void ExplicitIecDoublePointCdcUsesSameGlobalStatusVocabulary(
        string cdc)
    {
        var owner = "LD/VENDOR1.Status";
        var schema = Index(RawTwoBitStatusModel(owner, cdc, hasQ: false));
        Assert.True(schema.IsDeclaredDoublePointValue(owner + ".stVal", "ST"));
        Assert.Equal("Intermediate [00]", IedBrowserDoublePointPresentation.Format(
            "bits(00, unused=6)", owner + ".stVal", owner, "ST", schema));
        Assert.Equal("Open [01]", Iec61850ValueFormatter.FormatReportProcessValue(
            "bits(40, unused=6)", cdc, "", "Status", owner + ".stVal"));
        Assert.Equal("Close [10]", Iec61850ValueFormatter.FormatReportProcessValue(
            "bits(80, unused=6)", cdc, "", "Status", owner + ".stVal"));
        Assert.Equal("Bad state [11]", IedBrowserDoublePointPresentation.Format(
            "bits(C0, unused=6)", owner + ".stVal", owner, "ST", schema));
    }
}
