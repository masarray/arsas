using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Protocol.Mms;

namespace ARSAS.Tests;

/// <summary>
/// M2.6A: evidence must be raw/canonical and identity-exact, never inferred
/// from a generic timestamp or an IEC name prefix or casing collision.
/// </summary>
public sealed class M2IedBrowserTimeAndReferenceTests
{
    [Fact]
    public void UtcTime_ExactEightBytePayload_ProducesRealTimeQuality()
    {
        byte[] payload = [0, 0, 0, 1, 0, 0, 0, 0xE5];
        Assert.True(MmsUtcTimeDecoder.TryDecode(payload, out var flags));
        Assert.True(flags.LeapSecondsKnown);
        Assert.True(flags.ClockFailure);
        Assert.True(flags.ClockNotSynchronized);
        Assert.Equal("5", flags.FormatField("TimeAccuracy"));
        Assert.Equal("true", flags.FormatField("ClockFailure"));
        var rendered = MmsUtcTimeDecoder.Decode(payload);
        Assert.True(MmsUtcTimeDecoder.TryDecodeRendered(rendered, out var roundTrip));
        Assert.Equal(flags, roundTrip);
        Assert.False(MmsUtcTimeDecoder.TryDecode(payload[..7], out _));
        Assert.False(MmsUtcTimeDecoder.TryDecode(new byte[9], out _));
    }

    [Theory]
    [InlineData("2026-10-14T10:20:30Z")]
    [InlineData("2026-10-14 10:20:30.000 UTC")]
    [InlineData("2026-10-14 10:20:30.000 UTC (accuracy=32)")]
    [InlineData("2026-10-14 10:20:30.000 UTC (accuracy=05)")]
    [InlineData("2026-10-14 10:20:30.000 UTC (accuracy=5, inventedFlag)")]
    [InlineData("2026-10-14 10:20:30.000 UTC (accuracy=5, clockFailure, clockFailure)")]
    [InlineData("2026-10-14 10:20:30.000 UTC (accuracy=5, notSynchronized, clockFailure)")]
    public void UtcTime_RejectsAnyNonCanonicalOrInventedTimeQuality(string value)
        => Assert.False(MmsUtcTimeDecoder.TryDecodeRendered(value, out _));

    [Fact]
    public void UtcTime_AccuracyCode31IsUnknown_NotAnInventedPrecision()
    {
        var payload = new byte[] { 0, 0, 0, 1, 0, 0, 0, 31 };
        Assert.True(MmsUtcTimeDecoder.TryDecodeRendered(MmsUtcTimeDecoder.Decode(payload),
            out var observed));
        Assert.Equal("Unknown (31)", observed.FormatField("TimeAccuracy"));
        Assert.Equal("false", observed.FormatField("ClockFailure"));
    }

    [Fact]
    public void LiveRegistry_TypedTimeOnlyForObservedExactT_AndAssociationFence()
    {
        var live = new IedBrowserLiveValues();
        var first = live.BeginAssociation();
        var raw = new byte[] { 0, 0, 0, 1, 0, 0, 0, 0x25 };
        var canonical = MmsUtcTimeDecoder.Decode(raw);
        Assert.True(live.PublishForAssociation(first,
            "LD/CSWI1$ST$Pos$t", "ST", canonical, "", "", "MMS Read"));
        Assert.True(live.TryGet("LD/CSWI1.Pos.t", "ST", out var value));
        Assert.Equal("true", value.TimeBits?.FormatField("ClockNotSynchronized"));
        Assert.Equal("5", value.TimeBits?.FormatField("TimeAccuracy"));
        Assert.Null(value.QualityBits);

        // A process value's associated device timestamp is NOT a received
        // .t field, even if a renderer-looking string is attached to it.
        Assert.True(live.PublishForAssociation(first,
            "LD/CSWI1.Pos.stVal", "ST", "on", "Good", canonical, "Report"));
        Assert.True(live.TryGet("LD/CSWI1.Pos.stVal", "ST", out var process));
        Assert.Null(process.TimeBits);
        Assert.True(live.PublishForAssociation(first,
            "LD/CSWI1.Pos.TIME", "ST", canonical, "", "", "MMS Read"));
        Assert.True(live.TryGet("LD/CSWI1.Pos.TIME", "ST", out var falseSibling));
        Assert.Null(falseSibling.TimeBits);

        var next = live.BeginAssociation();
        Assert.NotEqual(first, next);
        Assert.False(live.PublishForAssociation(first,
            "LD/CSWI1.Pos.t", "ST", canonical, "", "", "MMS Read"));
        Assert.False(live.TryGet("LD/CSWI1.Pos.t", "ST", out _));
    }

    [Fact]
    public void DataSetMemberExpansion_IsolatesExactLeaf_FcAndMmsCanonicalization()
    {
        static LiveIedDataAttributeModel Attr(string path, string fc = "ST") => new()
        {
            ObjectReference = "LD/CSWI1.Pos", AttributePath = "Pos." + path,
            FunctionalConstraint = fc,
            MmsReference = "LD/CSWI1$" + fc + "$Pos$" + path.Replace('.', '$')
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Name = "Pos", Reference = "LD/CSWI1.Pos",
                                    Attributes = [Attr("stVal"), Attr("stValue"),
                                        Attr("stValChild"), Attr("stVal", "MX"),
                                        Attr("stVal.sub")]
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        var member = new LiveIedDataSetMemberModel
        {
            Reference = "LD/CSWI1$ST$Pos$stVal", FunctionalConstraint = "ST"
        };
        var rows = IedBrowserStructuredDetails.DataSetMemberChildren(member, model, 1);
        var root = Assert.Single(rows);
        Assert.StartsWith("stVal [ST]", root.Name, StringComparison.Ordinal);
        Assert.DoesNotContain(rows, x => x.Name.Contains("stValue", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, x => x.Name.Contains("stValChild", StringComparison.Ordinal));
        var projection = new IedBrowserDetailProjection();
        projection.Reset(rows);
        projection.Toggle(root);
        Assert.Contains(projection.Rows, x => x.Name == "sub [ST]");
        Assert.DoesNotContain(projection.Rows, x => x.Name == "stValue [ST]");
    }

    [Fact]
    public void ExplicitReadBinding_CaseSensitiveIecIdentity_AndNormalizedFc()
    {
        var device = new Iec61850MonitorDevice();
        var upper = new Iec61850MonitorPoint
        {
            IecReference = "LD/CSWI1.Pos.stVal", FunctionalConstraint = "ST", Value = "before-upper"
        };
        var lower = new Iec61850MonitorPoint
        {
            IecReference = "LD/CSWI1.pos.stVal", FunctionalConstraint = "ST", Value = "before-lower"
        };
        device.Points.Add(upper);
        device.Points.Add(lower);
        var index = new IedBrowserReadBindingIndex(device);
        index.Apply(new IedBrowserReadResult(
            new IedBrowserReadTarget("LD/CSWI1$st$Pos$stVal", "st", "BOOLEAN"),
            "true", "", "", ""));
        Assert.Equal("true", upper.Value);
        Assert.Equal("before-lower", lower.Value);
        index.Apply(new IedBrowserReadResult(
            new IedBrowserReadTarget("LD/CSWI1.pos.stVal", "ST", "BOOLEAN"),
            "false", "", "", ""));
        Assert.Equal("true", upper.Value);
        Assert.Equal("false", lower.Value);
    }
}
