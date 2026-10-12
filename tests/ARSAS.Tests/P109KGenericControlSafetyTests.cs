using AR.Iec61850.Control;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P109KGenericControlSafetyTests
{
    [Fact]
    public void ControlModelAloneNeverEnablesUnverifiedCommand()
    {
        var signal = new SignalDefinition { IsControlSignal = true, ObjectReference = "LD/CSWI1.Pos" };
        Assert.False(signal.ControlSupportsOperate);
        Assert.False(signal.ControlSemanticVerified);
    }

    [Theory]
    [InlineData("BSC", true, false)]
    [InlineData("INC", false, true)]
    [InlineData("ISC", false, true)]
    public void DistinctCdcOperatorActions(string cdc, bool directional, bool numeric)
    {
        var signal = new SignalDefinition {
            Name = "Control", ObjectReference = "LD/ATCC1.Command",
            FunctionalConstraint = "CO", IsControlSignal = true, ControlCdc = cdc
        };
        Assert.Equal(directional, signal.IsRaiseLowerControl);
        Assert.Equal(numeric, signal.IsSetPointControl);
    }

    [Fact]
    public void ClaimedBscCannotOverrideLiveDpc()
    {
        var descriptor = new Iec61850ControlObjectDescriptor {
            Cdc = "DPC", ObjectReference = "LD/ATCC1.TapChg",
            StatusReference = "LD/ATCC1.TapChg.stVal",
            ControlModel = Iec61850ControlModel.DirectNormal,
            CtlValSpecification = new MmsTypeSpecificationNode { MmsType = "bit-string", Size = 2 }
        };
        Assert.False(Iec61850ControlSemanticResolver.Resolve(descriptor, "BSC").IsCommandSafe);
    }
}
