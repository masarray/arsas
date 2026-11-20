using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class CanonicalLiveValueSsotM213DTests
{
    [Fact]
    public void DisconnectAndMonitorPauseNeverPromoteLastKnownSignalToFresh()
    {
        var device = new Iec61850MonitorDevice();
        var signal = new SignalDefinition {
            ObjectReference = "LD/MMXU1.Hz.mag.f", FunctionalConstraint = "MX",
            Value = "50 Hz", Quality = "good", DeviceTimestamp = "T1"
        };
        device.Signals.Add(signal);
        Assert.Equal(CanonicalSignalObservationState.Unobserved, signal.ObservationState);

        device.IsConnected = true;
        var association = device.LiveValues.Association;
        Assert.Equal(CanonicalSignalObservationState.Awaiting, signal.ObservationState);
        Assert.False(signal.IsLiveObservation);
        device.IsMonitoring = true;
        Assert.Equal(CanonicalSignalObservationState.Awaiting, signal.ObservationState);

        var projection = new IedBrowserReadBindingIndex(device);
        Assert.True(device.LiveValues.PublishForAssociation(association,
            signal.ObjectReference, "MX", "60", "good", "T2", "Report"));
        Assert.Equal(1, projection.ProjectReportedSignals(
            device.LiveValues, association, signal.ObjectReference, "MX"));
        Assert.Equal(CanonicalSignalObservationState.LiveReport, signal.ObservationState);
        Assert.True(signal.IsLiveObservation);
        Assert.Equal(association, signal.ObservationAssociation);
        Assert.Equal("60", signal.Value);
        device.IsMonitoring = false;
        Assert.Equal(CanonicalSignalObservationState.Paused, signal.ObservationState);
        Assert.False(signal.IsLiveObservation);
        Assert.Equal("60", signal.Value);
        Assert.Equal("good", signal.Quality);
        Assert.Equal("T2", signal.DeviceTimestamp);

        device.IsConnected = false;
        Assert.Equal(CanonicalSignalObservationState.Offline, signal.ObservationState);
        Assert.False(signal.IsLiveObservation);
        Assert.Equal("Offline · last known", signal.ObservationLabel);
        Assert.Equal("60", signal.Value);
        device.IsConnected = true;
        Assert.NotEqual(association, device.LiveValues.Association);
        Assert.Equal(CanonicalSignalObservationState.Awaiting, signal.ObservationState);
        Assert.False(signal.IsLiveObservation);
        Assert.Equal(0, projection.ProjectReportedSignals(
            device.LiveValues, association, signal.ObjectReference, "MX"));
    }

    [Fact]
    public void ExplicitReadIsLiveOnlyDuringCurrentConnectionAndItsOwnAssociation()
    {
        var device = new Iec61850MonitorDevice();
        var signal = new SignalDefinition {
            ObjectReference = "LD/MMXU1.TotW.mag.f", FunctionalConstraint = "MX"
        };
        device.Signals.Add(signal);
        device.IsConnected = true;
        var association = device.LiveValues.Association;
        var projection = new IedBrowserReadBindingIndex(device);
        Assert.True(device.LiveValues.PublishForAssociation(association,
            signal.ObjectReference, "MX", "7", "", "", "MMS Read"));
        Assert.Equal(1, projection.Apply(new IedBrowserReadResult(
            new IedBrowserReadTarget(signal.ObjectReference, "MX", "FLOAT32"),
            "tampered", "", "", ""), device.LiveValues, association));
        Assert.Equal(CanonicalSignalObservationState.LiveRead, signal.ObservationState);
        Assert.Equal("7", signal.Value);
        device.IsConnected = false;
        device.IsConnected = true;
        Assert.Equal(CanonicalSignalObservationState.Awaiting, signal.ObservationState);
        Assert.Equal(0, projection.Apply(new IedBrowserReadResult(
            new IedBrowserReadTarget(signal.ObjectReference, "MX", "FLOAT32"),
            "7", "", "", ""), device.LiveValues, association));
        Assert.Equal("7", signal.Value);
    }

    [Fact]
    public void LifecycleStateIsNotIECQualityOrPersistentModelAuthority()
    {
        var signal = new SignalDefinition {
            ObjectReference = "LD/CSWI1.Pos.stVal", FunctionalConstraint = "ST",
            Quality = "good", Value = "Open [01]"
        };
        signal.MarkCanonicalObservation(CanonicalSignalObservationState.Offline, 4);
        Assert.Equal("good", signal.Quality);
        Assert.Equal("Open [01]", signal.Value);
        Assert.False(signal.IsLiveObservation);
        var json = System.Text.Json.JsonSerializer.Serialize(signal);
        Assert.DoesNotContain("ObservationState", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ObservationAssociation", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ObservationLabel", json, StringComparison.Ordinal);
    }

    [Fact]
    public void UiMustRejectUnstampedDisconnectedAndStoppedMonitorSnapshots()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml.cs")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var ui = File.ReadAllText(Path.Combine(directory!.FullName, "MainWindow.xaml.cs"));
        Assert.Contains("!observedOwner.IsConnected || !observedOwner.IsMonitoring", ui);
        Assert.Contains("!observedOwner.IsDemo &&", ui);
        Assert.Contains("snapshot.ObservationAssociation == 0", ui);
        Assert.Contains("snapshot.ObservationAssociation != observedOwner.LiveValues.Association", ui);
        Assert.Contains("_pendingPointSnapshots.TryRemove(key, out _)", ui);
    }
}
