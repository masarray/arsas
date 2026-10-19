using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Reproduces field regression: six RCBs were activated after tree construction,
/// but their existing Explorer nodes retained '?' instead of caller-owned ●.
/// </summary>
public sealed class P0BrowserRcbLiveOwnershipReconciliationTests
{
    private static RcbExportRow Rcb(string path) => new()
    {
        Name = path[(path.LastIndexOf('.') + 1)..],
        Reference = path,
        Availability = MmsRcbOperationalAvailability.Unknown,
        StatusText = "Not checked"
    };

    [Fact]
    public void ExistingTreeBecomesCallerOwnedWhenRuntimeActivatesAfterConstruction()
    {
        var active = Rcb("BCUGEF650/LLN0.RP.URB_MX_SOGI10101");
        var untouched = Rcb("BCUGEF650/LLN0.RP.URB_MX_SOGI10102");
        var node = new IedBrowserTreeRow("reports/rcb/active",
            active.Name, "RCB", 3, active);
        Assert.Equal("?", node.RcbUsageGlyph);
        var activeRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BCUGEF650/LLN0$RP$URB_MX_SOGI10101"
        };
        Assert.Equal(1, IedBrowserRcbLiveOwnershipPresentation.Reconcile(
            [active, untouched], activeRefs, connected: true));
        Assert.Equal("●", node.RcbUsageGlyph);
        Assert.Equal("ARSAS active", active.StatusText);
        Assert.Equal("?", new IedBrowserTreeRow("reports/rcb/untouched",
            untouched.Name, "RCB", 3, untouched).RcbUsageGlyph);
        Assert.Equal(0, IedBrowserRcbLiveOwnershipPresentation.Reconcile(
            [active, untouched], activeRefs, connected: true));
    }

    [Fact]
    public void DisconnectOrMonitorStopCannotLeaveFalseCallerOwnedIndicator()
    {
        var row = Rcb("BCUGEF650/LLN0.BR.BRB_CSWI_GW010101");
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { row.Reference };
        Assert.Equal(1, IedBrowserRcbLiveOwnershipPresentation.Reconcile(
            [row], owned, connected: true));
        Assert.Equal(1, IedBrowserRcbLiveOwnershipPresentation.Reconcile(
            [row], owned, connected: false));
        Assert.Equal(MmsRcbOperationalAvailability.Unknown, row.Availability);
        Assert.Equal(0, IedBrowserRcbLiveOwnershipPresentation.Reconcile(
            [row], owned, connected: false));
        Assert.Equal(1, IedBrowserRcbLiveOwnershipPresentation.Reconcile(
            [row], owned, connected: true));
        Assert.Equal(1, IedBrowserRcbLiveOwnershipPresentation.Reconcile(
            [row], new HashSet<string>(), connected: true));
        Assert.Equal(MmsRcbOperationalAvailability.Unknown, row.Availability);
    }

    [Fact]
    public void UnrelatedReadOnlyInUseSnapshotMustNotBeOverwritten()
    {
        var external = Rcb("BCUGEF650/LLN0.BR.BRB_EVE_GW01");
        external.Availability = MmsRcbOperationalAvailability.InUse;
        external.Reason = "Verified read-only third-party reservation";
        Assert.Equal(0, IedBrowserRcbLiveOwnershipPresentation.Reconcile(
            [external], new HashSet<string>(), connected: true));
        Assert.Equal(MmsRcbOperationalAvailability.InUse, external.Availability);
        Assert.Equal("Verified read-only third-party reservation", external.Reason);
    }

    [Fact]
    public void ExactIdentityMustExcludeOtherRCBInstance()
    {
        var one = Rcb("BCUGEF650/LLN0.RP.urcb01_4401");
        var two = Rcb("BCUGEF650/LLN0.RP.urcb01_4402");
        Assert.Equal(1, IedBrowserRcbLiveOwnershipPresentation.Reconcile(
            [one, two], new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "BCUGEF650/LLN0$RP$urcb01_4402" }, connected: true));
        Assert.Equal(MmsRcbOperationalAvailability.Unknown, one.Availability);
        Assert.Equal(MmsRcbOperationalAvailability.UsedByCaller, two.Availability);
    }
}
