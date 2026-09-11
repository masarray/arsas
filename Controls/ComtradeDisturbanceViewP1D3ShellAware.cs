using System.Windows.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester.Controls;

/// <summary>
/// Thin host around the optimized P1D.3 renderer. It reconciles CFG absolute Start/Trigger time
/// with legacy DAT files whose first raw timestamp is not zero, and is the single cursor-snap
/// authority shared by the waveform and the workstation ruler.
/// </summary>
public sealed class ComtradeDisturbanceViewP1D3ShellAware : Grid
{
    private const double PlotLabelWidth = 150.0;
    private const double PlotRightMargin = 18.0;
    private readonly ComtradeDisturbanceViewP1D3 _inner = new();
    private double[] _snapTimesMilliseconds = Array.Empty<double>();
    private double _timeMultiplier = 1.0;
    private bool _timeOriginInitialized;
    private uint _firstRawTimestamp;
    private double _timeOriginMilliseconds;
    private double? _effectiveTriggerMilliseconds;
    private double _lastRelayedViewStartMilliseconds = double.NaN;
    private double _lastRelayedViewEndMilliseconds = double.NaN;

    internal event EventHandler<ComtradeDisturbanceNavigationChangedEventArgs>? NavigationChanged;
    internal event EventHandler<ComtradeDisturbanceCursorChangedEventArgs>? CursorChanged;
    internal event EventHandler<ComtradeDisturbancePanRequestedEventArgs>? PanRequested;

    internal double? Cursor1Milliseconds => _inner.Cursor1Milliseconds;
    internal double? Cursor2Milliseconds => _inner.Cursor2Milliseconds;
    internal double? CursorAMilliseconds => _inner.CursorAMilliseconds;
    internal double? CursorBMilliseconds => _inner.CursorBMilliseconds;
    internal double ViewStartMilliseconds => _inner.ViewStartMilliseconds;
    internal double ViewEndMilliseconds => _inner.ViewEndMilliseconds;
    internal double FullStartMilliseconds => _inner.FullStartMilliseconds;
    internal double FullEndMilliseconds => _inner.FullEndMilliseconds;
    internal double? EffectiveTriggerMilliseconds => _effectiveTriggerMilliseconds;
    internal uint FirstRawTimestamp => _firstRawTimestamp;
    internal double TimeOriginMilliseconds => _timeOriginMilliseconds;
    internal double PlotLeftInset => PlotLabelWidth;
    internal double PlotRightInset => PlotRightMargin;

    public ComtradeDisturbanceViewP1D3ShellAware()
    {
        Children.Add(_inner);
        _inner.NavigationChanged += Inner_NavigationChanged;
        _inner.CursorChanged += Inner_CursorChanged;
        _inner.PanRequested += Inner_PanRequested;
        _inner.ToolTip = null;
        ToolTip = null;
    }

    private void Inner_NavigationChanged(object? sender, ComtradeDisturbanceNavigationChangedEventArgs e)
    {
        var start = _inner.ViewStartMilliseconds;
        var end = _inner.ViewEndMilliseconds;
        if (NearlyEqual(start, _lastRelayedViewStartMilliseconds) &&
            NearlyEqual(end, _lastRelayedViewEndMilliseconds))
            return;

        _lastRelayedViewStartMilliseconds = start;
        _lastRelayedViewEndMilliseconds = end;
        NavigationChanged?.Invoke(this, e);
    }

    private void Inner_CursorChanged(object? sender, ComtradeDisturbanceCursorChangedEventArgs e)
    {
        CursorChanged?.Invoke(this, e);
    }

    private void Inner_PanRequested(object? sender, ComtradeDisturbancePanRequestedEventArgs e)
        => PanRequested?.Invoke(this, e);

    internal void ShowTracks(
        IReadOnlyList<ComtradeDisturbanceTrack> tracks,
        double timeMultiplier,
        double? triggerMilliseconds,
        bool preserveCursor = true)
    {
        _timeMultiplier = timeMultiplier > 0 && double.IsFinite(timeMultiplier) ? timeMultiplier : 1.0;
        var orderedTracks = StableAnalogThenDigital(tracks);

        InitializeTimeOrigin(orderedTracks, _timeMultiplier);
        _effectiveTriggerMilliseconds = triggerMilliseconds is { } trigger && double.IsFinite(trigger)
            ? trigger + _timeOriginMilliseconds
            : null;
        _snapTimesMilliseconds = BuildSnapIndex(orderedTracks);
        _inner.ShowTracks(orderedTracks, _timeMultiplier, _effectiveTriggerMilliseconds, preserveCursor);
        _inner.ToolTip = null;
    }

    internal void ShowMessage(string message) => _inner.ShowMessage(message);
    internal void ApplyTriggerFocusedDefault(double nominalFrequencyHz) => _inner.ApplyTriggerFocusedDefault(nominalFrequencyHz);
    internal void ResetToTriggerView() => _inner.ResetToTriggerView();
    internal void SetCursorAFromAbsoluteMilliseconds(double milliseconds) => _inner.SetCursorAFromAbsoluteMilliseconds(milliseconds);
    internal void SetCursorFromHost(ComtradeDisturbanceCursor cursor, double milliseconds) => _inner.SetCursorFromHost(cursor, milliseconds);
    internal void SetAnalogRepresentationLabel(string representation) => _inner.SetAnalogRepresentationLabel(representation);
    internal void ResetNavigation() => _inner.ResetNavigation();
    internal void SetViewWindow(double startMilliseconds, double endMilliseconds) => _inner.SetViewWindow(startMilliseconds, endMilliseconds);
    internal double PlotFractionAt(double x) => _inner.PlotFractionAt(x);

