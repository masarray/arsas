using AR.Iec61850.Control;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Consumer + pinned engine contract: a saved/static SPC label must never
/// reclassify a typed, same-object BSC step command as Boolean.
/// No connection, MMS write or control Operate is performed by these tests.
/// </summary>
public sealed class P109VLiveBscAuthorityRegressionTests
{
    [Fact]
    public void SavedSpcToTypedLiveBsc_RefreshReportsReconnectAndSclReopen_StayDirectional()
    {
        const string reference = "ZIVAVRLD1/ATCC1.TapChg";
        const string feedback = reference + ".valWTr.posVal";
        var descriptor = Descriptor(reference, "BSC", "bit-string", 2, feedback);

        SignalDefinition FromSavedScl() => new()
        {
            Name = "TapChg", ObjectReference = reference, FunctionalConstraint = "CO",
            IsControlSignal = true, IsSelected = true, ControlCdc = "SPC",
            ControlStatusReference = feedback
        };

        var tap = FromSavedScl();
        VerifyAfterLiveInspect(tap, descriptor);
        Assert.True(tap.ControlSupportsOperate);
        Assert.True(tap.IsRaiseLowerControl);
        Assert.False(tap.IsBooleanControl);
        Assert.True(tap.TryStageControlConfirmation("Raise", "Raise", out _));
        tap.ClearControlConfirmation();

        // A late RCB/static DataSet projection cannot mutate verified command CDC.
        Assert.False(tap.ApplyDataSetAdvisoryControlMetadata("SPC", feedback));
        Assert.Equal("BSC", tap.ControlCdc);
        Assert.True(tap.IsRaiseLowerControl);
        Assert.False(tap.IsBooleanControl);

        // Refresh discards previous live authority and constructs a fresh
        // descriptor, without silently relying on the previous control session.
        tap.ClearLiveControlAuthority();
        tap.ControlModelText = "Auto-detect";
        Assert.False(tap.ControlSupportsOperate);
        VerifyAfterLiveInspect(tap, descriptor);

        // Reconnect again revokes authorization before reinspection.
        tap.ClearLiveControlAuthority();
        tap.ControlModelText = "Auto-detect";
        Assert.False(tap.ControlSupportsOperate);
        VerifyAfterLiveInspect(tap, descriptor);

        // A reopened SCL starts with SPC again; the same verified MMS
        // evidence must recover BSC instead of falling into a false/true loop.
        var reopened = FromSavedScl();
        VerifyAfterLiveInspect(reopened, descriptor);
        Assert.True(reopened.IsRaiseLowerControl);
        Assert.False(reopened.IsBooleanControl);
    }

    [Fact]
    public void WrongTypedStatusCannotPromoteSpcOrAuthorizeBooleanOperate()
    {
        const string reference = "ZIVAVRLD1/ATCC1.TapChg";
        var signal = new SignalDefinition
        {
            Name = "TapChg", ObjectReference = reference,
            FunctionalConstraint = "CO", IsControlSignal = true, ControlCdc = "SPC"
        };
        signal.ControlModelText = "Direct Operate (DO) • Normal security";
        var invalid = Descriptor(reference, "BSC", "bit-string", 1,
            reference + ".valWTr.posVal");
        var resolution = Iec61850ControlSemanticResolver.Resolve(invalid, signal.ControlCdc);
        Assert.False(resolution.IsCommandSafe);
        signal.ControlSemanticVerified = resolution.IsCommandSafe;
        Assert.False(signal.ControlSupportsOperate);
        Assert.False(signal.TryStageControlConfirmation("Raise", "Raise", out _));
    }

    [Theory]
    [InlineData("CSWI1", "DPC", "bit-string", 2)]
    [InlineData("CSWI2", "SPC", "boolean", 0)]
    public void ExistingPositionControlsPreserveOpenCloseSemanticFamily(
        string ln, string descriptorCdc, string wireType, int width)
    {
        var reference = $"ZIVAVRLD1/{ln}.Pos";
        var signal = new SignalDefinition
        {
            Name = "Pos", ObjectReference = reference, FunctionalConstraint = "CO",
            IsControlSignal = true, ControlCdc = "DPC"
        };
        var descriptor = Descriptor(reference, descriptorCdc, wireType, width,
            reference + ".stVal");
        var resolution = Iec61850ControlSemanticResolver.Resolve(
            descriptor, signal.ControlCdc, signal.ControlStatusReference);
        Assert.True(resolution.IsCommandSafe);
        Assert.Equal("DPC", resolution.Cdc);
        signal.AcceptVerifiedLiveControlCdc(resolution.Cdc);
        signal.ControlModelText = "Direct Operate (DO) • Normal security";
        signal.ControlSemanticVerified = true;
        Assert.True(signal.IsPositionControl);
        Assert.False(signal.IsRaiseLowerControl);
        Assert.True(signal.ControlSupportsOperate);
    }

    private static void VerifyAfterLiveInspect(
        SignalDefinition signal, Iec61850ControlObjectDescriptor descriptor)
    {
        var semantic = Iec61850ControlSemanticResolver.Resolve(
            descriptor, signal.ControlCdc, signal.ControlStatusReference);
        Assert.True(semantic.IsCommandSafe);
        Assert.Equal("BSC", semantic.Cdc);
        signal.AcceptVerifiedLiveControlCdc(semantic.Cdc);
        signal.ControlSemanticVerified = semantic.IsCommandSafe;
        signal.ControlModelText = "Direct Operate (DO) • Normal security";
        Assert.Equal("BSC", signal.VerifiedLiveControlCdc);
        Assert.True(signal.IsRaiseLowerControl);
        Assert.False(signal.IsBooleanControl);
    }

    private static Iec61850ControlObjectDescriptor Descriptor(
        string reference, string cdc, string wireType, int width, string feedback) => new()
    {
        ObjectReference = reference, Cdc = cdc,
        ControlModel = Iec61850ControlModel.DirectNormal,
        CtlValSpecification = new MmsTypeSpecificationNode
        {
            Name = "ctlVal", MmsType = wireType, Size = width == 0 ? null : width
        },
        OperSpecification = new MmsTypeSpecificationNode { MmsType = "structure" },
        StatusReference = feedback,
        // DPC can expose Boolean CO. A live TWO-BIT status read, not the
        // CSWI name or a stale SCL label, must corroborate this one fixture.
        LiveStatusIsDoublePoint =
            cdc == "SPC" && wireType == "boolean" &&
            feedback.Equals(reference + ".stVal", StringComparison.Ordinal)
    };
}
