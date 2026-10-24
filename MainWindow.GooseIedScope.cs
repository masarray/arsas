using System.ComponentModel;
using System.Windows.Data;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    // 0 = selected IED; 1 = all IEDs. Never filters the capture/decoder.
    private int _gooseIedScopeIndex = 1; // Unresolved publishers must be visible at first capture.
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

    // Keep the 500-event canonical timeline unchanged; this is only the
    // presentation scope. A selected IED cannot claim unresolved Ethernet
    // publisher identity solely from its MMS endpoint.
    public string GooseEmptyEventTitle => GooseEvents.Count == 0
        ? "Waiting for the first meaningful GOOSE event"
        : "No events match the selected IED";
    public System.Windows.Visibility GooseShowAllPublishersVisibility =>
        GooseIedScopeIndex == 0 && GooseEvents.Count > 0 &&
        !GooseVisibleEvents.Cast<GooseEventRow>().Any()
            ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public string GooseEmptyEventDetail => GooseEvents.Count == 0
        ? "Frames are captured independently. New publishers and actual changes will appear here."
        : $"Captured {GooseEvents.Count:N0} timeline event(s), but none has a verified publisher identity matching the selected IED. Choose All IED GOOSE signals to view them.";

    public string GooseSelectedScopeHint => GooseIedScopeIndex == 1
        ? "Display messages from every captured GOOSE publisher, including unresolved identities."
        : SelectedDevice is null
            ? "No IED selected; showing all GOOSE publishers."
            : $"Only confirmed {SelectedDevice.Name} GOOSE streams. Unresolved publishers remain in All IED mode.";

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
        Raise(nameof(GooseEmptyEventTitle));
        Raise(nameof(GooseEmptyEventDetail));
        Raise(nameof(GooseShowAllPublishersVisibility));
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
