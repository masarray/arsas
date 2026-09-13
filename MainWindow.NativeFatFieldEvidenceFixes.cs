using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Field hardening for native FAT evidence presentation/persistence.
/// Evidence identity remains IEDName + IEC Telegram; this layer only prevents recycled WPF
/// containers from showing stale evidence and guarantees the latest sparse evidence snapshot
/// is flushed before the native FAT persistence service is disposed on application close.
/// </summary>
public partial class MainWindow
{
    private bool _nativeFatFieldEvidenceHooksInstalled;

    [ModuleInitializer]
    internal static void RegisterNativeFatFieldEvidenceHardening()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(NativeFatFieldEvidence_MainWindowLoaded),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(DataGridRow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(NativeFatFieldEvidence_DataGridRowLoaded),
            handledEventsToo: true);
    }

    private static void NativeFatFieldEvidence_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._nativeFatFieldEvidenceHooksInstalled)
            return;

        window._nativeFatFieldEvidenceHooksInstalled = true;
        window.Closing += window.NativeFatFieldEvidence_WindowClosing;
    }

    private static void NativeFatFieldEvidence_DataGridRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGridRow row ||
            Window.GetWindow(row) is not MainWindow window ||
            window._nativeFatCanonicalGrid == null ||
            !ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(row), window._nativeFatCanonicalGrid))
        {
            return;
        }

        if (row.Tag is not NativeFatEvidenceRecycleHookMarker)
        {
            row.Tag = NativeFatEvidenceRecycleHookMarker.Instance;
            row.DataContextChanged += NativeFatFieldEvidence_RowDataContextChanged;
        }

        window.RefreshNativeFatEvidenceRowAfterRecycle(row);
    }

    private static void NativeFatFieldEvidence_RowDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (sender is not DataGridRow row ||
            Window.GetWindow(row) is not MainWindow window ||
            window._nativeFatCanonicalGrid == null ||
            !ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(row), window._nativeFatCanonicalGrid))
        {
            return;
        }

        // Fail closed immediately: a recycled visual must never show evidence from its
        // previous DataContext while WPF finishes assigning the new canonical row.
        window.ClearNativeFatEvidenceRowVisual(row);
        window.RefreshNativeFatEvidenceRowAfterRecycle(row);
    }

    private void ClearNativeFatEvidenceRowVisual(DataGridRow row)
    {
        if (_nativeFatCanonicalGrid == null)
            return;

        foreach (var column in _nativeFatCanonicalGrid.Columns.OfType<NativeFatEvidenceColumn>())
        {
            if (column.GetCellContent(row) is TextBlock textBlock)
                textBlock.Text = string.Empty;
        }
    }

    private void RefreshNativeFatEvidenceRowAfterRecycle(DataGridRow row)
    {
        Dispatcher.BeginInvoke(
            () =>
            {
                if (_nativeFatCanonicalGrid == null ||
                    !ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(row), _nativeFatCanonicalGrid) ||
                    row.Item is not Iec61850MonitorPoint point)
                {
                    return;
                }

                // Re-read by the current canonical point. ReadNativeFatEvidence ultimately
                // resolves only IEDName + IEC Telegram; row index and SignalName never enter.
                RefreshNativeFatEvidenceCells(point);
            },
            DispatcherPriority.DataBind);
    }

    private void NativeFatFieldEvidence_WindowClosing(object? sender, CancelEventArgs e)
    {
        CommitNativeFatEvidenceEdits();
        FlushNativeFatEvidenceBeforeShutdown();
    }

    private void FlushNativeFatEvidenceBeforeShutdown()
    {
        foreach (var device in Devices.ToArray())
        {
            if (!_nativeFatSessionByIed.TryGetValue(device.DeviceId, out var cache))
                continue;

            var hasEvidence = false;
            lock (cache.EvidenceByRow)
                hasEvidence = cache.EvidenceByRow.Count > 0;
            if (!hasEvidence)
                continue;

            try
            {
                // SaveAsync snapshots the canonical rows synchronously before yielding and
                // writes an atomic file whose path is derived from IEDName, not DeviceId.
                // Closing waits for this small sparse snapshot so the subsequent Closed
                // cleanup cannot cancel the only pending persistence operation.
                _nativeFatEvidenceHydrationService
                    .SaveAsync(device, cache, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                Trace.WriteLine(
                    $"[FAT field] evidence flush completed before shutdown; ied={device.Name}; deviceId={device.DeviceId}; rows={cache.EvidenceByRow.Count}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Trace.WriteLine(
                    $"[FAT field] evidence flush failed before shutdown for {device.Name}: {ex.Message}");
            }
        }
    }

    private sealed class NativeFatEvidenceRecycleHookMarker
    {
        public static NativeFatEvidenceRecycleHookMarker Instance { get; } = new();

        private NativeFatEvidenceRecycleHookMarker()
        {
        }
    }
}
