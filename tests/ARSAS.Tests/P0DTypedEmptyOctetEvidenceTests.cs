using AR.Iec61850.Mms;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;
using System.Reflection;

namespace ARSAS.Tests;

public sealed class P0DTypedEmptyOctetEvidenceTests
{
    [Fact]
    public void ExactTypedEmptyOriginIdentifier_IsObserved_NotReadFailed()
    {
        using var client = new NativeIec61850Client();
        var method = typeof(NativeIec61850Client).GetMethod(
            "ProjectReadValue", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        const string reference = "LABPROT/LLN0.Mod.origin.orIdent";
        var value = MmsDataValue.OctetString(ReadOnlySpan<byte>.Empty);
        var read = method!.Invoke(client, [value, "Octet64", reference, reference])
            as Iec61850ReadValue;
        Assert.NotNull(read);
        Assert.Equal("typed-empty-octet-string", read.Projection);
        Assert.Equal("Empty OCTET STRING (0 bytes)", read.ToString());
        Assert.True(IedBrowserReadValuePolicy.TryAcceptDisplay(
            read.ToString(), out _, out _));
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association, reference, "ST",
            read.ToString(), "", "", "MMS Read"));
        Assert.True(ledger.TryGet(reference, "ST", out var observed));
        Assert.Equal("Empty OCTET STRING (0 bytes)", observed.Value);
    }

    [Fact]
    public void EmptyTextOrUnknownTag_NeverGetsOctetWireAuthority()
    {
        using var client = new NativeIec61850Client();
        var method = typeof(NativeIec61850Client).GetMethod(
            "ProjectReadValue", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        const string reference = "LABPROT/LLN0.Mod.origin.orIdent";
        var emptyVisible = method!.Invoke(client,
            [MmsDataValue.VisibleString(""), "Octet64", reference, reference])
            as Iec61850ReadValue;
        Assert.NotEqual("typed-empty-octet-string", emptyVisible?.Projection);
        Assert.False(IedBrowserReadValuePolicy.TryAcceptDisplay(
            "", out _, out _));
    }
}
