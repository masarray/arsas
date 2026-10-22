using AR.Iec61850.Control;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Synthetic IEC 61850 Edition 2 LN prefix cases from the SG7 BCU IID.
/// No network, MMS Write, Select or Operate is performed by these tests.
/// </summary>
public sealed class GeEd2PrefixedLnDpcCommandSemanticTests
{
    [Theory]
    [InlineData("SG7_BCUSystem/Bkr0XCBR2.Pos")]
    [InlineData("SG7_BCUSystem/Disc0XSWI1.Pos")]
    [InlineData("SG7_BCUSystem/BkrCSWI1.Pos")]
    [InlineData("LD/CSWI1.Pos")]
    public void LiveBooleanCtlValWithVerifiedDpcPosition_AdmitsCommandSemantics(
        string reference)
    {
        var descriptor = NewDescriptor(reference, "SPC", "boolean",
            reference + ".stVal");
        var resolved = Iec61850ControlSemanticResolver.Resolve(
            descriptor, "DPC", reference + ".stVal");
        Assert.True(resolved.IsCommandSafe, resolved.Reason);
        Assert.Equal("DPC", resolved.Cdc);
        var signal = new SignalDefinition
        {
            ObjectReference = reference, Name = "Breaker position",
            FunctionalConstraint = "CO", IsControlSignal = true,
            ControlCdc = "DPC", ControlModelText = "SBO • Enhanced security"
        };
        Assert.True(signal.IsPositionControl);
        Assert.False(signal.ControlSupportsOperate); // No operator authority from SCL alone.
        signal.AcceptVerifiedLiveControlCdc(resolved.Cdc);
        signal.ControlSemanticVerified = resolved.IsCommandSafe;
        Assert.True(signal.ControlSupportsOperate);
        Assert.Equal("DPC", signal.VerifiedLiveControlCdc);
    }

    [Theory]
    [InlineData("SG7_BCUSystem/Bkr0XCBR2.Bad")]
    [InlineData("SG7_BCUSystem/Bkr0XCBR.Pos")]
    [InlineData("SG7_BCUSystem/Bkr0XCBR2.PosExtra")]
    [InlineData("SG7_BCUSystem/OTHER2.Pos")]
    public void ArbitraryObjectsCannotGainDpcAuthority(string reference)
    {
        var descriptor = NewDescriptor(reference, "SPC", "boolean",
            reference + ".stVal");
        Assert.False(Iec61850ControlSemanticResolver.Resolve(
            descriptor, "DPC").IsCommandSafe);
    }

    [Fact]
    public void CrossObjectStatusOrBadCtlValRemainsDenied()
    {
        const string reference = "SG7_BCUSystem/Bkr0XCBR2.Pos";
        Assert.False(Iec61850ControlSemanticResolver.Resolve(
            NewDescriptor(reference, "SPC", "boolean",
                "SG7_BCUSystem/Bkr0XCBR3.Pos.stVal"), "DPC").IsCommandSafe);
        Assert.False(Iec61850ControlSemanticResolver.Resolve(
            NewDescriptor(reference, "SPC", "integer",
                reference + ".stVal"), "DPC").IsCommandSafe);
    }

    private static Iec61850ControlObjectDescriptor NewDescriptor(
        string reference, string inferredCdc, string ctlValType, string status)
        => new()
        {
            ObjectReference = reference,
            Cdc = inferredCdc,
            ControlModel = Iec61850ControlModel.SelectBeforeOperateEnhanced,
            CtlValSpecification = new MmsTypeSpecificationNode
                { Name = "ctlVal", MmsType = ctlValType },
            OperSpecification = new MmsTypeSpecificationNode
            {
                MmsType = "structure", Children =
                [new MmsTypeSpecificationNode
                    { Name = "ctlVal", MmsType = ctlValType }]
            },
            SelectWithValueSpecification = new MmsTypeSpecificationNode
                { MmsType = "structure" },
            CancelSpecification = new MmsTypeSpecificationNode
                { MmsType = "structure" },
            StatusReference = status,
            SupportsCommandTermination = true
        };
}
