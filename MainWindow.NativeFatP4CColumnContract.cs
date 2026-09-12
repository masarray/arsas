using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
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

        _nativeFatCanonicalGrid.Columns.Clear();
        _nativeFatCanonicalGrid.FrozenColumnCount = 2;

        AddCanonicalTextColumn("Signal", nameof(Iec61850MonitorPoint.SignalName), 190);
        AddCanonicalTextColumn("IEC Telegram", nameof(Iec61850MonitorPoint.IecTelegram), 320);
        AddCanonicalTextColumn("Quality", nameof(Iec61850MonitorPoint.Quality), 95);
        AddCanonicalTemplateColumn("Live Value", "ProcessValueBadgeTemplate", 125);

        _nativeFatCanonicalGrid.Columns.Add(
            new NativeFatEvidenceColumn(this, "Value 1", NativeFatEvidenceField.Value1, 120));
        _nativeFatCanonicalGrid.Columns.Add(
            new NativeFatEvidenceColumn(this, "V1 Timestamp", NativeFatEvidenceField.Value1Timestamp, 185)
            {
                IsReadOnly = true
            });
        _nativeFatCanonicalGrid.Columns.Add(
            new NativeFatEvidenceColumn(this, "Value 2", NativeFatEvidenceField.Value2, 120));
        _nativeFatCanonicalGrid.Columns.Add(
            new NativeFatEvidenceColumn(this, "V2 Timestamp", NativeFatEvidenceField.Value2Timestamp, 185)
            {
                IsReadOnly = true
            });
        _nativeFatCanonicalGrid.Columns.Add(
            new NativeFatEvidenceColumn(this, "Result", NativeFatEvidenceField.Result, 110));
    }

    // Retained as an isolated formatter for report/tests and compatibility paths. The visible
    // grid now routes timestamp fields through NativeFatEvidenceColumn so the existing evidence
    // refresh loop updates Value, Timestamp and Result atomically after capture and hydration.
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
    /// Compatibility timestamp column retained for source/binary compatibility. The production
    /// P4C grid uses read-only NativeFatEvidenceColumn timestamp fields so its established
    /// RefreshNativeFatEvidenceCells loop refreshes all five evidence cells together.
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
