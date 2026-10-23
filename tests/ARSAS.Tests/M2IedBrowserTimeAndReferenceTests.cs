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
    public void TypedReadProvidesMissingReportQualityDetails_WithoutReplacingProcessAuthority()
    {
        var ledger = new IedBrowserLiveValues();
        var session = ledger.BeginAssociation();
        var qualityReference = "LD/GGIO6.CBPond.q";
        var timeReference = "LD/GGIO6.CBPond.t";
        Assert.True(ledger.PublishForAssociation(session, qualityReference,
            "ST", "good", "", "", "Report quality"));
        Assert.True(ledger.PublishForAssociation(session, timeReference,
            "ST", "2026-10-14 10:00:00", "", "", "Report timestamp"));
        Assert.True(ledger.PublishForAssociation(session, "LD/GGIO6.CBPond.stVal",
            "ST", "False", "", "", "Report"));

        var qWire = Assert.Single(ArIED61850Tester.Services.IedBrowserReportWireEvidence.Extract(
            new MmsReportFrame
            {
                Values =
                [
                    new MmsReportValue
                    {
                        Member = new MmsDataSetDirectoryMember
                        {
                            UserReference = qualityReference,
                            FunctionalConstraint = "ST"
                        },
                        Value = MmsDataValue.BitString(3, [0, 0])
                    }
                ]
            })).QualityBits!;
        var tWire = WireTime(0x25);
        Assert.True(ledger.PublishForAssociation(session, qualityReference,
            "ST", qWire.Summary, "", "", "MMS Read", qualityEvidence: qWire));
        Assert.True(ledger.PublishForAssociation(session, timeReference,
            "ST", tWire.EngineeringUtc, "", "", "MMS Read", utcTimeEvidence: tWire));

        // A normal row remains Report-owned during monitoring.
        Assert.True(ledger.TryGet(qualityReference, "ST", out var qParent));
        Assert.Equal("Report quality", qParent.Source);
        Assert.Null(qParent.QualityBits);
        Assert.True(ledger.TryGet(qualityReference, "ST", out var qChild,
            requiredEvidence: "Quality"));
        Assert.Equal("MMS Read", qChild.Source);
        Assert.Equal("false", qChild.QualityBits?.FormatField("Overflow"));
        Assert.True(ledger.TryGet(timeReference, "ST", out var tChild,
            requiredEvidence: "Timestamp"));
        Assert.Equal("MMS Read", tChild.Source);
        Assert.Equal("true", IedBrowserUtcTimeQualityPresentation.FormatField(
            tChild.TimeBits, "ClockNotSynchronized"));
        Assert.True(ledger.TryGet("LD/GGIO6.CBPond.stVal", "ST",
            out var process));
        Assert.Equal("Report", process.Source);

        // A newer summary-only Report makes the older explicit Read stale;
        // its individual flags must no longer be represented as current.
        Assert.True(ledger.PublishForAssociation(session, qualityReference,
            "ST", "good", "", "", "Report quality"));
        Assert.True(ledger.TryGet(qualityReference, "ST", out var newerQuality,
            requiredEvidence: "Quality"));
        Assert.Equal("Report quality", newerQuality.Source);
        Assert.Null(newerQuality.QualityBits);
        Assert.True(ledger.PublishForAssociation(session, timeReference,
            "ST", "2026-10-14 10:01:00", "", "", "Report timestamp"));
        Assert.True(ledger.TryGet(timeReference, "ST", out var newerTime,
            requiredEvidence: "Timestamp"));
        Assert.Equal("Report timestamp", newerTime.Source);
        Assert.Null(newerTime.TimeBits);

        // The association fence still prevents old typed data from being shown.
        ledger.BeginAssociation();
        Assert.False(ledger.TryGet(qualityReference, "ST", out _,
            requiredEvidence: "Quality"));
        Assert.False(ledger.TryGet(timeReference, "ST", out _,
            requiredEvidence: "Timestamp"));
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
        var device = new Iec61850MonitorDevice { IsConnected = true };
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
        var association = device.LiveValues.Association;
        var first = new IedBrowserReadResult(
            new IedBrowserReadTarget("LD/CSWI1$st$Pos$stVal", "st", "BOOLEAN"),
            "true", "", "", "");
        Assert.True(device.LiveValues.PublishForAssociation(association,
            first.Target.Reference, first.Target.FunctionalConstraint,
            first.Value, first.Quality, first.DeviceTimestamp, "MMS Read"));
        Assert.Equal(1, index.Apply(first, device.LiveValues, association));
        Assert.Equal("true", upper.Value);
        Assert.Equal("before-lower", lower.Value);
        var second = new IedBrowserReadResult(
            new IedBrowserReadTarget("LD/CSWI1.pos.stVal", "ST", "BOOLEAN"),
            "false", "", "", "");
        Assert.True(device.LiveValues.PublishForAssociation(association,
            second.Target.Reference, second.Target.FunctionalConstraint,
            second.Value, second.Quality, second.DeviceTimestamp, "MMS Read"));
        Assert.Equal(1, index.Apply(second, device.LiveValues, association));
        Assert.Equal("true", upper.Value);
        Assert.Equal("false", lower.Value);
    }

    [Fact]
    public void Browser_DataDetail_LabelsOnlyObservedReportOrReadValues()
    {
        var xaml = File.ReadAllText(Find("MainWindow.xaml"));
        var code = File.ReadAllText(Find("MainWindow.IedBrowser.cs"));
        Assert.Contains("Text=\"{Binding ObservationSource}\"", xaml, StringComparison.Ordinal);
        // Screenshot P0: do not auto-open giant provenance hover strips
        // across the active Browser detail grid. Evidence remains in
        // ObservationTooltip on the row for explicit future inspection.
        Assert.DoesNotContain("ToolTip=\"{Binding ObservationTooltip}\"", xaml, StringComparison.Ordinal);
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
