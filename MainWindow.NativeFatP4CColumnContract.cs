using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    /// <summary>
    /// P4C makes FAT a thin view over the canonical IEC Explorer rows.
    /// The grid keeps SelectedDevice.Points as its ItemsSource and exposes only the
    /// exact Explorer-facing contract plus the three FAT evidence fields.
    /// </summary>
    private void ApplyNativeFatP4CColumnContract()
    {
        if (_nativeFatCanonicalGrid == null)
            return;

        _nativeFatCanonicalGrid.Columns.Clear();
        _nativeFatCanonicalGrid.FrozenColumnCount = 2;

        AddCanonicalTextColumn("Signal", nameof(Iec61850MonitorPoint.SignalName), 220);
        AddCanonicalTextColumn("IEC Telegram", nameof(Iec61850MonitorPoint.IecTelegram), 340);
        AddCanonicalTextColumn("Quality", nameof(Iec61850MonitorPoint.Quality), 105);
        AddCanonicalTemplateColumn("Live Value", "ProcessValueBadgeTemplate", 140);

        // Value 1 / Value 2 deliberately remain row-bound evidence columns.
        // Their displayed timestamp is formatted by the P4B structured evidence layer.
        _nativeFatCanonicalGrid.Columns.Add(
            new NativeFatEvidenceColumn(this, "Value 1", NativeFatEvidenceField.Value1, 225));
        _nativeFatCanonicalGrid.Columns.Add(
            new NativeFatEvidenceColumn(this, "Value 2", NativeFatEvidenceField.Value2, 225));
        _nativeFatCanonicalGrid.Columns.Add(
            new NativeFatEvidenceColumn(this, "Result", NativeFatEvidenceField.Result, 110));
    }
}
