using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>Presentation-only SOE inspector. Raw provenance stays in the event and CSV.</summary>
public partial class MainWindow
{
    // Event detail projection is independent from deferred RCB inspector models.
    private sealed record EventDetailProperty(string Property, string Value, string Source);

    private DispatcherTimer? _eventDrawerAutoHide;
    private bool _eventDrawerHovering;

    private void EventLogGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, EventLogGrid) || EventLogGrid.SelectedItem is not Iec61850EventEntry entry)
        {
            HideEventDetails();
            return;
        }

        EventDetailsSubtitle.Text = $"{entry.DeviceName} · {entry.EventCause} · #{entry.Sequence}";
        static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
        EventDetailsGrid.ItemsSource = new[]
        {
            new EventDetailProperty("IED", Show(entry.DeviceName), "Event snapshot"),
            new EventDetailProperty("Signal", Show(entry.IecReference), "Event snapshot"),
            new EventDetailProperty("Category", entry.EventCause, "Presentation mapping"),
            new EventDetailProperty("Before → after", $"{entry.OldValue} → {entry.NewValue}", "Event snapshot"),
            new EventDetailProperty("Timestamp", Show(entry.DeviceTimestamp), "Event snapshot"),
            new EventDetailProperty("Quality", Show(entry.Quality), "Event snapshot"),
            new EventDetailProperty("Raw Reason", Show(entry.Reason), "Original event reason"),
            new EventDetailProperty("ReasonForInclusion", Show(entry.Reason),
                "Original event reason if provided; not separately decoded"),
            new EventDetailProperty("Acquisition", Show(entry.SourceMode), "Runtime acquisition evidence"),
            new EventDetailProperty("RCB Name(s)", Show(entry.AcquisitionShort), "Observed RCB routing"),
            new EventDetailProperty("RCB type", Show(entry.ReportChannel), "Acquisition classification"),
            new EventDetailProperty("Report ID / RptID", Show(entry.ReportId), "Received report metadata"),
            new EventDetailProperty("RCB reference", Show(entry.ReportControlReference), "Received report metadata"),
            new EventDetailProperty("EntryID", "— (not captured)", "Unavailable"),
            new EventDetailProperty("Buffer overflow", "— (not captured)", "Unavailable"),
            new EventDetailProperty("Buffered replay", entry.VerifiedBufferedReplay ? "Verified" : "Not proven", "Continuity proof")
        };
        EventDrawerColumn.Width = new GridLength(370);
        EventDetailsDrawer.Visibility = Visibility.Visible;
        RestartEventDetailsAutoHide();
    }

    private void RestartEventDetailsAutoHide()
    {
        _eventDrawerAutoHide ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(18)
        };
        _eventDrawerAutoHide.Tick -= EventDrawerAutoHide_Tick;
        _eventDrawerAutoHide.Tick += EventDrawerAutoHide_Tick;
        _eventDrawerAutoHide.Stop();
        if (!_eventDrawerHovering)
            _eventDrawerAutoHide.Start();
    }

    private void EventDrawerAutoHide_Tick(object? sender, EventArgs e) => HideEventDetails();

    private void EventDetailsDrawer_MouseEnter(object sender, MouseEventArgs e)
    {
        _eventDrawerHovering = true;
        _eventDrawerAutoHide?.Stop();
    }

    private void EventDetailsDrawer_MouseLeave(object sender, MouseEventArgs e)
    {
        _eventDrawerHovering = false;
        if (EventDetailsDrawer.Visibility == Visibility.Visible)
            RestartEventDetailsAutoHide();
    }

    private void EventLogGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideEventDetails();
            e.Handled = true;
        }
    }

    private void EventDetailsClose_Click(object sender, RoutedEventArgs e) => HideEventDetails();

    private void HideEventDetails()
    {
        _eventDrawerAutoHide?.Stop();
        _eventDrawerHovering = false;
        if (EventDetailsDrawer == null) return;
        EventDetailsDrawer.Visibility = Visibility.Collapsed;
        EventDrawerColumn.Width = new GridLength(0);
        EventDetailsGrid.ItemsSource = null;
    }
}
