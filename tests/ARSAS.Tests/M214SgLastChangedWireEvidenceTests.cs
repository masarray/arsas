using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214SgLastChangedWireEvidenceTests
{
    private const string Reference = "LD/LLN0.SGCB.LActTm";

    [Fact]
    public void ZeroWireUtcTime_IsUnsetGroupChange_NotA1970EngineeringEvent()
    {
        // Typed fixture represents a validated raw eight-byte LActTm from
        // the pinned engine's decoder; no formatted-date guessing.
        var time = new Iec61850UtcTimeEvidence
        {
            IsDecoded = true, HasWireProvenance = true,
            RawHex = "000000000000003F",
            SecondsSinceEpoch = 0, FractionOfSecond24 = 0,
            Quality = 0x3F, EngineeringUtc = "1970-01-01 00:00:00 UTC"
        };
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
        var nonzero = new Iec61850UtcTimeEvidence
        {
            IsDecoded = true, HasWireProvenance = true,
            RawHex = "651000000000003F", SecondsSinceEpoch = 0x65100000,
            FractionOfSecond24 = 0, Quality = 0x3F,
            EngineeringUtc = "2023-09-24 00:00:00 UTC"
        };
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
