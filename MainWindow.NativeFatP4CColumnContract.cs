using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private bool _nativeFatObservationStatusHooked;

    /// <summary>
    /// Native FAT is a thin view over the canonical IEC Explorer rows. The grid keeps
    /// SelectedDevice.Points as its ItemsSource and adds only sparse evidence columns.
    /// Values and timestamps are deliberately separate so the operator can scan evidence
    /// without parsing a combined presentation string.
    /// </summary>
    private void ApplyNativeFatP4CColumnContract()
    {
        if (_nativeFatCanonicalGrid == null)
            return;

        if (!_nativeFatObservationStatusHooked)
        {
            // Keep observation progress on the same evidence event authority as V1/V2/Result.
            // The callback is posted at Background priority so the ARM click's generic status
            // cannot overwrite the more useful row-level "1 / 2" or "2 / 2" confirmation.
            _nativeFatArmCoordinator.EvidenceChanged += NativeFatObservationStatus_EvidenceChanged;
            _nativeFatObservationStatusHooked = true;
        }

        _nativeFatCanonicalGrid.Columns.Clear();
        _nativeFatCanonicalGrid.FrozenColumnCount = 2;

        AddCanonicalTextColumn("Signal", nameof(Iec61850MonitorPoint.SignalName), 190);
        AddCanonicalTextColumn("IEC Telegram", nameof(Iec61850MonitorPoint.IecTelegram), 320);
        AddCanonicalTextColumn("Quality", nameof(Iec61850MonitorPoint.Quality), 95);
        AddCanonicalTemplateColumn("Live Value", "ProcessValueBadgeTemplate", 125);

        // P0 field hardening: evidence text is bound to the current DataContext. WPF row
        // recycling therefore re-evaluates IEDName + IEC Telegram for the newly assigned
        // canonical point instead of carrying imperative TextBlock.Text from a previous row.
        _nativeFatCanonicalGrid.Columns.Add(
            new NativeFatEvidenceBindingColumn("Value 1", NativeFatEvidenceField.Value1, 120, ReadNativeFatEvidence));
        _nativeFatCanonicalGrid.Columns.Add(
            new NativeFatEvidenceBindingColumn("V1 Timestamp", NativeFatEvidenceField.Value1Timestamp, 185, ReadNativeFatEvidence)
            {
                IsReadOnly = true
            });
        _nativeFatCanonicalGrid.Columns.Add(
            new NativeFatEvidenceBindingColumn("Value 2", NativeFatEvidenceField.Value2, 120, ReadNativeFatEvidence));
        _nativeFatCanonicalGrid.Columns.Add(
            new NativeFatEvidenceBindingColumn("V2 Timestamp", NativeFatEvidenceField.Value2Timestamp, 185, ReadNativeFatEvidence)
            {
                IsReadOnly = true
            });
        _nativeFatCanonicalGrid.Columns.Add(
            new NativeFatEvidenceBindingColumn("Result", NativeFatEvidenceField.Result, 110, ReadNativeFatEvidence));
    }

    private void NativeFatObservationStatus_EvidenceChanged(object? sender, NativeFatEvidenceChangedEventArgs e)
    {
        void UpdateObservationStatus()
        {
            if (_nativeFatStatusText == null ||
                !string.Equals(_nativeFatBoundIedKey, e.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                !_nativeFatSessionByIed.TryGetValue(e.DeviceId, out var cache))
            {
                return;
            }

            var value1 = NativeFatCanonicalEvidenceOverlay.Read(cache, e.Point, NativeFatEvidenceField.Value1);
            var value2 = NativeFatCanonicalEvidenceOverlay.Read(cache, e.Point, NativeFatEvidenceField.Value2);
            var observations = (string.IsNullOrWhiteSpace(value1) ? 0 : 1) +
                               (string.IsNullOrWhiteSpace(value2) ? 0 : 1);
            var result = NativeFatCanonicalEvidenceOverlay.Read(cache, e.Point, NativeFatEvidenceField.Result);
            var signal = string.IsNullOrWhiteSpace(e.Point.SignalName)
                ? e.Point.IecTelegram
                : e.Point.SignalName.Trim();

            _nativeFatStatusText.Text = string.IsNullOrWhiteSpace(result)
                ? $"{signal} · {observations} / 2 observations"
                : $"{signal} · {observations} / 2 observations · {result}";
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(UpdateObservationStatus));
    }

    // Retained as an isolated formatter for report/tests and compatibility paths. The visible
    // grid routes all evidence fields through NativeFatEvidenceBindingColumn.
    private string ReadNativeFatTimestamp(Iec61850MonitorPoint point, NativeFatEvidenceField field)
    {
        if (string.IsNullOrWhiteSpace(_nativeFatBoundIedKey) ||
            !_nativeFatSessionByIed.TryGetValue(_nativeFatBoundIedKey, out var cache))
        {
            return string.Empty;
        }

        var evidence = NativeFatCanonicalEvidenceOverlay.ReadCapture(cache, point, field);
        if (evidence is null)
            return string.Empty;

        var timestamp = evidence.IedTimestamp ?? evidence.CapturedAt;
        return timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Compatibility timestamp column retained for source/binary compatibility. Production
    /// native FAT uses NativeFatEvidenceBindingColumn for timestamp fields as well.
    /// </summary>
    private sealed class NativeFatEvidenceTimestampColumn : DataGridColumn
    {
        private readonly MainWindow _owner;

        internal NativeFatEvidenceTimestampColumn(
            MainWindow owner,
            string header,
            NativeFatEvidenceField field,
            double width)
        {
            _owner = owner;
            Header = header;
            Field = field;
            Width = new DataGridLength(width);
            MinWidth = 130;
            IsReadOnly = true;
        }

        internal NativeFatEvidenceField Field { get; }

        protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
        {
            return new TextBlock
            {
                Text = dataItem is Iec61850MonitorPoint point
                    ? _owner.ReadNativeFatTimestamp(point, Field)
                    : string.Empty,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
            => GenerateElement(cell, dataItem);
    }
}
