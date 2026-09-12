using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private const string NativeFatPrintPreviewTitle = "IEC 61850 FAT Evidence Report";
    private Button? _nativeFatPrintPreviewButton;

    private void NativeFatPrintPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        var device = SelectedDevice;
        if (device == null || device.Points.Count == 0)
        {
            SetStatus("FAT · select an Engineering IED with canonical rows before opening Print Preview");
            return;
        }

        // Commit the current operator evidence cell before copying the report snapshot.
        // Capture is intentionally invoked only from this click path: normal FAT navigation,
        // row binding, hydration, and acquisition never build a hidden report.
        _nativeFatCanonicalGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
        _nativeFatCanonicalGrid?.CommitEdit(DataGridEditingUnit.Row, true);

        var snapshot = NativeFatPrintPreviewSnapshot.Capture(
            device,
            GetNativeFatSession(device.DeviceId));
        ShowNativeFatPrintPreview(snapshot);
        SetStatus(
            $"FAT · Print Preview captured {snapshot.Rows.Count} immutable canonical row(s) for {snapshot.IedName}");
    }

    /// <summary>
    /// P4D preview path: immutable selected-IED snapshot -> thin report layout adapter ->
    /// existing FixedDocument renderer -> DocumentViewer. No live row, evidence cache,
    /// SCL import, discovery, reconnect, or second acquisition engine is retained here.
    /// </summary>
    private void ShowNativeFatPrintPreview(NativeFatPrintPreviewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var layout = NativeFatP4DReportAdapter.Build(snapshot, draft: true);
        var document = IoFatReportPreviewDocumentBuilder.Render(layout);

        var preview = new Window
        {
            Owner = this,
            Title = NativeFatPrintPreviewTitle,
            Width = 1220,
            Height = 860,
            MinWidth = 920,
            MinHeight = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(232, 237, 244))
        };

        var viewer = new DocumentViewer
        {
            Document = document,
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // DocumentViewer is the WPF FixedDocument authority for P4D. Its native chrome
        // provides pagination, zoom and print without rebuilding the report as a DataGrid.
        preview.Content = viewer;
        preview.Show();
    }
}
