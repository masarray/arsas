using ArIED61850Tester.Models.IoTesting;

namespace ARSAS.Tests;

public sealed class IoSignalDisplayNameTests
{
    [Theory]
    [InlineData("IED1MEAS/MMXU1.A.phsA.cVal.mag.f", "A Phs A")]
    [InlineData("IED1MEAS/MMXU1.A.phsB.cVal.mag.f", "A Phs B")]
    [InlineData("IED1MEAS/MMXU1.A.phsC.cVal.mag.f", "A Phs C")]
    public void Format_AppendsPhaseContextWithoutAddingMeasurementOwner(string reference, string expected)
    {
        Assert.Equal(expected, IoSignalDisplayName.Format("A", reference));
    }

    [Theory]
    [InlineData("IED1CTRL/XCBR1.Pos.stVal", "XCBR Pos")]
    [InlineData("IED1CTRL/CSWI1.Pos.stVal", "CSWI Pos")]
    [InlineData("IED1CTRL/XCBR1$ST$Pos$stVal", "XCBR Pos")]
    public void Format_QualifiesAmbiguousDataObjectWithLogicalNodeOwner(string reference, string expected)
    {
        Assert.Equal(expected, IoSignalDisplayName.Format("Pos", reference));
    }

    [Theory]
    [InlineData("Mod", "IED1CTRL/XCBR1.Mod.stVal", "XCBR Mod")]
    [InlineData("Beh", "IED1CTRL/CSWI1.Beh.stVal", "CSWI Beh")]
    [InlineData("Health", "IED1CTRL/LPHD1.Health.stVal", "LPHD Health")]
    [InlineData("Loc", "IED1CTRL/CSWI1.Loc.stVal", "CSWI Loc")]
    [InlineData("OpCnt", "IED1CTRL/XCBR1.OpCnt.stVal", "XCBR OpCnt")]
    public void Format_UsesSameOwnerRuleForOtherGenericIecDataObjects(
        string signalName,
        string reference,
        string expected)
    {
        Assert.Equal(expected, IoSignalDisplayName.Format(signalName, reference));
    }

    [Fact]
    public void Format_DoesNotDuplicateExistingOwnerContext()
    {
        Assert.Equal(
            "XCBR Pos",
            IoSignalDisplayName.Format("XCBR Pos", "IED1CTRL/XCBR1.Pos.stVal"));
    }

    [Fact]
    public void Format_LeavesSpecificDataObjectNameConcise()
    {
        Assert.Equal(
            "Dig01",
            IoSignalDisplayName.Format("Dig01", "IED1ADD/GGIO1.Dig01.stVal"));
    }

    [Fact]
    public void Format_LeavesCustomLabelUntouched()
    {
        Assert.Equal(
            "Breaker position",
            IoSignalDisplayName.Format("Breaker position", "IED1CTRL/XCBR1.Pos.stVal"));
    }

    [Fact]
    public void Format_FallsBackGracefullyWithoutReference()
    {
        Assert.Equal("Pos", IoSignalDisplayName.Format("Pos", null));
    }
}
