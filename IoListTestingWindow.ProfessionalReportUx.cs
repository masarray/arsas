using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;
using Microsoft.Win32;

namespace ArIED61850Tester;

/// <summary>
/// Customer-facing FAT report polish layered on the existing relay-bench-safe workspace.
/// Keeps report actions per IED, replaces ambiguous Unicode preview glyphs with Lucide-style
/// vectors, and adds a targeted IED-card close action without disturbing sibling sessions.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool ProfessionalReportUxRegistered = RegisterProfessionalReportUx();
    private bool _professionalReportUxInstalled;
    private int _professionalReportUxInstallAttempts;
    private readonly HashSet<IoTestIedPlan> _iedCardStopsInProgress = new();

    private enum PreviewLucideIcon
    {
        Printer,
        Copy,
        Minus,
        Plus,
        Maximize2,
        ChevronLeft,
        ChevronRight,
        RefreshCw,
        Save,
        X
    }

    private static bool RegisterProfessionalReportUx()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ProfessionalReportUx_Loaded));
        return true;
    }

    private static void ProfessionalReportUx_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window || window._professionalReportUxInstalled)
            return;

        window.Dispatcher.BeginInvoke(
            new Action(window.InstallProfessionalReportUx),
            DispatcherPriority.ContextIdle);
    }

    private void InstallProfessionalReportUx()
    {
        if (_professionalReportUxInstalled)
            return;
        if (_printPreviewHost == null)
        {
            if (++_professionalReportUxInstallAttempts < 8)
                Dispatcher.BeginInvoke(new Action(InstallProfessionalReportUx), DispatcherPriority.ContextIdle);
            return;
        }

        _professionalReportUxInstalled = true;
        PolishPreviewToolbar();
        ClarifyCombinedPdfAction();

        FatIedList.ItemContainerGenerator.StatusChanged += IedCardGenerator_StatusChanged;
        Session.PropertyChanged += ProfessionalReportUx_SessionPropertyChanged;
        Closed += ProfessionalReportUx_Closed;
        InstallIedCardCloseButtons();
    }

    private void PolishPreviewToolbar()
    {
        if (_printPreviewHost == null)
            return;

        var iconByToolTip = new Dictionary<string, PreviewLucideIcon>(StringComparer.Ordinal)
        {
            ["Print native report"] = PreviewLucideIcon.Printer,
            ["Copy selected report text"] = PreviewLucideIcon.Copy,
            ["Zoom out"] = PreviewLucideIcon.Minus,
            ["Zoom in"] = PreviewLucideIcon.Plus,
            ["Fit report page to width"] = PreviewLucideIcon.Maximize2,
            ["Previous page"] = PreviewLucideIcon.ChevronLeft,
            ["Next page"] = PreviewLucideIcon.ChevronRight,
            ["Refresh from current IED evidence"] = PreviewLucideIcon.RefreshCw
        };

        foreach (var button in VisualDescendants<Button>(_printPreviewHost))
        {
            var toolTip = button.ToolTip?.ToString() ?? string.Empty;
            if (iconByToolTip.TryGetValue(toolTip, out var icon))
            {
                button.Content = BuildLucideIcon(icon);
                button.Width = 30;
                button.Height = 28;
                continue;
            }

            if (!string.Equals(toolTip, "Export selected IED native PDF", StringComparison.Ordinal))
                continue;

            button.Click -= ExportSelectedIedPdf_Click;
            button.Click += ExportSelectedIedPdfIndependent_Click;
            button.Width = 92;
            button.Height = 28;
            button.Padding = new Thickness(8, 0, 9, 0);
            button.Content = BuildLucideLabeledContent(PreviewLucideIcon.Save, "Save PDF");
            button.ToolTip = "Save PDF for this IED only; other IED FAT sessions may keep running";
        }

        var nativeLabel = VisualDescendants<TextBlock>(_printPreviewHost)
            .FirstOrDefault(text => string.Equals(text.Text, "Native preview", StringComparison.Ordinal));
        if (nativeLabel != null)
            nativeLabel.Text = "Selected IED report";
    }

    private void ClarifyCombinedPdfAction()
    {
        var globalPdf = VisualDescendants<Button>(this)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "PDF", StringComparison.Ordinal));
        if (globalPdf == null)
            return;

        globalPdf.Content = "Combined PDF";
        globalPdf.ToolTip = "Export one combined report for all IEDs after every active IED FAT session is sealed";
    }

    private void IedCardGenerator_StatusChanged(object? sender, EventArgs e)
    {
        if (FatIedList.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            Dispatcher.BeginInvoke(new Action(InstallIedCardCloseButtons), DispatcherPriority.Background);
    }

    private void ProfessionalReportUx_SessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IoTestMultiSessionCoordinator.ActiveSessionCount) or nameof(IoTestMultiSessionCoordinator.HasActiveSessions) or
            nameof(IoTestMultiSessionCoordinator.IsSessionActive) or nameof(IoTestMultiSessionCoordinator.State))
        {
            Dispatcher.BeginInvoke(new Action(RefreshIedCardCloseButtons), DispatcherPriority.Background);
        }
    }

    private void InstallIedCardCloseButtons()
    {
        foreach (var ied in Project.Ieds)
        {
            if (FatIedList.ItemContainerGenerator.ContainerFromItem(ied) is not ListBoxItem container)
                continue;

            var headerGrid = VisualDescendants<Grid>(container)
                .FirstOrDefault(grid => grid.Children.OfType<TextBlock>().Any(text =>
                    string.Equals(
                        BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path,
                        nameof(IoTestIedPlan.IedName),
                        StringComparison.Ordinal)));
            if (headerGrid == null)
                continue;

            var existing = headerGrid.Children.OfType<Button>()
                .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "ARSAS_IED_SESSION_CLOSE", StringComparison.Ordinal));
            if (existing != null)
            {
                existing.Visibility = Session.IsIedSessionActive(ied) ? Visibility.Visible : Visibility.Collapsed;
                continue;
            }

            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var close = new Button
            {
                Tag = "ARSAS_IED_SESSION_CLOSE",
                DataContext = ied,
                Width = 24,
                Height = 24,
                Padding = new Thickness(4),
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(82, 98, 122)),
                Cursor = Cursors.Hand,
                ToolTip = "End FAT session for this IED. The IED and its saved evidence remain in the project.",
                Content = BuildLucideIcon(PreviewLucideIcon.X),
                Visibility = Session.IsIedSessionActive(ied) ? Visibility.Visible : Visibility.Collapsed
            };
            close.Click += CloseIedFatSession_Click;
            Grid.SetColumn(close, headerGrid.ColumnDefinitions.Count - 1);
            headerGrid.Children.Add(close);
        }
    }

    private void RefreshIedCardCloseButtons()
    {
        InstallIedCardCloseButtons();
        foreach (var ied in Project.Ieds)
        {
            if (FatIedList.ItemContainerGenerator.ContainerFromItem(ied) is not ListBoxItem container)
                continue;
            var close = VisualDescendants<Button>(container)
                .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "ARSAS_IED_SESSION_CLOSE", StringComparison.Ordinal));
            if (close != null)
                close.Visibility = Session.IsIedSessionActive(ied) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void CloseIedFatSession_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button button || button.DataContext is not IoTestIedPlan ied ||
            _iedCardStopsInProgress.Contains(ied) || !Session.IsIedSessionActive(ied))
        {
            return;
        }

        _iedCardStopsInProgress.Add(ied);
        button.IsHitTestVisible = false;
        var originalOpacity = button.Opacity;
        button.Opacity = 0.45;
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            IoTestSessionActionResult result;
            using (IoTestEvidenceJournal.BeginDeferredSealScope())
                result = StopIedSessionWithoutChangingExplorer(ied, "Stopped from IED Explorer card; per-IED evidence journal sealed.");

            ShowActionResult(result, $"{ied.IedName} FAT session could not stop");
            if (result.Succeeded)
            {
                await IoTestEvidenceJournal.AwaitDeferredSealsAsync();
                if (Storage != null)
                    await Task.Run(Storage.SaveNow);
            }

            RaiseStatusProperties();
            RaiseSelectedIedContextProperties();
            RefreshIedCardCloseButtons();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                ex.Message,
                $"{ied.IedName} evidence could not be sealed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _iedCardStopsInProgress.Remove(ied);
            button.Opacity = originalOpacity;
            button.IsHitTestVisible = true;
            RefreshIedCardCloseButtons();
        }
    }

    private IoTestSessionActionResult StopIedSessionWithoutChangingExplorer(IoTestIedPlan ied, string reason)
    {
        var previousContext = Session.SelectedIed;
        try
        {
            Session.SelectContext(ied);
            return Session.Stop(reason);
        }
        finally
        {
            Session.SelectContext(previousContext);
        }
    }

    private void ExportSelectedIedPdfIndependent_Click(object sender, RoutedEventArgs e)
    {
        var ied = SelectedIed;
        if (ied == null)
            return;
        if (Session.IsIedSessionActive(ied))
        {
            MessageBox.Show(
                this,
                $"Stop the {ied.IedName} FAT evidence session before saving its PDF. Other IED FAT sessions may remain active.",
                "Stop this IED before PDF export",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = $"Save {ied.IedName} ARSAS FAT evidence PDF",
            Filter = "PDF evidence report (*.pdf)|*.pdf",
            FileName = $"{SafeFileName(Project.ProjectId)}_{SafeFileName(ied.IedName)}_IO-FAT_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
            AddExtension = true,
            DefaultExt = ".pdf",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            Storage?.SaveNow();
            var scopedProject = IoFatReportPreviewService.CreateIedScopedProject(Project, ied);
            IoFatPdfReportService.Save(dialog.FileName, scopedProject);
            MessageBox.Show(
                this,
                $"Per-IED FAT evidence PDF created successfully.\n\n{dialog.FileName}",
                "IED PDF saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "IED PDF export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static FrameworkElement BuildLucideLabeledContent(PreviewLucideIcon icon, string label)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(BuildLucideIcon(icon));
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

    private static Viewbox BuildLucideIcon(PreviewLucideIcon icon)
    {
        // Geometry follows the Lucide 24x24 source paths. Keep these as vectors so the
        // toolbar remains crisp at Windows DPI scaling without shipping raster assets.
        var geometry = icon switch
        {
            PreviewLucideIcon.Printer => "M6,9 L6,2 L18,2 L18,9 M6,18 L4,18 C2.9,18 2,17.1 2,16 L2,11 C2,9.9 2.9,9 4,9 L20,9 C21.1,9 22,9.9 22,11 L22,16 C22,17.1 21.1,18 20,18 L18,18 M6,14 L18,14 L18,22 L6,22 Z",
            PreviewLucideIcon.Copy => "M8,8 L20,8 L20,20 L8,20 Z M4,16 L4,4 L16,4",
            PreviewLucideIcon.Minus => "M5,12 L19,12",
            PreviewLucideIcon.Plus => "M12,5 L12,19 M5,12 L19,12",
            PreviewLucideIcon.Maximize2 => "M8,3 L3,3 L3,8 M16,3 L21,3 L21,8 M8,21 L3,21 L3,16 M16,21 L21,21 L21,16",
            PreviewLucideIcon.ChevronLeft => "M15,18 L9,12 L15,6",
            PreviewLucideIcon.ChevronRight => "M9,18 L15,12 L9,6",
            PreviewLucideIcon.RefreshCw => "M3,12 A9,9 0 0 1 12,3 A9.75,9.75 0 0 1 18.74,5.74 L21,8 M21,3 L21,8 L16,8 M21,12 A9,9 0 0 1 12,21 A9.75,9.75 0 0 1 5.26,18.26 L3,16 M8,16 L3,16 L3,21",
            PreviewLucideIcon.Save => "M15.2,3 A2,2 0 0 1 16.6,3.6 L20.4,7.4 A2,2 0 0 1 21,8.8 L21,19 A2,2 0 0 1 19,21 L5,21 A2,2 0 0 1 3,19 L3,5 A2,2 0 0 1 5,3 Z M17,21 L17,14 A1,1 0 0 0 16,13 L8,13 A1,1 0 0 0 7,14 L7,21 M7,3 L7,7 A1,1 0 0 0 8,8 L15,8",
            PreviewLucideIcon.X => "M18,6 L6,18 M6,6 L18,18",
            _ => "M5,12 L19,12"
        };

        var path = new System.Windows.Shapes.Path
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

    private void ProfessionalReportUx_Closed(object? sender, EventArgs e)
    {
        Closed -= ProfessionalReportUx_Closed;
        Session.PropertyChanged -= ProfessionalReportUx_SessionPropertyChanged;
        FatIedList.ItemContainerGenerator.StatusChanged -= IedCardGenerator_StatusChanged;
    }
}