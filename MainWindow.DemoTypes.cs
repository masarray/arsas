using System.Globalization;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private static string ToggleDemoValue(string value)
    {
        var normalized = value.Trim();
        if (normalized.Equals("false", StringComparison.OrdinalIgnoreCase)) return "true";
        if (normalized.Equals("true", StringComparison.OrdinalIgnoreCase)) return "false";
        if (normalized.Contains("Closed", StringComparison.OrdinalIgnoreCase)) return "Open [01]";
        if (normalized.Contains("Open", StringComparison.OrdinalIgnoreCase)) return "Closed [10]";
        if (normalized.Equals("Remote", StringComparison.OrdinalIgnoreCase)) return "Local";
        if (normalized.Equals("Local", StringComparison.OrdinalIgnoreCase)) return "Remote";
        return normalized;
    }

    private static string BuildSidecarRoot(string reference)
    {
        foreach (var suffix in new[] { ".stVal", ".mag.f", ".general" })
        {
            if (reference.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return reference[..^suffix.Length];
        }
        var lastDot = reference.LastIndexOf('.');
        return lastDot > 0 ? reference[..lastDot] : reference;
    }

    private static string FormatDemoAnalog(double value, string unit)
    {
        var decimals = Math.Abs(value) < 10 ? "0.00" : "0.0";
        return string.IsNullOrWhiteSpace(unit)
            ? value.ToString(decimals, CultureInfo.InvariantCulture)
            : $"{value.ToString(decimals, CultureInfo.InvariantCulture)} {unit}";
    }

    private static DemoSignalSeed Position(string name, string path, string initial)
        => new(name, path, "ST", "Dbpos", "Status", string.Empty, initial, DemoValueKind.Discrete, 0, 0, new[] { "Open [01]", "Closed [10]" }, true);

    private static DemoSignalSeed Boolean(string name, string path, bool initial, bool eventEligible)
        => new(name, path, "ST", "BOOLEAN", "Status", string.Empty, initial ? "true" : "false", DemoValueKind.Discrete, 0, 0, new[] { "false", "true" }, eventEligible);

    private static DemoSignalSeed State(string name, string path, string[] values, string initial, bool eventEligible)
        => new(name, path, "ST", "INT32", "Status", string.Empty, initial, DemoValueKind.Discrete, 0, 0, values, eventEligible);

    private static DemoSignalSeed Counter(string name, string path, int initial)
        => new(name, path, "ST", "INT32", "Counter", string.Empty, initial.ToString(CultureInfo.InvariantCulture), DemoValueKind.Counter, initial, 0, null, false);

    private static DemoSignalSeed Analog(string name, string path, string unit, double baseValue, double variation)
        => new(name, path, "MX", "FLOAT32", "Measurement", unit, FormatDemoAnalog(baseValue, unit), DemoValueKind.Analog, baseValue, variation, null, false);

    private enum DemoDeviceRole { Bcu, Ocr, LineDiff, Distance, TrafoDiff, BusbarDiff, CapBank, Coupler }
    private enum DemoValueKind { Discrete, Analog, Counter }

    private sealed record DemoDeviceSpec(string Name, string Description, string IpAddress, string ModelSummary, DemoDeviceRole Role);

    private sealed record DemoSignalSeed(
        string Name,
        string Path,
        string FunctionalConstraint,
        string DataType,
        string Category,
        string Unit,
        string InitialValue,
        DemoValueKind Kind,
        double BaseValue,
        double Variation,
        string[]? DiscreteValues,
        bool EventEligible);

    private sealed record DemoPointState(
        Iec61850MonitorDevice Device,
        SignalDefinition Signal,
        Iec61850MonitorPoint Point,
        DemoSignalSeed Seed);

    private sealed record DemoGooseStreamSpec(
        string StreamKey,
        string IedName,
        string AppId,
        string SourceMac,
        string DestinationMac,
        int Vlan,
        string ControlBlock,
        string DataSet,
        IReadOnlyList<DemoGooseLeafState> Leaves);

    private sealed class DemoGooseLeafState
    {
        public DemoGooseLeafState(string name, string path, string typeText, string value)
        {
            Name = name;
            Path = path;
            TypeText = typeText;
            Value = value;
            PreviousValue = value;
        }

        public string Name { get; }
        public string Path { get; }
        public string TypeText { get; }
        public string Value { get; set; }
        public string PreviousValue { get; set; }
    }

    private sealed class DemoGooseStreamState
    {
        public DemoGooseStreamState(DemoGooseStreamSpec spec, int stateNumber, int sequenceNumber, long packetCount)
        {
            Spec = spec;
            StateNumber = stateNumber;
            SequenceNumber = sequenceNumber;
            PacketCount = packetCount;
            Leaves = spec.Leaves.Select(leaf => new DemoGooseLeafState(leaf.Name, leaf.Path, leaf.TypeText, leaf.Value)).ToList();
            LastTimelineTimestamp = DateTimeOffset.Now.AddSeconds(-8);
        }

        public DemoGooseStreamSpec Spec { get; }
        public GooseStreamRow Row { get; set; } = null!;
        public List<DemoGooseLeafState> Leaves { get; }
        public int StateNumber { get; set; }
        public int SequenceNumber { get; set; }
        public long PacketCount { get; set; }
        public DateTimeOffset LastTimelineTimestamp { get; set; }
    }
}
