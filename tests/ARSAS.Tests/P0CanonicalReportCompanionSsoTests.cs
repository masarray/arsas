using ArIED61850Tester.Models;
using ArIED61850Tester.Protocol.Mms;

namespace ARSAS.Tests;

/// <summary>
/// Reproduces the field failure: raw Report q was verified, runtime q snapshot
/// changed, but canonical process Report still had Unknown and WPF projected it
/// back to Unknown. The model owner/FC/association check precedes this API.
/// </summary>
public sealed class P0CanonicalReportCompanionSsoTests
{
    private const string Tap = "ZIVAVRLD1/ATCC1.TapChg.valWTr.posVal";
    private const string Q = "ZIVAVRLD1/ATCC1.TapChg.valWTr.q";

    private static MmsQualityEvidence Quality(byte first)
    {
        Assert.True(MmsQualityDecoder.TryDecode([3, first, 0], out var q));
        return q;
    }

    [Fact]
    public void TypedQAtomicallyUpdatesCanonicalProcessReportUsedByWpf()
    {
        var device = new Iec61850MonitorDevice { IsConnected = true, IsMonitoring = true };
        var assoc = device.LiveValues.BeginAssociation();
        Assert.True(device.LiveValues.PublishForAssociation(
            assoc, Tap, "ST", "6", "", "", "Report"));
        Assert.True(device.LiveValues.PublishForAssociation(
            assoc, Q, "ST", "Good", "", "", "Report quality · raw MMS",
            qualityEvidence: Quality(0)));

        Assert.True(device.LiveValues.TryGet(Tap, "ST", out var before));
        Assert.True(string.IsNullOrEmpty(before.Quality));
        Assert.True(device.LiveValues.MergeVerifiedReportCompanionForAssociation(
            assoc, Tap, "ST", qualityEvidence: Quality(0)));
        Assert.True(device.LiveValues.TryGet(Tap, "ST", out var after));
        Assert.Equal("6", after.Value);
        Assert.Equal("Good", after.Quality);
        Assert.NotNull(after.QualityBits);
        Assert.True(after.Revision > before.Revision);
        Assert.Equal("Report", after.Source);

        var point = new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId,
            IecReference = Tap,
            FunctionalConstraint = "ST",
            IecDataType = "Int32"
        };
        var snapshot = new Iec61850PointSnapshot
        {
            Point = point,
            Value = "6",
            Quality = "Good",
            ObservationAssociation = assoc
        };
        var projected = CanonicalPointSnapshotAuthority.Project(device, point, snapshot);
        Assert.True(projected.FromCanonicalReport);
        Assert.Equal("6", projected.Value);
        Assert.Equal("Good", projected.Quality);
    }

    [Fact]
    public void NextValueOnlyReportPreservesActualPreviouslyTypedQualityAndNoNewRead()
    {
        var ledger = new IedBrowserLiveValues();
        var assoc = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(assoc, Tap, "ST", "6", "", "", "Report"));
        Assert.True(ledger.MergeVerifiedReportCompanionForAssociation(
            assoc, Tap, "ST", qualityEvidence: Quality(0)));
        Assert.True(ledger.PublishForAssociation(assoc, Tap, "ST", "7", "", "", "Report"));
        Assert.True(ledger.TryGet(Tap, "ST", out var after));
        Assert.Equal("7", after.Value);
        Assert.Equal("Good", after.Quality);
        Assert.NotNull(after.QualityBits);
        Assert.Equal("Report", after.Source);

        Assert.True(ledger.PublishForAssociation(assoc, Tap, "ST",
            "7", "Invalid", "", "Report"));
        Assert.True(ledger.TryGet(Tap, "ST", out var explicitBad));
        Assert.Equal("Invalid", explicitBad.Quality);
        // The new explicit text contradicts an old typed Good. Do not show
        // stale detail flags under a newer invalid textual label.
        Assert.Null(explicitBad.QualityBits);
    }

    [Fact]
    public void NeverPromotesUnobservedValueOrStaleAssociation()
    {
        var ledger = new IedBrowserLiveValues();
        var a = ledger.BeginAssociation();
        var q = Quality(0);
        // q alone cannot create a synthetic process value / event.
        Assert.False(ledger.MergeVerifiedReportCompanionForAssociation(
            a, Tap, "ST", qualityEvidence: q));
        Assert.True(ledger.PublishForAssociation(a, Tap, "ST", "6", "", "", "MMS Read"));
        Assert.False(ledger.MergeVerifiedReportCompanionForAssociation(
            a, Tap, "ST", qualityEvidence: q));
        Assert.True(ledger.PublishForAssociation(a, Tap, "ST", "6", "", "", "Report"));
        var next = ledger.BeginAssociation();
        Assert.False(ledger.MergeVerifiedReportCompanionForAssociation(
            a, Tap, "ST", qualityEvidence: q));
        Assert.False(ledger.TryGet(Tap, "ST", out _));
        Assert.True(ledger.PublishForAssociation(next, Tap, "ST", "2", "", "", "Report"));
        Assert.False(ledger.MergeVerifiedReportCompanionForAssociation(
            a, Tap, "ST", qualityEvidence: q));
        Assert.True(ledger.TryGet(Tap, "ST", out var actual));
        Assert.True(string.IsNullOrEmpty(actual.Quality));
    }

    [Fact]
    public void ExactFcAndReferenceRemainIsolatedAndDualCompanionPayloadRejected()
    {
        var ledger = new IedBrowserLiveValues();
        var assoc = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(assoc, Tap, "ST", "6", "", "", "Report"));
        var q = Quality(0x40);
        Assert.False(ledger.MergeVerifiedReportCompanionForAssociation(
            assoc, Tap, "MX", qualityEvidence: q));
        Assert.False(ledger.MergeVerifiedReportCompanionForAssociation(
            assoc, "ZIVAVRLD1/ATCC10.TapChg.valWTr.posVal",
            "ST", qualityEvidence: q));
        Assert.False(ledger.MergeVerifiedReportCompanionForAssociation(
            assoc, Tap, "ST"));
        Assert.True(ledger.TryGet(Tap, "ST", out var unchanged));
        Assert.True(string.IsNullOrEmpty(unchanged.Quality));
        Assert.True(ledger.MergeVerifiedReportCompanionForAssociation(
            assoc, Tap, "ST", qualityEvidence: q));
        Assert.True(ledger.TryGet(Tap, "ST", out var bad));
        Assert.Equal("Invalid", bad.Quality);
        Assert.Equal("invalid", bad.QualityBits!.FormatField("Validity"));
    }

    [Fact]
    public void RawQualityRemainsIndependentAndNeverChangesOriginalBytes()
    {
        var ledger = new IedBrowserLiveValues();
        var a = ledger.BeginAssociation();
        var quality = Quality(0x40);
        Assert.True(ledger.PublishForAssociation(
            a, Tap, "ST", "6", "", "", "Report"));
        Assert.True(ledger.PublishForAssociation(
            a, Q, "ST", "bits(4000,unused=3)", "", "", "Report quality · raw MMS",
            qualityEvidence: quality));
        Assert.True(ledger.MergeVerifiedReportCompanionForAssociation(
            a, Tap, "ST", qualityEvidence: quality));
        Assert.True(ledger.TryGet(Q, "ST", out var raw));
        Assert.Equal("bits(4000,unused=3)", raw.Value);
        Assert.NotNull(raw.QualityBits);
        Assert.True(ledger.TryGet(Tap, "ST", out var process));
        Assert.Equal("Invalid", process.Quality);
        Assert.Equal("6", process.Value);
    }

    [Fact]
    public void QualityBeforeProcessValueRemainsRawUntilExactProcessReportExists()
    {
        var ledger = new IedBrowserLiveValues();
        var assoc = ledger.BeginAssociation();
        var q = Quality(0);
        // A typed q Report may precede the selected process member across
        // receive slices; it cannot create an independent process value.
        Assert.True(ledger.PublishForAssociation(assoc, Q, "ST",
            "bits(0000,unused=3)", "", "", "Report quality · raw MMS",
            qualityEvidence: q));
        Assert.False(ledger.MergeVerifiedReportCompanionForAssociation(
            assoc, Tap, "ST", qualityEvidence: q));
        Assert.False(ledger.TryGet(Tap, "ST", out _));

        // Later exact, independent process ingress admits the existing typed
        // q into that process record, without changing raw q or borrowing FC.
        Assert.True(ledger.PublishForAssociation(assoc, Tap, "ST",
            "6", "", "", "Report"));
        Assert.True(ledger.TryGet(Q, "ST", out var observedQ));
        Assert.Equal(assoc, observedQ.Association);
        Assert.NotNull(observedQ.QualityBits);
        Assert.Equal("Report quality · raw MMS", observedQ.Source);
        Assert.True(ledger.MergeVerifiedReportCompanionForAssociation(
            assoc, Tap, "ST", qualityEvidence: observedQ.QualityBits));
        Assert.True(ledger.TryGet(Tap, "ST", out var process));
        Assert.Equal("Good", process.Quality);
        Assert.Equal("6", process.Value);
    }

    [Fact]
    public void TwoDifferentIedsWithIdenticalReferenceAndFcNeverShareCompanionEvidence()
    {
        // One canonical ledger per device. Equal IEC references and FC are
        // NOT cross-IED permissions, even with coincident association numbers.
        var deviceA = new IedBrowserLiveValues();
        var deviceB = new IedBrowserLiveValues();
        var a = deviceA.BeginAssociation();
        var b = deviceB.BeginAssociation();
        Assert.Equal(a, b);
        Assert.True(deviceA.PublishForAssociation(a, Tap, "ST",
            "6", "", "", "Report"));
        Assert.True(deviceB.PublishForAssociation(b, Tap, "ST",
            "12", "", "", "Report"));
        Assert.True(deviceA.MergeVerifiedReportCompanionForAssociation(
            a, Tap, "ST", qualityEvidence: Quality(0)));
        Assert.True(deviceA.TryGet(Tap, "ST", out var known));
        Assert.True(deviceB.TryGet(Tap, "ST", out var unverified));
        Assert.Equal("Good", known.Quality);
        Assert.Equal("6", known.Value);
        Assert.True(string.IsNullOrEmpty(unverified.Quality));
        Assert.Equal("12", unverified.Value);
        // Other device does not upgrade merely because a peer received q.
        Assert.Null(unverified.QualityBits);
    }
}
