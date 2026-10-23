using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private IReadOnlyList<DemoSignalSeed> BuildDemoSignalSeeds(DemoDeviceRole role, int index)
    {
        var common = new List<DemoSignalSeed>
        {
            Position("Breaker position", "XCBR1.Pos.stVal", index % 4 == 3 ? "Open [01]" : "Closed [10]"),
            Boolean("Trip circuit healthy", "GGIO1.Ind1.stVal", true, eventEligible: false),
            Boolean("Protection enabled", "LLN0.Mod.stVal", true, eventEligible: false),
            State("Control authority", "CSWI1.Loc.stVal", new[] { "Remote", "Local" }, "Remote", eventEligible: false),
            Analog("Phase A current", "MMXU1.A.phsA.cVal.mag.f", "A", 620 + index * 38, 34),
            Analog("Phase B current", "MMXU1.A.phsB.cVal.mag.f", "A", 608 + index * 36, 32),
            Analog("Phase C current", "MMXU1.A.phsC.cVal.mag.f", "A", 631 + index * 35, 36),
            Analog("Bus voltage", "MMXU1.PhV.phsA.cVal.mag.f", "kV", index is 2 or 3 or 8 ? 20.4 : 149.7, index is 2 or 3 or 8 ? 0.22 : 0.55)
        };

        common.AddRange(role switch
        {
            DemoDeviceRole.Bcu => new[]
            {
                Boolean("Open interlock release", "CILO1.EnaOpn.stVal", true, false),
                Boolean("Close interlock release", "CILO1.EnaCls.stVal", true, false),
                Counter("Breaker operation count", "XCBR1.OpCnt.stVal", 184 + index * 11),
                Boolean("Bay alarm summary", "GGIO1.Alm1.stVal", false, true)
            },
            DemoDeviceRole.Ocr => new[]
            {
                Boolean("Overcurrent trip phase A", "PTOC1.Op.phsA", false, true),
                Boolean("Overcurrent trip phase B", "PTOC1.Op.phsB", false, true),
                Boolean("Earth-fault trip", "PTOC2.Op.general", false, true),
                Boolean("Master trip", "PTRC1.Op.general", false, true)
            },
            DemoDeviceRole.LineDiff => new[]
            {
                Boolean("Line differential trip phase A", "PDIF1.Op.phsA", false, true),
                Boolean("Line differential trip phase B", "PDIF1.Op.phsB", false, true),
                Boolean("Line differential trip", "PDIF1.Op.general", false, true),
                Analog("Differential current", "PDIF1.Idiff.mag.f", "A", 0.18, 0.08)
            },
            DemoDeviceRole.Distance => new[]
            {
                Boolean("Distance zone 1 pickup", "PDIS1.Str.general", false, true),
                Boolean("Distance zone 2 pickup", "PDIS2.Str.general", false, true),
                Boolean("Power swing blocking", "RPSB1.BlkZn.general", false, true),
                Analog("Positive-sequence impedance", "MMXU2.Z.phsA.cVal.mag.f", "Ω", 38.6, 0.9)
            },
            DemoDeviceRole.TrafoDiff => new[]
            {
                Boolean("Transformer differential trip phase A", "PDIF1.Op.phsA", false, true),
                Boolean("Transformer differential trip phase B", "PDIF1.Op.phsB", false, true),
                Boolean("Transformer differential trip", "PDIF1.Op.general", false, true),
                Analog("Winding temperature", "STMP1.Tmp.mag.f", "°C", 57.8, 1.7)
            },
            DemoDeviceRole.BusbarDiff => new[]
            {
                Boolean("Busbar differential pickup", "PDIF1.Str.general", false, true),
                Boolean("Busbar zone 1 trip", "PDIF1.Op.general", false, true),
                Boolean("Breaker-failure initiate", "RBRF1.OpEx.general", false, true),
                Analog("Zone differential current", "PDIF1.Idiff.mag.f", "A", 0.11, 0.06)
            },
            DemoDeviceRole.CapBank => new[]
            {
                Boolean("Capacitor unbalance pickup", "PTOC1.Str.general", false, true),
                Boolean("Overvoltage trip", "PTOV1.Op.general", false, true),
                Boolean("Undervoltage blocking", "PTUV1.Str.general", false, true),
                Analog("Reactive power", "MMXU1.VAr.threePh.cVal.mag.f", "MVAr", 18.4, 0.8)
            },
            DemoDeviceRole.Coupler => new[]
            {
                Boolean("Synchrocheck release", "RSYN1.Rel.stVal", true, false),
                Boolean("Bus 1 voltage healthy", "GGIO1.Ind2.stVal", true, false),
                Boolean("Bus 2 voltage healthy", "GGIO1.Ind3.stVal", true, false),
                Analog("Phase-angle difference", "RSYN1.DifAng.mag.f", "°", 1.8, 0.6)
            },
            _ => Array.Empty<DemoSignalSeed>()
        });

        return common;
    }

    private void AddDemoPoint(Iec61850MonitorDevice device, DemoSignalSeed seed, int deviceIndex, string reportInstance)
    {
        var reference = $"{device.Name}{ResolveDemoLogicalDevice(seed)}/{seed.Path}";
        var sidecarRoot = BuildSidecarRoot(reference);
        var dataSet = $"{device.Name}DR/LLN0$DataSet$A_DS01";
        var rcb = $"{device.Name}DR/LLN0$BR${reportInstance}";
        var timestamp = DateTime.Now.AddMilliseconds(-_demoRandom.Next(20, 780));

        var signal = new SignalDefinition
        {
            Name = seed.Name,
            ObjectReference = reference,
            DisplayReference = Iec61850MonitorPoint.StripIedNamePrefix(reference, device.Name),
            FunctionalConstraint = seed.FunctionalConstraint,
            DataType = seed.DataType,
            Category = seed.Category,
            Unit = seed.Unit,
            Confidence = "High",
            DataSetReference = dataSet,
            ReportControlReference = rcb,
            ReportCoverageReason = "Configured operational DataSet with active report control block.",
            QualityReference = sidecarRoot + ".q",
            TimestampReference = sidecarRoot + ".t",
            Source = "Live MMS discovery",
            IsSelected = true,
            IsReportCapable = true,
            ReportCoverage = $"Dynamic: {reportInstance}",
            Value = seed.InitialValue,
            Quality = "Good",
            DeviceTimestamp = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            ProbeStatus = "Read OK",
            Timestamp = timestamp
        };

        var point = new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            IpAddress = device.IpAddress,
            SignalName = seed.Name,
            IecReference = reference,
            QualityReference = sidecarRoot + ".q",
            TimestampReference = sidecarRoot + ".t",
            FunctionalConstraint = seed.FunctionalConstraint,
            IecDataType = seed.DataType,
            Category = seed.Category,
            Unit = seed.Unit,
            DataSetReference = dataSet,
            ReportControlReference = rcb,
            PollingIntervalMs = 1000,
            Value = seed.InitialValue,
            Quality = "Good",
            DeviceTimestamp = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            SourceMode = $"Dynamic: {reportInstance}",
            Reason = seed.EventEligible ? "dchg" : "integrity",
            Status = "Live",
            Sequence = 1000 + _demoPointStates.Count
        };

        signal.PropertyChanged += Signal_PropertyChanged;
        _signalOwners[signal] = device;
        device.Signals.Add(signal);
        device.Points.Add(point);
        GlobalPoints.Add(point);
        _pointIndex[point.PointKey] = point;
        _demoPointStates.Add(new DemoPointState(device, signal, point, seed));
    }

    private void BuildDemoEventHistory()
    {
        var candidates = _demoPointStates.Where(state => state.Seed.EventEligible).ToArray();
        var events = new List<Iec61850EventEntry>();
        var now = DateTime.Now;

        for (var index = 0; index < 54; index++)
        {
            var state = candidates[index % candidates.Length];
            var values = state.Seed.DiscreteValues ?? new[] { "false", "true" };
            var oldValue = values[index % values.Length];
            var newValue = values[(index + 1) % values.Length];
            var eventTime = now.AddSeconds(-(54 - index) * 17 - _demoRandom.Next(0, 8));
            events.Add(CreateDemoEvent(state, oldValue, newValue, eventTime));
        }

        Events.AddRange(events.OrderBy(item => item.DeviceTimestamp, StringComparer.Ordinal));
    }
}
