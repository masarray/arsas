using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Evidence column whose text is a real WPF binding to the current row DataContext.
/// Recycling a DataGridRow therefore re-evaluates the evidence against the new canonical
/// Iec61850MonitorPoint instead of retaining imperative TextBlock.Text from the prior row.
/// </summary>
internal sealed class NativeFatEvidenceBindingColumn : DataGridColumn
{
    private readonly NativeFatEvidenceBindingConverter _converter;

    internal NativeFatEvidenceBindingColumn(
        string header,
        NativeFatEvidenceField field,
        double width,
        Func<Iec61850MonitorPoint, NativeFatEvidenceField, string> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Header = header;
        Field = field;
        Width = new DataGridLength(width);
        MinWidth = 78;
        _converter = new NativeFatEvidenceBindingConverter(field, reader);
    }

    internal NativeFatEvidenceField Field { get; }

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var block = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        block.SetBinding(TextBlock.TextProperty, CreateBinding());
        return block;
    }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        var editor = new TextBox
        {
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0)
        };
        editor.SetBinding(TextBox.TextProperty, CreateBinding());
        return editor;
    }

    internal void RefreshTarget(Iec61850MonitorPoint point)
    {
        if (GetCellContent(point) is TextBlock block)
        {
            block.GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget();
            return;
        }

        if (GetCellContent(point) is TextBox editor)
            editor.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    private Binding CreateBinding()
        => new()
        {
            Path = new PropertyPath("."),
            Mode = BindingMode.OneWay,
            Converter = _converter
        };

    private sealed class NativeFatEvidenceBindingConverter : IValueConverter
    {
        private readonly NativeFatEvidenceField _field;
        private readonly Func<Iec61850MonitorPoint, NativeFatEvidenceField, string> _reader;

        internal NativeFatEvidenceBindingConverter(
            NativeFatEvidenceField field,
            Func<Iec61850MonitorPoint, NativeFatEvidenceField, string> reader)
        {
            _field = field;
            _reader = reader;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Iec61850MonitorPoint point
                ? _reader(point, _field)
                : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}

public partial class MainWindow
{
    private DataGrid? _nativeFatEvidenceBindingGrid;
    private bool _nativeFatEvidenceBindingArmHooked;
    private DispatcherTimer? _nativeFatEvidenceBindingHydrationTimer;
    private bool _nativeFatEvidenceBindingHydrationWasActive;

    /// <summary>
    /// Hooks editing/refresh behavior for the bound evidence columns. The canonical grid still
    /// owns SelectedDevice.Points and keeps row virtualization/recycling enabled.
    /// </summary>
    private void InstallNativeFatEvidenceBindingRuntime()
    {
        var grid = _nativeFatCanonicalGrid;
        if (grid == null)
            return;

        if (!ReferenceEquals(_nativeFatEvidenceBindingGrid, grid))
        {
            if (_nativeFatEvidenceBindingGrid != null)
            {
                _nativeFatEvidenceBindingGrid.BeginningEdit -= NativeFatEvidenceBinding_BeginningEdit;
                _nativeFatEvidenceBindingGrid.CellEditEnding -= NativeFatEvidenceBinding_CellEditEnding;
            }

            _nativeFatEvidenceBindingGrid = grid;
            grid.BeginningEdit += NativeFatEvidenceBinding_BeginningEdit;
            grid.CellEditEnding += NativeFatEvidenceBinding_CellEditEnding;
        }

        if (!_nativeFatEvidenceBindingArmHooked)
        {
            _nativeFatArmCoordinator.EvidenceChanged += NativeFatEvidenceBinding_EvidenceChanged;
            _nativeFatEvidenceBindingArmHooked = true;
        }
    }

    /// <summary>
    /// Called after each selected-IED bind. Hydration changes the sparse cache rather than the
    /// canonical row object, so this short-lived timer only refreshes binding targets while
    /// hydration is active and performs one final refresh when hydration resolves.
    /// </summary>
    private void RefreshNativeFatEvidenceBindingRuntime()
    {
        RefreshAllNativeFatEvidenceBindingTargets();

        var hydrating = IsNativeFatEvidenceBindingHydrating();
        _nativeFatEvidenceBindingHydrationWasActive = hydrating;
        if (!hydrating)
        {
            _nativeFatEvidenceBindingHydrationTimer?.Stop();
            return;
        }

        _nativeFatEvidenceBindingHydrationTimer ??= CreateNativeFatEvidenceBindingHydrationTimer();
        if (!_nativeFatEvidenceBindingHydrationTimer.IsEnabled)
            _nativeFatEvidenceBindingHydrationTimer.Start();
    }

    private DispatcherTimer CreateNativeFatEvidenceBindingHydrationTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(320)
        };
        timer.Tick += NativeFatEvidenceBindingHydrationTimer_Tick;
        return timer;
    }

    private void NativeFatEvidenceBindingHydrationTimer_Tick(object? sender, EventArgs e)
    {
        var hydrating = IsNativeFatEvidenceBindingHydrating();
        if (hydrating)
        {
            _nativeFatEvidenceBindingHydrationWasActive = true;
            RefreshAllNativeFatEvidenceBindingTargets();
            return;
        }

        if (_nativeFatEvidenceBindingHydrationWasActive)
            RefreshAllNativeFatEvidenceBindingTargets();

        _nativeFatEvidenceBindingHydrationWasActive = false;
        _nativeFatEvidenceBindingHydrationTimer?.Stop();
    }

    private bool IsNativeFatEvidenceBindingHydrating()
        => !string.IsNullOrWhiteSpace(_nativeFatBoundIedKey) &&
           _nativeFatSessionByIed.TryGetValue(_nativeFatBoundIedKey, out var cache) &&
           cache.IsEvidenceHydrating;

    private void NativeFatEvidenceBinding_EvidenceChanged(object? sender, NativeFatEvidenceChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => NativeFatEvidenceBinding_EvidenceChanged(sender, e));
            return;
        }

        if (!string.Equals(_nativeFatBoundIedKey, e.DeviceId, StringComparison.OrdinalIgnoreCase))
            return;

        RefreshNativeFatEvidenceBindingTargets(e.Point);
    }

    private void RefreshNativeFatEvidenceBindingTargets(Iec61850MonitorPoint point)
    {
        if (_nativeFatCanonicalGrid == null)
            return;

        foreach (var column in _nativeFatCanonicalGrid.Columns.OfType<NativeFatEvidenceBindingColumn>())
            column.RefreshTarget(point);
    }

    private void RefreshAllNativeFatEvidenceBindingTargets()
    {
        if (_nativeFatCanonicalGrid == null)
            return;

        foreach (var point in _nativeFatCanonicalGrid.Items.OfType<Iec61850MonitorPoint>())
            RefreshNativeFatEvidenceBindingTargets(point);
    }

    private void NativeFatEvidenceBinding_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Column is not NativeFatEvidenceBindingColumn || string.IsNullOrWhiteSpace(_nativeFatBoundIedKey))
            return;

        if (_nativeFatSessionByIed.TryGetValue(_nativeFatBoundIedKey, out var cache) && cache.IsEvidenceHydrating)
            e.Cancel = true;
    }

    private void NativeFatEvidenceBinding_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit ||
            e.Row.Item is not Iec61850MonitorPoint point ||
            e.Column is not NativeFatEvidenceBindingColumn evidenceColumn ||
            e.EditingElement is not TextBox editor ||
            string.IsNullOrWhiteSpace(_nativeFatBoundIedKey))
        {
            return;
        }

        var cache = GetNativeFatSession(_nativeFatBoundIedKey);
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, evidenceColumn.Field, editor.Text);
        cache.ActiveRowKey = NativeFatCanonicalEvidenceOverlay.BuildRowKey(point);
        ScheduleNativeFatEvidencePersist(_nativeFatBoundIedKey);
        Dispatcher.BeginInvoke(
            () => evidenceColumn.RefreshTarget(point),
            DispatcherPriority.DataBind);
    }

    /// <summary>
    /// Flush sparse FAT evidence before the arm/persistence services are disposed. Snapshot
    /// paths are IEDName-based and row identity remains IEDName + IEC Telegram.
    /// </summary>
    private void FlushNativeFatEvidenceBeforeShutdown()
    {
        foreach (var device in Devices.ToArray())
        {
            if (!_nativeFatSessionByIed.TryGetValue(device.DeviceId, out var cache))
                continue;

            bool hasEvidence;
            lock (cache.EvidenceByRow)
                hasEvidence = cache.EvidenceByRow.Count > 0;
            if (!hasEvidence)
                continue;

            try
            {
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

    private void DisposeNativeFatEvidenceBindingRuntime()
    {
        if (_nativeFatEvidenceBindingGrid != null)
        {
            _nativeFatEvidenceBindingGrid.BeginningEdit -= NativeFatEvidenceBinding_BeginningEdit;
            _nativeFatEvidenceBindingGrid.CellEditEnding -= NativeFatEvidenceBinding_CellEditEnding;
            _nativeFatEvidenceBindingGrid = null;
        }

        if (_nativeFatEvidenceBindingArmHooked)
        {
            _nativeFatArmCoordinator.EvidenceChanged -= NativeFatEvidenceBinding_EvidenceChanged;
            _nativeFatEvidenceBindingArmHooked = false;
        }

        if (_nativeFatEvidenceBindingHydrationTimer != null)
        {
            _nativeFatEvidenceBindingHydrationTimer.Tick -= NativeFatEvidenceBindingHydrationTimer_Tick;
            _nativeFatEvidenceBindingHydrationTimer.Stop();
            _nativeFatEvidenceBindingHydrationTimer = null;
        }

        _nativeFatEvidenceBindingHydrationWasActive = false;
    }
}
