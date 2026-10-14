using AR.Iec61850.Asn1;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// TimeQuality is evidence from the pinned ARIEC engine's original 8 MMS wire
/// bytes, not a newly interpreted ISO string. Reference/FC identity is exact.
/// </summary>
public sealed class M2IedBrowserTimeAndReferenceTests
{
    private static Iec61850UtcTimeEvidence WireTime(byte quality = 0xE5)
    {
        byte[] raw = [0, 0, 0, 1, 0, 0, 0, quality];
        var tag = BerWriter.EncodeIdentifier(BerClass.ContextSpecific, false, 17);
        var encoded = BerWriter.EncodeTlv(tag, raw);
        var decoded = Assert.Single(MmsDataCodec.DecodeAllData(encoded));
        return Iec61850UtcTimeEvidence.Decode(decoded);
    }

    [Fact]
    public void UtcTime_OriginalEightByteEngineEvidence_ReportsExactClockBitsAndPrecision()
    {
        var observed = WireTime();
        Assert.True(observed.IsDecoded);
        Assert.True(observed.HasWireProvenance);
        Assert.Equal("00000001000000E5", observed.RawHex);
        Assert.True(observed.LeapSecondsKnown);
        Assert.True(observed.ClockFailure);
        Assert.True(observed.ClockNotSynchronized);
        Assert.Equal(5, observed.AccuracyCode);
        Assert.Equal("2^-5 s", observed.TimeAccuracy);
        Assert.Equal("true", IedBrowserUtcTimeQualityPresentation.FormatField(observed, "ClockFailure"));
        Assert.Equal("2^-5 s", IedBrowserUtcTimeQualityPresentation.FormatField(observed, "TimeAccuracy"));
        Assert.Equal("Not exposed", IedBrowserUtcTimeQualityPresentation.FormatField(observed, "InvalidFlag"));
    }

    [Fact]
    public void UtcTime_UnknownAccuracyAndSyntheticData_NeverFakesOriginalWire()
    {
        var unknown = WireTime(31);
        Assert.Equal("unspecified",
            IedBrowserUtcTimeQualityPresentation.FormatField(unknown, "TimeAccuracy"));

        byte[] raw = [0, 0, 0, 1, 0, 0, 0, 0xE5];
        var synthetic = Iec61850UtcTimeEvidence.Decode(
            MmsDataValue.UtcTime(Iec61850UtcTime.FromBytes(raw)));
        Assert.True(synthetic.IsDecoded);
        Assert.False(synthetic.HasWireProvenance);
        Assert.Equal("Not exposed",
            IedBrowserUtcTimeQualityPresentation.FormatField(synthetic, "ClockFailure"));
        Assert.Throws<ArgumentException>(() => Iec61850UtcTime.FromBytes(raw[..7]));
    }

    [Fact]
    public void LiveRegistry_NeverInfersClockFlagsFromRenderedTextOrDeviceTimestamp()
    {
        var live = new IedBrowserLiveValues();
        var first = live.BeginAssociation();
        var fakeRenderer = "1970-01-01 00:00:01.000 UTC (accuracy=5, clockFailure)";
        Assert.True(live.PublishForAssociation(first,
            "LD/CSWI1$ST$Pos$t", "ST", fakeRenderer, "", "", "MMS Read"));
        Assert.True(live.TryGet("LD/CSWI1.Pos.t", "ST", out var unverified));
        Assert.Null(unverified.TimeBits);

        var verified = WireTime(0x25);
        Assert.True(live.PublishForAssociation(first,
            "LD/CSWI1$ST$Pos$t", "ST", fakeRenderer, "", "", "MMS Read", verified));
        Assert.True(live.TryGet("LD/CSWI1.Pos.t", "ST", out var time));
        Assert.True(time.TimeBits?.HasWireProvenance);
        Assert.Equal("true",
            IedBrowserUtcTimeQualityPresentation.FormatField(time.TimeBits,
                "ClockNotSynchronized"));

        Assert.True(live.PublishForAssociation(first,
            "LD/CSWI1.Pos.stVal", "ST", "on", "Good", fakeRenderer, "Report", verified));
        Assert.True(live.TryGet("LD/CSWI1.Pos.stVal", "ST", out var process));
        Assert.Null(process.TimeBits);
        Assert.True(live.PublishForAssociation(first,
            "LD/CSWI1.Pos.TIME", "ST", fakeRenderer, "", "", "MMS Read", verified));
        Assert.True(live.TryGet("LD/CSWI1.Pos.TIME", "ST", out var sibling));
        Assert.Null(sibling.TimeBits);

        var next = live.BeginAssociation();
        Assert.NotEqual(first, next);
        Assert.False(live.PublishForAssociation(first,
            "LD/CSWI1.Pos.t", "ST", fakeRenderer, "", "", "MMS Read", verified));
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

    [Fact]
    public void Browser_DataDetail_LabelsOnlyObservedReportOrReadValues()
    {
        var xaml = File.ReadAllText(Find("MainWindow.xaml"));
        var code = File.ReadAllText(Find("MainWindow.IedBrowser.cs"));
        Assert.Contains("Text=\"{Binding ObservationSource}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding ObservationTooltip}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("row.ObservationSource = \"\";", code, StringComparison.Ordinal);
        Assert.Contains("row.ObservationSource = live.IsStale", code, StringComparison.Ordinal);
        Assert.Contains("IedBrowserUtcTimeQualityPresentation.FormatField(", code,
            StringComparison.Ordinal);
    }

    private static string Find(string name)
    {
        for (DirectoryInfo? parent = new(AppContext.BaseDirectory); parent != null;
             parent = parent.Parent)
        {
            var candidate = Path.Combine(parent.FullName, name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(name);
    }
}
