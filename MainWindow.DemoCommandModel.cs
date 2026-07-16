using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private void AddDemoCircuitBreakerControl(Iec61850MonitorDevice device)
    {
        var position = _demoPointStates.First(item =>
            ReferenceEquals(item.Device, device) &&
            item.Seed.Name.Equals("Breaker position", StringComparison.OrdinalIgnoreCase));

        var oldPointKey = position.Point.PointKey;
        var statusReference = $"{device.Name}CTRL/CSWI1.Pos.stVal";
        position.Signal.ObjectReference = statusReference;
        position.Signal.DisplayReference = "CTRL.CSWI1.Pos.stVal";
        position.Signal.QualityReference = $"{device.Name}CTRL/CSWI1.Pos.q";
        position.Signal.TimestampReference = $"{device.Name}CTRL/CSWI1.Pos.t";
        position.Point.IecReference = statusReference;
        position.Point.QualityReference = position.Signal.QualityReference;
        position.Point.TimestampReference = position.Signal.TimestampReference;
        _pointIndex.Remove(oldPointKey);
        _pointIndex[position.Point.PointKey] = position.Point;

        var signal = new SignalDefinition
        {
            Name = "Circuit breaker control",
            ObjectReference = $"{device.Name}CTRL/CSWI1.Pos",
            DisplayReference = "CTRL.CSWI1.Pos.Oper.ctlVal",
            FunctionalConstraint = "CO",
            DataType = "Dbpos",
            Category = "Control",
            Confidence = "High",
            Source = "Live model",
            IsSelected = true,
            IsControlSignal = true,
            ControlCdc = "DPC",
            ControlModelReference = $"{device.Name}CTRL/CSWI1.Pos.ctlModel",
            ControlStatusReference = statusReference,
            ControlModelText = "Select Before Operate (SBO) • Enhanced security",
            ControlValueType = "Dbpos",
            ControlCurrentValue = position.Point.Value,
            ControlLastResult = "Ready • live feedback available",
            ProbeStatus = "Resolved",
            Quality = "Good",
            DeviceTimestamp = position.Point.DeviceTimestamp
        };
        signal.PropertyChanged += Signal_PropertyChanged;
        _signalOwners[signal] = device;
        device.Signals.Add(signal);
    }

    private static string ResolveDemoLogicalDevice(DemoSignalSeed seed)
    {
        var path = seed.Path;
        if (path.StartsWith("MMXU", StringComparison.OrdinalIgnoreCase) || path.StartsWith("MMXN", StringComparison.OrdinalIgnoreCase) || path.StartsWith("STMP", StringComparison.OrdinalIgnoreCase)) return "MEAS";
        if (path.StartsWith("CSWI", StringComparison.OrdinalIgnoreCase) || path.StartsWith("XCBR", StringComparison.OrdinalIgnoreCase) || path.StartsWith("CILO", StringComparison.OrdinalIgnoreCase) || path.StartsWith("RSYN", StringComparison.OrdinalIgnoreCase)) return "CTRL";
        if (path.StartsWith("GGIO", StringComparison.OrdinalIgnoreCase)) return seed.Name.Contains("alarm", StringComparison.OrdinalIgnoreCase) ? "DR" : "CTRL";
        return "PROT";
    }
}
