using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ArIED61850Tester.Services.IoTesting;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private const string NativeFatPrintPreviewTitle = "IEC 61850 FAT Evidence Report";
    private Button? _nativeFatPrintPreviewButton;

    private enum NativePreviewLucideIcon
    {
        Printer,
        Minus,
        Plus,
        Maximize2,
        ChevronLeft,
        ChevronRight,
        RefreshCw,
        Save,
        X
    }

    private void NativeFatPrintPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        var device = SelectedDevice;
        if (device == null || device.Points.Count == 0)
        {
            SetStatus("FAT · select an Engineering IED with canonical rows before opening Print Preview");
            return;
        }

        CommitNativeFatEvidenceEdits();
        var snapshot = NativeFatPrintPreviewSnapshot.Capture(
            device,
            GetNativeFatSession(device.DeviceId));
        ShowNativeFatPrintPreview(snapshot);
        SetStatus(
            $"FAT · Print Preview captured {snapshot.Rows.Count} immutable canonical row(s) for {snapshot.IedName}");
    }

    /// <summary>
    /// Professional native preview: immutable selected-IED snapshot -> shared layout adapter ->
    /// existing FixedDocument renderer. The stock DocumentViewer toolbar is hidden and the
    /// ARSAS/Lucide-style toolbar owns print, zoom, fit, page navigation, refresh and Save PDF.
    /// Preview and Save PDF always consume the exact same IoFatReportLayoutPlan instance.
    /// </summary>
    private void ShowNativeFatPrintPreview(NativeFatPrintPreviewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var currentSnapshot = snapshot;
        var currentLayout = NativeFatP4DReportAdapter.Build(currentSnapshot, draft: true);
        var document = IoFatReportPreviewDocumentBuilder.Render(currentLayout);

        var preview = new Window
        {
            Owner = this,
            Title = NativeFatPrintPreviewTitle,
            Width = 1260,
            Height = 880,
            MinWidth = 960,
            MinHeight = 660,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(232, 237, 244))
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Border
        {
            Padding = new Thickness(14, 9, 14, 9),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 224, 234)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var toolbarGrid = new Grid();
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = "Report Preview",
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
        });
        var summary = new TextBlock
        {
            Text = NativeFatPreviewSummary(currentSnapshot),
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
        };
        titleStack.Children.Add(summary);
        toolbarGrid.Children.Add(titleStack);

        var viewer = new DocumentViewer
        {
            Document = document,
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Zoom = 100d
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };

        var pageText = new TextBlock
        {
            Text = "Page — / —",
            MinWidth = 76,
            Margin = new Thickness(7, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
        };
        var zoomText = new TextBlock
        {
            Text = "100%",
            MinWidth = 46,
            Margin = new Thickness(5, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
        };

        void UpdateViewerState()
        {
            pageText.Text = viewer.PageCount > 0
                ? $"Page {Math.Max(1, viewer.MasterPageNumber)} / {viewer.PageCount}"
                : "Page — / —";
            zoomText.Text = $"{viewer.Zoom:0}%";
        }

        Button IconButton(NativePreviewLucideIcon icon, string toolTip, Action action)
        {
            var button = new Button
            {
                Width = 32,
                Height = 30,
                Padding = new Thickness(6),
                Margin = new Thickness(2, 0, 2, 0),
                Style = TryFindResource("SoftButton") as Style,
                ToolTip = toolTip,
                Cursor = Cursors.Hand,
                Content = BuildNativePreviewLucideIcon(icon)
            };
            button.Click += (_, _) =>
            {
                action();
                preview.Dispatcher.BeginInvoke(UpdateViewerState, DispatcherPriority.Background);
            };
            return button;
        }

        actions.Children.Add(IconButton(NativePreviewLucideIcon.Printer, "Print report", viewer.Print));
        actions.Children.Add(IconButton(NativePreviewLucideIcon.Minus, "Zoom out", viewer.DecreaseZoom));
        actions.Children.Add(zoomText);
        actions.Children.Add(IconButton(NativePreviewLucideIcon.Plus, "Zoom in", viewer.IncreaseZoom));
        actions.Children.Add(IconButton(NativePreviewLucideIcon.Maximize2, "Fit report page to width", viewer.FitToWidth));
        actions.Children.Add(IconButton(NativePreviewLucideIcon.ChevronLeft, "Previous page", viewer.PreviousPage));
        actions.Children.Add(pageText);
        actions.Children.Add(IconButton(NativePreviewLucideIcon.ChevronRight, "Next page", viewer.NextPage));

        actions.Children.Add(IconButton(NativePreviewLucideIcon.RefreshCw, "Refresh from current FAT evidence", () =>
        {
            var device = SelectedDevice;
            if (device == null || device.Points.Count == 0)
                return;

            CommitNativeFatEvidenceEdits();
            currentSnapshot = NativeFatPrintPreviewSnapshot.Capture(
                device,
                GetNativeFatSession(device.DeviceId));
            currentLayout = NativeFatP4DReportAdapter.Build(currentSnapshot, draft: true);
            viewer.Document = IoFatReportPreviewDocumentBuilder.Render(currentLayout);
            summary.Text = NativeFatPreviewSummary(currentSnapshot);
            SetStatus($"FAT · Print Preview refreshed from {currentSnapshot.IedName} evidence");
        }));

        var savePdfButton = new Button
        {
            Height = 30,
            MinWidth = 94,
            Padding = new Thickness(9, 0, 10, 0),
            Margin = new Thickness(8, 0, 2, 0),
            Style = TryFindResource("PrimaryButton") as Style,
            ToolTip = "Save the exact layout currently shown in Print Preview as PDF.",
            Cursor = Cursors.Hand,
            Content = BuildNativePreviewLabeledContent(NativePreviewLucideIcon.Save, "Save PDF")
        };
        savePdfButton.Click += (_, _) => SaveNativeFatPreviewPdf(preview, currentSnapshot, currentLayout);
        actions.Children.Add(savePdfButton);
        actions.Children.Add(IconButton(NativePreviewLucideIcon.X, "Close preview", preview.Close));

        Grid.SetColumn(actions, 1);
        toolbarGrid.Children.Add(actions);
        toolbar.Child = toolbarGrid;
        root.Children.Add(toolbar);

        viewer.Loaded += (_, _) =>
        {
            CollapseNativeDocumentViewerChrome(viewer);
            viewer.FitToWidth();
            preview.Dispatcher.BeginInvoke(UpdateViewerState, DispatcherPriority.Background);
        };
        viewer.PageViewsChanged += (_, _) => UpdateViewerState();
        Grid.SetRow(viewer, 1);
        root.Children.Add(viewer);

        preview.Content = root;
        preview.Show();
    }

    private void CommitNativeFatEvidenceEdits()
    {
        _nativeFatCanonicalGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
        _nativeFatCanonicalGrid?.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private static string NativeFatPreviewSummary(NativeFatPrintPreviewSnapshot snapshot)
        => $"{snapshot.IedName} · {snapshot.Rows.Count} row(s) · {snapshot.ProgressText} · captured {snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss zzz}";

    private static FrameworkElement BuildNativePreviewLabeledContent(NativePreviewLucideIcon icon, string label)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(BuildNativePreviewLucideIcon(icon));
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold
        });
        return panel;
    }

    private static Viewbox BuildNativePreviewLucideIcon(NativePreviewLucideIcon icon)
    {
        // Same 24x24 vector language used by the legacy professional report UX.
        var geometry = icon switch
        {
            NativePreviewLucideIcon.Printer => "M6,9 L6,2 L18,2 L18,9 M6,18 L4,18 C2.9,18 2,17.1 2,16 L2,11 C2,9.9 2.9,9 4,9 L20,9 C21.1,9 22,9.9 22,11 L22,16 C22,17.1 21.1,18 20,18 L18,18 M6,14 L18,14 L18,22 L6,22 Z",
            NativePreviewLucideIcon.Minus => "M5,12 L19,12",
            NativePreviewLucideIcon.Plus => "M12,5 L12,19 M5,12 L19,12",
            NativePreviewLucideIcon.Maximize2 => "M8,3 L3,3 L3,8 M16,3 L21,3 L21,8 M8,21 L3,21 L3,16 M16,21 L21,21 L21,16",
            NativePreviewLucideIcon.ChevronLeft => "M15,18 L9,12 L15,6",
            NativePreviewLucideIcon.ChevronRight => "M9,18 L15,12 L9,6",
            NativePreviewLucideIcon.RefreshCw => "M3,12 A9,9 0 0 1 12,3 A9.75,9.75 0 0 1 18.74,5.74 L21,8 M21,3 L21,8 L16,8 M21,12 A9,9 0 0 1 12,21 A9.75,9.75 0 0 1 5.26,18.26 L3,16 M8,16 L3,16 L3,21",
            NativePreviewLucideIcon.Save => "M15.2,3 A2,2 0 0 1 16.6,3.6 L20.4,7.4 A2,2 0 0 1 21,8.8 L21,19 A2,2 0 0 1 19,21 L5,21 A2,2 0 0 1 3,19 L3,5 A2,2 0 0 1 5,3 Z M17,21 L17,14 A1,1 0 0 0 16,13 L8,13 A1,1 0 0 0 7,14 L7,21 M7,3 L7,7 A1,1 0 0 0 8,8 L15,8",
            NativePreviewLucideIcon.X => "M18,6 L6,18 M6,6 L18,18",
            _ => "M5,12 L19,12"
        };

        var path = new Path
        {
            Data = Geometry.Parse(geometry),
            Fill = Brushes.Transparent,
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform
        };
        path.SetBinding(
            Shape.StrokeProperty,
            new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1)
            });

        return new Viewbox
        {
            Width = 16,
            Height = 16,
            Child = path,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
    }

    private static void CollapseNativeDocumentViewerChrome(DocumentViewer viewer)
    {
        foreach (var toolbar in NativePreviewVisualDescendants<ToolBar>(viewer))
            toolbar.Visibility = Visibility.Collapsed;
    }

    private static IEnumerable<T> NativePreviewVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                yield return typed;
            foreach (var nested in NativePreviewVisualDescendants<T>(child))
                yield return nested;
        }
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
