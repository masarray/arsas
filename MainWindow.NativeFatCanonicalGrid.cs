using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private readonly Dictionary<string, NativeFatIedSessionCacheState> _nativeFatSessionByIed =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly NativeFatArmCoordinator _nativeFatArmCoordinator = new();

    private DataGrid? _nativeFatCanonicalGrid;
    private TextBlock? _nativeFatIedText;
    private TextBlock? _nativeFatRowCountText;
    private TextBlock? _nativeFatStatusText;
    private Button? _nativeFatStartButton;
    private string? _nativeFatBoundIedKey;
    private bool _nativeFatArmEventsHooked;

    /// <summary>
    /// P1A: FAT renders the exact Engineering live-row objects. There is no projection,
    /// SCL parse, IoTestPointPlan collection, or second acquisition owner in this surface.
    /// P1B adds only three sparse evidence columns keyed outside those canonical rows.
    /// P1C reuses the Engineering grid visual authority and virtualization contract.
    /// P1D makes Start FAT an ARM-only operation over those already-live row objects.
    /// </summary>
    private FrameworkElement BuildNativeFatCanonicalWorkspace(string? statusText = null)
    {
        if (!_nativeFatArmEventsHooked)
        {
            _nativeFatArmCoordinator.EvidenceChanged += NativeFatArmCoordinator_EvidenceChanged;
            _nativeFatArmEventsHooked = true;
        }

        var root = new Grid
        {
            Margin = new Thickness(16)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(12),
            Background = TryFindResource("CardBackground") as Brush ?? Brushes.White,
            BorderBrush = TryFindResource("CardBorder") as Brush ?? new SolidColorBrush(Color.FromRgb(220, 228, 239)),
            BorderThickness = new Thickness(1)
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel();
        _nativeFatIedText = new TextBlock
        {
            Text = "FAT · select an Engineering IED",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryFindResource("Ink") as Brush ?? Brushes.Black
        };
        _nativeFatStatusText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(statusText)
                ? "Canonical Engineering live rows · shared acquisition · sparse FAT evidence overlay"
                : statusText,
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 10.8,
            Foreground = TryFindResource("Muted") as Brush ?? Brushes.DimGray
        };
        titlePanel.Children.Add(_nativeFatIedText);
        titlePanel.Children.Add(_nativeFatStatusText);
        headerGrid.Children.Add(titlePanel);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        _nativeFatRowCountText = new TextBlock
        {
            Text = "0 rows",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryFindResource("Accent") as Brush ?? Brushes.RoyalBlue,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        actionPanel.Children.Add(_nativeFatRowCountText);

        _nativeFatStartButton = new Button
        {
            Content = "Start FAT",
            MinWidth = 92,
            Padding = new Thickness(12, 6, 12, 6),
            Style = TryFindResource("PrimaryButton") as Style,
            IsEnabled = false,
            ToolTip = "Arm FAT evidence on the already-running Engineering live stream."
        };
        _nativeFatStartButton.Click += NativeFatStartButton_Click;
        actionPanel.Children.Add(_nativeFatStartButton);

        Grid.SetColumn(actionPanel, 1);
        headerGrid.Children.Add(actionPanel);
        header.Child = headerGrid;
        root.Children.Add(header);

        var modernDataGridStyle = FindResource("ModernDataGrid") as Style
            ?? throw new InvalidOperationException("ModernDataGrid visual authority was not found.");

        _nativeFatCanonicalGrid = new DataGrid
        {
            Style = modernDataGridStyle,
            RowStyle = BuildEngineeringLiveRowStyle(),
            CellStyle = BuildEngineeringLiveCellStyle(),
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = false,
            FrozenColumnCount = 2,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true
        };
        VirtualizingPanel.SetIsVirtualizing(_nativeFatCanonicalGrid, true);
        VirtualizingPanel.SetVirtualizationMode(_nativeFatCanonicalGrid, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_nativeFatCanonicalGrid, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(_nativeFatCanonicalGrid, ScrollBarVisibility.Auto);
        _nativeFatCanonicalGrid.CellEditEnding += NativeFatCanonicalGrid_CellEditEnding;

        AddCanonicalTextColumn("Status", nameof(Iec61850MonitorPoint.Status), 90);
        AddCanonicalTextColumn("Type", nameof(Iec61850MonitorPoint.IecDataType), 84);
        AddCanonicalTextColumn("Address", nameof(Iec61850MonitorPoint.IecTelegram), 210);
        AddCanonicalTextColumn("Message", nameof(Iec61850MonitorPoint.SignalName), 180);
        AddCanonicalTextColumn("Data Reference", nameof(Iec61850MonitorPoint.IecReference), 290);
        AddCanonicalTextColumn("Quality", nameof(Iec61850MonitorPoint.Quality), 105);
        AddCanonicalTextColumn("Timestamp", nameof(Iec61850MonitorPoint.DeviceTimestamp), 155);
        AddCanonicalTemplateColumn("Value", "ProcessValueBadgeTemplate", 125);
        _nativeFatCanonicalGrid.Columns.Add(new NativeFatEvidenceColumn(this, "Value 1", NativeFatEvidenceField.Value1, 104));
        _nativeFatCanonicalGrid.Columns.Add(new NativeFatEvidenceColumn(this, "Value 2", NativeFatEvidenceField.Value2, 104));
        _nativeFatCanonicalGrid.Columns.Add(new NativeFatEvidenceColumn(this, "Result", NativeFatEvidenceField.Result, 104));

        Grid.SetRow(_nativeFatCanonicalGrid, 2);
        root.Children.Add(_nativeFatCanonicalGrid);
        return root;
    }

    private Style BuildEngineeringLiveRowStyle()
    {
        var style = new Style(typeof(DataGridRow), FindResource(typeof(DataGridRow)) as Style);
        var changed = new DataTrigger
        {
            Binding = new Binding(nameof(Iec61850MonitorPoint.IsRecentlyChanged)),
            Value = true
        };
        changed.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(255, 240, 168))));
        changed.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(245, 158, 11))));
        changed.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(4, 1, 1, 1)));
        style.Triggers.Add(changed);
        return style;
    }

    private Style BuildEngineeringLiveCellStyle()
    {
        var style = new Style(typeof(DataGridCell), FindResource(typeof(DataGridCell)) as Style);
        var changed = new DataTrigger
        {
            Binding = new Binding(nameof(Iec61850MonitorPoint.IsRecentlyChanged)),
            Value = true
        };
        changed.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(255, 245, 194))));
        changed.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(92, 59, 0))));
        style.Triggers.Add(changed);
        return style;
    }

    private void AddCanonicalTextColumn(string header, string path, double width)
    {
        if (_nativeFatCanonicalGrid == null)
            return;

        _nativeFatCanonicalGrid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path) { Mode = BindingMode.OneWay },
            Width = new DataGridLength(width),
            IsReadOnly = true
        });
    }

    private void AddCanonicalTemplateColumn(string header, string templateKey, double width)
    {
        if (_nativeFatCanonicalGrid == null)
            return;

        var template = FindResource(templateKey) as DataTemplate
            ?? throw new InvalidOperationException($"Engineering cell template '{templateKey}' was not found.");
        _nativeFatCanonicalGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = header,
            CellTemplate = template,
            Width = new DataGridLength(width),
            IsReadOnly = true
        });
    }

    private void BindNativeFatCanonicalRows()
    {
        if (_nativeFatCanonicalGrid == null || _productionFatWindow is { IsLoaded: true })
            return;

        // Finish any in-cell evidence edit against the previously bound IED before switching.
        _nativeFatCanonicalGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        _nativeFatCanonicalGrid.CommitEdit(DataGridEditingUnit.Row, true);
        SaveNativeFatSessionState();

        var device = SelectedDevice;
        _nativeFatBoundIedKey = device?.DeviceId;

        // P1A invariant: this is the exact same collection used by Engineering.
        // No Select/ToList/projection/wrapper is allowed here.
        _nativeFatCanonicalGrid.ItemsSource = device?.Points;

        _nativeFatIedText!.Text = device == null
            ? "FAT · select an Engineering IED"
            : $"FAT · {device.Name} · {device.IpAddress}:{device.Port}";
        _nativeFatRowCountText!.Text = device == null ? "0 rows" : $"{device.Points.Count} rows";

        RestoreNativeFatSessionState(device);
        UpdateNativeFatArmUi(device);
    }

    private void UpdateNativeFatArmUi(Iec61850MonitorDevice? device, string? overrideStatus = null)
    {
        if (_nativeFatStatusText == null || _nativeFatStartButton == null)
            return;

        if (device == null)
        {
            _nativeFatStartButton.Content = "Start FAT";
            _nativeFatStartButton.IsEnabled = false;
            _nativeFatStatusText.Text = overrideStatus ?? "Select an Engineering IED with canonical live rows.";
            return;
        }

        var cache = GetNativeFatSession(device.DeviceId);
        var armed = cache.IsArmed || _nativeFatArmCoordinator.IsArmed(device.DeviceId);
        _nativeFatStartButton.Content = armed ? "FAT Armed" : "Start FAT";
        _nativeFatStartButton.IsEnabled = !armed && device.IsConnected && device.IsMonitoring && device.Points.Count > 0;
        _nativeFatStartButton.ToolTip = armed
            ? "FAT evidence is armed on the existing Engineering acquisition stream."
            : device.IsConnected && device.IsMonitoring
                ? "Arm FAT evidence only. No reconnect, SCL import, discovery, report restart, or polling change."
                : "Start Engineering monitoring first; FAT will reuse that live acquisition.";

        _nativeFatStatusText.Text = overrideStatus ?? (armed
            ? $"FAT armed · shared Engineering acquisition untouched · {device.Points.Count} canonical row(s)"
            : "Canonical Engineering live rows · Start FAT only arms evidence; acquisition remains untouched");
    }

    private void NativeFatStartButton_Click(object sender, RoutedEventArgs e)
    {
        var stopwatch = Stopwatch.StartNew();
        var device = SelectedDevice;
        if (device == null)
        {
            UpdateNativeFatArmUi(null, "Select an Engineering IED before starting FAT.");
            return;
        }

        var cache = GetNativeFatSession(device.DeviceId);
        var result = _nativeFatArmCoordinator.Arm(device, cache);
        stopwatch.Stop();
        cache.LastArmElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        var status = result.Succeeded
            ? result.AlreadyArmed
                ? result.Message
                : $"{device.Name} FAT armed in {stopwatch.ElapsedMilliseconds} ms · {result.ArmedRows} canonical row(s) · {result.SeededValue1Rows} Value 1 seeded · no acquisition restart"
            : result.Message;

        UpdateNativeFatArmUi(device, status);
        SetStatus(result.Succeeded
            ? $"FAT · {device.Name} armed on shared Engineering live data in {stopwatch.ElapsedMilliseconds} ms"
            : $"FAT · {result.Message}");

        Trace.WriteLine(
            $"[FAT P1D] ARM completed in {stopwatch.ElapsedMilliseconds} ms; " +
            $"ied={device.Name}; deviceId={device.DeviceId}; rows={device.Points.Count}; " +
            $"seededV1={result.SeededValue1Rows}; alreadyArmed={result.AlreadyArmed}; succeeded={result.Succeeded}; " +
            "networkPrepare=false; reconnect=false; sclImport=false; discovery=false; reportRestart=false; pollingChange=false.");
    }

    private void NativeFatArmCoordinator_EvidenceChanged(object? sender, NativeFatEvidenceChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => NativeFatArmCoordinator_EvidenceChanged(sender, e));
            return;
        }

        if (!string.Equals(_nativeFatBoundIedKey, e.DeviceId, StringComparison.OrdinalIgnoreCase))
            return;

        RefreshNativeFatEvidenceCells(e.Point);
    }

    private void RefreshNativeFatEvidenceCells(Iec61850MonitorPoint point)
    {
        if (_nativeFatCanonicalGrid == null)
            return;

        foreach (var column in _nativeFatCanonicalGrid.Columns.OfType<NativeFatEvidenceColumn>())
        {
            if (column.GetCellContent(point) is TextBlock textBlock)
                textBlock.Text = ReadNativeFatEvidence(point, column.Field);
        }
    }

    private void SaveNativeFatSessionState()
    {
        if (_nativeFatCanonicalGrid == null || string.IsNullOrWhiteSpace(_nativeFatBoundIedKey))
            return;

        var cache = GetNativeFatSession(_nativeFatBoundIedKey);
        if (_nativeFatCanonicalGrid.SelectedItem is Iec61850MonitorPoint point)
            cache.ActiveRowKey = NativeFatCanonicalEvidenceOverlay.BuildRowKey(point);
        cache.LastScrollIndex = Math.Max(0, _nativeFatCanonicalGrid.SelectedIndex);
    }

    private void RestoreNativeFatSessionState(Iec61850MonitorDevice? device)
    {
        if (_nativeFatCanonicalGrid == null || device == null || string.IsNullOrWhiteSpace(_nativeFatBoundIedKey))
            return;

        if (!_nativeFatSessionByIed.TryGetValue(_nativeFatBoundIedKey, out var cache))
            return;

        Iec61850MonitorPoint? target = null;
        if (!string.IsNullOrWhiteSpace(cache.ActiveRowKey))
        {
            target = device.Points.FirstOrDefault(point =>
                string.Equals(
                    NativeFatCanonicalEvidenceOverlay.BuildRowKey(point),
                    cache.ActiveRowKey,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (target == null && cache.LastScrollIndex >= 0 && cache.LastScrollIndex < device.Points.Count)
            target = device.Points[cache.LastScrollIndex];

        if (target == null)
            return;

        _nativeFatCanonicalGrid.SelectedItem = target;
        _nativeFatCanonicalGrid.ScrollIntoView(target);
    }

    private NativeFatIedSessionCacheState GetNativeFatSession(string iedKey)
    {
        if (!_nativeFatSessionByIed.TryGetValue(iedKey, out var cache))
        {
            cache = new NativeFatIedSessionCacheState();
            _nativeFatSessionByIed[iedKey] = cache;
        }
        return cache;
    }

    private string ReadNativeFatEvidence(Iec61850MonitorPoint point, NativeFatEvidenceField field)
    {
        if (string.IsNullOrWhiteSpace(_nativeFatBoundIedKey) ||
            !_nativeFatSessionByIed.TryGetValue(_nativeFatBoundIedKey, out var cache))
        {
            return string.Empty;
        }

        return NativeFatCanonicalEvidenceOverlay.Read(cache, point, field);
    }

    private void NativeFatCanonicalGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit ||
            e.Row.Item is not Iec61850MonitorPoint point ||
            e.Column is not NativeFatEvidenceColumn evidenceColumn ||
            e.EditingElement is not TextBox editor ||
            string.IsNullOrWhiteSpace(_nativeFatBoundIedKey))
        {
            return;
        }

        var cache = GetNativeFatSession(_nativeFatBoundIedKey);
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, evidenceColumn.Field, editor.Text);
        cache.ActiveRowKey = NativeFatCanonicalEvidenceOverlay.BuildRowKey(point);
    }

    private void DisposeNativeFatArmCoordinator()
    {
        if (_nativeFatArmEventsHooked)
        {
            _nativeFatArmCoordinator.EvidenceChanged -= NativeFatArmCoordinator_EvidenceChanged;
            _nativeFatArmEventsHooked = false;
        }

        _nativeFatArmCoordinator.Dispose();
        if (_nativeFatStartButton != null)
            _nativeFatStartButton.Click -= NativeFatStartButton_Click;
        _nativeFatStartButton = null;
    }

    private sealed class NativeFatEvidenceColumn : DataGridColumn
    {
        private readonly MainWindow _owner;

        internal NativeFatEvidenceColumn(
            MainWindow owner,
            string header,
            NativeFatEvidenceField field,
            double width)
        {
            _owner = owner;
            Header = header;
            Field = field;
            Width = new DataGridLength(width);
            MinWidth = 78;
        }

        internal NativeFatEvidenceField Field { get; }

        protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
        {
            return new TextBlock
            {
                Text = dataItem is Iec61850MonitorPoint point
                    ? _owner.ReadNativeFatEvidence(point, Field)
                    : string.Empty,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
        {
            return new TextBox
            {
                Text = dataItem is Iec61850MonitorPoint point
                    ? _owner.ReadNativeFatEvidence(point, Field)
                    : string.Empty,
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0)
            };
        }
    }
}
