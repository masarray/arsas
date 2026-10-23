using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class IedBrowserControlEligibilityTests
{
    private const string Pos = "SG7_BCULD0/BkrCSWI1.Pos";

    private static LiveIedDataObjectModel DataObject(string reference = Pos, string cdc = "DPC")
        => new() { Reference = reference, Name = "Pos", InferredCdc = cdc };

    private static Iec61850ControlCapabilities Capability(
        string reference = Pos, Iec61850ControlModelKind model = Iec61850ControlModelKind.SboEnhanced,
        string cdc = "DPC", bool ready = true)
        => new()
        {
            ObjectReference = reference,
            ControlModelReference = reference + ".ctlModel",
            StatusReference = reference + ".stVal",
            ControlModel = model,
            ControlCdc = cdc,
            CtlValSignature = "oper.ctlVal:BOOLEAN (live GVA verified)",
            EngineControlServiceAvailable = true,
            IsOperationallyReady = ready
        };

    private static IedBrowserControlEligibility Decide(
        bool focusDo = true, string focus = Pos,
        LiveIedDataObjectModel? declared = null,
        Iec61850ControlCapabilities? capability = null,
        bool association = true, bool authorized = true)
        => IedBrowserControlEligibility.Check(
            focusDo, focus, declared ?? DataObject(), capability ?? Capability(),
            association, authorized);

    [Fact]
    public void ExactLiveTypedDpcSboEnhanced_IsEnabledWithoutNameBasedShortcuts()
        => Assert.True(Decide().CanControl);

    [Fact]
    public void DataAttributeFocus_CannotEnableControl()
        => Assert.False(Decide(focusDo: false).CanControl);

    [Fact]
    public void OrdinaryStatusOnlyModel_IsDisabled()
    {
        var decision = Decide(capability: Capability(model: Iec61850ControlModelKind.StatusOnly));
        Assert.False(decision.CanControl);
        Assert.Contains("StatusOnly", decision.Reason);
    }

    [Fact]
    public void UnknownOrUnreadyControl_IsDisabled()
    {
        Assert.False(Decide(capability: Capability(model: Iec61850ControlModelKind.Unknown)).CanControl);
        Assert.False(Decide(capability: Capability(ready: false)).CanControl);
    }

    [Fact]
    public void CrossObjectOrStaleAssociation_CannotEnableControl()
    {
        Assert.False(Decide(capability: Capability("SG7_BCULD0/BkrCSWI2.Pos")).CanControl);
        Assert.False(Decide(declared: DataObject("SG7_BCULD0/LLN0.NamPlt")).CanControl);
        Assert.False(Decide(association: false).CanControl);
        Assert.False(Decide(authorized: false).CanControl);
    }

    [Fact]
    public void UnsupportedOrContradictoryCdc_BlocksWithoutGuessing()
    {
        Assert.False(Decide(capability: Capability(cdc: "UNKNOWN")).CanControl);
        Assert.False(Decide(declared: DataObject(cdc: "SPS")).CanControl);
    }

    [Fact]
    public void IncompleteTypedCtlValOrCtlModel_StaysDisabled()
    {
        var incomplete = new Iec61850ControlCapabilities
        {
            ObjectReference = Pos,
            ControlModel = Iec61850ControlModelKind.SboEnhanced,
            ControlCdc = "DPC",
            EngineControlServiceAvailable = true,
            IsOperationallyReady = true,
            StatusReference = Pos + ".stVal"
        };
        Assert.False(Decide(capability: incomplete).CanControl);
    }

    [Fact]
    public void VendorNeutralTapChangerBsc_UsesLiveTypingNotPosLabel()
    {
        const string tap = "ZIVAVRLD1/ATCC1.TapChg";
        var decision = IedBrowserControlEligibility.Check(
            true, tap, DataObject(tap, "BSC"),
            Capability(tap, Iec61850ControlModelKind.SboNormal, "BSC"), true, true);
        Assert.True(decision.CanControl);
    }
}
