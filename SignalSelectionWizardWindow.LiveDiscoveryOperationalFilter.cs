using System.Windows;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Owns the Signal Selection presentation contract after a live MMS discovery.
///
/// The ARIEC/live SignalDefinition inventory intentionally remains complete enough for
/// diagnostics, comparison, report planning, persistence and engineering inspection.
/// Signal Selection is narrower: it shows operator points and mandatory static DataSet
/// members, never protocol/service leaves such as Mod/Beh/Health/NamPlt/origin/q/t.
///
/// This filter is installed on the wizard after its constructor has installed search and
/// column filters. That lifecycle point is important because a default WPF collection view
/// can be shared with another DataGrid; a previously installed global filter can otherwise
/// be replaced by the wizard's own FilterSignal predicate.
/// </summary>
public partial class SignalSelectionWizardWindow
{
    private Predicate<object>? _signalSelectionBaseFilter;
    private Predicate<object>? _signalSelectionOperationalFilter;
    private bool _signalSelectionOperationalFilterInstalled;

    static SignalSelectionWizardWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(SignalSelectionWizardWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnSignalSelectionOperationalFilterLoaded));
    }

    private static void OnSignalSelectionOperationalFilterLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is SignalSelectionWizardWindow window)
            window.InstallSignalSelectionOperationalFilter();
    }

    private void InstallSignalSelectionOperationalFilter()
    {
        if (_signalSelectionOperationalFilterInstalled)
            return;

        _signalSelectionBaseFilter = SignalsView.Filter;
        _signalSelectionOperationalFilter = item =>
        {
            if (_signalSelectionBaseFilter is not null && !_signalSelectionBaseFilter(item))
                return false;

            return item is SignalDefinition signal &&
                   SasOperationalUiPolicy.IsPresentationVisible(signal);
        };

        SignalsView.Filter = _signalSelectionOperationalFilter;
        _signalSelectionOperationalFilterInstalled = true;
        Closed -= SignalSelectionOperationalFilter_Closed;
        Closed += SignalSelectionOperationalFilter_Closed;
        SignalsView.Refresh();
        RefreshViewState();
    }

    private void SignalSelectionOperationalFilter_Closed(object? sender, EventArgs args)
    {
        Closed -= SignalSelectionOperationalFilter_Closed;

        // Restore only our own wrapper. If another owner intentionally changed the shared
        // collection view while this window was open, do not overwrite that newer filter.
        if (_signalSelectionOperationalFilter is not null &&
            Equals(SignalsView.Filter, _signalSelectionOperationalFilter))
        {
            SignalsView.Filter = _signalSelectionBaseFilter;
        }

        _signalSelectionOperationalFilterInstalled = false;
        _signalSelectionOperationalFilter = null;
        _signalSelectionBaseFilter = null;
    }
}
