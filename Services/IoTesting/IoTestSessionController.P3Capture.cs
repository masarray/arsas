using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed partial class IoTestSessionController
{
    private readonly Dictionary<string, P3StableCaptureState> _p3StableCapture = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _p3PendingPairPointIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Atomic manual recapture for one slot. All rows are preflighted first, all immutable
    /// evidence records are appended as one journal batch, and only then are the replaceable
    /// current V1/V2 pointers promoted. No partial pointer mutation is possible on failure.
    /// OperatorRecapture is deliberately valid for automatic-transition and snapshot rows.
    /// </summary>
    public IoTestSessionActionResult RecaptureBatch(
        IReadOnlyCollection<IoTestPointPlan> points,
        FatValueSlot slot,
        FatEvidenceCaptureKind captureKind = FatEvidenceCaptureKind.OperatorRecapture)
    {
        ThrowIfDisposed();
        if (State != IoTestSessionState.Running || ActiveIed == null)
            return IoTestSessionActionResult.Failure("Start the owning IED FAT session before recapturing Value 1 or Value 2.");
        if (points == null || points.Count == 0)
            return IoTestSessionActionResult.Failure("Select one or more FAT rows before recapturing evidence.");
        if (captureKind is not (FatEvidenceCaptureKind.OperatorRecapture or FatEvidenceCaptureKind.OperatorSnapshot))
            return IoTestSessionActionResult.Failure("Manual capture requires OperatorSnapshot or OperatorRecapture provenance.");

        var distinct = points.Distinct().ToArray();
        var invalid = distinct.Where(point =>
                !ActiveIed.TestPoints.Contains(point) ||
                !_sessionPointIds.Contains(point.TestPointId) ||
                !point.WorkspaceSelected ||
                !point.IsIncludedInFat ||
                !point.TestEnabled ||
                !point.ImportReady ||
                !_activeLivePoints.ContainsKey(point.TestPointId) ||
                (captureKind == FatEvidenceCaptureKind.OperatorSnapshot && point.CaptureMode != FatCaptureMode.OperatorSnapshot))
            .ToArray();
        if (invalid.Length > 0)
        {
            return IoTestSessionActionResult.Failure(
                $"{invalid.Length} of {distinct.Length} selected row(s) are not eligible/live in this IED session — no evidence changed.");
        }

        var prepared = new List<(IoTestPointPlan Point, FatValueEvidence Evidence)>(distinct.Length);
        try
        {
            foreach (var point in distinct)
            {
                var livePoint = _activeLivePoints[point.TestPointId];
                var observation = new FatLiveValueObservation(
                    livePoint.Value,
                    DateTimeOffset.UtcNow,
                    IoTestValueNormalizer.ParseIedTimestamp(livePoint.DeviceTimestamp),
                    livePoint.Quality,
                    livePoint.SourceMode,
                    livePoint.Sequence,
                    _connectionGeneration);
                var evidence = FatOperatorSnapshotCaptureService.CreateEvidence(slot, observation, captureKind);
                prepared.Add((point, evidence));
            }

            AppendBatchRequired(prepared.Select(item => FatValueEvent(item.Point, item.Evidence)).ToArray());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return IoTestSessionActionResult.Failure(ex.Message);
        }

        foreach (var item in prepared)
        {
            item.Point.Runtime.SetFatValueEvidence(item.Evidence);
            item.Point.Runtime.StatusReason =
                $"{(slot == FatValueSlot.Value1 ? "Value 1" : "Value 2")} manually recaptured; prior evidence remains in immutable history.";
        }

        if (captureKind == FatEvidenceCaptureKind.OperatorRecapture)
        {
            foreach (var point in distinct)
                _p3PendingPairPointIds.Remove(point.TestPointId);
        }

        RaiseProgress();
        UpdateRunningCompletionStatus();
        var label = slot == FatValueSlot.Value1 ? "Value 1" : "Value 2";
        return IoTestSessionActionResult.Success(
            $"{distinct.Length} signal(s) · {label} recaptured · {ActiveIed.IedName} · {DateTime.Now:HH:mm:ss.fff}");
    }

    /// <summary>
    /// Arms a two-condition recapture without replacing either current pointer. The operator
    /// captures V1 under condition 1, changes the process condition, then captures V2. The
    /// existing pair remains current until those explicit slot actions occur.
    /// </summary>
    public IoTestSessionActionResult BeginRecapturePair(IReadOnlyCollection<IoTestPointPlan> points)
    {
        ThrowIfDisposed();
        if (State != IoTestSessionState.Running || ActiveIed == null)
            return IoTestSessionActionResult.Failure("Start the owning IED FAT session before re-arming Value 1 & Value 2.");
        if (points == null || points.Count == 0)
            return IoTestSessionActionResult.Failure("Select one or more FAT rows before re-arming Value 1 & Value 2.");

        var distinct = points.Distinct().ToArray();
        var invalid = distinct.Where(point =>
                !ActiveIed.TestPoints.Contains(point) ||
                !_sessionPointIds.Contains(point.TestPointId) ||
                !point.WorkspaceSelected ||
                !point.IsIncludedInFat ||
                !point.TestEnabled ||
                !point.ImportReady ||
                !_activeLivePoints.ContainsKey(point.TestPointId))
            .ToArray();
        if (invalid.Length > 0)
            return IoTestSessionActionResult.Failure($"{invalid.Length} of {distinct.Length} selected row(s) are not live/eligible — recapture was not armed.");

        _p3PendingPairPointIds.Clear();
        foreach (var point in distinct)
            _p3PendingPairPointIds.Add(point.TestPointId);

        if (!AppendOrFault(SessionEvent(
                "operator_pair_recapture_armed",
                $"Operator armed two-condition Value 1 & Value 2 recapture for {distinct.Length} selected signal(s). Existing current evidence remains authoritative until explicit slot captures.")))
        {
            _p3PendingPairPointIds.Clear();
            return IoTestSessionActionResult.Failure(StatusText);
        }

        return IoTestSessionActionResult.Success(
            $"{distinct.Length} signal(s) armed · capture Value 1 under condition 1, then Value 2 after the condition changes.");
    }

    private bool ObserveP3AutoCapture(IoTestPointPlan point, QueuedSnapshot snapshot)
    {
        if (point.CaptureMode != FatCaptureMode.OperatorSnapshot ||
            !_sessionPointIds.Contains(point.TestPointId) ||
            !point.WorkspaceSelected ||
            !point.IsIncludedInFat ||
            !point.TestEnabled ||
            !point.ImportReady ||
            !IsP3TrustworthyQuality(snapshot.Quality) ||
            string.IsNullOrWhiteSpace(snapshot.Value) ||
            snapshot.Value.Trim() is "-" or "—")
        {
            return false;
        }

        // Explicit operator recapture pins a slot: automatic settling must never silently
        // replace a value the operator deliberately chose.
        if (point.Runtime.Value1Evidence?.CaptureKind == FatEvidenceCaptureKind.OperatorRecapture &&
            point.Runtime.Value2Evidence?.CaptureKind == FatEvidenceCaptureKind.OperatorRecapture)
        {
            return false;
        }

        if (!_p3StableCapture.TryGetValue(point.TestPointId, out var state))
        {
            state = new P3StableCaptureState();
            _p3StableCapture[point.TestPointId] = state;
        }

        var raw = snapshot.Value.Trim();
        if (point.Runtime.Value1Evidence == null)
        {
            if (!state.Observe(raw))
            {
                point.Runtime.StatusReason = "Waiting for stable Value 1 baseline…";
                return false;
            }

            return CommitAutomaticStableValue(point, FatValueSlot.Value1, snapshot, state);
        }

        if (point.Runtime.Value2Evidence != null)
            return false;

        if (Iec61850MonitorPoint.AreSemanticallyEquivalent(point.Runtime.Value1Evidence.RawValue, raw))
        {
            state.Reset();
            point.Runtime.StatusReason = "Value 1 captured · waiting for a meaningful value change";
            return false;
        }

        if (!state.Observe(raw))
        {
            point.Runtime.StatusReason = "Value changed · stabilizing Value 2…";
            return false;
        }

        return CommitAutomaticStableValue(point, FatValueSlot.Value2, snapshot, state);
    }

    private bool CommitAutomaticStableValue(
        IoTestPointPlan point,
        FatValueSlot slot,
        QueuedSnapshot snapshot,
        P3StableCaptureState state)
    {
        try
        {
            var observation = new FatLiveValueObservation(
                snapshot.Value,
                snapshot.CapturedAtUtc,
                IoTestValueNormalizer.ParseIedTimestamp(snapshot.DeviceTimestamp),
                snapshot.Quality,
                snapshot.SourceMode,
                snapshot.Sequence,
                _connectionGeneration);
            var evidence = FatOperatorSnapshotCaptureService.CreateEvidence(
                slot,
                observation,
                FatEvidenceCaptureKind.AutomaticStableSnapshot);
            if (!AppendOrFault(FatValueEvent(point, evidence)))
                return false;

            point.Runtime.SetFatValueEvidence(evidence);
            point.Runtime.StatusReason = slot == FatValueSlot.Value1
                ? "Value 1 stable baseline captured · waiting for a meaningful value change"
                : "Value 1 / Value 2 stable evidence complete";
            state.Reset();
            return true;
        }
        catch (InvalidOperationException)
        {
            state.Reset();
            return false;
        }
    }

    private void ResetP3AutoCaptureState()
    {
        _p3StableCapture.Clear();
        _p3PendingPairPointIds.Clear();
    }

    private static bool IsP3TrustworthyQuality(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
            return true;
        var value = quality.Trim();
        return !value.Contains("bad", StringComparison.OrdinalIgnoreCase) &&
               !value.Contains("invalid", StringComparison.OrdinalIgnoreCase) &&
               !value.Contains("questionable", StringComparison.OrdinalIgnoreCase) &&
               !value.Contains("failure", StringComparison.OrdinalIgnoreCase) &&
               !value.Contains("oldData", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class P3StableCaptureState
    {
        private string _candidate = string.Empty;
        private int _equivalentCount;

        // Two consecutive semantically equivalent observations is intentionally a
        // conservative, amplitude-free settling rule. It rejects transient ramp samples
        // without inventing a vendor-specific analog tolerance.
        public bool Observe(string raw)
        {
            if (_candidate.Length > 0 && Iec61850MonitorPoint.AreSemanticallyEquivalent(_candidate, raw))
            {
                _equivalentCount++;
            }
            else
            {
                _candidate = raw;
                _equivalentCount = 1;
            }
            return _equivalentCount >= 2;
        }

        public void Reset()
        {
            _candidate = string.Empty;
            _equivalentCount = 0;
        }
    }
}
