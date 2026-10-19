using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// SCL-neutral MMS data: a wire-proven zero-length OctetString must not
/// become READ_FAILED merely because its rendered text is empty.
/// </summary>
public sealed class P0DWireEmptyOctetReadTests
{
    [Fact]
    public void ZeroLengthOctetString_IsAnObservedValue_OnlyWithExactWireKind()
    {
        var wire = MmsDataCodec.DecodeAllData(Convert.FromHexString("8900"));
        var empty = Assert.Single(wire);
        Assert.Equal(MmsDataKind.OctetString, empty.Kind);
        Assert.Empty(empty.RawValue);

        Assert.True(IedBrowserReadValuePolicy.TryFormatVerifiedEmptyOctet(
            empty, "Octet64", out var display));
        Assert.Equal("Empty OCTET STRING (0 bytes)", display);
        Assert.True(IedBrowserReadValuePolicy.TryAcceptDisplay(
            display, out var accepted, out _));

        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association,
            "TESTLD/LLN0.Mod.origin.orIdent", "ST", accepted, "", "", "MMS Read"));
        Assert.True(ledger.TryGet("TESTLD/LLN0.Mod.origin.orIdent", "ST",
            out var evidence));
        Assert.Equal(display, evidence.Value);
        Assert.Equal(association, evidence.Association);
        Assert.False(evidence.IsStale);
        Assert.False(ledger.TryGet("TESTLD/LLN0.Mod.origin.orIdent", "CF", out _));
        ledger.BeginAssociation();
        Assert.False(ledger.TryGet("TESTLD/LLN0.Mod.origin.orIdent", "ST", out _));
    }

    [Fact]
    public void BlankText_UnknownTag_Struct_AndWrongModelNeverEnterObservedOctetState()
    {
        var bytes = new byte[] {1,2};
        Assert.False(IedBrowserReadValuePolicy.TryFormatVerifiedEmptyOctet(
            MmsDataValue.VisibleString(""), "Octet64", out _));
        Assert.False(IedBrowserReadValuePolicy.TryFormatVerifiedEmptyOctet(
            MmsDataValue.MmsString(""), "Octet64", out _));
        Assert.False(IedBrowserReadValuePolicy.TryFormatVerifiedEmptyOctet(
            MmsDataValue.Unknown(9, []), "Octet64", out _));
        Assert.False(IedBrowserReadValuePolicy.TryFormatVerifiedEmptyOctet(
            MmsDataValue.Structure([]), "Octet64", out _));
        Assert.False(IedBrowserReadValuePolicy.TryFormatVerifiedEmptyOctet(
            MmsDataValue.OctetString(bytes), "Octet64", out _));
        Assert.False(IedBrowserReadValuePolicy.TryFormatVerifiedEmptyOctet(
            MmsDataValue.OctetString([]), "INT32", out _));
        Assert.False(IedBrowserReadValuePolicy.TryFormatVerifiedEmptyOctet(
            null, "Octet64", out _));
        Assert.False(IedBrowserReadValuePolicy.TryAcceptDisplay(
            "", out _, out _));
    }
}
