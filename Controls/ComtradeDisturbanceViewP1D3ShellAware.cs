using System.Windows.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester.Controls;

/// <summary>
/// Thin host around the optimized P1D.3 renderer. It reconciles CFG absolute Start/Trigger time
/// with legacy DAT files whose first raw timestamp is not zero, while keeping the renderer/host
/// source-frame coordinate system unchanged.
/// </summary>
public sealed class ComtradeDisturbanceViewP1D3ShellAware : Grid
{
    private readonly ComtradeDisturbanceViewP1D3 _inner = new();
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
        InitializeTimeOrigin(tracks, timeMultiplier);
        _effectiveTriggerMilliseconds = triggerMilliseconds is { } trigger && double.IsFinite(trigger)
            ? trigger + _timeOriginMilliseconds
            : null;
        _inner.ShowTracks(tracks, timeMultiplier, _effectiveTriggerMilliseconds, preserveCursor);
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

        // Initial workspace load is the full record. Preserve a conservative fallback for unusual
        // decimators that omit source-frame identity while still retaining the first DAT timestamp.
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
