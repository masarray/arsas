using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Makes the native FAT report preview useful as an operator evidence/history inspector.
/// The immutable report snapshot remains the preview/export authority; this enhancement
/// only appends the persisted chronological audit trail for the selected IEC identity.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _nativeFatHistoryInspectorInstallRetry;
    private ScrollViewer? _nativeFatHistoryScrollViewer;
    private bool _nativeFatHistoryInspectorAttached;
    private bool _nativeFatHistoryRenderQueued;

    [ModuleInitializer]
    internal static void RegisterNativeFatHistoryInspector()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(NativeFatHistoryInspector_MainWindowLoaded),
            handledEventsToo: true);
    }

    private static void NativeFatHistoryInspector_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._nativeFatHistoryInspectorAttached)
            return;

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(window.TryAttachNativeFatHistoryInspector));
    }

    private void TryAttachNativeFatHistoryInspector()
    {
        if (_nativeFatHistoryInspectorAttached || !IsLoaded)
            return;

        if (!_nativeFatReportPreviewEnhanced ||
            _nativeFatPreviewPane?.Child is not Grid previewRoot ||
            _nativeFatPreviewGrid == null ||
            _nativeFatPreviewEvidenceText == null)
        {
            _nativeFatHistoryInspectorInstallRetry ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _nativeFatHistoryInspectorInstallRetry.Tick -= NativeFatHistoryInspectorInstallRetry_Tick;
            _nativeFatHistoryInspectorInstallRetry.Tick += NativeFatHistoryInspectorInstallRetry_Tick;
            _nativeFatHistoryInspectorInstallRetry.Start();
            return;
        }

        _nativeFatHistoryInspectorInstallRetry?.Stop();
        _nativeFatHistoryInspectorAttached = true;

        // The evidence block can become long once retest history exists. Keep the report
        // pane compact and give only the evidence/history area its own vertical scrolling.
        if (previewRoot.Children.Contains(_nativeFatPreviewEvidenceText))
            previewRoot.Children.Remove(_nativeFatPreviewEvidenceText);

        _nativeFatPreviewEvidenceText.Margin = new Thickness(0);
        _nativeFatPreviewEvidenceText.TextWrapping = TextWrapping.Wrap;
        _nativeFatHistoryScrollViewer = new ScrollViewer
        {
            Content = _nativeFatPreviewEvidenceText,
            Margin = new Thickness(0, 8, 0, 0),
            MaxHeight = 190,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false
        };
        Grid.SetRow(_nativeFatHistoryScrollViewer, 5);
        previewRoot.Children.Add(_nativeFatHistoryScrollViewer);

        _nativeFatPreviewGrid.SelectionChanged += NativeFatHistoryInspector_SelectionChanged;
        Closed += NativeFatHistoryInspector_MainWindowClosed;
        QueueNativeFatHistoryInspectorRender();
    }

    private void NativeFatHistoryInspectorInstallRetry_Tick(object? sender, EventArgs e)
    {
        _nativeFatHistoryInspectorInstallRetry?.Stop();
        TryAttachNativeFatHistoryInspector();
    }

    private void NativeFatHistoryInspector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => QueueNativeFatHistoryInspectorRender();

    private void QueueNativeFatHistoryInspectorRender()
    {
        if (!_nativeFatHistoryInspectorAttached || _nativeFatHistoryRenderQueued)
            return;

        _nativeFatHistoryRenderQueued = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                _nativeFatHistoryRenderQueued = false;
                RenderNativeFatHistoryInspector();
            }));
    }

    private void RenderNativeFatHistoryInspector()
    {
        if (_nativeFatPreviewEvidenceText == null)
            return;

        if (_nativeFatPreviewGrid?.SelectedItem is not NativeFatReportRow reportRow)
        {
            _nativeFatPreviewEvidenceText.Text = "Select a report row to inspect captured evidence and retest history.";
            return;
        }

        var text = new StringBuilder(reportRow.EvidenceSummaryText);
        var state = _nativeFatCurrentState;
        var key = NativeFatIdentity.BuildKey(reportRow.IecReference, reportRow.FunctionalConstraint);
        var persisted = state?.Signals.FirstOrDefault(signal =>
            signal.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        var history = persisted?.History ?? new List<NativeFatHistoryEntry>();

        text.Append("\n\nHISTORY · latest first");
        if (history.Count == 0)
        {
            text.Append("\nNo previous capture/result transitions recorded.");
        }
        else
        {
            foreach (var entry in history
                         .OrderByDescending(item => item.TimestampUtc)
                         .Take(8))
            {
                text.Append("\n")
                    .Append(entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))
                    .Append("  ·  ")
                    .Append(string.IsNullOrWhiteSpace(entry.Action) ? "State update" : entry.Action.Trim());

                if (!string.IsNullOrWhiteSpace(entry.Result))
                    text.Append("  ·  ").Append(entry.Result.Trim());
                if (entry.Value1 != null)
                    text.Append("  ·  V1=").Append(entry.Value1.Value);
                if (entry.Value2 != null)
                    text.Append("  ·  V2=").Append(entry.Value2.Value);
                if (!string.IsNullOrWhiteSpace(entry.Note))
                    text.Append("  ·  ").Append(entry.Note.Trim());
            }

            if (history.Count > 8)
                text.Append("\n+").Append(history.Count - 8).Append(" earlier record(s) retained in the per-IED JSON.");
        }

        if (reportRow.IsHistorical)
            text.Append("\n\nThis IEC identity is historical: it is no longer in the current Explorer scope, but its FAT evidence is retained.");

        _nativeFatPreviewEvidenceText.Text = text.ToString();
        _nativeFatHistoryScrollViewer?.ScrollToTop();
    }

    private void NativeFatHistoryInspector_MainWindowClosed(object? sender, EventArgs e)
    {
        _nativeFatHistoryInspectorInstallRetry?.Stop();
        if (_nativeFatPreviewGrid != null)
            _nativeFatPreviewGrid.SelectionChanged -= NativeFatHistoryInspector_SelectionChanged;
        Closed -= NativeFatHistoryInspector_MainWindowClosed;
        _nativeFatHistoryInspectorAttached = false;
        _nativeFatHistoryRenderQueued = false;
        _nativeFatHistoryScrollViewer = null;
    }
}
