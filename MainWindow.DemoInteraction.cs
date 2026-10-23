using System.Globalization;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private void StartDemoDevice(Iec61850MonitorDevice device)
    {
        device.IsConnected = true;
        device.IsMonitoring = true;
        device.HasReportStream = true;
        device.Status = "Monitoring • report active";
        device.Detail = "MMS association established. Dynamic report acquisition and command feedback are active.";

        if (device.Points.Count == 0)
        {
            foreach (var state in _demoPointStates.Where(item => ReferenceEquals(item.Device, device)))
            {
                device.Points.Add(state.Point);
                GlobalPoints.Add(state.Point);
                _pointIndex[state.Point.PointKey] = state.Point;
            }
        }

        device.RefreshComputed();
        RaiseWorkspaceCounts();
        AddLog("INFO", device.Name, $"MMS association restored; {device.AcquisitionMode} enabled for {device.Points.Count} point(s).");
        SetStatus($"{device.Name}: monitoring started • {device.AcquisitionMode}");
    }

    private void StopDemoDevice(Iec61850MonitorDevice device)
    {
        if (device.IsMonitoring)
        {
            device.IsMonitoring = false;
            device.HasReportStream = false;
            device.ReportPulseActive = false;
            _reportPulseUntil.Remove(device.DeviceId);
            RemoveDeviceHighlights(device.DeviceId);
            RemoveDevicePoints(device.DeviceId);
            device.Points.Clear();
            device.Status = "Connected • monitoring stopped";
            device.Detail = "MMS association remains active. Press Start to re-enable the dynamic report stream.";
            device.RefreshComputed();
            RaiseWorkspaceCounts();
            AddLog("INFO", device.Name, "Report monitoring stopped; MMS association remains available.");
            SetStatus($"{device.Name}: monitoring stopped; press Start to resume.");
            return;
        }

        if (device.IsConnected)
        {
            device.IsConnected = false;
            device.Status = "Disconnected";
            device.Detail = "Communication session closed. Press Start to reconnect using the cached IED model.";
            device.RefreshComputed();
            RaiseWorkspaceCounts();
            AddLog("INFO", device.Name, "MMS association released; cached model retained.");
            SetStatus($"{device.Name}: disconnected; press Start to reconnect.");
        }
    }

    private void RemoveDemoDevice(Iec61850MonitorDevice device)
    {
        RemoveDeviceHighlights(device.DeviceId);
        RemoveDevicePoints(device.DeviceId);
        device.Points.Clear();

        foreach (var signal in device.Signals)
        {
            signal.PropertyChanged -= Signal_PropertyChanged;
            _signalOwners.Remove(signal);
        }

        for (var index = _demoPointStates.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(_demoPointStates[index].Device, device))
                _demoPointStates.RemoveAt(index);
        }

        for (var index = Events.Count - 1; index >= 0; index--)
        {
            if (Events[index].DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase))
                Events.RemoveAt(index);
        }

        Devices.Remove(device);
        SelectedDevice = Devices.FirstOrDefault();
        RaiseWorkspaceCounts();
        AddLog("INFO", device.Name, "IED removed from the communication workspace.");
        SetStatus($"{device.Name}: removed from the workspace.");
    }

    private async Task ExecuteDemoControlAsync(
        Iec61850MonitorDevice device,
        SignalDefinition signal,
        ControlCommandClaim claim)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(360), _applicationCancellation.Token);

        var requested = claim.RequestedValue.Contains("Open", StringComparison.OrdinalIgnoreCase)
            ? "Open [01]"
            : claim.RequestedValue.Contains("Close", StringComparison.OrdinalIgnoreCase)
                ? "Closed [10]"
                : claim.RequestedValue;
        var previous = signal.ControlCurrentValue;
        signal.ControlCurrentValue = requested;
        signal.ControlLastResult = $"CommandTermination: success • feedback {(_demoRandom.Next(286, 468)):N0} ms";

        var position = _demoPointStates.FirstOrDefault(state =>
            ReferenceEquals(state.Device, device) &&
            state.Point.IecReference.Contains("CSWI1.Pos.stVal", StringComparison.OrdinalIgnoreCase));
        if (position is not null)
        {
            var timestamp = DateTime.Now.AddMilliseconds(-_demoRandom.Next(4, 22));
            position.Point.Value = requested;
            position.Point.Quality = "Good";
            position.Point.DeviceTimestamp = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            position.Point.SourceMode = device.AcquisitionMode;
            position.Point.Reason = "dchg";
            position.Point.Sequence++;
            position.Point.IsRecentlyChanged = true;
            position.Signal.Value = requested;
            position.Signal.Quality = "Good";
            position.Signal.DeviceTimestamp = position.Point.DeviceTimestamp;
            position.Signal.Timestamp = timestamp;
            _pointHighlightUntil[position.Point.PointKey] = DateTime.UtcNow.AddSeconds(3);

            Events.AddRange(new[] { CreateDemoEvent(position, previous, requested, timestamp) });
            if (MainTabs.SelectedIndex != 2)
                device.AddUnreadEvents(1);
        }

        var gooseState = _demoGooseStates.FirstOrDefault(state =>
            state.Spec.IedName.Equals(device.Name, StringComparison.OrdinalIgnoreCase));
        if (gooseState is not null)
        {
            var leafIndex = gooseState.Leaves.FindIndex(leaf =>
                leaf.Path.Contains("CSWI1.Pos.stVal", StringComparison.OrdinalIgnoreCase) ||
                leaf.Path.Contains("XCBR1.Pos.stVal", StringComparison.OrdinalIgnoreCase));
            if (leafIndex >= 0)
            {
                var leaf = gooseState.Leaves[leafIndex];
                leaf.PreviousValue = leaf.Value;
                leaf.Value = requested;
                gooseState.StateNumber++;
                gooseState.SequenceNumber = 0;
                var now = DateTimeOffset.Now;
                gooseState.Row.Apply(BuildDemoGooseSnapshot(gooseState, leafIndex, string.Empty, now));
                GooseEvents.Add(new GooseEventRow
                {
                    StreamKey = gooseState.Spec.StreamKey,
                    Timestamp = now,
                    DeltaText = FormatGooseDelta(now - gooseState.LastTimelineTimestamp),
                    EventText = "State change",
                    EventTone = "Change",
                    Publisher = device.Name,
                    StateSequenceText = $"{gooseState.StateNumber} / 0",
                    Summary = $"Breaker position: {requested}"
                });
                gooseState.LastTimelineTimestamp = now;
                while (GooseEvents.Count > 300)
                    GooseEvents.RemoveAt(0);
                RaiseGoosePresentationState();
            }
        }

        AddLog("INFO", device.Name,
            $"SBO enhanced command completed: {signal.DisplayReference} = {requested}; CommandTermination positive; process feedback confirmed.");
        SetStatus($"{device.Name}: circuit breaker {requested} • command and feedback complete.");
        RaiseWorkspaceCounts();
    }
}
