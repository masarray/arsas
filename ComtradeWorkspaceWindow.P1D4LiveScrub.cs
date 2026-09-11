using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow
{
    private const int P1D4PhasorCacheCapacity = 64;

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
    private readonly BoundedFifoCache<ulong, ComtradePhasorWorkspaceResult> _p1d4PhasorFrameCache =
        new(P1D4PhasorCacheCapacity);

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
            var outcome = await TryLoadP1D4LiveAnalysisAsync(request, EnsureP1D4ScrubToken()).ConfigureAwait(true);
            if (outcome.State == P1D4LiveAnalysisState.Cancelled || !ShouldPresentP1D4LiveAnalysis(request))
                return;

            if (outcome.State == P1D4LiveAnalysisState.Failed)
            {
                StatusTextBlock.Text = outcome.OperatorMessage;
                return;
            }

            if (request.Mode == AnalysisMode.Phasor && outcome.Phasor is { } phasor)
            {
                PresentPhasor(request.ReferenceFrame, request.ReferenceMilliseconds, phasor);
                return;
            }

            if (request.Mode == AnalysisMode.Harmonics && outcome.Harmonics is { Count: > 0 } overview)
            {
                PresentP1D4HarmonicOverview(
                    request.ReferenceFrame,
                    request.ReferenceMilliseconds,
                    request.HarmonicSignature,
                    overview);
            }
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

    /// <summary>
    /// Native/framework exceptions are contained at this asynchronous boundary and translated into
    /// an explicit result state. Expected cancellation never reaches presentation as an error.
    /// Detailed exception context is handed to the bounded diagnostic queue without blocking UI.
    /// </summary>
    private async Task<P1D4LiveAnalysisOutcome> TryLoadP1D4LiveAnalysisAsync(
        P1D4LiveAnalysisRequest request,
        CancellationToken token)
    {
        try
        {
            if (token.IsCancellationRequested)
                return P1D4LiveAnalysisOutcome.Cancelled();

            if (request.Mode == AnalysisMode.Phasor)
            {
                if (!_p1d4PhasorFrameCache.TryGetValue(request.ReferenceFrame, out var phasor))
                {
                    phasor = await LoadP1D4PhasorWorkspaceAsync(request.ReferenceFrame, token).ConfigureAwait(true);
                    if (token.IsCancellationRequested)
                        return P1D4LiveAnalysisOutcome.Cancelled();
                    _p1d4PhasorFrameCache.Set(request.ReferenceFrame, phasor);
                }
                return P1D4LiveAnalysisOutcome.Success(phasor);
            }

            if (request.HarmonicSignals is not { Count: > 0 } harmonicSignals)
            {
                return P1D4LiveAnalysisOutcome.Failed(
                    "HARMONIC_SELECTION_EMPTY",
                    "Native COMTRADE harmonics unavailable: no checked analog channel is active.");
            }

            var overview = await LoadP1D4HarmonicOverviewAsync(
                harmonicSignals,
                request.ReferenceFrame,
                token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
                return P1D4LiveAnalysisOutcome.Cancelled();
            return P1D4LiveAnalysisOutcome.Success(overview);
        }
        catch (OperationCanceledException)
        {
            return P1D4LiveAnalysisOutcome.Cancelled();
        }
        catch (ObjectDisposedException)
        {
            return P1D4LiveAnalysisOutcome.Cancelled();
        }
        catch (Exception ex)
        {
            var code = request.Mode == AnalysisMode.Phasor
                ? "PHASOR_NATIVE_FAILURE"
                : "HARMONIC_NATIVE_FAILURE";
            ComtradeDiagnosticQueue.TryEnqueue(
                "P1D4.LiveAnalysis",
                code,
                $"Mode={request.Mode}; frame={request.ReferenceFrame}; revision={request.Revision}; final={request.IsFinal}",
                ex);
            return P1D4LiveAnalysisOutcome.Failed(
                code,
                $"Native COMTRADE {request.Mode.ToString().ToLowerInvariant()} analysis is unavailable at this reference. Diagnostics captured.");
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

    private enum P1D4LiveAnalysisState
    {
        Success,
        Cancelled,
        Failed
    }

    private readonly record struct P1D4LiveAnalysisOutcome(
        P1D4LiveAnalysisState State,
        ComtradePhasorWorkspaceResult? Phasor,
        IReadOnlyList<P1D4HarmonicOverviewEntry>? Harmonics,
        string ErrorCode,
        string OperatorMessage)
    {
        internal static P1D4LiveAnalysisOutcome Success(ComtradePhasorWorkspaceResult phasor)
            => new(P1D4LiveAnalysisState.Success, phasor, null, string.Empty, string.Empty);

        internal static P1D4LiveAnalysisOutcome Success(IReadOnlyList<P1D4HarmonicOverviewEntry> harmonics)
            => new(P1D4LiveAnalysisState.Success, null, harmonics, string.Empty, string.Empty);

        internal static P1D4LiveAnalysisOutcome Cancelled()
            => new(P1D4LiveAnalysisState.Cancelled, null, null, "CANCELLED", string.Empty);

        internal static P1D4LiveAnalysisOutcome Failed(string code, string operatorMessage)
            => new(P1D4LiveAnalysisState.Failed, null, null, code, operatorMessage);
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
