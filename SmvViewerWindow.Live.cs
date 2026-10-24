using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class SmvViewerWindow
{
    private readonly SmvLiveCaptureService _liveService = new();
    private CancellationTokenSource? _liveCancellation;
    private DispatcherTimer? _liveFreshnessTimer;
    private SmvLiveUpdate? _pendingLiveUpdate;
    private IReadOnlyList<SmvLiveStreamSummary> _liveSummaries = [];
    private string? _liveSelectedKey;
    private int _liveUiScheduled;
    private int _liveGeneration;
    private bool _liveCaptureRunning;
    private DateTimeOffset _liveStartedAtUtc;
    private bool _hasLivePresentation;

    public bool IsLiveCaptureRunning => _liveCaptureRunning;

    public bool HasLivePresentation
    {
        get => _hasLivePresentation;
        private set => Set(ref _hasLivePresentation, value);
    }

    private async void StartLive_Click(object sender, RoutedEventArgs e)
    {
        if (_liveCaptureRunning || CancelButton.IsEnabled || IsEvidenceExportBusy)
            return;
        if (SelectedAdapter is null)
        {
            CaptureStatusText = "Select a valid Npcap Ethernet adapter before starting live monitoring.";
            return;
        }

        var options = new SmvLiveCaptureOptions
        {
            AdapterSelector = SelectedAdapter.Selector,
            NominalFrequencyHz = ReadSelectedFrequency(),
            Cycles = 2,
            MaximumChannels = 8,
            MaximumStreams = 32,
            RefreshMilliseconds = 200,
            ExpectedStreams = Streams.Where(s => s.Source != "Live observed")
                .Select(s => new SmvLiveExpectedStream(
                    TryParseAppId(s.AppId, out var appid) ? appid : null,
                    NormalizeOptionalIdentity(s.StreamId),
                    NormalizeOptionalIdentity(s.DestinationMac),
                    TryParseUShort(s.SampleRate),
                    ParseSampleMode(s.SampleMode))).ToArray()
        };

        var generation = Interlocked.Increment(ref _liveGeneration);
        _liveCancellation = new CancellationTokenSource();
        _liveCaptureRunning = true;
        _liveStartedAtUtc = DateTimeOffset.UtcNow;
        Raise(nameof(IsLiveCaptureRunning));
        HasLivePresentation = true;
        _p0CaptureSelection = null;
        _liveSummaries = [];
        _pendingLiveUpdate = null;
        _liveSelectedKey = SelectedStream is null
            ? null : (SelectedStream.LiveKey.Length > 0 ? SelectedStream.LiveKey : "#await-matching-stream");
        ResetSnapshotPresentation();
        SnapshotBadgeText = "LIVE · WAITING";
        CaptureStatusText = $"Listening passively on {SelectedAdapter.DisplayText}…";
        StatusText = "Live SV discovery is running. No packet is transmitted. Waiting for observed publishers.";
        EvidenceExportStatusText = "Live monitoring cannot be exported as bounded proof; use Capture 2 cycles.";
        UpdateLiveControls();

        _liveFreshnessTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _liveFreshnessTimer.Tick += LiveFreshness_Tick;
        _liveFreshnessTimer.Start();
        try
        {
            await _liveService.RunAsync(options,
                () => Volatile.Read(ref _liveSelectedKey),
                update => QueueLiveUi(update, generation), _liveCancellation.Token);
            if (generation == _liveGeneration)
                CaptureStatusText = "Passive SV capture ended.";
        }
        catch (OperationCanceledException) when (_liveCancellation.IsCancellationRequested)
        {
            if (generation == _liveGeneration)
                CaptureStatusText = "Live SV capture stopped.";
        }
        catch (Exception ex)
        {
            if (generation == _liveGeneration)
            {
                CaptureStatusText = $"Live capture stopped: {ex.Message}";
                StatusText = "Check Npcap installation, adapter availability, access and physical Ethernet link.";
            }
        }
        finally
        {
            if (generation == _liveGeneration)
            {
                _liveCaptureRunning = false;
                Raise(nameof(IsLiveCaptureRunning));
                _liveFreshnessTimer?.Stop();
                if (_liveFreshnessTimer is not null)
                    _liveFreshnessTimer.Tick -= LiveFreshness_Tick;
                _liveFreshnessTimer = null;
                _liveCancellation?.Dispose();
                _liveCancellation = null;
                StopLiveButton.IsEnabled = false;
                UpdateLiveControls();
                if (_snapshot is not null)
                    SnapshotBadgeText = "HOLD · LAST LIVE";
            }
        }
    }

    private void StopLive_Click(object sender, RoutedEventArgs e)
    {
        StopLiveButton.IsEnabled = false;
        CaptureStatusText = "Stopping live capture…";
        _liveCancellation?.Cancel();
    }

    private void UpdateLiveControls()
    {
        var canStart = !_liveCaptureRunning && !CancelButton.IsEnabled &&
                       !IsEvidenceExportBusy && AdapterOptions.Count > 0;
        LiveButton.IsEnabled = canStart;
        CaptureButton.IsEnabled = canStart;
        StopLiveButton.IsEnabled = _liveCaptureRunning;
        AdapterCombo.IsEnabled = !_liveCaptureRunning && !CancelButton.IsEnabled;
        FrequencyCombo.IsEnabled = !_liveCaptureRunning && !CancelButton.IsEnabled;
    }

    private void OnLiveSelectedStreamChanged()
    {
        if (!_liveCaptureRunning)
            return;
        var key = SelectedStream?.LiveKey;
        if (string.IsNullOrWhiteSpace(key) && SelectedStream is not null)
        {
            var matches = _liveSummaries.Where(s => MatchesConfiguredStream(SelectedStream, s)).Take(2).ToArray();
            key = matches.Length == 1 ? matches[0].Key : "#await-matching-stream";
        }
        Volatile.Write(ref _liveSelectedKey, key);
        SnapshotBadgeText = "LIVE · WAITING";
    }

    private void QueueLiveUi(SmvLiveUpdate update, int generation)
    {
        if (generation != Volatile.Read(ref _liveGeneration))
            return;
        Interlocked.Exchange(ref _pendingLiveUpdate, update);
        if (Interlocked.CompareExchange(ref _liveUiScheduled, 1, 0) != 0)
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            Interlocked.Exchange(ref _liveUiScheduled, 0);
            var next = Interlocked.Exchange(ref _pendingLiveUpdate, null);
            if (!_liveCaptureRunning || generation != _liveGeneration || next is null || !IsLoaded)
                return;
            ApplyLiveUpdate(next);
        }));
    }

    private void ApplyLiveUpdate(SmvLiveUpdate update)
    {
        _liveSummaries = update.Streams;
        foreach (var summary in update.Streams)
        {
            if (Streams.Any(row => row.LiveKey.Equals(summary.Key, StringComparison.Ordinal)))
                continue;

            // An SCL definition must not silently bind the first publisher when
            // two physical sources advertise the same APPID/svID/destination.
            var matches = Streams.Where(row => row.LiveKey.Length == 0 &&
                MatchesConfiguredStream(row, summary) &&
                update.Streams.Count(peer => MatchesConfiguredStream(row, peer)) == 1)
                .Take(2).ToArray();
            var matched = matches.Length == 1 ? matches[0] : null;
            var row = new SmvStreamRow
            {
                Source = matched is null ? "Live observed" : "Live + configured",
                LiveKey = summary.Key,
                ControlReference = matched?.ControlReference ?? "",
                StreamId = summary.SvId,
                DataSetReference = summary.DataSetReference,
                AppId = $"0x{summary.AppId:X4}",
                DestinationMac = summary.DestinationMac,
                Vlan = summary.Vlan,
                SampleRate = matched?.SampleRate ?? "-",
                SampleMode = matched?.SampleMode ?? "-",
                NumberOfAsdu = matched?.NumberOfAsdu ?? "-",
                MemberCount = matched?.MemberCount ?? 0
            };
            if (matched is not null)
            {
                var index = Streams.IndexOf(matched);
                var wasSelected = ReferenceEquals(SelectedStream, matched);
                Streams[index] = row;
                if (wasSelected)
                    SelectedStream = row;
            }
            else
                Streams.Add(row);
        }

        var keys = update.Streams.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var obsolete in Streams.Where(s => s.LiveKey.Length > 0 && !keys.Contains(s.LiveKey)).ToArray())
            Streams.Remove(obsolete);
        Raise(nameof(StreamCountText));

        if (SelectedStream is null && update.Streams.Count > 0)
            SelectedStream = Streams.FirstOrDefault(s => s.LiveKey == update.SelectedKey);
        if (SelectedStream is null)
            return;

        if (SelectedStream.LiveKey.Length == 0)
            OnLiveSelectedStreamChanged();

        var activeKey = Volatile.Read(ref _liveSelectedKey);
        var selected = update.Streams.FirstOrDefault(s => s.Key == activeKey);
        if (selected is null)
        {
            CaptureStatusText = update.Streams.Count == 0
                ? "No IEC 61850 SV publisher observed. Verify adapter, VLAN and mirroring."
                : "Select an observed publisher; configured stream has no unique wire match.";
            return;
        }

        var age = DateTimeOffset.UtcNow - selected.LastSeen;
        if (age > TimeSpan.FromSeconds(2))
        {
            SnapshotBadgeText = "HOLD · STALE";
            CaptureStatusText = $"{selected.SvId} stopped arriving ({age.TotalSeconds:0.0}s ago).";
            return;
        }

        CaptureStatusText = $"{update.Streams.Count} publisher(s) · {update.TotalFrames:N0} received frames · {selected.SvId}";
        SnapshotContinuityEvidenceText = $"Observed since Live start: gap {selected.Gaps}, missing {selected.Missing}, duplicate {selected.Duplicates}, out-of-order {selected.OutOfOrder}, restart {selected.Restarts}.";
        Raise(nameof(SnapshotContinuityEvidenceText));
        if (update.SelectedKey != activeKey || update.SelectedSnapshot is null)
        {
            SnapshotBadgeText = "LIVE · ACQUIRING";
            StatusText = selected.Diagnostic;
            return;
        }

        // This is a coherent, selected-stream-only, raw INT32 snapshot. The
        // existing rendering/DFT pipeline consumes the same immutable window.
        ApplySnapshot(update.SelectedSnapshot);
        SnapshotBadgeText = $"LIVE · {update.SelectedSnapshot.CycleCount} cycles";
        CaptureStatusText = $"{update.Streams.Count} publisher(s) · {update.TotalFrames:N0} frames · {selected.SvId}";
        SnapshotContinuityEvidenceText = $"Lifetime counter evidence: gap {selected.Gaps}, missing {selected.Missing}, duplicate {selected.Duplicates}, out-of-order {selected.OutOfOrder}, restart {selected.Restarts}.";
        Raise(nameof(SnapshotContinuityEvidenceText));
        StatusText = $"Passive live SV · {selected.SourceMac} → {selected.DestinationMac} · unscaled raw samples; ordinary Windows timestamp, not calibrated.";
    }

    private void LiveFreshness_Tick(object? sender, EventArgs e)
    {
        if (!_liveCaptureRunning)
            return;
        if (_liveSummaries.Count == 0 && DateTimeOffset.UtcNow - _liveStartedAtUtc > TimeSpan.FromSeconds(2))
        {
            CaptureStatusText = "No SV traffic seen. Check Ethernet cable, adapter, VLAN and switch mirror/TAP.";
            SnapshotBadgeText = "LIVE · NO TRAFFIC";
            return;
        }
        var selected = _liveSummaries.FirstOrDefault(s => s.Key == Volatile.Read(ref _liveSelectedKey));
        if (selected is null)
            return;
        if (DateTimeOffset.UtcNow - selected.LastSeen > TimeSpan.FromSeconds(2))
        {
            SnapshotBadgeText = "HOLD · STALE";
            CaptureStatusText = $"No fresh frames for {selected.SvId}. Check publisher, NIC or mirror port.";
        }
    }

    private static bool MatchesConfiguredStream(SmvStreamRow row, SmvLiveStreamSummary summary)
    {
        if (!string.IsNullOrWhiteSpace(row.StreamId) && row.StreamId != "-" &&
            !row.StreamId.Equals(summary.SvId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (TryParseAppId(row.AppId, out var appid) && appid != summary.AppId)
            return false;
        var mac = new string((row.DestinationMac ?? "").Where(Uri.IsHexDigit)
            .Select(char.ToUpperInvariant).ToArray());
        var observedMac = new string(summary.DestinationMac.Where(Uri.IsHexDigit)
            .Select(char.ToUpperInvariant).ToArray());
        return mac.Length == 0 || mac == observedMac;
    }
}
