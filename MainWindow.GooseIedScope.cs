using System.ComponentModel;
using System.Windows.Data;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    // 0 = selected IED; 1 = all IEDs. Never filters the capture/decoder.
    // The station-bus sniffer is independent of the selected MMS IED.
    // Show all observed publishers until an operator deliberately scopes them.
    private int _gooseIedScopeIndex = 1;
    private ListCollectionView? _gooseEventScopeView;
    private ListCollectionView? _gooseStreamScopeView;

    public ICollectionView GooseVisibleEvents => _gooseEventScopeView ??= CreateGooseEventsView();
    public ICollectionView GooseVisibleStreams => _gooseStreamScopeView ??= CreateGooseStreamsView();

    public int GooseIedScopeIndex
    {
        get => _gooseIedScopeIndex;
        set
        {
            var scope = value == 1 ? 1 : 0;
            if (!Set(ref _gooseIedScopeIndex, scope)) return;
            Raise(nameof(GooseSelectedScopeHint));
            RefreshGooseScopeViews();
        }
    }

    public string GooseSelectedScopeHint => GooseIedScopeIndex == 1
        ? "Display messages from every captured GOOSE publisher, including unresolved identities."
        : SelectedDevice is null
            ? "No IED selected; showing all GOOSE publishers."
            : $"Only confirmed {SelectedDevice.Name} GOOSE streams. Unresolved publishers remain in All IED mode.";

    public string GooseEmptyTimelineTitle =>
        GooseIedScopeIndex == 0 && GooseEvents.Count > 0
            ? "No GOOSE events match the selected IED"
            : "Waiting for a meaningful event";

    public string GooseEmptyTimelineDescription =>
        GooseIedScopeIndex == 0 && GooseEvents.Count > 0
            ? $"{GooseEvents.Count:N0} event(s) received from other or unresolved publishers. Select All IED GOOSE signals to see them."
            : GooseFrames > 0
                ? $"{GooseFrames:N0} GOOSE frame(s) received. Unchanged retransmissions are summarized; enable Retransmissions to inspect every frame."
                : "New publishers, changed values and new warnings appear here.";

    private ListCollectionView CreateGooseEventsView()
    {
        var view = new ListCollectionView(GooseEvents);
        view.Filter = value => value is GooseEventRow row && IsGooseIedInScope(row.IedName);
        return view;
    }

    private ListCollectionView CreateGooseStreamsView()
    {
        var view = new ListCollectionView(GooseStreams);
        view.Filter = value => value is GooseStreamRow row && IsGooseIedInScope(row.ModelIedName);
        return view;
    }

    private bool IsGooseIedInScope(string? publisherIedName)
        => GooseIedScopePolicy.Matches(GooseIedScopeIndex,
            SelectedDevice?.Name, SelectedDevice?.SclIedName, publisherIedName);

    private void RefreshGooseScopeViews()
    {
        // Refresh only the operator views. The 500-event ring and up to 256
        // original publisher streams continue collecting all captured traffic.
        _gooseEventScopeView?.Refresh();
        _gooseStreamScopeView?.Refresh();
        Raise(nameof(GooseSelectedScopeHint));
        Raise(nameof(GooseEmptyTimelineTitle));
        Raise(nameof(GooseEmptyTimelineDescription));
        _settingGooseLiveSelection = true;
        try
        {
            if (SelectedGooseEvent is not null &&
                !IsGooseIedInScope(SelectedGooseEvent.IedName))
                SelectedGooseEvent = null;
            if (FollowLatestGooseEvents)
                SelectedGooseEvent = GooseVisibleEvents.Cast<GooseEventRow>().LastOrDefault();
            if (SelectedGooseEvent is null &&
                SelectedGooseStream is not null &&
                !IsGooseIedInScope(SelectedGooseStream.ModelIedName))
                SelectedGooseStream = GooseVisibleStreams.Cast<GooseStreamRow>().FirstOrDefault();
        }
        finally { _settingGooseLiveSelection = false; }
        RaiseGoosePresentationState();
    }
}
