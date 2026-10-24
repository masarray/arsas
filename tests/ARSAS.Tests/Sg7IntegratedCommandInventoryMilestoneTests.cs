using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// SG7_BCU exact command-inventory regression. Visibility/ranking uses typed
/// engineering-service evidence; command admission requires independent live
/// semantic proof. Never synthesizes or transmits any physical Operate.
/// </summary>
public sealed class Sg7IntegratedCommandInventoryMilestoneTests
{
    private static SignalDefinition Candidate(string reference, string cdc,
        bool coEvidence = true) => new()
    {
        ObjectReference = reference,
        Name = reference,
        FunctionalConstraint = "CO",
        IsControlSignal = true,
        HasControlServiceEvidence = coEvidence,
        ControlCdc = cdc,
        ControlModelText = "Auto-detect"
    };

    [Fact]
    public void FourSg7CswiRemainVisibleBeforeTypeVerification_WithoutOperate()
    {
        var genericBank = Enumerable.Range(1, 200)
            .Select(i => Candidate($"SG7_BCUMaster/GGIO2.SPCSO{i:000}", "SPC"))
            .ToArray();
        var cswi = Enumerable.Range(1, 4)
            .Select(i => Candidate($"SG7_BCUSystem/BkrCSWI{i}.Pos", i == 2 ? "" : "DPC"))
            .ToArray();
        var breakerFeedback = Candidate("SG7_BCUSystem/Bkr0XCBR1.Pos", "DPC");
        breakerFeedback.ControlModelText = "Status only";
        var device = new Iec61850MonitorDevice
        {
            IsConnected = true,
            LiveDiscoveryModel = new LiveIedModelDiscoveryDocument()
        };

        device.Signals.ReplaceAll(genericBank.Concat([breakerFeedback]).Concat(cswi));
        device.RecountSelectedSignals();

        Assert.Equal(cswi, device.CommandSignals.Take(4));
        Assert.All(cswi, candidate =>
        {
            Assert.Equal(0, Iec61850ControlIdentity.CommandDockPriority(candidate));
            Assert.Contains("verification required",
                Iec61850ControlIdentity.CommandDockRole(candidate));
            Assert.False(candidate.ControlSupportsOperate);
            Assert.DoesNotContain(candidate, device.OperableCommandSignals);
        });
        Assert.All(device.CommandSignals.Skip(4), candidate =>
            Assert.NotEqual("CSWI", candidate.LogicalNodeClass));
        Assert.Empty(device.OperableCommandSignals);

        // Read-only status/quality changes cannot cause row collection churn,
        // fabricate semantic verification or convert a candidate into Operate.
        var first = device.CommandSignals.ToArray();
        for (var n = 0; n < 80; n++)
        {
            cswi[0].ControlCurrentValue = n % 2 == 0 ? "Open" : "Closed";
            device.RefreshCommandSignalProjection();
        }
        Assert.True(first.SequenceEqual(device.CommandSignals));
        Assert.All(cswi, candidate => Assert.False(candidate.ControlSupportsOperate));
    }

    [Fact]
    public void SameCswiPromotesOnlyAfterVerifiedLiveCdcAndSemanticProof()
    {
        var cswi = Candidate("SG7_BCUSystem/BkrCSWI1.Pos", "DPC");
        Assert.Equal(0, Iec61850ControlIdentity.CommandDockPriority(cswi));
        Assert.Contains("verification required", Iec61850ControlIdentity.CommandDockRole(cswi));
        cswi.ControlModelText = "Select Before Operate (SBO) • Enhanced security";
        Assert.False(cswi.ControlSupportsOperate);
        cswi.AcceptVerifiedLiveControlCdc("DPC");
        Assert.False(cswi.ControlSupportsOperate);
        cswi.ControlSemanticVerified = true;
        Assert.True(cswi.ControlSupportsOperate);
        Assert.Equal("Switch controller · CSWI",
            Iec61850ControlIdentity.CommandDockRole(cswi));
        Assert.Same(cswi, Assert.Single(
            Iec61850ControlIdentity.VerifiedCommandTargets([cswi], admitDiscovered: true)));
    }

    [Fact]
    public void UnprovenNameOnlyCswiCannotGainCommandDockPriority()
    {
        var spoof = Candidate("SG7_BCUSystem/BkrCSWI1.Pos", "DPC", coEvidence: false);
        Assert.NotEqual(0, Iec61850ControlIdentity.CommandDockPriority(spoof));
        Assert.False(spoof.ControlSupportsOperate);
        Assert.Empty(Iec61850ControlIdentity.DiscoveredIndependentCswiControls([spoof]));
        Assert.Empty(Iec61850ControlIdentity.VerifiedCommandTargets([spoof], true));
    }

    [Fact]
    public void ScalarBooleanOperCtlValIsNotSufficientToPromoteDpcAdmission()
    {
        var candidate = Candidate("SG7_BCUSystem/BkrCSWI1.Pos", "DPC");
        candidate.ControlModelText = "Select Before Operate (SBO) • Enhanced security";
        // A Boolean CO Oper.ctlVal is a payload shape, not a verified DPC CDC.
        candidate.AcceptVerifiedLiveControlCdc("SPC");
        Assert.False(candidate.ControlSupportsOperate);
        Assert.Empty(Iec61850ControlIdentity.VerifiedCommandTargets([candidate], true));
        Assert.Equal(0, Iec61850ControlIdentity.CommandDockPriority(candidate));
        Assert.Contains("verification required", Iec61850ControlIdentity.CommandDockRole(candidate));
    }
}
