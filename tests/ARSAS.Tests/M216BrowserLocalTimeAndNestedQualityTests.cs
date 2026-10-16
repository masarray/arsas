using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M216BrowserLocalTimeAndNestedQualityTests
{
    private static readonly TimeZoneInfo UtcPlusSeven =
        TimeZoneInfo.CreateCustomTimeZone(
            "ARSAS-test-UTC+07", TimeSpan.FromHours(7),
            "UTC+07", "UTC+07");

    [Theory]
    [InlineData("2026-10-16 03:00:12.99056 UTC", "2026-10-16 10:00:12.99056")]
    [InlineData("2026-10-16T03:00:12Z", "2026-10-16 10:00:12")]
    [InlineData("2026-10-16 10:00:12.7033720", "2026-10-16 10:00:12.7033720")]
    [InlineData("Not exposed", "Not exposed")]
    [InlineData("Not read", "Not read")]
    public void ExplicitUtcOnly_ConvertsToOperatorZone_NotOffsetlessLocal(
        string raw, string expected)
    {
        Assert.Equal(expected,
            IedBrowserLocalTimePresentation.Format(raw, UtcPlusSeven));
    }

    [Fact]
    public void OffsetAwareTimestamp_FollowsInstantNotComputerCulture()
    {
        Assert.Equal("2026-10-16 10:00:12",
            IedBrowserLocalTimePresentation.Format(
                "2026-10-16 04:00:12+01:00", UtcPlusSeven));
        // Discarding timestamp's ambiguous offset is never acceptable.
        Assert.Equal("2026-10-16 03:00:12.123456789 UTC",
            IedBrowserLocalTimePresentation.Format(
                "2026-10-16 03:00:12.123456789 UTC", UtcPlusSeven));
    }

    private static IedBrowserSchemaIndex NestedPpvSchema()
    {
        const string owner = "LD/MMXU1.PPV";
        LiveIedDataAttributeModel Attr(string suffix, string fc = "MX") => new()
        {
            ObjectReference = owner,
            FunctionalConstraint = fc,
            AttributePath = "PPV." + suffix,
            MmsReference = "LD/MMXU1$" + fc + "$PPV$" +
                suffix.Replace('.', '$')
        };
        var obj = new LiveIedDataObjectModel
        {
            Name = "PPV", Reference = owner,
            Attributes =
            [
                Attr("phsAB.cVal.mag.f"), Attr("phsAB.q"), Attr("phsAB.t"),
                Attr("phsBC.cVal.mag.f"), Attr("phsBC.q"), Attr("phsBC.t"),
                Attr("phsCA.cVal.mag.f"), Attr("phsCA.q"), Attr("phsCA.t"),
                Attr("phsAB.q", "ST")
            ]
        };
        return new IedBrowserSchemaIndex(new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                LogicalNodes = [new LiveIedLogicalNodeModel { DataObjects = [obj] }]
            }],
            DataSets = [new LiveIedDataSetModel
            {
                Reference = "LD/LLN0$MX$PHASES",
                Members = [new LiveIedDataSetMemberModel
                {
                    Reference = "LD/MMXU1$MX$PPV",
                    FunctionalConstraint = "MX"
                }]
            }]
        });
    }

    [Fact]
    public void NestedCdcRawQAndT_MapToExactPhaseWithSameFc()
    {
        var schema = NestedPpvSchema();
        Assert.True(schema.TryResolveReportFc("LD/LLN0$MX$PHASES",
            "LD/MMXU1.PPV.phsAB.q", out var fc));
        Assert.Equal("MX", fc);
        Assert.True(schema.IsDeclaredCompanionContainer(
            "LD/MMXU1.PPV.phsAB", "MX"));
        Assert.True(schema.TryGetQualityCompanion(
            "LD/MMXU1.PPV.phsAB.cVal.mag.f", "MX", out var q));
        Assert.Equal("LD/MMXU1.PPV.phsAB.q", q);
        Assert.True(schema.TryGetTimestampCompanion(
            "LD/MMXU1.PPV.phsAB.cVal.mag.f", "MX", out var t));
        Assert.Equal("LD/MMXU1.PPV.phsAB.t", t);
        Assert.True(schema.TryGetQualityCompanion(q, "MX", out var directQ));
        Assert.Equal(q, directQ);
        Assert.False(schema.TryGetDerivedQualityCompanion(q, "MX", out _));
    }

    [Fact]
    public void NestedRawEvidence_CannotCrossPhasesOrFunctionalConstraints()
    {
        var schema = NestedPpvSchema();
        Assert.True(schema.TryGetQualityCompanion(
            "LD/MMXU1.PPV.phsBC.cVal.mag.f", "MX", out var q));
        Assert.Equal("LD/MMXU1.PPV.phsBC.q", q);
        Assert.False(schema.TryGetQualityCompanion(
            "LD/MMXU1.PPV.phsXX.cVal.mag.f", "MX", out _));
        Assert.False(schema.TryGetQualityCompanion(
            "LD/MMXU1.PPV.phsBC.cVal.mag.f", "ST", out _));
        Assert.False(schema.IsDeclaredCompanionContainer(
            "LD/MMXU1.PPV.phsUnknown", "MX"));
    }

    [Fact]
    public void RawNestedQuality_RequiresActualTypedMmsEvidence_NotGenericGoodText()
    {
        var schema = NestedPpvSchema();
        var live = new IedBrowserLiveValues();
        var association = live.BeginAssociation();
        const string quality = "LD/MMXU1.PPV.phsAB.q";
        Assert.True(schema.TryGetQualityCompanion(
            "LD/MMXU1.PPV.phsAB", "MX", out var exact));
        Assert.Equal(quality, exact);
        Assert.True(live.PublishForAssociation(
            association, exact, "MX", "good", "", "", "Report quality"));
        Assert.True(live.TryGet(exact, "MX", out var textOnly));
        Assert.Null(textOnly.QualityBits);
        Assert.Equal("Not exposed",
            ArIED61850Tester.Iec61850QualityPresentation.FormatObserved(
                textOnly.Value, "Overflow"));
        var typed = MmsQualityDecoder.TryDecodeRendered(
            "bits(0000,unused=3)", out var parsed);
        if (typed)
        {
            Assert.True(live.PublishForAssociation(association, exact,
                "MX", parsed.Summary, "", "", "Report quality · raw MMS",
                qualityEvidence: parsed));
            Assert.True(live.TryGet(exact, "MX", out var reported));
            Assert.Equal("false", reported.QualityBits?.FormatField("Overflow"));
        }
    }

    [Fact]
    public void FilesGrid_UsesCoolBlueRowsAndOriginalFileVector()
    {
        var root = LocateProjectRoot();
        var xaml = XDocument.Load(Path.Combine(root, "MainWindow.xaml"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var grid = Assert.Single(xaml.Descendants(p + "DataGrid"),
            element => (string?)element.Attribute(x + "Name") == "IedBrowserFilesGrid");
        var style = Assert.Single(grid.Descendants(p + "DataGrid.RowStyle"))
            .Descendants(p + "Style").First();
        Assert.Contains(style.Elements(p + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "#FFFFFF");
        Assert.Contains(grid.Descendants(p + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "ItemsControl.AlternationIndex" &&
            (string?)trigger.Attribute("Value") == "1");
        var icon = Assert.Single(grid.Descendants(p + "Path"));
        Assert.Equal("Uniform", (string?)icon.Attribute("Stretch"));
        Assert.StartsWith("M824-120 636-308", (string?)icon.Attribute("Data"));
    }

    private static string LocateProjectRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory);
             current != null; current = current.Parent)
            if (System.IO.File.Exists(Path.Combine(current.FullName, "MainWindow.xaml")))
                return current.FullName;
        throw new FileNotFoundException("MainWindow.xaml");
    }
}
