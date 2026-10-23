using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>Presentation-only SOE inspector. Raw provenance stays in the event and CSV.</summary>
public partial class MainWindow
{
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
            new RcbInspectionProperty("IED", Show(entry.DeviceName), "Event snapshot"),
            new RcbInspectionProperty("Signal", Show(entry.IecReference), "Event snapshot"),
            new RcbInspectionProperty("Category", entry.EventCause, "Presentation mapping"),
            new RcbInspectionProperty("Before → after", $"{entry.OldValue} → {entry.NewValue}", "Event snapshot"),
            new RcbInspectionProperty("Timestamp", Show(entry.DeviceTimestamp), "Event snapshot"),
            new RcbInspectionProperty("Quality", Show(entry.Quality), "Event snapshot"),
            new RcbInspectionProperty("Raw Reason", Show(entry.Reason), "Original event reason"),
            new RcbInspectionProperty("ReasonForInclusion", Show(entry.Reason),
                "Original event reason if provided; not separately decoded"),
            new RcbInspectionProperty("Acquisition", Show(entry.SourceMode), "Runtime acquisition evidence"),
            new RcbInspectionProperty("RCB Name(s)", Show(entry.AcquisitionShort), "Observed RCB routing"),
            new RcbInspectionProperty("RCB type", Show(entry.ReportChannel), "Acquisition classification"),
            new RcbInspectionProperty("Report ID / RptID", Show(entry.ReportId), "Received report metadata"),
            new RcbInspectionProperty("RCB reference", Show(entry.ReportControlReference), "Received report metadata"),
            new RcbInspectionProperty("EntryID", "— (not captured)", "Unavailable"),
            new RcbInspectionProperty("Buffer overflow", "— (not captured)", "Unavailable"),
            new RcbInspectionProperty("Buffered replay", entry.VerifiedBufferedReplay ? "Verified" : "Not proven", "Continuity proof")
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
