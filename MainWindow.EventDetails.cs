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
    private Iec61850EventEntry? _eventDrawerCurrentEntry;

    private void EventLogGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, EventLogGrid) &&
            Mouse.LeftButton == MouseButtonState.Released &&
            Mouse.RightButton == MouseButtonState.Released)
            ShowCurrentEventDetails();
    }

    private void EventLogGrid_CurrentCellChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, EventLogGrid) &&
            Mouse.LeftButton == MouseButtonState.Released &&
            Mouse.RightButton == MouseButtonState.Released)
            ShowCurrentEventDetails();
    }

    private void EventLogGrid_PreviewMouseLeftButtonUp(
        object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(sender, EventLogGrid)) return;
        // Finish native drag/Shift selection before changing drawer width;
        // otherwise the grid reflows mid-gesture and breaks range selection.
        Dispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() =>
            {
                if (IsLoaded)
                    ShowCurrentEventDetails();
            }));
    }

    private void ShowCurrentEventDetails()
    {
        if (EventDetailsDrawer is null) return;
        // Never resize a closed drawer as part of a multi-row copy gesture.
        if (EventLogGrid.SelectedItems.Count > 1 &&
            EventDetailsDrawer.Visibility != Visibility.Visible)
            return;
        var entry = EventLogGrid.CurrentCell.IsValid
            ? EventLogGrid.CurrentCell.Item as Iec61850EventEntry
            : EventLogGrid.SelectedItem as Iec61850EventEntry;
        if (entry is null)
        {
            HideEventDetails();
            return;
        }
        if (ReferenceEquals(entry, _eventDrawerCurrentEntry) &&
            EventDetailsDrawer.Visibility == Visibility.Visible)
            return;

        EventDetailsSubtitle.Text = $"{entry.DeviceName} · {entry.EventLabel} · #{entry.Sequence}";
        static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
        var details = new List<RcbInspectionProperty>
        {
            new RcbInspectionProperty("IED", Show(entry.DeviceName), "Event snapshot"),
            new RcbInspectionProperty("Signal", Show(entry.IecReference), "Event snapshot"),
            new RcbInspectionProperty("Event", entry.EventLabel, "Operator event category"),
            new RcbInspectionProperty("Before → after", $"{entry.OldValue} → {entry.NewValue}", "Event snapshot"),
            new RcbInspectionProperty("Timestamp", Show(entry.EventTimestamp), entry.TimestampOrigin),
            new RcbInspectionProperty("Quality", Show(entry.Quality), "Event snapshot"),
            new RcbInspectionProperty("Raw Reason", Show(entry.Reason), "Original event reason"),
            new RcbInspectionProperty("ReasonForInclusion", Show(entry.Reason),
                "Original event reason if provided; not separately decoded"),
            new RcbInspectionProperty("Acquisition", Show(entry.SourceMode), "Runtime acquisition evidence"),
            new RcbInspectionProperty("RCB Name(s)", Show(entry.AcquisitionShort), "Observed RCB routing"),
            new RcbInspectionProperty("RCB type", Show(entry.ReportChannel), "Acquisition classification"),
            new RcbInspectionProperty("Report ID / RptID", Show(entry.ReportId), "Received report metadata"),
            new RcbInspectionProperty("RCB reference", Show(entry.ReportControlReference), "Received report metadata"),
            new RcbInspectionProperty("DataSet", Show(entry.ReportFrame?.DataSetReference), "Wire frame"),
            new RcbInspectionProperty("SqNum", entry.ReportFrame?.SequenceNumber?.ToString() ?? "—", "Wire frame / optional"),
            new RcbInspectionProperty("SubSqNum", entry.ReportFrame?.SubSequenceNumber?.ToString() ?? "—", "Wire frame / optional"),
            new RcbInspectionProperty("More segments", entry.ReportFrame?.MoreSegmentsFollow?.ToString() ?? "—", "Wire frame / optional"),
            new RcbInspectionProperty("ConfRev", entry.ReportFrame?.ConfRev?.ToString() ?? "—", "Wire frame / optional"),
            new RcbInspectionProperty("EntryID", Show(entry.ReportFrame?.EntryIdHex), "Opaque wire identifier"),
            new RcbInspectionProperty("Report TimeOfEntry", Show(entry.ReportFrame?.ReportTimestamp), "Wire BRCB timestamp, NOT process t"),
            new RcbInspectionProperty("Received UTC", entry.ReportFrame?.ReceivedAt.ToString("O") ?? "—", "Local receive clock"),
            new RcbInspectionProperty("OptFlds", Show(entry.ReportFrame?.OptFldsRawHex), "Original decoded option bits"),
            new RcbInspectionProperty("Buffer overflow", entry.ReportFrame?.BufferOverflow?.ToString() ?? "—", "Independent potential-history-loss warning")
        };
        if (entry.IsControlEvent)
        {
            details.AddRange(new[]
            {
                new RcbInspectionProperty("Audit kind", "Operator control", "Not an RCB data-change or measured IED event"),
                new RcbInspectionProperty("Outcome", entry.ControlOutcome, "Exact native service + termination + feedback classification"),
                new RcbInspectionProperty("Requested", entry.NewValue, "Requested command value, not assumed process state"),
                new RcbInspectionProperty("Feedback", Show(entry.ControlFeedback), "Independent observed process feedback only when confirmed"),
                new RcbInspectionProperty("Checks requested", entry.ControlChecks, "Interlock, synchro, Test request flags; not proofs of IED acceptance"),
                new RcbInspectionProperty("Origin", entry.ControlOrigin, "Client-origin category / originator"),
                new RcbInspectionProperty("Control error", Show(entry.ControlError), "Native control error, not guessed"),
                new RcbInspectionProperty("Additional cause", Show(entry.ControlAddCause), "IED addCause when supplied; no inferred interlock/authority"),
                new RcbInspectionProperty("Completion", Show(entry.ControlCompletionState), "Native control completion"),
                new RcbInspectionProperty("ctlNum", Show(entry.ControlFlightSequence), "Native command number"),
                new RcbInspectionProperty("Wire evidence", entry.ControlEvidence, "Ordered native MMS steps, termination and feedback")
            });
        }
        EventDetailsGrid.ItemsSource = details;
        _eventDrawerCurrentEntry = entry;
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
        _eventDrawerCurrentEntry = null;
        _eventDrawerAutoHide?.Stop();
        _eventDrawerHovering = false;
        if (EventDetailsDrawer == null) return;
        EventDetailsDrawer.Visibility = Visibility.Collapsed;
        EventDrawerColumn.Width = new GridLength(0);
        EventDetailsGrid.ItemsSource = null;
    }
}
