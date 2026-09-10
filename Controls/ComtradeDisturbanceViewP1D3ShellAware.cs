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
        _inner.NavigationChanged += (_, e) => NavigationChanged?.Invoke(this, e);
        _inner.CursorChanged += (_, e) => CursorChanged?.Invoke(this, e);
        _inner.PanRequested += (_, e) => PanRequested?.Invoke(this, e);
        _inner.ToolTip = null;
        ToolTip = null;
    }

    internal void ShowTracks(
        IReadOnlyList<ComtradeDisturbanceTrack> tracks,
        double timeMultiplier,
        double? triggerMilliseconds,
        bool preserveCursor = true)
    {
        _timeMultiplier = timeMultiplier > 0 && double.IsFinite(timeMultiplier) ? timeMultiplier : 1.0;

        // Canonical workstation ordering: analog traces always stay above the protection/digital
        // timeline, regardless of the order in which checkboxes were activated. This is a stable
        // two-pass partition (O(n), one bounded array, no comparison sort) so order inside each
        // category remains deterministic and no extra churn is introduced on the render hot path.
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
    internal void ResetNavigation() => _inner.ResetNavigation();
    internal void SetViewWindow(double startMilliseconds, double endMilliseconds) => _inner.SetViewWindow(startMilliseconds, endMilliseconds);
    internal double PlotFractionAt(double x) => _inner.PlotFractionAt(x);

    /// <summary>
    /// Places a Time Signals cursor from the common shell and returns the actual snapped value.
    /// The exact same sorted digital-edge index and pixel-derived tolerance are used for both the
    /// ruler and waveform, eliminating the previous split cursor identities.
    /// </summary>
    internal double PlaceCursorFromShell(
        ComtradeDisturbanceCursor cursor,
        double requestedMilliseconds,
        double snapToleranceMilliseconds,
        bool isFinal)
    {
        var value = SnapAnalysisCursorFromShell(requestedMilliseconds, snapToleranceMilliseconds);
        _inner.SetCursorFromHost(cursor, value);
        CursorChanged?.Invoke(this, new ComtradeDisturbanceCursorChangedEventArgs(
            cursor,
            value,
            Math.Max(0.0, snapToleranceMilliseconds),
            isFinal,
            Math.Abs(value - requestedMilliseconds) > 1e-9));
        return value;
    }

    /// <summary>
    /// P/H analysis cursors use the same visible digital-edge snap index without becoming C1/C2.
    /// </summary>
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
        foreach (var track in tracks)
        {
            if (!track.IsDigital) continue;
            if (track.DigitalEdges is { Count: > 0 })
            {
                times.AddRange(track.DigitalEdges.Select(edge => ComtradeTimeMath.ToMilliseconds(edge.Timestamp, _timeMultiplier)));
                continue;
            }
            if (track.Digital is null) continue;
            var count = Math.Min(track.Digital.Length, track.Timestamps.Length);
            for (var i = 1; i < count; i++)
            {
                if ((track.Digital[i - 1] != 0) != (track.Digital[i] != 0))
                    times.Add(ComtradeTimeMath.ToMilliseconds(track.Timestamps[i], _timeMultiplier));
            }
        }
        if (times.Count == 0) return Array.Empty<double>();
        times.Sort();
        var unique = new List<double>(times.Count) { times[0] };
        for (var i = 1; i < times.Count; i++)
        {
            if (Math.Abs(times[i] - unique[^1]) > 1e-9)
                unique.Add(times[i]);
        }
        return unique.ToArray();
    }

    private void InitializeTimeOrigin(IReadOnlyList<ComtradeDisturbanceTrack> tracks, double timeMultiplier)
    {
        if (_timeOriginInitialized || tracks.Count == 0)
            return;

        foreach (var track in tracks)
        {
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

        var first = tracks
            .Where(track => track.Timestamps.Length > 0)
            .Select(track => track.Timestamps[0])
            .DefaultIfEmpty(0u)
            .Min();
        _firstRawTimestamp = first;
        _timeOriginMilliseconds = ComtradeTimeMath.ToMilliseconds(first, timeMultiplier);
        _timeOriginInitialized = true;
    }
}