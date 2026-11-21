using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M217QRcbFocusedStateTests
{
    private const string Root = "BCUGEF650/LLN0.RP.URB_ALS_SOGI20101";
    private static LiveIedModelDiscoveryDocument Model() => new()
    {
        ReportControls = [new LiveIedReportControlModel
        {
            Reference = Root, Name = "URB_ALS_SOGI20101"
        }]
    };

    [Fact]
    public void ReadOnlyRcbFocusContainsThreeFamilySpecificFieldsWithoutWholeIedSweep()
    {
        var row = new RcbExportRow { Reference = Root, Buffered = false };
        var plan = IedBrowserRcbFocusedReadPlan.Build(Model(), row);
        Assert.Equal(3, plan.Count);
        Assert.Equal(new[] { "RptEna", "Owner", "Resv" },
            plan.Select(x => x.Reference.Split('.').Last()).ToArray());
        Assert.All(plan, x => Assert.Equal("RP", x.FunctionalConstraint));
        Assert.All(plan, x => Assert.StartsWith(Root + ".", x.Reference));
        Assert.Equal("OctetString", plan.Single(x =>
            x.Reference.EndsWith(".Owner", StringComparison.Ordinal)).DataType);
    }

    [Fact]
    public void NoSourceOnlyOrDifferentRcbsAreAuthorizedForFocusedRead()
    {
        Assert.Empty(IedBrowserRcbFocusedReadPlan.Build(null,
            new RcbExportRow { Reference = Root }));
        Assert.Empty(IedBrowserRcbFocusedReadPlan.Build(Model(),
            new RcbExportRow { Reference = Root + "_OTHER" }));
        Assert.Empty(IedBrowserRcbFocusedReadPlan.Build(Model(),
            new RcbExportRow { Reference = Root, Buffered = true }));
    }


    [Fact]
    public void SelectedSg7BrcbC01ReadsExactOwnerWithoutCheckingSiblingRcbs()
    {
        const string selected = "SG7_BCUMaster/LLN0.BR.BrcbC01";
        var model = new LiveIedModelDiscoveryDocument
        {
            ReportControls =
            [
                new LiveIedReportControlModel { Reference = selected, Name = "BrcbC01" },
                new LiveIedReportControlModel
                {
                    Reference = "SG7_BCUMaster/LLN0.BR.BrcbB01", Name = "BrcbB01"
                }
            ]
        };
        var plan = IedBrowserRcbFocusedReadPlan.Build(model, new RcbExportRow
        {
            Reference = selected,
            Buffered = true
        });
        Assert.Equal(3, plan.Count);
        Assert.Equal(new[] { "RptEna", "Owner", "ResvTms" },
            plan.Select(item => item.Reference.Split('.').Last()).ToArray());
        Assert.All(plan, item =>
        {
            Assert.Equal("BR", item.FunctionalConstraint);
            Assert.StartsWith(selected + ".", item.Reference);
        });
        Assert.Equal("OctetString", plan.Single(item =>
            item.Reference.EndsWith(".Owner", StringComparison.Ordinal)).DataType);
    }

    [Fact]
    public void VerifiedOwnerOctetDisplaysExactFieldIpv4Bytes()
    {
        Assert.True(IedBrowserRcbOwnerDisplay.TryFormatVerifiedOctet(
            "C0100149", MmsDataKind.OctetString, out var display));
        Assert.Equal("192.16.1.73 (x'C0100149')", display);
        Assert.True(IedBrowserRcbOwnerDisplay.TryFormatVerifiedOctet(
            "x'C0100149'", MmsDataKind.OctetString, out var diagnostic));
        Assert.Equal(display, diagnostic);
        Assert.True(IedBrowserRcbOwnerDisplay.TryFormatVerifiedOctet(
            "010203040506", MmsDataKind.OctetString, out var unknownEncoding));
        Assert.Equal("x'010203040506'", unknownEncoding);
        Assert.Equal("192.16.1.73 (x'C0100149')",
            IedBrowserRcbOwnerDisplay.FormatCheckedSnapshot("C0100149"));
    }

    [Theory]
    [InlineData("C0100149", null)]
    [InlineData("C0100149", MmsDataKind.VisibleString)]
    [InlineData("unknown(tag=9, raw=C0100149)", MmsDataKind.OctetString)]
    [InlineData("C010014", MmsDataKind.OctetString)]
    [InlineData("", MmsDataKind.OctetString)]
    public void OwnerIsNotInventedWithoutExactWireKindAndValidHex(
        string raw, MmsDataKind? kind)
    {
        Assert.False(IedBrowserRcbOwnerDisplay.TryFormatVerifiedOctet(
            raw, kind, out var display));
        Assert.Equal("", display);
    }

    [Fact]
    public void OwnerReadIsAssociationBoundAndReportTrafficDoesNotImpersonateIt()
    {
        const string ownerRef = "SG7_BCUMaster/LLN0.BR.BrcbC01.Owner";
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association, ownerRef,
            "BR", "C0100149", "", "", "MMS Read",
            verifiedStringWireKind: MmsDataKind.OctetString,
            declaredDataType: "OctetString"));
        Assert.True(ledger.TryGet(ownerRef, "BR", out var seen, preferReport: false));
        Assert.Equal(MmsDataKind.OctetString, seen.VerifiedReadWireKind);
        ledger.MarkOffline();
        Assert.False(ledger.PublishForAssociation(association, ownerRef,
            "BR", "C0100149", "", "", "MMS Read",
            verifiedStringWireKind: MmsDataKind.OctetString,
            declaredDataType: "OctetString"));
        Assert.True(ledger.TryGet(ownerRef, "BR", out var stale));
        Assert.True(stale.IsStale); // Audit history remains visible, not current proof.
        var reconnect = ledger.BeginAssociation();
        Assert.NotEqual(association, reconnect);
        Assert.False(ledger.TryGet(ownerRef, "BR", out _));
        Assert.True(ledger.PublishForAssociation(reconnect, ownerRef,
            "BR", "C0100149", "", "", "Report",
            verifiedStringWireKind: MmsDataKind.OctetString));
        Assert.True(ledger.TryGet(ownerRef, "BR", out var reported));
        Assert.Null(reported.VerifiedReadWireKind);
    }

    [Fact]
    public void ReportTrafficCannotProveRptEnaAndStaleRcbReadCannotCrossAssociation()
    {
        var row = new RcbExportRow { Reference = Root };
        var notProven = RcbInspectionEvidence.FromDiscovery(
            Model().ReportControls.Single(), null);
        Assert.Equal("Not verified", notProven.Properties(row, true)
            .Single(p => p.Property == "Enabled / RptEna").Value);
        var ledger = new IedBrowserLiveValues();
        var old = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(old, Root + ".RptEna",
            "RP", "true", "", "", "MMS Read"));
        ledger.BeginAssociation();
        Assert.False(ledger.TryGet(Root + ".RptEna", "RP", out _));
        Assert.False(ledger.PublishForAssociation(old, Root + ".RptEna",
            "RP", "true", "", "", "MMS Read"));
    }
}
