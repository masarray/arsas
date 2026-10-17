using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214BrowserReadFailureEvidenceTests
{
    private static Iec61850MonitorDevice Device() => new()
    {
        IsConnected = true
    };

    [Fact]
    public void ExactFailedSgLeafDisplaysRealReasonAndNeverBorrowOtherFc()
    {
        var device = Device();
        var failures = new IedBrowserReadFailureEvidence(device);
        failures.RecordFailure(new IedBrowserReadTarget(
            "LD/SGgndPIOC1$SG$InMagTyp$setVal", "SG", "Int32"),
            "IED returned object-non-existent");
        Assert.True(failures.TryGet(device, "LD/SGgndPIOC1.InMagTyp.setVal",
            "", "SG", out var reason));
        Assert.Contains("object-non-existent", reason);
        Assert.False(failures.TryGet(device, "LD/SGgndPIOC1.InMagTyp.setVal",
            "", "SE", out _));
        Assert.False(failures.TryGet(device, "LD/SGgndPIOC2.InMagTyp.setVal",
            "", "SG", out _));
    }

    [Fact]
    public void SuccessClearsPreviousFailure_AndOnlyDeclaredFallbackCanMatch()
    {
        var device = Device();
        var failures = new IedBrowserReadFailureEvidence(device);
        var target = new IedBrowserReadTarget("LD/ATCC1$SG$BndWid$setVal",
            "SG", "Float32");
        failures.RecordFailure(target, "Read timeout");
        Assert.False(failures.TryGet(device, "LD/ATCC1.Other",
            "", "SG", out _));
        Assert.True(failures.TryGet(device, "LD/ATCC1.Other",
            "LD/ATCC1.BndWid.setVal", "SG", out var reason));
        Assert.Equal("Read timeout", reason);
        failures.ClearFailure(target);
        Assert.False(failures.TryGet(device, target.Reference, "", "SG", out _));
    }

    [Fact]
    public void ReconnectSchemaReplacementAndOtherIed_InvalidateAllFailureEvidence()
    {
        var device = Device();
        var failures = new IedBrowserReadFailureEvidence(device);
        const string reference = "LD/ATCC1.BndWid.setVal";
        failures.RecordFailure(new(reference, "SG", "Float32"), "Access denied");
        Assert.False(failures.TryGet(Device(), reference, "", "SG", out _));
        device.LiveValues.BeginAssociation();
        Assert.False(failures.TryGet(device, reference, "", "SG", out _));
        var current = new IedBrowserReadFailureEvidence(device);
        current.RecordFailure(new(reference, "SG", "Float32"), "Access denied");
        device.LiveDiscoveryModel = new AR.Iec61850.Discovery.LiveIedModelDiscoveryDocument();
        Assert.False(current.TryGet(device, reference, "", "SG", out _));
    }

    [Fact]
    public void DeferredBudgetIsNotMisreportedAsMmsFailureAndCannotEraseWireFailure()
    {
        var device = Device();
        var absences = new IedBrowserReadFailureEvidence(device);
        var target = new IedBrowserReadTarget(
            "LD/MMXU1$MX$PhV$phsA$q", "MX", "Quality");

        absences.RecordDeferred(target, "Not attempted: initial 10-second budget exhausted");
        Assert.True(absences.TryGet(device, "LD/MMXU1.PhV.phsA.q", "",
            "MX", out var reason, out var kind));
        Assert.Equal(IedBrowserReadAbsenceKind.Deferred, kind);
        Assert.Contains("Not attempted", reason);
        Assert.Equal(IedBrowserReadAbsenceKind.Deferred,
            Assert.Single(absences.SnapshotCurrent(device)).Kind);

        absences.RecordFailure(target, "IED data-access-error: object-non-existent");
        absences.RecordDeferred(target, "Not attempted: later budget exhausted");
        Assert.True(absences.TryGet(device, target.Reference, "",
            "MX", out reason, out kind));
        Assert.Equal(IedBrowserReadAbsenceKind.Failed, kind);
        Assert.Contains("object-non-existent", reason);
        Assert.False(absences.TryGet(device, target.Reference, "",
            "ST", out _, out _));

        absences.ClearFailure(target);
        Assert.False(absences.TryGet(device, target.Reference, "",
            "MX", out _, out _));
        absences.RecordDeferred(target, "Not attempted: initial budget exhausted");
        device.LiveValues.BeginAssociation();
        Assert.False(absences.TryGet(device, target.Reference, "",
            "MX", out _, out _));
    }

    [Fact]
    public void MissingAndOversizedFailuresRemainBoundedAndNeverLookLikeValues()
    {
        var device = Device();
        var failures = new IedBrowserReadFailureEvidence(device);
        var target = new IedBrowserReadTarget("LD/X1.stVal", "ST", "Boolean");
        failures.RecordFailure(target, " ");
        Assert.False(failures.TryGet(device, target.Reference, "", "ST", out _));
        failures.RecordFailure(target, new string('x', 1000));
        Assert.True(failures.TryGet(device, target.Reference, "", "ST", out var reason));
        Assert.True(reason.Length <= 321);
    }
}
