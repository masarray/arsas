using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214SgLastChangedWireEvidenceTests
{
    private const string Reference = "LD/LLN0.SGCB.LActTm";

    [Fact]
    public void ZeroWireUtcTime_IsUnsetGroupChange_NotA1970EngineeringEvent()
    {
        var time = Iec61850UtcTimeEvidence.Decode(MmsDataValue.UtcTime(
            Iec61850UtcTime.FromBytes(new byte[8] { 0, 0, 0, 0, 0, 0, 0, 0x3F })));
        Assert.True(time.HasWireProvenance);
        Assert.Equal(0u, time.SecondsSinceEpoch);
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association, Reference, "SP",
            time.EngineeringUtc, "", "", "MMS Read", utcTimeEvidence: time));
        Assert.True(ledger.TryGet(Reference, "SP", out var observed));
        Assert.NotNull(observed.TimeBits);
        Assert.Equal("Not initialized", IedBrowserControlStateEvidence.FormatObservedField(
            "SGCB", "LActTm", observed));
        Assert.Equal(time.RawHex, observed.TimeBits.RawHex);
    }

    [Fact]
    public void RealTimestamp_AndUnverifiedText_AreNeverRewrittenAsUnset()
    {
        var nonzero = Iec61850UtcTimeEvidence.Decode(MmsDataValue.UtcTime(
            Iec61850UtcTime.FromBytes(new byte[8] { 0x65, 0x10, 0x00, 0x00, 0, 0, 0, 0x3F })));
        var value = new IedBrowserObservedValue(
            nonzero.EngineeringUtc, "", "", "MMS Read", DateTime.UtcNow,
            TimeBits: nonzero);
        Assert.Equal(nonzero.EngineeringUtc,
            IedBrowserControlStateEvidence.FormatObservedField("SGCB", "LActTm", value));
        var legacy = value with { Value = "1970-01-01 00:00:00 UTC", TimeBits = null };
        Assert.Equal(legacy.Value,
            IedBrowserControlStateEvidence.FormatObservedField("SGCB", "LActTm", legacy));
        Assert.Equal(legacy.Value,
            IedBrowserControlStateEvidence.FormatObservedField("GoCB", "LActTm", legacy));
    }
}
