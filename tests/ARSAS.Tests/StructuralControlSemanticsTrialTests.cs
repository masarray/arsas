using AR.Iec61850.Control;
using AR.Iec61850.Mms;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Edition-independent typed control proof. The LN name is never permission:
/// operative ctlModel, typed CO Oper, exact ST double-point and enhanced SBO
/// prerequisites jointly determine eligibility. No MMS command is emitted.
/// </summary>
public sealed class StructuralControlSemanticsTrialTests
{
    private static Iec61850ControlObjectDescriptor Candidate(
        string reference, bool typedTwoBitStatus = true,
        string? status = null,
        bool withSbo = true,
        Iec61850ControlModel model = Iec61850ControlModel.SelectBeforeOperateEnhanced)
        => new()
        {
            ObjectReference = reference,
            Cdc = typedTwoBitStatus ? "DPC" : "SPC",
            ControlModel = model,
            CtlValSpecification = new MmsTypeSpecificationNode
                { Name = "ctlVal", MmsType = "boolean" },
            OperSpecification = new MmsTypeSpecificationNode
                { MmsType = "structure" },
            StatusReference = status ?? reference + ".stVal",
            StatusFunctionalConstraint = "ST",
            LiveStatusIsDoublePoint = typedTwoBitStatus,
            SelectWithValueSpecification = withSbo
                ? new MmsTypeSpecificationNode { MmsType = "structure" } : null,
            CancelSpecification = withSbo
                ? new MmsTypeSpecificationNode { MmsType = "structure" } : null,
            SupportsCommandTermination = true
        };

    [Theory]
    [InlineData("SG7_BCUSystem/Bkr0XCBR1.Pos")]
    [InlineData("SG7_BCUSystem/BkrCSWI1.Pos")]
    [InlineData("SG7_BCUSystem/Disc0XSWI1.Pos")]
    [InlineData("IED/ArbitraryNode99.SwitchPoint")]
    public void TypedControl_IsAuthorizedByLiveStructureNotLnName(string reference)
    {
        var descriptor = Candidate(reference);
        var resolution = Iec61850ControlSemanticResolver.Resolve(descriptor, "DPC");
        Assert.True(resolution.IsCommandSafe, resolution.Reason);
        Assert.Equal("DPC", resolution.Cdc);
        Assert.True(NativeIec61850Client.HasExactDpcBooleanFeedback(
            descriptor, resolution.Cdc));
    }

    [Fact]
    public void ControllableNameWithoutTwoBitStatusCannotAuthorizeDpc()
    {
        const string reference = "SG7_BCUSystem/BkrCSWI1.Pos";
        var descriptor = Candidate(reference, typedTwoBitStatus: false);
        Assert.False(Iec61850ControlSemanticResolver.Resolve(descriptor, "DPC").IsCommandSafe);
    }

    [Fact]
    public void EnhancedSboMissingLiveSelectOrCancelNeverOperable()
    {
        var descriptor = Candidate("SG7_BCUSystem/Bkr0XCBR1.Pos", withSbo: false);
        Assert.False(Iec61850ControlSemanticResolver.Resolve(descriptor, "DPC").IsCommandSafe);
    }

    [Fact]
    public void CrossObjectStatusAndStatusOnlyRemainBlocked()
    {
        var reference = "SG7_BCUSystem/Bkr0XCBR1.Pos";
        var cross = Candidate(reference,
            status: "SG7_BCUSystem/Bkr0XCBR2.Pos.stVal");
        Assert.False(Iec61850ControlSemanticResolver.Resolve(cross, "DPC").IsCommandSafe);
        Assert.False(NativeIec61850Client.HasExactDpcBooleanFeedback(cross, "DPC"));
        var readOnly = Candidate(reference, model: Iec61850ControlModel.StatusOnly);
        Assert.False(Iec61850ControlSemanticResolver.Resolve(readOnly, "DPC").IsCommandSafe);
    }
}
