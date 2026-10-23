using System.Globalization;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private void DemoTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isDemoMode || _shutdownStarted)
            return;

        _demoTick++;
        UpdateDemoAnalogValues();
        if (_demoTick % 3 == 0)
            GenerateDemoProcessEvent();
        if (_demoTick % 4 == 0)
            GenerateRunningCommunicationDiagnostic();
        RaiseWorkspaceCounts();
    }

    private void UpdateDemoAnalogValues()
    {
        var analogStates = _demoPointStates.Where(state => state.Seed.Kind == DemoValueKind.Analog).ToArray();
        if (analogStates.Length == 0)
            return;

        var updateCount = Math.Min(12, analogStates.Length);
        for (var index = 0; index < updateCount; index++)
        {
            var state = analogStates[(_demoTick * 7 + index * 5) % analogStates.Length];
            var phase = (_demoTick + state.Point.Sequence % 17) * 0.21;
            var noise = (_demoRandom.NextDouble() - 0.5) * state.Seed.Variation * 0.18;
            var value = state.Seed.BaseValue + Math.Sin(phase) * state.Seed.Variation * 0.55 + noise;
            var formatted = FormatDemoAnalog(value, state.Seed.Unit);
            var timestamp = DateTime.Now.AddMilliseconds(-_demoRandom.Next(8, 95));

            state.Point.Value = formatted;
            state.Point.Quality = "Good";
            state.Point.DeviceTimestamp = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            state.Point.SourceMode = state.Device.AcquisitionMode;
            state.Point.Reason = "dupd";
            state.Point.Sequence++;
            state.Signal.Value = formatted;
            state.Signal.Quality = state.Point.Quality;
            state.Signal.DeviceTimestamp = state.Point.DeviceTimestamp;
            state.Signal.Timestamp = timestamp;
            state.Device.ReportPulseActive = true;
            _reportPulseUntil[state.Device.DeviceId] = DateTime.UtcNow.AddMilliseconds(520);
        }
    }

    private void GenerateDemoProcessEvent()
    {
        var candidates = _demoPointStates.Where(state => state.Seed.EventEligible && state.Seed.DiscreteValues is { Length: > 1 }).ToArray();
        if (candidates.Length == 0)
            return;

        var state = candidates[(_demoTick / 3) % candidates.Length];
        var values = state.Seed.DiscreteValues!;
        var currentIndex = Array.FindIndex(values, value => value.Equals(state.Point.Value, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            currentIndex = 0;
        var nextValue = values[(currentIndex + 1) % values.Length];
        var oldValue = state.Point.Value;
        var timestamp = DateTime.Now.AddMilliseconds(-_demoRandom.Next(5, 45));

        state.Point.Value = nextValue;
        state.Point.Quality = "Good";
        state.Point.DeviceTimestamp = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        state.Point.SourceMode = state.Device.AcquisitionMode;
        state.Point.Reason = "dchg";
        state.Point.Sequence++;
        state.Point.IsRecentlyChanged = true;
        state.Signal.Value = nextValue;
        state.Signal.Quality = state.Point.Quality;
        state.Signal.DeviceTimestamp = state.Point.DeviceTimestamp;
        state.Signal.Timestamp = timestamp;
        state.Device.ReportPulseActive = true;
        _reportPulseUntil[state.Device.DeviceId] = DateTime.UtcNow.AddMilliseconds(650);
        _pointHighlightUntil[state.Point.PointKey] = DateTime.UtcNow.AddSeconds(3);

        var processEvent = CreateDemoEvent(state, oldValue, nextValue, timestamp);
        Events.AddRange(new[] { processEvent });
        Events.TrimStart(10000);
        if (MainTabs.SelectedIndex != 2)
            state.Device.AddUnreadEvents(1);

        LastStatusText = $"SOE • {state.Device.Name} • {state.Point.IecTelegram} = {nextValue} • Quality Good • {state.Device.AcquisitionMode}";
    }

    private void GenerateRunningCommunicationDiagnostic()
    {
        var activeDevices = Devices.Where(device => device.IsConnected).ToArray();
        if (activeDevices.Length == 0)
            return;

        var device = activeDevices[(_demoTick / 4) % activeDevices.Length];
        var devicePoints = _demoPointStates.Where(state => ReferenceEquals(state.Device, device)).ToArray();
        var point = devicePoints.Length == 0 ? null : devicePoints[_demoTick % devicePoints.Length].Point;
        var reportName = device.AcquisitionMode.StartsWith("Dynamic: ", StringComparison.OrdinalIgnoreCase)
            ? device.AcquisitionMode["Dynamic: ".Length..]
            : device.AcquisitionMode;
        var cycle = (_demoTick / 4) % 5;

        var entry = cycle switch
        {
            0 => new DiagnosticEntry
            {
                Time = DateTime.Now,
                Level = "INFO",
                Source = "MMS",
                Message = $"{device.Name} association healthy on {device.IpAddress}:102; confirmed response in {_demoRandom.Next(7, 24)} ms."
            },
            1 => new DiagnosticEntry
            {
                Time = DateTime.Now,
                Level = "INFO",
                Source = "Reporting",
                Message = $"{device.Name} {reportName}: report received; reason=dupd; sqNum={_demoRandom.Next(1200, 9800)}; quality Good."
            },
            2 => new DiagnosticEntry
            {
                Time = DateTime.Now,
                Level = "INFO",
                Source = "Process",
                Message = point == null
                    ? $"{device.Name} process values refreshed with quality Good."
                    : $"{device.Name} {point.IecTelegram}: value={point.Value}; quality={point.Quality}; timestamp accepted."
            },
            3 => new DiagnosticEntry
            {
                Time = DateTime.Now,
                Level = "INFO",
                Source = "GOOSE",
                Message = $"GOOSE supervision healthy; {_demoGooseStates.Count} publishers active; stNum/sqNum continuity and TAL valid."
            },
            _ => new DiagnosticEntry
            {
                Time = DateTime.Now,
                Level = "INFO",
                Source = "Control",
                Message = $"{device.Name} CTRL/CSWI1.Pos resolved as DPC SBO Enhanced; status and command termination channels available."
            }
        };

        Logs.AddRange(new[] { entry });
        Logs.TrimStart(2000);
    }

    private Iec61850EventEntry CreateDemoEvent(DemoPointState state, string oldValue, string newValue, DateTime timestamp)
        => new()
        {
            Sequence = ++_demoEventSequence,
            DeviceId = state.Device.DeviceId,
            PointKey = state.Point.PointKey,
            DeviceTimestamp = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            DeviceName = state.Device.Name,
            IpAddress = state.Device.IpAddress,
            SignalName = state.Point.SignalName,
            IecReference = state.Point.IecReference,
            OldValue = oldValue,
            NewValue = newValue,
            Quality = "Good",
            SourceMode = state.Device.AcquisitionMode,
            Reason = "dchg"
        };
}
