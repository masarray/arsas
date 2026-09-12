using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArIED61850Tester.Services.IoTesting;
using Microsoft.Win32;

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
    /// existing FixedDocument renderer -> DocumentViewer. Preview and Save PDF consume the
    /// exact same IoFatReportLayoutPlan instance. No live row, evidence cache, SCL import,
    /// discovery, reconnect, or second acquisition engine is retained here.
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

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 224, 234)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var toolbarGrid = new Grid();
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var summary = new TextBlock
        {
            Text = $"{snapshot.IedName} · {snapshot.Rows.Count} row(s) · {snapshot.ProgressText} · captured {snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss zzz}",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85))
        };
        toolbarGrid.Children.Add(summary);

        var savePdfButton = new Button
        {
            Content = "Save PDF",
            MinWidth = 96,
            Padding = new Thickness(14, 7, 14, 7),
            Style = TryFindResource("PrimaryButton") as Style,
            ToolTip = "Save this exact immutable preview layout as PDF."
        };
        savePdfButton.Click += (_, _) => SaveNativeFatPreviewPdf(preview, snapshot, layout);
        Grid.SetColumn(savePdfButton, 1);
        toolbarGrid.Children.Add(savePdfButton);
        toolbar.Child = toolbarGrid;
        root.Children.Add(toolbar);

        var viewer = new DocumentViewer
        {
            Document = document,
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(viewer, 1);
        root.Children.Add(viewer);

        // DocumentViewer is the WPF FixedDocument authority for P4D. Its native chrome
        // provides pagination, zoom and print without rebuilding the report as a DataGrid.
        preview.Content = root;
        preview.Show();
    }

    private void SaveNativeFatPreviewPdf(
        Window owner,
        NativeFatPrintPreviewSnapshot snapshot,
        IoFatReportLayoutPlan layout)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save IEC 61850 FAT Evidence Report",
            Filter = "PDF document (*.pdf)|*.pdf",
            AddExtension = true,
            DefaultExt = ".pdf",
            OverwritePrompt = true,
            FileName = BuildNativeFatPdfFileName(snapshot.IedName)
        };

        if (dialog.ShowDialog(owner) != true)
            return;

        try
        {
            var primaryReference = snapshot.Rows
                .Select(row => row.IecTelegram)
                .FirstOrDefault(reference => !string.IsNullOrWhiteSpace(reference))
                ?? snapshot.DeviceId;

            // Critical P4D invariant: serialize the exact layout already rendered above.
            // Do not rebuild a project, snapshot, row list, SCL model, or report layout here.
            IoFatPdfReportService.SaveLayout(
                dialog.FileName,
                layout,
                snapshot.IedName,
                primaryReference);
            SetStatus($"FAT · PDF saved · {dialog.FileName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatus($"FAT · PDF save failed · {ex.Message}");
            MessageBox.Show(
                owner,
                ex.Message,
                "Save PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string BuildNativeFatPdfFileName(string? iedName)
    {
        var source = string.IsNullOrWhiteSpace(iedName) ? "IED" : iedName.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(source.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (safe.Length == 0)
            safe = "IED";
        return $"{safe}-FAT-Evidence.pdf";
    }
}
