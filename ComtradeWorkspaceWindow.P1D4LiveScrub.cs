using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow
{
    // P1D.4 field-scrub path. Visual cursor motion is synchronous and cheap; native analysis is
    // coalesced at WPF composition cadence with at most one worker in flight. A final mouse-up
    // revision invalidates any older in-flight result so the view cannot flash back to a stale
    // phasor/harmonic frame after the user has already stopped scrubbing.
    private bool _p1d4ScrubRenderingHooked;
    private bool _p1d4ScrubWorkerRunning;
    private bool _p1d4ScrubDirty;
    private bool _p1d4FinalRequested;
    private long _p1d4TargetRevision;
    private long _p1d4SettledRevision;
    private AnalysisMode? _p1d4OwnedAnalysisMode;
    private CancellationTokenSource? _p1d4ScrubCts = new();
    private IReadOnlyList<ComtradePhasorChannelDescriptor>? _p1d4VoltageChannels;
    private IReadOnlyList<ComtradePhasorChannelDescriptor>? _p1d4CurrentChannels;

    private void QueueP1D4LiveAnalysisScrub(bool isFinal)
    {
        if (_analysisMode == AnalysisMode.Waveform) return;

        if (_p1d4OwnedAnalysisMode != _analysisMode)
        {
            StopAnalysisRenderingPump();
            ResetAnalysisContext();
            _p1d4OwnedAnalysisMode = _analysisMode;
        }

        unchecked { _p1d4TargetRevision++; }
        if (_p1d4TargetRevision <= 0) _p1d4TargetRevision = 1;
        _p1d4ScrubDirty = true;
        if (isFinal)
        {
            _p1d4FinalRequested = true;
            _p1d4SettledRevision = _p1d4TargetRevision;
        }

        if (!_p1d4ScrubWorkerRunning)
            EnsureP1D4LiveAnalysisRenderingPump();
    }

    private void EnsureP1D4LiveAnalysisRenderingPump()
    {
        if (_p1d4ScrubRenderingHooked) return;
        CompositionTarget.Rendering += P1D4LiveAnalysisCompositionFrame;
        _p1d4ScrubRenderingHooked = true;
    }

    private void StopP1D4LiveAnalysisRenderingPump()
    {
        if (!_p1d4ScrubRenderingHooked) return;
        CompositionTarget.Rendering -= P1D4LiveAnalysisCompositionFrame;
        _p1d4ScrubRenderingHooked = false;
    }

    private void StopP1D4LiveAnalysisScrub()
    {
        StopP1D4LiveAnalysisRenderingPump();
        _p1d4ScrubDirty = false;
        _p1d4FinalRequested = false;
        _p1d4OwnedAnalysisMode = null;
        unchecked { _p1d4TargetRevision++; }
        _p1d4SettledRevision = _p1d4TargetRevision;
        _p1d4ScrubCts?.Cancel();
        _p1d4ScrubCts?.Dispose();
        _p1d4ScrubCts = null;
    }

    private void P1D4LiveAnalysisCompositionFrame(object? sender, EventArgs e)
    {
        if (_analysisMode == AnalysisMode.Waveform)
        {
            StopP1D4LiveAnalysisRenderingPump();
            return;
        }
        if (_p1d4ScrubWorkerRunning || !_p1d4ScrubDirty)
            return;

        _p1d4ScrubDirty = false;
        var final = _p1d4FinalRequested;
        _p1d4FinalRequested = false;
        if (!TryCreateP1D4LiveAnalysisRequest(final, out var request))
        {
            if (!_p1d4ScrubDirty)
                StopP1D4LiveAnalysisRenderingPump();
            return;
        }

        if (IsP1D4AnalysisAlreadyRendered(request))
        {
            if (!_p1d4ScrubDirty)
                StopP1D4LiveAnalysisRenderingPump();
            return;
        }

        _p1d4ScrubWorkerRunning = true;
        StopP1D4LiveAnalysisRenderingPump();
        _ = ExecuteP1D4LiveAnalysisRequestAsync(request);
    }

    private bool TryCreateP1D4LiveAnalysisRequest(bool isFinal, out P1D4LiveAnalysisRequest request)
    {
        request = default;
        if (_record.Info.FrameCount == 0) return false;

        if (_analysisMode == AnalysisMode.Phasor)
        {
            EnsurePhasorCursor();
            var referenceMilliseconds = _phasorCursorMilliseconds ??
                                        (DisturbanceView.ViewStartMilliseconds + DisturbanceView.ViewEndMilliseconds) * 0.5;
            if (!TryResolveDisturbanceFrameAtMilliseconds(referenceMilliseconds, out var frame) &&
                !TryResolveDisturbanceViewportCenterFrame(out frame))
                return false;

            request = new P1D4LiveAnalysisRequest(
                AnalysisMode.Phasor,
                frame,
                null,
                string.Empty,
                referenceMilliseconds,
                _p1d4TargetRevision,
                isFinal);
            return true;
        }

        var harmonicSignals = ResolveP1D4HarmonicOverviewSignals();
        if (harmonicSignals.Count == 0) return false;

        EnsureHarmonicCursor();
        var harmonicMilliseconds = _harmonicCursorMilliseconds ??
                                   (DisturbanceView.ViewStartMilliseconds + DisturbanceView.ViewEndMilliseconds) * 0.5;
        if (!TryResolveDisturbanceFrameAtMilliseconds(harmonicMilliseconds, out var harmonicFrame) &&
            !TryResolveDisturbanceViewportCenterFrame(out harmonicFrame))
            return false;

        request = new P1D4LiveAnalysisRequest(
            AnalysisMode.Harmonics,
            harmonicFrame,
            harmonicSignals,
            BuildP1D4HarmonicOverviewSignature(harmonicSignals),
            harmonicMilliseconds,
            _p1d4TargetRevision,
            isFinal);
        return true;
    }

    private bool IsP1D4AnalysisAlreadyRendered(P1D4LiveAnalysisRequest request)
        => request.Mode == AnalysisMode.Phasor
            ? request.ReferenceFrame == _lastRenderedPhasorFrame
            : request.ReferenceFrame == _p1d4LastRenderedHarmonicOverviewFrame &&
              string.Equals(
                  request.HarmonicSignature,
                  _p1d4LastRenderedHarmonicOverviewSignature,
                  StringComparison.Ordinal);

    private async Task ExecuteP1D4LiveAnalysisRequestAsync(P1D4LiveAnalysisRequest request)
    {
        try
        {
            var token = EnsureP1D4ScrubToken();
            if (request.Mode == AnalysisMode.Phasor)
            {
                if (!_phasorFrameCache.TryGetValue(request.ReferenceFrame, out var phasor))
                {
                    phasor = await LoadP1D4PhasorWorkspaceAsync(request.ReferenceFrame, token).ConfigureAwait(true);
                    RememberPhasor(request.ReferenceFrame, phasor);
                }

                if (!ShouldPresentP1D4LiveAnalysis(request)) return;
                PresentPhasor(request.ReferenceFrame, request.ReferenceMilliseconds, phasor);
            }
            else if (request.HarmonicSignals is { Count: > 0 } harmonicSignals)
            {
                var overview = await LoadP1D4HarmonicOverviewAsync(
                    harmonicSignals,
                    request.ReferenceFrame,
                    token).ConfigureAwait(true);

                if (!ShouldPresentP1D4LiveAnalysis(request)) return;
                PresentP1D4HarmonicOverview(
                    request.ReferenceFrame,
                    request.ReferenceMilliseconds,
                    request.HarmonicSignature,
                    overview);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            if (ShouldPresentP1D4LiveAnalysis(request))
                StatusTextBlock.Text = $"Native COMTRADE live analysis failed: {ex.Message}";
        }
        finally
        {
            _p1d4ScrubWorkerRunning = false;
            if (_p1d4ScrubDirty && _analysisMode != AnalysisMode.Waveform)
                EnsureP1D4LiveAnalysisRenderingPump();
            else
                StopP1D4LiveAnalysisRenderingPump();
        }
    }

    private bool ShouldPresentP1D4LiveAnalysis(P1D4LiveAnalysisRequest request)
    {
        if (request.Mode != _analysisMode)
            return false;

        if (request.Mode == AnalysisMode.Harmonics)
        {
            var currentSignals = ResolveP1D4HarmonicOverviewSignals();
            if (!string.Equals(
                    request.HarmonicSignature,
                    BuildP1D4HarmonicOverviewSignature(currentSignals),
                    StringComparison.Ordinal))
                return false;
        }

        return request.Revision >= _p1d4SettledRevision;
    }

    private CancellationToken EnsureP1D4ScrubToken()
    {
        if (_p1d4ScrubCts is null || _p1d4ScrubCts.IsCancellationRequested)
        {
            _p1d4ScrubCts?.Dispose();
            _p1d4ScrubCts = new CancellationTokenSource();
        }
        return _p1d4ScrubCts.Token;
    }

    private async Task<ComtradePhasorWorkspaceResult> LoadP1D4PhasorWorkspaceAsync(
        ulong referenceFrame,
        CancellationToken token)
    {
        await _nativeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                EnsureP1D4PhasorChannelSets(token);
                return new ComtradePhasorWorkspaceResult(
                    ReadPhasorVectors(_p1d4VoltageChannels!, referenceFrame, token),
                    ReadPhasorVectors(_p1d4CurrentChannels!, referenceFrame, token));
            }, token).ConfigureAwait(false);
        }
        finally
        {
            _nativeGate.Release();
        }
    }

    private void EnsureP1D4PhasorChannelSets(CancellationToken token)
    {
        if (_p1d4VoltageChannels is not null && _p1d4CurrentChannels is not null)
            return;

        var descriptors = new List<ComtradePhasorChannelDescriptor>(_record.AnalogChannels.Count);
        for (var index = 0; index < _record.AnalogChannels.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var channel = _record.AnalogChannels[index];
            var hasSemantics = _record.TryReadAnalogSemantics(checked((uint)index), out var semantics) && semantics is not null;
            var fallbackPhase = NormalizePhase(channel.Phase, channel.Id);
            var role = hasSemantics
                ? semantics!.Role
                : ResolveAnalogSection(channel.Units, null) switch
                {
                    "Voltage" => ComtradePhasorWorkspaceMath.RoleVoltage,
                    "Current" => ComtradePhasorWorkspaceMath.RoleCurrent,
                    _ => 0
                };
            var phaseRole = hasSemantics
                ? semantics!.PhaseRole
                : ComtradePhasorWorkspaceMath.PhaseRoleFromCanonicalName(fallbackPhase);
            descriptors.Add(new ComtradePhasorChannelDescriptor(
                checked((uint)index),
                role,
                phaseRole,
                channel.Id,
                ComtradePhasorWorkspaceMath.CanonicalPhaseName(phaseRole, fallbackPhase),
                channel.Circuit,
                channel.Units));
        }

        _p1d4VoltageChannels = ComtradePhasorWorkspaceMath
            .SelectRoleSet(descriptors, ComtradePhasorWorkspaceMath.RoleVoltage)
            .ToArray();
        _p1d4CurrentChannels = ComtradePhasorWorkspaceMath
            .SelectRoleSet(descriptors, ComtradePhasorWorkspaceMath.RoleCurrent)
            .ToArray();
    }

    private readonly record struct P1D4LiveAnalysisRequest(
        AnalysisMode Mode,
        ulong ReferenceFrame,
        IReadOnlyList<ComtradeSignalItem>? HarmonicSignals,
        string HarmonicSignature,
        double ReferenceMilliseconds,
        long Revision,
        bool IsFinal);
}
