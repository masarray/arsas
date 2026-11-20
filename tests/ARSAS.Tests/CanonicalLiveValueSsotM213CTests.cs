using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class CanonicalLiveValueSsotM213CTests
{
    [Fact]
    public void ReportCanonicalValueQualityAndTimeAreProjectedAsOneEvidenceTuple()
    {
        var device = new Iec61850MonitorDevice { IsConnected = true, IsMonitoring = true };
        var point = new Iec61850MonitorPoint {
            DeviceId = device.DeviceId, IecReference = "LD/MMXU1.Hz.mag.f",
            FunctionalConstraint = "MX", IecDataType = "FLOAT32", Unit = "Hz"
        };
        var association = device.LiveValues.Association;
        Assert.True(device.LiveValues.PublishForAssociation(association,
            point.IecReference, "MX", "50", "good", "T1", "Report"));
        var staleUiSnapshot = new Iec61850PointSnapshot {
            Point = point, Value = "99 Hz", Quality = "bad",
            DeviceTimestamp = "T0", SourceMode = "MMS Polling",
            ObservationAssociation = association
        };
        var presentation = CanonicalPointSnapshotAuthority.Project(
            device, point, staleUiSnapshot);
        Assert.True(presentation.FromCanonicalReport);
        Assert.Equal("50 Hz", presentation.Value);
        Assert.Equal("good", presentation.Quality);
        Assert.Equal("T1", presentation.DeviceTimestamp);
        Assert.Equal("Report", presentation.SourceMode);

        // Later explicit Read never contaminates Report value OR metadata.
        Assert.True(device.LiveValues.PublishForAssociation(association,
            point.IecReference, "MX", "200", "questionable", "T2", "MMS Read"));
        Assert.Equal(presentation, CanonicalPointSnapshotAuthority.Project(
            device, point, staleUiSnapshot));
    }

    [Fact]
    public void MissingReportMetadataMustNotInheritOldQualityOrTimestamp()
    {
        var device = new Iec61850MonitorDevice { IsConnected = true, IsMonitoring = true };
        var point = new Iec61850MonitorPoint {
            IecReference = "LD/MMXU1.Hz.mag.f",
            FunctionalConstraint = "MX", Unit = "Hz", IecDataType = "FLOAT32"
        };
        var association = device.LiveValues.Association;
        Assert.True(device.LiveValues.PublishForAssociation(association,
            point.IecReference, "MX", "52", "", "", "Report"));
        var snapshot = new Iec61850PointSnapshot {
            Point = point, Value = "50 Hz", Quality = "good",
            DeviceTimestamp = "old", SourceMode = "StaticBRCB",
            IsReportTraffic = true, ObservationAssociation = association
        };
        var presentation = CanonicalPointSnapshotAuthority.Project(device, point, snapshot);
        Assert.Equal("52 Hz", presentation.Value);
        Assert.Equal("Unknown", presentation.Quality);
        Assert.Equal("-", presentation.DeviceTimestamp);
        Assert.Equal("StaticBRCB", presentation.SourceMode);
        Assert.True(presentation.FromCanonicalReport);
    }

    [Fact]
    public void ReconnectOrOtherFcFallsBackWithoutMixingReportMetadata()
    {
        var device = new Iec61850MonitorDevice { IsConnected = true, IsMonitoring = true };
        var point = new Iec61850MonitorPoint {
            IecReference = "LD/MMXU1.Hz.mag.f", FunctionalConstraint = "MX"
        };
        var association = device.LiveValues.Association;
        var snapshot = new Iec61850PointSnapshot {
            Point = point, Value = "44", Quality = "Q",
            DeviceTimestamp = "T", SourceMode = "Runtime",
            ObservationAssociation = association
        };
        Assert.True(device.LiveValues.PublishForAssociation(association,
            point.IecReference, "ST", "1", "other", "other", "Report"));
        var fallback = CanonicalPointSnapshotAuthority.Project(device, point, snapshot);
        Assert.False(fallback.FromCanonicalReport);
        Assert.Equal(("44", "Q", "T", "Runtime"),
            (fallback.Value, fallback.Quality, fallback.DeviceTimestamp, fallback.SourceMode));

        device.IsConnected = false;
        device.IsConnected = true;
        Assert.True(device.LiveValues.PublishForAssociation(device.LiveValues.Association,
            point.IecReference, "MX", "55", "fresh", "fresh", "Report"));
        Assert.Equal(fallback, CanonicalPointSnapshotAuthority.Project(device, point, snapshot));
    }

    [Fact]
    public void SignalAndPointReadProjectionClearMissingMetadataFromPriorObservation()
    {
        var device = new Iec61850MonitorDevice { IsConnected = true };
        var signal = new SignalDefinition {
            ObjectReference = "LD/MMXU1.Hz.mag.f", FunctionalConstraint = "MX",
            Value = "old", Quality = "good", DeviceTimestamp = "old"
        };
        var point = new Iec61850MonitorPoint {
            IecReference = signal.ObjectReference, FunctionalConstraint = "MX",
            Quality = "good", DeviceTimestamp = "old"
        };
        device.Signals.Add(signal);
        device.Points.Add(point);
        var index = new IedBrowserReadBindingIndex(device);
        var association = device.LiveValues.Association;
        Assert.True(device.LiveValues.PublishForAssociation(association,
            signal.ObjectReference, "MX", "60", "", "", "MMS Read"));
        Assert.Equal(2, index.Apply(new IedBrowserReadResult(
            new IedBrowserReadTarget(signal.ObjectReference, "MX", "FLOAT32"),
            "tampered", "", "", ""), device.LiveValues, association));
        Assert.Equal("60", signal.Value);
        Assert.Equal("60", point.Value);
        Assert.Equal("Unknown", signal.Quality);
        Assert.Equal("Unknown", point.Quality);
        Assert.Equal("-", signal.DeviceTimestamp);
        Assert.Equal("-", point.DeviceTimestamp);
    }

    [Fact]
    public void WpfUsesOneCanonicalTupleForPointValueQualityTimestampAndSource()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml.cs")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "MainWindow.xaml.cs"));
        Assert.Contains("CanonicalPointSnapshotAuthority.Project(", source);
        Assert.Contains("point.Quality = presentation.Quality", source);
        Assert.Contains("point.DeviceTimestamp = presentation.DeviceTimestamp", source);
        Assert.Contains("point.SourceMode = presentation.SourceMode", source);
    }
}
