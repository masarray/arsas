using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214BrowserReadValueVerdictTests
{
    [Theory]
    [InlineData("False")]
    [InlineData("0")]
    [InlineData("-12")]
    [InlineData("true")]
    [InlineData("Good")]
    [InlineData("Open [01]")]
    [InlineData("bits(0000,unused=3)")]
    [InlineData("2026-10-15 09:48:50.90088 UTC")]
    [InlineData("unknowning")] // no substring-based guessing
    public void RealScalarRepresentationsRemainReadable(string actual)
    {
        Assert.True(IedBrowserReadValuePolicy.TryAcceptDisplay(actual,
            out var display, out var diagnostic), diagnostic);
        Assert.Equal(actual, display);
        Assert.Empty(diagnostic);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("-")]
    [InlineData("unknown")]
    [InlineData("UNKNOWN")]
    [InlineData(" Unknown ")]
    [InlineData("Not read")]
    [InlineData("pending read")]
    [InlineData("READ FAILED")]
    [InlineData("Unavailable")]
    [InlineData("unknown(tag=13, raw=0001)")]
    [InlineData("Structure(2) { [0] = 5, [1] = 6 }")]
    [InlineData("Array(2) { [0] = 0, [1] = 1 }")]
    [InlineData("{stVal=3}")]
    [InlineData("[0]=3")]
    [InlineData("System.Collections.Generic.List")]
    public void PlaceholderAndUnprojectedWireValuesAreNotVerifiedScalarReads(
        string candidate)
    {
        Assert.False(IedBrowserReadValuePolicy.TryAcceptDisplay(candidate,
            out _, out var diagnostic));
        Assert.NotEmpty(diagnostic);
    }

    [Fact]
    public void RejectedMmsReadCannotPoisonCanonicalLedger_ButRealZeroCan()
    {
        var ledger = new IedBrowserLiveValues();
        var assoc = ledger.BeginAssociation();
        const string reference = "LD/PIOC1.SgndPIOC1.InMagTyp.setVal";
        Assert.False(ledger.PublishForAssociation(
            assoc, reference, "SG", "unknown", "", "", "MMS Read"));
        Assert.False(ledger.PublishForAssociation(
            assoc, reference, "SG", "Structure(1) { [0] = 0 }",
            "", "", "MMS Read"));
        Assert.False(ledger.TryGet(reference, "SG", out _));
        Assert.True(ledger.PublishForAssociation(
            assoc, reference, "SG", "0", "", "", "MMS Read"));
        Assert.True(ledger.TryGet(reference, "SG", out var observed));
        Assert.Equal("0", observed.Value);
        Assert.Equal("MMS Read", observed.Source);
        Assert.False(ledger.PublishForAssociation(
            assoc - 1, reference, "SG", "7", "", "", "MMS Read"));
        Assert.True(ledger.TryGet(reference, "SG", out observed));
        Assert.Equal("0", observed.Value);
    }

    [Fact]
    public void BothBrowserReadServicesAndUiRequireTheSameEvidenceVerdict()
    {
        var isolated = File.ReadAllText(Find("Services/IedBrowserIsolatedReadService.cs"));
        var shared = File.ReadAllText(Find("Services/Iec61850MonitorRuntime.cs"));
        var ui = File.ReadAllText(Find("MainWindow.IedBrowser.cs"));
        // Both reader and UI now require wire-kind proof when an MMS string
        // begins with array/structure-like punctuation. The shared monitoring
        // service retains the conservative display-only policy; its untyped
        // observations must never receive the explicit-read exception.
        Assert.Contains("IedBrowserReadValuePolicy.TryAcceptVerifiedMmsString", isolated);
        Assert.Contains("IedBrowserReadValuePolicy.TryAcceptDisplay", shared);
        Assert.Contains("IedBrowserReadValuePolicy.TryAcceptVerifiedMmsString", ui);
        Assert.Contains("result.VerifiedStringWireKind", ui);
        Assert.Contains("declaredDataType: result.Target.DataType", ui);
        Assert.Contains("failureExamples", ui);
    }

    private static string Find(string file)
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var path = Path.Combine(d.FullName, file);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(file);
    }
}
