using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0ModelTypedMmsStringTests
{
    [Theory]
    [InlineData("[IRV:vendor-valid", "VisString255", MmsDataKind.VisibleString)]
    [InlineData("{literal vendor text}", "visible-string", MmsDataKind.VisibleString)]
    [InlineData("[literal MMS text]", "MmsString128", MmsDataKind.MmsString)]
    public void VerifiedDecodedStringsWithBracketPrefixAreNotMistakenForMmsArrays(
        string scalar, string declaredType, MmsDataKind wireKind)
    {
        Assert.False(IedBrowserReadValuePolicy.TryAcceptDisplay(scalar, out _, out _));
        Assert.True(IedBrowserReadValuePolicy.TryAcceptVerifiedMmsString(
            scalar, declaredType, wireKind, out var accepted, out var reason));
        Assert.Equal(scalar, accepted);
        Assert.Equal("", reason);
    }

    [Theory]
    [InlineData("[unverified", "VisString255", null)]
    [InlineData("{not a string}", "Int32", MmsDataKind.VisibleString)]
    [InlineData("[wrong decoder]", "VisString255", MmsDataKind.MmsString)]
    [InlineData("[wrong primitive]", "VisString255", MmsDataKind.Array)]
    [InlineData("[unknown declaration]", "", MmsDataKind.VisibleString)]
    [InlineData("unknown(tag=0x83)", "VisString255", MmsDataKind.VisibleString)]
    [InlineData("Structure(2)", "VisString255", MmsDataKind.VisibleString)]
    [InlineData("", "VisString255", MmsDataKind.VisibleString)]
    public void UnverifiedArrayLikeAndUnsupportedResultsFailClosed(
        string scalar, string modelType, MmsDataKind? kind)
    {
        Assert.False(IedBrowserReadValuePolicy.TryAcceptVerifiedMmsString(
            scalar, modelType, kind, out _, out _));
    }

    [Fact]
    public void ScalarBoundaryRemainsBoundedDespiteWireProof()
    {
        Assert.False(IedBrowserReadValuePolicy.TryAcceptVerifiedMmsString(
            "[" + new string('x', 513), "VisString600",
            MmsDataKind.VisibleString, out _, out _));
    }

    [Fact]
    public void LiveLedgerRetainsTypedProofOnlyForCurrentReadAssociation()
    {
        const string reference = "ZIVAVR/LLN0.NamPlt.swRev";
        var ledger = new IedBrowserLiveValues();
        var epoch = ledger.BeginAssociation();
        Assert.False(ledger.PublishForAssociation(epoch, reference, "DC", "[IRV:firmware",
            "", "", "MMS Read"));
        Assert.False(ledger.PublishForAssociation(epoch, reference, "DC", "[IRV:firmware",
            "", "", "MMS Read", verifiedStringWireKind: MmsDataKind.VisibleString,
            declaredDataType: "Int32"));
        Assert.True(ledger.PublishForAssociation(epoch, reference, "DC", "[IRV:firmware",
            "", "", "MMS Read", verifiedStringWireKind: MmsDataKind.VisibleString,
            declaredDataType: "VisString255"));
        Assert.True(ledger.TryGet(reference, "DC", out var observed, preferReport: false));
        Assert.Equal("[IRV:firmware", observed.Value);
        Assert.Equal("MMS Read", observed.Source);
        ledger.BeginAssociation();
        Assert.False(ledger.PublishForAssociation(epoch, reference, "DC", "[IRV:old",
            "", "", "MMS Read", verifiedStringWireKind: MmsDataKind.VisibleString,
            declaredDataType: "VisString255"));
        Assert.False(ledger.TryGet(reference, "DC", out _, preferReport: false));
    }

    [Fact]
    public void ReadDecodeProofMustFlowThroughIsolatedReaderAndBrowserIngestion()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MainWindow.IedBrowser.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.IedBrowser.cs"));
        var isolated = File.ReadAllText(Path.Combine(root.FullName,
            "Services", "IedBrowserIsolatedReadService.cs"));
        var native = File.ReadAllText(Path.Combine(root.FullName,
            "Services", "NativeIec61850Client.cs"));
        Assert.Contains("result.VerifiedStringWireKind", source);
        Assert.Contains("declaredDataType: result.Target.DataType", source);
        Assert.Contains("read?.VerifiedStringWireKind", isolated);
        Assert.Contains("raw.Value is string decoded", native);
        Assert.Contains("CanonicalReference(projected.ReadReference)", native);
        Assert.Contains("CanonicalReference(reference)", native);
        Assert.Contains("projected.VerifiedStringWireKind = raw.Kind", native);
    }
}
