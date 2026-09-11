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

    private DataGrid? _nativeFatCanonicalGrid;
    private TextBlock? _nativeFatIedText;
    private TextBlock? _nativeFatRowCountText;
    private TextBlock? _nativeFatStatusText;
    private string? _nativeFatBoundIedKey;

    /// <summary>
    /// P1A: FAT renders the exact Engineering live-row objects. There is no projection,
    /// SCL parse, IoTestPointPlan collection, or second acquisition owner in this surface.
    /// P1B adds only three sparse evidence columns keyed outside those canonical rows.
    /// </summary>
    private FrameworkElement BuildNativeFatCanonicalWorkspace(string? statusText = null)
    {
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

        _nativeFatRowCountText = new TextBlock
        {
            Text = "0 rows",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryFindResource("Accent") as Brush ?? Brushes.RoyalBlue,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        Grid.SetColumn(_nativeFatRowCountText, 1);
        headerGrid.Children.Add(_nativeFatRowCountText);
        header.Child = headerGrid;
        root.Children.Add(header);

        _nativeFatCanonicalGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = false,
            CanUserResizeColumns = true,
            IsReadOnly = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(225, 231, 240)),
            Background = Brushes.White,
            RowBackground = Brushes.White,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            RowHeaderWidth = 0,
            FrozenColumnCount = 1,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(232, 237, 245)),
            VerticalGridLinesBrush = Brushes.Transparent
        };
        VirtualizingPanel.SetIsVirtualizing(_nativeFatCanonicalGrid, true);
        VirtualizingPanel.SetVirtualizationMode(_nativeFatCanonicalGrid, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_nativeFatCanonicalGrid, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(_nativeFatCanonicalGrid, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_nativeFatCanonicalGrid, ScrollBarVisibility.Auto);
        _nativeFatCanonicalGrid.CellEditEnding += NativeFatCanonicalGrid_CellEditEnding;

        AddCanonicalTextColumn("Status", nameof(Iec61850MonitorPoint.Status), 90);
        AddCanonicalTextColumn("Type", nameof(Iec61850MonitorPoint.IecDataType), 84);
        AddCanonicalTextColumn("Address", nameof(Iec61850MonitorPoint.IecTelegram), 210);
        AddCanonicalTextColumn("Message", nameof(Iec61850MonitorPoint.SignalName), 180);
        AddCanonicalTextColumn("Data Reference", nameof(Iec61850MonitorPoint.IecReference), 290);
        AddCanonicalTextColumn("Quality", nameof(Iec61850MonitorPoint.Quality), 92);
        AddCanonicalTextColumn("Timestamp", nameof(Iec61850MonitorPoint.DeviceTimestamp), 152);
        AddCanonicalTextColumn("Value", nameof(Iec61850MonitorPoint.DisplayValue), 90);
        _nativeFatCanonicalGrid.Columns.Add(new NativeFatEvidenceColumn(this, "Value 1", NativeFatEvidenceField.Value1, 104));
        _nativeFatCanonicalGrid.Columns.Add(new NativeFatEvidenceColumn(this, "Value 2", NativeFatEvidenceField.Value2, 104));
        _nativeFatCanonicalGrid.Columns.Add(new NativeFatEvidenceColumn(this, "Result", NativeFatEvidenceField.Result, 104));

        Grid.SetRow(_nativeFatCanonicalGrid, 2);
        root.Children.Add(_nativeFatCanonicalGrid);
        return root;
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
        _nativeFatStatusText!.Text = device == null
            ? "Select an Engineering IED with canonical live rows."
            : "Canonical Engineering live rows · no reconnect · sparse Value 1 / Value 2 / Result overlay";

        RestoreNativeFatSessionState(device);
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
                TextTrimming = TextTrimming.CharacterEllipsis,
                Padding = new Thickness(5, 0, 5, 0)
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
                Padding = new Thickness(4, 1, 4, 1),
                BorderThickness = new Thickness(1)
            };
        }
    }
}
