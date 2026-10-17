using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class CanonicalLiveValueSsotM213BTests
{
    [Fact]
    public void ExplicitReadProjectionUsesCanonicalAcceptedValueNotMutableCallbackPayload()
    {
        var device = new Iec61850MonitorDevice { IsConnected = true };
        var signal = new SignalDefinition {
            ObjectReference = "LD/MMXU1.Hz.mag.f", FunctionalConstraint = "MX"
        };
        var point = new Iec61850MonitorPoint {
            DeviceId = device.DeviceId, IecReference = signal.ObjectReference,
            FunctionalConstraint = "MX"
        };
        device.Signals.Add(signal);
        device.Points.Add(point);
        var bindings = new IedBrowserReadBindingIndex(device);
        var association = device.LiveValues.Association;
        var target = new IedBrowserReadTarget(signal.ObjectReference, "MX", "FLOAT32");
        var callback = new IedBrowserReadResult(target, "999", "", "", "");
        Assert.Equal(0, bindings.Apply(callback, device.LiveValues, association));
        Assert.True(device.LiveValues.PublishForAssociation(association,
            target.Reference, "MX", "50", "good", "now", "MMS Read"));
        Assert.Equal(2, bindings.Apply(callback, device.LiveValues, association));
        Assert.Equal("50", signal.Value);
        Assert.Equal("50", point.Value);
        Assert.Equal("MMS Read", point.SourceMode);
        Assert.Equal("good", point.Quality);
        Assert.Equal("now", signal.DeviceTimestamp);
    }

    [Fact]
    public void MonitoringReportWinsAcrossSignalProjectionEvenAfterExplicitRead()
    {
        var device = new Iec61850MonitorDevice { IsConnected = true, IsMonitoring = true };
        var signal = new SignalDefinition {
            ObjectReference = "LD/MMXU1.Hz.mag.f", FunctionalConstraint = "MX",
            Unit = "Hz", DataType = "FLOAT32"
        };
        device.Signals.Add(signal);
        var bindings = new IedBrowserReadBindingIndex(device);
        var association = device.LiveValues.Association;
        Assert.True(device.LiveValues.PublishForAssociation(association,
            signal.ObjectReference, "MX", "50", "Good", "01:23", "Report"));
        Assert.Equal(1, bindings.ProjectReportedSignals(device.LiveValues, association,
            signal.ObjectReference, "MX"));
        Assert.Equal("50 Hz", signal.Value);
        Assert.Equal("Good", signal.Quality);
        Assert.Equal("01:23", signal.DeviceTimestamp);
        Assert.True(device.LiveValues.PublishForAssociation(association,
            signal.ObjectReference, "MX", "999", "", "", "MMS Read"));
        Assert.Equal(1, bindings.ProjectReportedSignals(device.LiveValues, association,
            signal.ObjectReference, "MX"));
        Assert.Equal("50 Hz", signal.Value);
        Assert.Equal(0, bindings.Apply(new IedBrowserReadResult(
            new IedBrowserReadTarget(signal.ObjectReference,"MX","FLOAT32"),
            "999", "", "", ""), device.LiveValues, association));
        Assert.Equal("50 Hz", signal.Value);
    }

    [Fact]
    public void StrictFcAssociationAndUnobservedSiblingDoNotLeakToSignalRows()
    {
        var device = new Iec61850MonitorDevice { IsConnected = true, IsMonitoring = true };
        var a = new SignalDefinition {
            ObjectReference = "LD/MMXU1.A.phsA.cVal.mag.f",
            FunctionalConstraint = "MX", DataType = "FLOAT32", Unit = "A"
        };
        var b = new SignalDefinition {
            ObjectReference = "LD/MMXU1.A.phsB.cVal.mag.f",
            FunctionalConstraint = "MX", DataType = "FLOAT32", Unit = "A"
        };
        device.Signals.Add(a);
        device.Signals.Add(b);
        var index = new IedBrowserReadBindingIndex(device);
        var association = device.LiveValues.Association;
        Assert.True(device.LiveValues.PublishForAssociation(association,
            a.ObjectReference, "ST", "111", "", "", "Report"));
        Assert.Equal(0, index.ProjectReportedSignals(device.LiveValues, association,
            a.ObjectReference, "MX"));
        Assert.True(device.LiveValues.PublishForAssociation(association,
            a.ObjectReference, "MX", "7", "", "", "Report"));
        Assert.Equal(1, index.ProjectReportedSignals(device.LiveValues, association,
            a.ObjectReference, "MX"));
        Assert.Equal("7 A", a.Value);
        Assert.Equal("-", b.Value);
        device.IsConnected = false;
        device.IsConnected = true;
        Assert.Equal(0, index.ProjectReportedSignals(device.LiveValues, association,
            a.ObjectReference, "MX"));
        Assert.Equal("7 A", a.Value); // last-known, never marked as new live
    }

    [Fact]
    public void MonitoringAndReadUiHandlersProjectFromSameCanonicalRegistry()
    {
        static string Source(string file)
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var path = Path.Combine(directory.FullName, file);
                if (File.Exists(path)) return File.ReadAllText(path);
                directory = directory.Parent;
            }
            throw new FileNotFoundException(file);
        }
        var read = Source("MainWindow.IedBrowser.cs");
        var monitor = Source("MainWindow.xaml.cs");
        Assert.Contains("bindingIndex.Apply(result, sourceDevice.LiveValues, readAssociation)",read);
        Assert.Contains("signalProjection.ProjectReportedSignals(",monitor);
        Assert.Contains("_canonicalSignalProjections.Remove(deviceId)",monitor);
        Assert.Contains("if (snapshot.IsReportTraffic &&",monitor);
    }
    [Fact]
    public void FocusedMmsReadDuringMonitoringFillsExplorerOnlyWhenReportAbsent()
    {
        var device = new Iec61850MonitorDevice
        {
            IsConnected = true, IsMonitoring = true
        };
        var signal = new SignalDefinition
        {
            ObjectReference = "GE650/MMXU1.Hz.mag.f",
            FunctionalConstraint = "MX",
            DataType = "FLOAT32", Unit = "Hz"
        };
        var point = new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId, IecReference = signal.ObjectReference,
            FunctionalConstraint = "MX", IecDataType = "FLOAT32"
        };
        device.Signals.Add(signal);
        device.Points.Add(point);
        var index = new IedBrowserReadBindingIndex(device);
        var association = device.LiveValues.Association;
        var target = new IedBrowserReadTarget(signal.ObjectReference, "MX", "FLOAT32");
        var callback = new IedBrowserReadResult(target, "999", "", "", "");
        Assert.True(device.LiveValues.PublishForAssociation(association,
            target.Reference, "MX", "49", "", "", "MMS Read"));
        Assert.Equal(1, index.Apply(callback, device.LiveValues, association));
        Assert.Equal("49", signal.Value);
        Assert.Equal("-", point.Value); // no manufactured point event or SOE

        Assert.True(device.LiveValues.PublishForAssociation(association,
            target.Reference, "MX", "50", "Good", "", "Report"));
        Assert.Equal(1, index.ProjectReportedSignals(device.LiveValues,
            association, target.Reference, "MX"));
        Assert.Equal("50 Hz", signal.Value);
        Assert.True(device.LiveValues.PublishForAssociation(association,
            target.Reference, "MX", "60", "", "", "MMS Read"));
        Assert.Equal(0, index.Apply(callback, device.LiveValues, association));
        Assert.Equal("50 Hz", signal.Value); // Report remains authoritative
        Assert.True(device.LiveValues.TryGet(target.Reference, "MX", out var observed));
        Assert.Equal("Report", observed.Source);
        device.IsConnected = false;
        device.IsConnected = true;
        Assert.Equal(0, index.Apply(callback, device.LiveValues, association));
    }

}
