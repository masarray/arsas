using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class Sg7ExplicitPendingSnapshotRegressionTests
{
    private static Iec61850MonitorPoint Point(string fc = "MX") => new()
    {
        DeviceId = "sg7",
        IecReference = "SG7_BCUMeter/ACsrcMMXU1.Hz.mag.f",
        FunctionalConstraint = fc,
        DataSetReference = "SG7_BCUMaster/LLN0.TT6DataSet4"
    };

    [Fact]
    public void OnlyExactPendingStaticStMxMembersAreEligibleForOperatorRead()
    {
        var point = Point();
        Assert.True(Iec61850MonitorRuntime.IsExactPendingStaticSnapshotCandidate(
            point, hasValue: false, reportTrafficSeen: false));
        Assert.False(Iec61850MonitorRuntime.IsExactPendingStaticSnapshotCandidate(
            point, hasValue: true, reportTrafficSeen: false));
        Assert.False(Iec61850MonitorRuntime.IsExactPendingStaticSnapshotCandidate(
            point, hasValue: false, reportTrafficSeen: true));
        Assert.False(Iec61850MonitorRuntime.IsExactPendingStaticSnapshotCandidate(
            Point("CO"), hasValue: false, reportTrafficSeen: false));
        Assert.False(Iec61850MonitorRuntime.IsExactPendingStaticSnapshotCandidate(
            Point("CF"), hasValue: false, reportTrafficSeen: false));
        point.DataSetReference = "";
        Assert.False(Iec61850MonitorRuntime.IsExactPendingStaticSnapshotCandidate(
            point, hasValue: false, reportTrafficSeen: false));
        Assert.Equal(77, Iec61850MonitorRuntime.StaticPendingSnapshotMaxTargets);
    }

    [Fact]
    public void AlternatesAndUnnamedWireReadsCannotImpersonateSelectedMember()
    {
        var selected = Point().IecReference;
        Assert.True(Iec61850MonitorRuntime.IsExactStaticSnapshotReadReference(
            selected, selected.Replace('.', '$')));
        Assert.False(Iec61850MonitorRuntime.IsExactStaticSnapshotReadReference(
            selected, "SG7_BCUMeter/ACsrcMMXU1.Hz.instMag.f"));
        Assert.False(Iec61850MonitorRuntime.IsExactStaticSnapshotReadReference(
            selected, ""));
    }

    [Fact]
    public void ExplicitReadIsCanonicalButNeverUpgradesToRcbOrAssumesQualityGood()
    {
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        var point = Point();
        Assert.True(Iec61850MonitorRuntime.PublishVerifiedMmsReadToCanonical(
            ledger, association,
            new ResolvedIecSignalRead(50.0d, point.IecReference),
            "MX", "FLOAT32", "Unknown", "-"));
        Assert.True(ledger.TryGet(point.IecReference, "MX", out var read));
        Assert.Equal("MMS Read", read.Source);
        Assert.Equal("Unknown", read.Quality);
        Assert.Equal("Snapshot Read", Iec61850EventOriginPolicy.PointAcquisition(
            Array.Empty<string>(), "MMS Snapshot Read", false));
        Assert.True(ledger.PublishForAssociation(association, point.IecReference,
            "MX", "50.5", "Good", "", "Report"));
        Assert.True(ledger.TryGet(point.IecReference, "MX", out var report));
        Assert.Equal("Report", report.Source);
        Assert.Equal("50.5", report.Value);
        Assert.Equal("Good", report.Quality);
    }
}
