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
            state.Point.Quality = "Good • validity=good";
            state.Point.DeviceTimestamp = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            state.Point.SourceMode = _demoTick % 5 == 0 ? "MMS validation read" : "Buffered Report • dupd";
            state.Point.Reason = _demoTick % 5 == 0 ? "verification" : "dupd";
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
        state.Point.Quality = "Good • validity=good";
        state.Point.DeviceTimestamp = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        state.Point.SourceMode = "Buffered Report • dchg";
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

        var demoEvent = CreateDemoEvent(state, oldValue, nextValue, timestamp);
        Events.AddRange(new[] { demoEvent });
        Events.TrimStart(10000);
        if (MainTabs.SelectedIndex != 2)
            state.Device.AddUnreadEvents(1);
        LastStatusText = $"DEMO EVENT • {state.Device.Name} / {state.Point.SignalName} = {nextValue}";
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
            Quality = "Good • validity=good",
            SourceMode = "Buffered Report • dchg",
            Reason = "dchg"
        };
}
