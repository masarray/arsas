using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class SmvViewerWindow
{
    private SmvSnapshotSelectionIdentity? _p0CaptureSelection;
    private bool _p0AssessmentScheduled;
    private bool _p0RejectingSnapshot;

    public string SnapshotContinuityEvidenceText { get; private set; } =
        "Counter continuity has not been assessed yet.";

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        CaptureButton.AddHandler(Button.ClickEvent, new RoutedEventHandler(P0CaptureButton_Click), handledEventsToo: true);
        SnapshotChannels.CollectionChanged += P0SnapshotChannels_CollectionChanged;
    }

    private void P0CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        var stream = SelectedStream;
        _p0CaptureSelection = stream is null
            ? null
            : SmvSnapshotSelectionIdentity.Create(
                stream.ControlReference,
                stream.StreamId,
                stream.DataSetReference,
                stream.AppId,
                stream.DestinationMac);
    }

    private void P0StreamGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!CancelButton.IsEnabled || _p0CaptureSelection is null || SelectedStream is null)
            return;

        if (_p0CaptureSelection.Matches(
                SelectedStream.ControlReference,
                SelectedStream.StreamId,
                SelectedStream.DataSetReference,
                SelectedStream.AppId,
                SelectedStream.DestinationMac))
        {
            return;
        }

        _captureCancellation?.Cancel();
        CaptureStatusText = "Snapshot capture cancelled because the selected stream changed.";
        StatusText = "The capture result was not attached to another stream. Start a new snapshot for the current selection.";
    }

    private void P0SnapshotChannels_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_p0AssessmentScheduled || _p0RejectingSnapshot)
            return;

        _p0AssessmentScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _p0AssessmentScheduled = false;
            ApplyP0SnapshotAssessment();
        }));
    }

    private void ApplyP0SnapshotAssessment()
    {
        var result = _snapshot;
        if (result is null)
        {
            SnapshotContinuityEvidenceText = "Counter continuity has not been assessed yet.";
            Raise(nameof(SnapshotContinuityEvidenceText));
            return;
        }

        if (_p0CaptureSelection is not null &&
            (SelectedStream is null || !_p0CaptureSelection.Matches(
                SelectedStream.ControlReference,
                SelectedStream.StreamId,
                SelectedStream.DataSetReference,
                SelectedStream.AppId,
                SelectedStream.DestinationMac)))
        {
            RejectMisassociatedSnapshot();
            return;
        }

        var clean = SmvSnapshotSafetyAssessment.IsCleanProof(result);
        var proof = clean ? "PASS" : "REVIEW";
        SnapshotBadgeText = $"{proof} · {result.CycleCount} cycles";
        SnapshotSummaryText = SmvSnapshotSafetyAssessment.ApplyVerdictToSummary(result, SnapshotSummaryText);
        SnapshotContinuityEvidenceText = SmvSnapshotSafetyAssessment.BuildContinuityEvidence(result) + ".";
        Raise(nameof(SnapshotContinuityEvidenceText));

        if (clean)
        {
            CaptureStatusText = "Two-cycle SV snapshot received and decoded with continuous smpCnt.";
            StatusText = "The selected SV stream produced a complete two-cycle proof window without detected smpCnt gaps, duplicates, out-of-order transitions or publisher restarts.";
            return;
        }

        CaptureStatusText = "Two-cycle SV snapshot decoded, but continuity anomalies require review.";
        StatusText = result.RestartTransitions > 0
            ? "The snapshot contains a publisher restart and is intentionally not accepted as continuous smpCnt proof."
            : "A complete two-cycle window was decoded, but counter continuity findings remain visible and must be reviewed.";
    }

    private void RejectMisassociatedSnapshot()
    {
        _p0RejectingSnapshot = true;
        try
        {
            _snapshot = null;
            SnapshotChannels.Clear();
            SnapshotBadgeText = "NOT PROVEN";
            SnapshotSummaryText = "The completed snapshot belonged to a different stream selection and was discarded.";
            SnapshotEvidenceText = "No evidence was attached to the newly selected stream.";
            SnapshotBoundaryText = "Stream identity is immutable for the duration of a bounded capture. Start a new capture after changing the selection.";
            SnapshotContinuityEvidenceText = "Counter continuity was not accepted because stream ownership changed.";
            CaptureStatusText = "Snapshot discarded after stream selection changed.";
            StatusText = "ARSAS prevented an SV proof from being misassociated with another stream.";
            Raise(nameof(SnapshotContinuityEvidenceText));
            RenderWaveform();
        }
        finally
        {
            _p0RejectingSnapshot = false;
        }
    }
}
