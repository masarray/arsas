using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Services.IoTesting;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private bool _printPreviewInstalled;
    private bool _printPreviewActive;
    private DataGrid? _signalWorkspaceGrid;
    private Grid? _printPreviewHost;
    private DocumentViewer? _printPreviewDocumentViewer;
    private Button? _printPreviewToggle;
    private TextBlock? _printPreviewZoomText;
    private TextBlock? _printPreviewPageText;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_printPreviewInstalled)
            return;

        InstallPerIedPrintPreview();
        PropertyChanged += PrintPreviewWindow_PropertyChanged;
        Session.PropertyChanged += PrintPreviewSession_PropertyChanged;
        Closed += PrintPreviewWindow_Closed;
        _printPreviewInstalled = true;
    }

    private void InstallPerIedPrintPreview()
    {
        if (Content is not Grid root)
            return;

        var middle = root.Children.OfType<Grid>().FirstOrDefault(child => Grid.GetRow(child) == 2);
        var workspaceBorder = middle?.Children.OfType<Border>().FirstOrDefault(child => Grid.GetColumn(child) == 2);
        if (workspaceBorder?.Child is not Grid workspaceGrid)
            return;

        _signalWorkspaceGrid = workspaceGrid.Children.OfType<DataGrid>().FirstOrDefault();
        if (_signalWorkspaceGrid == null)
            return;

        InstallSignalGridPolish(_signalWorkspaceGrid);
        RemoveMainPreparationSurface(workspaceGrid);
        InstallPreviewToggle(root);
        _printPreviewHost = BuildPrintPreviewHost();
        Grid.SetRow(_printPreviewHost, Grid.GetRow(_signalWorkspaceGrid));
        workspaceGrid.Children.Add(_printPreviewHost);
    }

    private static void RemoveMainPreparationSurface(Grid workspaceGrid)
    {
        var preparationSurface = workspaceGrid.Children
            .OfType<Border>()
            .FirstOrDefault(border => Grid.GetRow(border) == 1 && border.Descendants<ProgressBar>().Any(progress => progress.IsIndeterminate));
        if (preparationSurface != null)
            workspaceGrid.Children.Remove(preparationSurface);
        if (workspaceGrid.RowDefinitions.Count > 1)
            workspaceGrid.RowDefinitions[1].Height = new GridLength(0);
        if (workspaceGrid.RowDefinitions.Count > 2)
            workspaceGrid.RowDefinitions[2].Height = new GridLength(10);
    }

    private void InstallPreviewToggle(Grid root)
    {
        var headerBorder = root.Children.OfType<Border>().FirstOrDefault(child => Grid.GetRow(child) == 0);
        var actions = (headerBorder?.Child as Grid)?.Children.OfType<WrapPanel>().FirstOrDefault();
        if (actions == null)
            return;

        _printPreviewToggle = BuildHeaderButton("Print Preview", TogglePrintPreview_Click);
        _printPreviewToggle.ToolTip = "Open the selected IED in the native paged report preview";
        var pdfIndex = actions.Children.OfType<Button>()
            .Select((button, index) => new { button, index })
            .FirstOrDefault(item => string.Equals(item.button.Content?.ToString(), "PDF", StringComparison.Ordinal))?.index;
        actions.Children.Insert(pdfIndex ?? actions.Children.Count, _printPreviewToggle);
    }

    private Grid BuildPrintPreviewHost()
    {
        var host = new Grid
        {
            Visibility = Visibility.Collapsed,
            Background = ColorBrush(238, 243, 249)
        };
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Border
        {
            Height = 36,
            Background = ColorBrush(243, 247, 252),
            BorderBrush = ColorBrush(221, 230, 242),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 3, 8, 3)
        };
        var toolbarGrid = new Grid();
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var commands = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        commands.Children.Add(BuildToolbarButton("⎙", "Print native report", PrintCurrentPreview_Click));
        commands.Children.Add(BuildToolbarButton("⧉", "Copy selected report text", CopyCurrentPreview_Click));
        commands.Children.Add(BuildToolbarSeparator());
        commands.Children.Add(BuildToolbarButton("−", "Zoom out", ZoomOutPreview_Click));
        _printPreviewZoomText = ToolbarText("100%", 42);
        commands.Children.Add(_printPreviewZoomText);
        commands.Children.Add(BuildToolbarButton("+", "Zoom in", ZoomInPreview_Click));
        commands.Children.Add(BuildToolbarButton("□", "Fit report page to width", FitPreviewWidth_Click));
        commands.Children.Add(BuildToolbarSeparator());
        commands.Children.Add(BuildToolbarButton("‹", "Previous page", PreviousPreviewPage_Click));
        _printPreviewPageText = ToolbarText("Page 1 / 1", 74);
        commands.Children.Add(_printPreviewPageText);
        commands.Children.Add(BuildToolbarButton("›", "Next page", NextPreviewPage_Click));
        commands.Children.Add(BuildToolbarSeparator());
        commands.Children.Add(BuildToolbarButton("↻", "Refresh from current IED evidence", (_, _) => RefreshPrintPreview()));
        commands.Children.Add(BuildToolbarButton("⇩", "Export selected IED native PDF", ExportSelectedIedPdf_Click, primary: true));
        toolbarGrid.Children.Add(commands);

        var nativeLabel = new TextBlock
        {
            Text = "Native preview",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10.5,
            Foreground = ColorBrush(101, 116, 139),
            Margin = new Thickness(12, 0, 2, 0)
        };
        Grid.SetColumn(nativeLabel, 1);
        toolbarGrid.Children.Add(nativeLabel);
        toolbar.Child = toolbarGrid;
        host.Children.Add(toolbar);

        var viewerFrame = new Border
        {
            Background = ColorBrush(238, 243, 249),
            BorderBrush = ColorBrush(221, 230, 242),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 8, 8),
            ClipToBounds = true
        };
        _printPreviewDocumentViewer = new DocumentViewer
        {
            Background = ColorBrush(238, 243, 249),
            BorderThickness = new Thickness(0),
            Focusable = false,
            ShowPageBorders = true,
            Template = BuildChromeFreeDocumentViewerTemplate()
        };
        _printPreviewDocumentViewer.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, _) => QueuePreviewChromeUpdate()));
        viewerFrame.Child = _printPreviewDocumentViewer;
        Grid.SetRow(viewerFrame, 1);
        host.Children.Add(viewerFrame);
        return host;
    }

    private static ControlTemplate BuildChromeFreeDocumentViewerTemplate()
    {
        var contentHost = new FrameworkElementFactory(typeof(ScrollViewer));
        contentHost.Name = "PART_ContentHost";
        contentHost.SetValue(Control.BackgroundProperty, ColorBrush(238, 243, 249));
        contentHost.SetValue(UIElement.FocusableProperty, false);
        contentHost.SetValue(ScrollViewer.CanContentScrollProperty, true);
        contentHost.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        contentHost.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        return new ControlTemplate(typeof(DocumentViewer)) { VisualTree = contentHost };
    }

    private static TextBlock ToolbarText(string text, double minimumWidth)
        => new()
        {
            Text = text,
            MinWidth = minimumWidth,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10.8,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColorBrush(63, 74, 95)
        };

    private Button BuildToolbarButton(string glyph, string toolTip, RoutedEventHandler click, bool primary = false)
    {
        var button = new Button
        {
            Width = primary ? 30 : 28,
            Height = 28,
            Margin = new Thickness(1, 0, 1, 0),
            Padding = new Thickness(0),
            Background = primary ? ColorBrush(239, 246, 255) : Brushes.Transparent,
            BorderBrush = primary ? ColorBrush(215, 231, 255) : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Foreground = primary ? ColorBrush(37, 99, 235) : ColorBrush(63, 74, 95),
            Cursor = Cursors.Hand,
            ToolTip = toolTip,
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"),
                FontSize = glyph is "−" or "+" or "‹" or "›" ? 17 : 14,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Template = BuildToolbarButtonTemplate()
        };
        button.Click += click;
        return button;
    }

    private static ControlTemplate BuildToolbarButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Chrome";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, ColorBrush(246, 250, 255), "Chrome"));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, ColorBrush(199, 213, 234), "Chrome"));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, ColorBrush(231, 239, 251), "Chrome"));
        pressed.Setters.Add(new Setter(Border.BorderBrushProperty, ColorBrush(183, 202, 228), "Chrome"));
        template.Triggers.Add(pressed);
        return template;
    }

    private static Border BuildToolbarSeparator()
        => new()
        {
            Width = 1,
            Height = 18,
            Margin = new Thickness(6, 0, 6, 0),
            Background = ColorBrush(215, 225, 239),
            VerticalAlignment = VerticalAlignment.Center
        };

    private Button BuildHeaderButton(string text, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = text,
            Style = TryFindResource("SoftButton") as Style,
            Padding = new Thickness(11, 7, 11, 7),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += click;
        return button;
    }

    private void TogglePrintPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_signalWorkspaceGrid == null || _printPreviewHost == null)
            return;
        _printPreviewActive = !_printPreviewActive;
        _signalWorkspaceGrid.Visibility = _printPreviewActive ? Visibility.Collapsed : Visibility.Visible;
        _printPreviewHost.Visibility = _printPreviewActive ? Visibility.Visible : Visibility.Collapsed;
        if (_printPreviewToggle != null)
            _printPreviewToggle.Content = _printPreviewActive ? "Signals" : "Print Preview";
        if (_printPreviewActive)
            RefreshPrintPreview();
    }

    private void RefreshPrintPreview()
    {
        if (!_printPreviewActive || _printPreviewDocumentViewer == null || SelectedIed == null)
            return;
        try
        {
            var scopedProject = IoFatReportPreviewService.CreateIedScopedProject(Project, SelectedIed);
            var draft = Session.IsSessionActive && ReferenceEquals(Session.ActiveIed, SelectedIed);
            _printPreviewDocumentViewer.Document = IoFatReportPreviewDocumentBuilder.Build(scopedProject, draft);
            _printPreviewDocumentViewer.UpdateLayout();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _printPreviewDocumentViewer?.FitToWidth();
                UpdatePreviewChromeState();
            }), DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Native print preview", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PrintCurrentPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_printPreviewDocumentViewer?.Document == null)
            return;
        if (ApplicationCommands.Print.CanExecute(null, _printPreviewDocumentViewer))
            ApplicationCommands.Print.Execute(null, _printPreviewDocumentViewer);
        QueuePreviewChromeUpdate();
    }

    private void CopyCurrentPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_printPreviewDocumentViewer?.Document != null && ApplicationCommands.Copy.CanExecute(null, _printPreviewDocumentViewer))
            ApplicationCommands.Copy.Execute(null, _printPreviewDocumentViewer);
    }

    private void ZoomOutPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_printPreviewDocumentViewer?.Document == null) return;
        _printPreviewDocumentViewer.Zoom = Math.Max(40d, _printPreviewDocumentViewer.Zoom - 10d);
        QueuePreviewChromeUpdate();
    }

    private void ZoomInPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_printPreviewDocumentViewer?.Document == null) return;
        _printPreviewDocumentViewer.Zoom = Math.Min(300d, _printPreviewDocumentViewer.Zoom + 10d);
        QueuePreviewChromeUpdate();
    }

    private void FitPreviewWidth_Click(object sender, RoutedEventArgs e)
    {
        if (_printPreviewDocumentViewer?.Document == null) return;
        _printPreviewDocumentViewer.FitToWidth();
        QueuePreviewChromeUpdate();
    }

    private void PreviousPreviewPage_Click(object sender, RoutedEventArgs e)
    {
        if (_printPreviewDocumentViewer?.Document != null && NavigationCommands.PreviousPage.CanExecute(null, _printPreviewDocumentViewer))
            NavigationCommands.PreviousPage.Execute(null, _printPreviewDocumentViewer);
        QueuePreviewChromeUpdate();
    }

    private void NextPreviewPage_Click(object sender, RoutedEventArgs e)
    {
        if (_printPreviewDocumentViewer?.Document != null && NavigationCommands.NextPage.CanExecute(null, _printPreviewDocumentViewer))
            NavigationCommands.NextPage.Execute(null, _printPreviewDocumentViewer);
        QueuePreviewChromeUpdate();
    }

    private void QueuePreviewChromeUpdate()
        => Dispatcher.BeginInvoke(new Action(UpdatePreviewChromeState), DispatcherPriority.Background);

    private void UpdatePreviewChromeState()
    {
        if (_printPreviewDocumentViewer == null)
            return;
        if (_printPreviewZoomText != null)
            _printPreviewZoomText.Text = Math.Round(_printPreviewDocumentViewer.Zoom).ToString(CultureInfo.InvariantCulture) + "%";
        if (_printPreviewPageText != null)
            _printPreviewPageText.Text = $"Page {Math.Max(1, _printPreviewDocumentViewer.MasterPageNumber)} / {Math.Max(1, _printPreviewDocumentViewer.PageCount)}";
    }

    private void ExportSelectedIedPdf_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedIed == null || !EnsureSessionSealedForExport($"{SelectedIed.IedName} PDF evidence report"))
            return;
        var dialog = new SaveFileDialog
        {
            Title = $"Export {SelectedIed.IedName} native ARSAS IO FAT PDF",
            Filter = "PDF evidence report (*.pdf)|*.pdf",
            FileName = $"{SafeFileName(Project.ProjectId)}_{SafeFileName(SelectedIed.IedName)}_IO-FAT_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
            AddExtension = true,
            DefaultExt = ".pdf",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            Storage?.SaveNow();
            IoFatPdfReportService.Save(dialog.FileName, IoFatReportPreviewService.CreateIedScopedProject(Project, SelectedIed));
            MessageBox.Show(this, $"Per-IED PDF created successfully.\n\n{dialog.FileName}", "IED PDF exported", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "IED PDF export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InstallSignalGridPolish(DataGrid grid)
    {
        var centeredHeader = new Style(typeof(DataGridColumnHeader), TryFindResource(typeof(DataGridColumnHeader)) as Style);
        centeredHeader.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));

        ApplyColumn(grid, "LIVE", centeredHeader, CenteredTemplate("LiveBindingText", new Binding("IsLiveBound") { Converter = new IoFatLiveBrushConverter() }, "LiveBindingReason", 10.8, FontWeights.SemiBold));
        ApplyColumn(grid, "VALUE", centeredHeader, CenteredTemplate("Runtime.CurrentValue", new Binding("Runtime.CurrentValue") { Converter = new IoFatBooleanValueBrushConverter() }, "Runtime.CurrentValue", 11.8, FontWeights.Bold));
        ApplyColumn(grid, "QUALITY", centeredHeader, CenteredTemplate("Runtime.CurrentQuality", new Binding("Runtime.CurrentQuality") { Converter = new IoFatQualityBrushConverter() }, "Runtime.CurrentQuality", 10.9, FontWeights.SemiBold));
        ApplyColumn(grid, "ON · RELAY TIME", null, CenteredTemplate("Runtime.OnRelayTimestampText", new Binding("Runtime.OnEvidence") { Converter = new IoFatEvidenceBrushConverter() }, "Runtime.OnEvidenceToolTip", 10.5, FontWeights.SemiBold, mono: true));
        ApplyColumn(grid, "OFF · RELAY TIME", null, CenteredTemplate("Runtime.OffRelayTimestampText", new Binding("Runtime.OffEvidence") { Converter = new IoFatEvidenceBrushConverter() }, "Runtime.OffEvidenceToolTip", 10.5, FontWeights.SemiBold, mono: true));
        ApplyColumn(grid, "STATUS", centeredHeader, CenteredTemplate("Runtime.StateText", new Binding("Runtime.State") { Converter = new IoFatStateBrushConverter() }, "Runtime.StatusReason", 10.7, FontWeights.SemiBold));
        ApplyColumn(grid, "RESULT", centeredHeader, CenteredTemplate("Runtime.State", new Binding("Runtime.State") { Converter = new IoFatStateBrushConverter() }, "Runtime.StatusReason", 10.8, FontWeights.Bold, textConverter: new IoFatResultTextConverter()));
    }

    private static void ApplyColumn(DataGrid grid, string header, Style? headerStyle, DataTemplate template)
    {
        if (grid.Columns.FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal)) is not DataGridTemplateColumn column)
            return;
        if (headerStyle != null)
            column.HeaderStyle = headerStyle;
        column.CellTemplate = template;
    }

    private static DataTemplate CenteredTemplate(
        string textPath,
        Binding foreground,
        string toolTipPath,
        double fontSize,
        FontWeight fontWeight,
        bool mono = false,
        IValueConverter? textConverter = null)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(textPath) { Converter = textConverter });
        text.SetBinding(TextBlock.ForegroundProperty, foreground);
        text.SetBinding(FrameworkElement.ToolTipProperty, new Binding(toolTipPath));
        text.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.FontSizeProperty, fontSize);
        text.SetValue(TextBlock.FontWeightProperty, fontWeight);
        if (mono)
            text.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Cascadia Mono, Consolas"));
        return new DataTemplate { VisualTree = text };
    }

    private void PrintPreviewWindow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_printPreviewActive && e.PropertyName == nameof(SelectedIed))
            Dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshPrintPreview);
    }

    private void PrintPreviewSession_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_printPreviewActive && e.PropertyName is nameof(Session.State) or nameof(Session.EvidenceRecordCount))
            Dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshPrintPreview);
    }

    private void PrintPreviewWindow_Closed(object? sender, EventArgs e)
    {
        PropertyChanged -= PrintPreviewWindow_PropertyChanged;
        Session.PropertyChanged -= PrintPreviewSession_PropertyChanged;
        Closed -= PrintPreviewWindow_Closed;
    }

    private static SolidColorBrush ColorBrush(byte red, byte green, byte blue)
        => new(Color.FromRgb(red, green, blue));
}

internal static class IoFatVisualTreeExtensions
{
    public static IEnumerable<T> Descendants<T>(this DependencyObject parent) where T : DependencyObject
    {
        if (parent == null)
            yield break;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;
            foreach (var nested in child.Descendants<T>())
                yield return nested;
        }
    }
}
