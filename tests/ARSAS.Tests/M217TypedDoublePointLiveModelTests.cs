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
    public void GenericEnumsMustNotBeMisrepresentedAsBreakerPosition()
    {
        Assert.Equal("1", Iec61850ValueFormatter.Format(1, "Enum", ""));
        Assert.Equal("2", Iec61850ValueFormatter.Format(2, "Enum", ""));
        Assert.Equal("on", IedBrowserDoublePointPresentation.Format(
            "on", "LD/CSWI1.Mod.stVal", "LD/CSWI1.Mod", "ST",
            Index(Obj("LD/CSWI1.Mod", "ENS"))));
    }
}
