using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class AlarmAnnunciatorStateMachineTests
{
    [Fact]
    public void MomentaryPulse_ReturnsBeforeAck_StaysLatchedUntilAcknowledged()
    {
        var item = NewItem();

        Assert.True(item.ApplyEvent(NewEvent("False", "True")));
        Assert.Equal(AlarmAnnunciatorItem.ActiveUnacknowledgedState, item.VisualState);
        Assert.True(item.CurrentProcessActive);
        Assert.True(item.IsFlashing);
        Assert.True(item.CanAcknowledge);
        Assert.Equal(1, item.ActivationCount);

        Assert.True(item.ApplyEvent(NewEvent("True", "False")));
        Assert.Equal(AlarmAnnunciatorItem.ReturnedUnacknowledgedState, item.VisualState);
        Assert.False(item.CurrentProcessActive);
        Assert.True(item.HasLatchedOccurrence);
        Assert.True(item.IsFlashing);
        Assert.True(item.CanAcknowledge);

        Assert.True(item.Acknowledge(new DateTimeOffset(2026, 8, 17, 4, 0, 0, TimeSpan.FromHours(7))));
        Assert.Equal(AlarmAnnunciatorItem.NormalState, item.VisualState);
        Assert.False(item.HasLatchedOccurrence);
        Assert.False(item.IsFlashing);
        Assert.False(item.CanAcknowledge);
    }

    [Fact]
    public void ActiveAlarm_AckStopsFlashing_ButAlarmRemainsUntilReturnToNormal()
    {
        var item = NewItem();
        item.ApplyEvent(NewEvent("False", "True"));

        Assert.True(item.Acknowledge(DateTimeOffset.Now));
        Assert.Equal(AlarmAnnunciatorItem.ActiveAcknowledgedState, item.VisualState);
        Assert.True(item.CurrentProcessActive);
        Assert.True(item.HasLatchedOccurrence);
        Assert.False(item.IsFlashing);
        Assert.False(item.CanAcknowledge);

        item.ApplyEvent(NewEvent("True", "False"));
        Assert.Equal(AlarmAnnunciatorItem.NormalState, item.VisualState);
        Assert.False(item.CurrentProcessActive);
        Assert.False(item.HasLatchedOccurrence);
    }

    [Fact]
    public void NormalInactiveEvent_DoesNotInventAlarmOccurrence()
    {
        var item = NewItem();

        Assert.True(item.ApplyEvent(NewEvent("True", "False")));
        Assert.Equal(AlarmAnnunciatorItem.NormalState, item.VisualState);
        Assert.False(item.HasLatchedOccurrence);
        Assert.Equal(0, item.ActivationCount);
    }

    [Fact]
    public void ExplicitlyConfiguredAbnormalDpcState_IsTreatedAsAlarmCondition()
    {
        var item = NewItem();
        var entry = NewEvent("Open [01]", "Intermediate [00]", "Dbpos");

        Assert.True(item.ApplyEvent(entry));
        Assert.Equal(AlarmAnnunciatorItem.ActiveUnacknowledgedState, item.VisualState);
        Assert.True(item.CurrentProcessActive);
        Assert.True(item.IsFlashing);
    }

    [Fact]
    public void DeviceGroup_AggregatesOnlyItsOwnAlarmState_AndFlashesIndependently()
    {
        var first = NewItem();
        var second = new AlarmAnnunciatorItem
        {
            DeviceId = "ied-1",
            PointKey = "ied-1|iedld/ggio1.alm.stval",
            ConfiguredReference = "iedld/ggio1.alm.stval",
            DeviceName = "IED1",
            SignalName = "GGIO Alarm",
            IecReference = "IEDLD/GGIO1.Alm.stVal",
            IecDataType = "Boolean"
        };
        var group = new AlarmAnnunciatorDeviceGroup { DeviceId = "ied-1", DeviceName = "IED1" };
        group.Alarms.Add(first);
        group.Alarms.Add(second);

        first.ApplyEvent(NewEvent("False", "True"));
        group.Recalculate(flashPhase: true);

        Assert.Equal(2, group.ConfiguredCount);
        Assert.Equal(1, group.ActiveCount);
        Assert.Equal(1, group.UnacknowledgedCount);
        Assert.Equal(AlarmAnnunciatorDeviceGroup.UnacknowledgedState, group.VisualState);
        Assert.Equal(1d, group.LampOpacity);

        group.SetFlashPhase(false);
        Assert.Equal(0.18d, group.LampOpacity, 3);

        first.Acknowledge(DateTimeOffset.Now);
        group.Recalculate(flashPhase: true);
        Assert.Equal(1, group.ActiveCount);
        Assert.Equal(0, group.UnacknowledgedCount);
        Assert.Equal(AlarmAnnunciatorDeviceGroup.ActiveState, group.VisualState);
        Assert.Equal(1d, group.LampOpacity);
    }

    [Theory]
    [InlineData("ST", true)]
    [InlineData("st", true)]
    [InlineData("MX", false)]
    [InlineData("CO", false)]
    public void ExplorerSelection_IsRestrictedToStatusFunctionalConstraint(string functionalConstraint, bool expected)
    {
        var point = new Iec61850MonitorPoint { FunctionalConstraint = functionalConstraint };
        Assert.Equal(expected, point.CanUseAsAnnunciator);
    }

    [Fact]
    public void ProjectProfile_PreservesAnnunciatorReferencesSeparatelyFromLiveSelection()
    {
        var profile = new Iec61850TesterDeviceProfile
        {
            SelectedReferences = ["IEDLD/MMXU1.A.phsA.cVal.mag.f"],
            AnnunciatorReferences = ["IEDLD/PTRC1.Tr.general", "IEDLD/GGIO1.Alm.stVal"]
        };

        Assert.Single(profile.SelectedReferences);
        Assert.Equal(2, profile.AnnunciatorReferences.Count);
        Assert.Contains("IEDLD/PTRC1.Tr.general", profile.AnnunciatorReferences);
    }

    private static AlarmAnnunciatorItem NewItem()
        => new()
        {
            DeviceId = "ied-1",
            PointKey = "ied-1|iedld/ptrc1.tr.general",
            ConfiguredReference = "iedld/ptrc1.tr.general",
            DeviceName = "IED1",
            SignalName = "PTRC1 Trip",
            IecReference = "IEDLD/PTRC1.Tr.general",
            IecDataType = "Boolean"
        };

    private static Iec61850EventEntry NewEvent(string oldValue, string newValue, string dataType = "Boolean")
        => new()
        {
            Sequence = 1,
            DeviceId = "ied-1",
            PointKey = "ied-1|iedld/ptrc1.tr.general",
            DeviceName = "IED1",
            SignalName = "PTRC1 Trip",
            IecReference = "IEDLD/PTRC1.Tr.general",
            IecDataType = dataType,
            OldValue = oldValue,
            NewValue = newValue,
            Quality = "good",
            SourceMode = "ARIEC Hybrid: StaticBrcb",
            DeviceTimestamp = "2026-08-17 04:00:00.000"
        };
}
