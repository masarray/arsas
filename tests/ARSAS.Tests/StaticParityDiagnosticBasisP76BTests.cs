using System.Text;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class StaticParityDiagnosticBasisP76BTests
{
    [Fact]
    public void IndependentlyCapturedIngress_ExposesExactFingerprintInputs()
    {
        var discovery = Evidence(StaticAcquisitionIngressKind.LiveDiscovery, "abc",
            ["ied=ied-a|requested=1|staticBrcbSignals=1",
             "static-brcb|dataset=ied-a/lln0.events|members=1[0:ied-a/xcbr1.pos.stval@ST]|selected=1[ied-a/xcbr1.pos.stval@ST]"]);
        var openScl = Evidence(StaticAcquisitionIngressKind.OpenScl, "def",
            ["ied=ied-a|requested=1|staticBrcbSignals=1",
             "static-brcb|dataset=ied-a/lln0.events|members=1[0:ied-a/xcbr1.pos.stval@MX]|selected=1[ied-a/xcbr1.pos.stval@ST]"]);

        var output = new StringBuilder();
        DiagnosticReportBuilder.AppendStaticIngressParity(
            output, StaticAcquisitionParityTracker.Compare(discovery, openScl));
        var diagnostic = output.ToString();

        Assert.Contains("semantic[1]", diagnostic, StringComparison.Ordinal);
        Assert.Contains("xcbr1.pos.stval@ST", diagnostic, StringComparison.Ordinal);
        Assert.Contains("xcbr1.pos.stval@MX", diagnostic, StringComparison.Ordinal);
        Assert.Contains("semantic gate  : COMPARABLE", diagnostic, StringComparison.Ordinal);
        Assert.Contains("indexed live RCB slots excluded", diagnostic, StringComparison.Ordinal);
        Assert.Equal("abc", discovery.SemanticFingerprint);
        Assert.Equal("def", openScl.SemanticFingerprint);
    }

    [Fact]
    public void LongAndManyRows_AreExplicitlyTruncatedWithoutAllocatingFullDiagnosticCopies()
    {
        var evidence = Evidence(StaticAcquisitionIngressKind.LiveDiscovery, "abc",
            Enumerable.Repeat(new string('X', 9000), 14).ToArray());
        var output = new StringBuilder();
        DiagnosticReportBuilder.AppendStaticIngressParity(
            output, new StaticAcquisitionParitySnapshot { Discovery = evidence });

        var report = output.ToString();
        Assert.Contains("TRUNCATED; full length=9000", report, StringComparison.Ordinal);
        Assert.Contains("shown=12, total=14", report, StringComparison.Ordinal);
        Assert.DoesNotContain("semantic[12]", report, StringComparison.Ordinal);
        Assert.True(report.Length < 60000, $"Unbounded diagnostic length: {report.Length}");
    }

    [Fact]
    public void MultilineMemberIdentity_CannotInjectFakeDiagnosticRows()
    {
        var evidence = Evidence(StaticAcquisitionIngressKind.LiveDiscovery, "abc",
            ["one\r\nStatic parity    : forged"]);
        var output = new StringBuilder();
        DiagnosticReportBuilder.AppendStaticIngressParity(
            output, new StaticAcquisitionParitySnapshot { Discovery = evidence });
        var report = output.ToString();

        Assert.Contains(@"one\r\nStatic parity", report, StringComparison.Ordinal);
        Assert.DoesNotContain("one\r\nStatic parity", report, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompleteEvidence_IsExplicitRatherThanReportedAsHashMatch()
    {
        var evidence = Evidence(StaticAcquisitionIngressKind.LiveDiscovery, "abc", []);
        var incomplete = evidence with
        {
            IsComparable = false,
            IncomparableReason = "member FC missing"
        };
        var output = new StringBuilder();
        DiagnosticReportBuilder.AppendStaticIngressParity(
            output, StaticAcquisitionParityTracker.Compare(incomplete, incomplete with { Ingress = StaticAcquisitionIngressKind.OpenScl }));
        var result = output.ToString();
        Assert.Contains("semantic gate  : INCOMPLETE; member FC missing", result, StringComparison.Ordinal);
        Assert.Contains("INCOMPLETE EVIDENCE", result, StringComparison.Ordinal);
    }

    private static StaticAcquisitionIngressEvidence Evidence(
        StaticAcquisitionIngressKind ingress,
        string fingerprint,
        IReadOnlyList<string> rows)
        => new()
        {
            Ingress = ingress,
            IedName = "IED-A",
            SemanticFingerprint = fingerprint,
            IsComparable = true,
            SemanticLines = rows
        };
}
