using AR.Iec61850.Discovery;
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
    public void ReadOnlyRcbFocusContainsThreeExactCurrentFieldsWithoutWholeIedSweep()
    {
        var row = new RcbExportRow { Reference = Root, Buffered = false };
        var plan = IedBrowserRcbFocusedReadPlan.Build(Model(), row);
        Assert.Equal(3, plan.Count);
        Assert.Equal(new[] { "RptEna", "Resv", "ResvTms" },
            plan.Select(x => x.Reference.Split('.').Last()).ToArray());
        Assert.All(plan, x => Assert.Equal("RP", x.FunctionalConstraint));
        Assert.All(plan, x => Assert.StartsWith(Root + ".", x.Reference));
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
