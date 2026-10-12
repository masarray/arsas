using AR.Iec61850.Control;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ZivBscStepControlRegressionTests
{
    [Theory]
    [InlineData("raise", 2L)]
    [InlineData("higher", 2L)]
    [InlineData("up", 2L)]
    [InlineData("lower", 1L)]
    [InlineData("down", 1L)]
    public void StandardBscDirection_ProducesExactTcmd_NotDpc(string input, long expectedCode)
    {
        Assert.True(Iec61850BscStepControlPolicy.TryBind(
            "bit-string", 2, input, out var command, out var expected, out var error), error);
        Assert.Equal(Iec61850ControlValueKind.Integer, command!.Kind);
        Assert.Equal(expectedCode, command.Value);
        Assert.Equal(expectedCode == 2 ? "Raise" : "Lower", expected);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("close")]
    [InlineData("12")]
    [InlineData("-40")]
    [InlineData("stop")]
    [InlineData("3")]
    [InlineData("reserved")]
    public void RejectsUnqualifiedBscCommands_BeforeWire(string input)
    {
        Assert.False(Iec61850BscStepControlPolicy.TryBind(
            "bit-string", 2, input, out var value, out _, out var error));
        Assert.Null(value);
        Assert.Contains("only Raise or Lower", error);
    }

    [Theory]
    [InlineData("bit-string", 3)]
    [InlineData("structure", 2)]
    [InlineData("boolean", 0)]
    public void UnsupportedLiveShape_FailsClosed(string type, int bits)
    {
        Assert.False(Iec61850BscStepControlPolicy.TryBind(
            type, bits, "raise", out _, out _, out var error));
        Assert.Contains("not safely supported", error);
    }

    [Fact]
    public void FeedbackRequiresExactDirectionalNumericMovement()
    {
        Assert.True(Iec61850BscStepControlPolicy.MovementConfirmed("Raise", "12", "13"));
        Assert.True(Iec61850BscStepControlPolicy.MovementConfirmed("Lower", "12", "11"));
        Assert.False(Iec61850BscStepControlPolicy.MovementConfirmed("Raise", "12", "12"));
        Assert.False(Iec61850BscStepControlPolicy.MovementConfirmed("Raise", "Unknown", "13"));
        Assert.False(Iec61850BscStepControlPolicy.MovementConfirmed("Lower", "12", "bad"));
        Assert.False(Iec61850BscStepControlPolicy.MovementConfirmed("Lower", "12", "13"));
    }

    [Fact]
    public void BscShowsRaiseLowerNotSwitchNorSetpoint()
    {
        var signal = new SignalDefinition
        {
            Name = "TapChg",
            ObjectReference = "ZIVAVRLD1/ATCC1.TapChg",
            FunctionalConstraint = "CO",
            DataType = "BSC",
            Category = "Control",
            ControlCdc = "BSC",
            ControlStatusReference = "ZIVAVRLD1/ATCC1.TapChg.valWTr.posVal",
            IsControlSignal = true
        };
        Assert.True(signal.IsRaiseLowerControl);
        Assert.False(signal.IsPositionControl);
        Assert.False(signal.IsSetPointControl);
        Assert.Contains("Raise / Lower", signal.ControlActionLabel);
    }
}