    internal double PlaceCursorFromShell(
        ComtradeDisturbanceCursor cursor,
        double requestedMilliseconds,
        double snapToleranceMilliseconds,
        bool isFinal)
    {
        var value = SnapAnalysisCursorFromShell(requestedMilliseconds, snapToleranceMilliseconds);
        _inner.SetCursorFromHost(cursor, value);
        if (isFinal)
        {
            CursorChanged?.Invoke(this, new ComtradeDisturbanceCursorChangedEventArgs(
                cursor,
                value,
                Math.Max(0.0, snapToleranceMilliseconds),
                true,
                Math.Abs(value - requestedMilliseconds) > 1e-9));
        }
        return value;
    }

    internal double SnapAnalysisCursorFromShell(double requestedMilliseconds, double snapToleranceMilliseconds)
    {
        if (!double.IsFinite(requestedMilliseconds))
            return FullStartMilliseconds;
        var clamped = Math.Clamp(requestedMilliseconds, FullStartMilliseconds, FullEndMilliseconds);
        return ComtradeInteractionPerformanceMath.TrySnapSorted(
            _snapTimesMilliseconds,
            clamped,
            Math.Max(0.0, snapToleranceMilliseconds),
            out var snapped)
            ? snapped
            : clamped;
    }

    internal static IReadOnlyList<ComtradeDisturbanceTrack> StableAnalogThenDigital(
        IReadOnlyList<ComtradeDisturbanceTrack> tracks)
    {
        if (tracks.Count <= 1)
            return tracks;

        var requiresReorder = false;
        var sawDigital = false;
        for (var index = 0; index < tracks.Count; index++)
        {
            if (tracks[index].IsDigital)
            {
                sawDigital = true;
                continue;
            }
            if (sawDigital)
            {
                requiresReorder = true;
                break;
            }
        }

        if (!requiresReorder)
            return tracks;

        var ordered = new ComtradeDisturbanceTrack[tracks.Count];
        var write = 0;
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            if (!track.IsDigital)
                ordered[write++] = track;
        }
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            if (track.IsDigital)
                ordered[write++] = track;
        }
        return ordered;
    }

    private double[] BuildSnapIndex(IReadOnlyList<ComtradeDisturbanceTrack> tracks)
    {
        var times = new List<double>();
        for (var trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
        {
            var track = tracks[trackIndex];
            if (!track.IsDigital) continue;
            if (track.DigitalEdges is { Count: > 0 } edges)
            {
                for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
                    times.Add(ComtradeTimeMath.ToMilliseconds(edges[edgeIndex].Timestamp, _timeMultiplier));
                continue;
            }
            if (track.Digital is null) continue;
            var count = Math.Min(track.Digital.Length, track.Timestamps.Length);
            for (var index = 1; index < count; index++)
            {
                if ((track.Digital[index - 1] != 0) != (track.Digital[index] != 0))
                    times.Add(ComtradeTimeMath.ToMilliseconds(track.Timestamps[index], _timeMultiplier));
            }
        }
        if (times.Count == 0) return Array.Empty<double>();

        times.Sort();
        var unique = new double[times.Count];
        var uniqueCount = 1;
        unique[0] = times[0];
        for (var index = 1; index < times.Count; index++)
        {
            if (Math.Abs(times[index] - unique[uniqueCount - 1]) <= 1e-9)
                continue;
            unique[uniqueCount++] = times[index];
        }
        if (uniqueCount == unique.Length)
            return unique;
        Array.Resize(ref unique, uniqueCount);
        return unique;
    }

    private void InitializeTimeOrigin(IReadOnlyList<ComtradeDisturbanceTrack> tracks, double timeMultiplier)
    {
        if (_timeOriginInitialized || tracks.Count == 0)
            return;

        for (var trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
        {
            var track = tracks[trackIndex];
            var count = Math.Min(track.Timestamps.Length, track.SourceFrames?.Length ?? 0);
            for (var index = 0; index < count; index++)
            {
                if (track.SourceFrames![index] != 0) continue;
                _firstRawTimestamp = track.Timestamps[index];
                _timeOriginMilliseconds = ComtradeTimeMath.ToMilliseconds(_firstRawTimestamp, timeMultiplier);
                _timeOriginInitialized = true;
                return;
            }
        }

        var found = false;
        var first = uint.MaxValue;
        for (var trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
        {
            var timestamps = tracks[trackIndex].Timestamps;
            if (timestamps.Length == 0) continue;
            if (!found || timestamps[0] < first)
            {
                first = timestamps[0];
                found = true;
            }
        }
        _firstRawTimestamp = found ? first : 0u;
        _timeOriginMilliseconds = ComtradeTimeMath.ToMilliseconds(_firstRawTimestamp, timeMultiplier);
        _timeOriginInitialized = true;
    }

    private static bool NearlyEqual(double left, double right)
    {
        if (!double.IsFinite(left) || !double.IsFinite(right))
            return false;
        return Math.Abs(left - right) <= 1e-9 * Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));
    }
}
