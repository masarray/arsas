using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeInvestigationTimelineMathTests
{
    [Fact]
    public void TriggerAnchoredTicks_AlwaysContainExactZeroWhenTriggerVisible()
    {
        var ticks = ComtradeInvestigationTimelineMath.BuildTriggerAnchoredTicks(52.0, 212.0, 124.0, 8);

        Assert.Contains(ticks, tick => tick.RelativeMilliseconds == 0.0 && tick.AbsoluteMilliseconds == 124.0);
    }

    [Fact]
    public void TriggerAnchoredTicks_AreSymmetricEngineeringStepsAroundTrigger()
    {
        var ticks = ComtradeInvestigationTimelineMath.BuildTriggerAnchoredTicks(40.0, 200.0, 120.0, 8);
        var relative = ticks.Select(tick => tick.RelativeMilliseconds).ToArray();

        Assert.Contains(-80.0, relative);
        Assert.Contains(-60.0, relative);
        Assert.Contains(0.0, relative);
        Assert.Contains(60.0, relative);
        Assert.Contains(80.0, relative);
    }

    [Fact]
    public void NiceStep_UsesEngineeringFriendlySequence()
    {
        Assert.Equal(0.5, ComtradeInvestigationTimelineMath.NiceStep(0.41), 8);
        Assert.Equal(2.5, ComtradeInvestigationTimelineMath.NiceStep(2.2), 8);
        Assert.Equal(20.0, ComtradeInvestigationTimelineMath.NiceStep(17.0), 8);
        Assert.Equal(50.0, ComtradeInvestigationTimelineMath.NiceStep(41.0), 8);
    }

    [Fact]
    public void ClampToRecord_ClampsCursorWithoutChangingFiniteInteriorValue()
    {
        Assert.Equal(10.0, ComtradeInvestigationTimelineMath.ClampToRecord(10.0, 0.0, 100.0), 8);
        Assert.Equal(0.0, ComtradeInvestigationTimelineMath.ClampToRecord(-2.0, 0.0, 100.0), 8);
        Assert.Equal(100.0, ComtradeInvestigationTimelineMath.ClampToRecord(120.0, 0.0, 100.0), 8);
    }
}
