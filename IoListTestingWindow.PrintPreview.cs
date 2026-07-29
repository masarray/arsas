using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
    private WebBrowser? _printPreviewBrowser;
    private Button? _printPreviewToggle;
    private TextBlock? _printPreviewTitle;
    private TextBlock? _printPreviewSubtitle;
    private DispatcherTimer? _preparationStateGuard;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_printPreviewInstalled)
            return;

        InstallPerIedPrintPreview();
        InstallPreparationStateGuard();
        PropertyChanged += PrintPreviewWindow_PropertyChanged;
        Session.PropertyChanged += PrintPreviewSession_PropertyChanged;
        Closed += PrintPreviewWindow_Closed;
        _printPreviewInstalled = true;
    }

    private void InstallPerIedPrintPreview()
    {
        if (Content is not Grid root)
            return;

        var middle = root.Children
            .OfType<Grid>()
            .FirstOrDefault(child => Grid.GetRow(child) == 2);
        var workspaceBorder = middle?.Children
            .OfType<Border>()
            .FirstOrDefault(child => Grid.GetColumn(child) == 2);
        if (workspaceBorder?.Child is not Grid workspaceGrid)
            return;

        _signalWorkspaceGrid = workspaceGrid.Children.OfType<DataGrid>().FirstOrDefault();
        if (_signalWorkspaceGrid == null)
            return;

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
            .FirstOrDefault(border => Grid.GetRow(border) == 1 &&
                border.Descendants<ProgressBar>().Any(progress => progress.IsIndeterminate));
        if (preparationSurface != null)
            workspaceGrid.Children.Remove(preparationSurface);

        if (workspaceGrid.RowDefinitions.Count > 1)
            workspaceGrid.RowDefinitions[1].Height = new GridLength(0);
        if (workspaceGrid.RowDefinitions.Count > 2)
            workspaceGrid.RowDefinitions[2].Height = new GridLength(10);
    }

    private void InstallPreviewToggle(Grid root)
    {
        var headerBorder = root.Children
            .OfType<Border>()
            .FirstOrDefault(child => Grid.GetRow(child) == 0);
        var headerGrid = headerBorder?.Child as Grid;
        var actions = headerGrid?.Children.OfType<WrapPanel>().FirstOrDefault();
        if (actions == null)
            return;

        _printPreviewToggle = BuildButton("Print Preview", TogglePrintPreview_Click, primary: false);
        _printPreviewToggle.ToolTip = "Preview the selected IED report in the workspace";
        var pdfButtonIndex = actions.Children
            .OfType<Button>()
            .Select((button, index) => new { button, index })
            .FirstOrDefault(item => string.Equals(item.button.Content?.ToString(), "PDF", StringComparison.Ordinal))?.index;
        actions.Children.Insert(pdfButtonIndex.HasValue ? pdfButtonIndex.Value + 1 : actions.Children.Count, _printPreviewToggle);
    }

    private Grid BuildPrintPreviewHost()
    {
        var host = new Grid
        {
            Visibility = Visibility.Collapsed,
            Background = Brushes.White
        };
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var commandBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 253)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 230, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(13, 10, 13, 10)
        };
        var commandGrid = new Grid();
        commandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        commandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        _printPreviewTitle = new TextBlock
        {
            Text = "Print Preview",
            FontSize = 15.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush?)TryFindResource("Ink") ?? Brushes.Black
        };
        _printPreviewSubtitle = new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 10.8,
            Foreground = (Brush?)TryFindResource("Muted") ?? Brushes.DimGray
        };
        text.Children.Add(_printPreviewTitle);
        text.Children.Add(_printPreviewSubtitle);
        commandGrid.Children.Add(text);

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        commands.Children.Add(BuildButton("Refresh", (_, _) => RefreshPrintPreview(), primary: false));
        commands.Children.Add(BuildButton("Print", PrintCurrentPreview_Click, primary: false));
        commands.Children.Add(BuildButton("Export IED PDF", ExportSelectedIedPdf_Click, primary: true));
        commands.Children.Add(BuildButton("Signals", TogglePrintPreview_Click, primary: false));
        Grid.SetColumn(commands, 1);
        commandGrid.Children.Add(commands);
        commandBar.Child = commandGrid;
        host.Children.Add(commandBar);

        var browserFrame = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 230, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true
        };
        _printPreviewBrowser = new WebBrowser();
        browserFrame.Child = _printPreviewBrowser;
        Grid.SetRow(browserFrame, 2);
        host.Children.Add(browserFrame);
        return host;
    }

    private Button BuildButton(string text, RoutedEventHandler click, bool primary)
    {
        var button = new Button
        {
            Content = text,
            Style = TryFindResource(primary ? "PrimaryButton" : "SoftButton") as Style,
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
        if (!_printPreviewActive || _printPreviewBrowser == null || SelectedIed == null)
            return;

        _printPreviewTitle!.Text = $"Print Preview · {SelectedIed.IedName}";
        _printPreviewSubtitle!.Text = $"{SelectedIed.IpAddress} · per-IED evidence · {(Session.IsSessionActive ? "live draft" : "sealed/current state")}";
        var html = IoFatReportPreviewService.BuildHtml(
            Project,
            SelectedIed,
            Session.IsSessionActive && ReferenceEquals(Session.ActiveIed, SelectedIed));
        _printPreviewBrowser.NavigateToString(html);
    }

    private void PrintCurrentPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_printPreviewBrowser == null)
            return;
        try
        {
            _printPreviewBrowser.InvokeScript("print");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Print preview", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportSelectedIedPdf_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedIed == null)
            return;
        if (!EnsureSessionSealedForExport($"{SelectedIed.IedName} PDF evidence report"))
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
            var scopedProject = IoFatReportPreviewService.CreateIedScopedProject(Project, SelectedIed);
            IoFatPdfReportService.Save(dialog.FileName, scopedProject);
            MessageBox.Show(
                this,
                $"Per-IED PDF created successfully.\n\n{dialog.FileName}",
                "IED PDF exported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "IED PDF export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InstallPreparationStateGuard()
    {
        _preparationStateGuard = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _preparationStateGuard.Tick += (_, _) => ClearStalePreparationFlags();
        _preparationStateGuard.Start();
    }

    private void ClearStalePreparationFlags()
    {
        foreach (var ied in Project.Ieds)
        {
            if (!ied.IsPreparing || ReferenceEquals(ied, _preparingIed))
                continue;
            ied.SetPreparationState(false, ied.LiveStatusText);
        }
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
        _preparationStateGuard?.Stop();
        PropertyChanged -= PrintPreviewWindow_PropertyChanged;
        Session.PropertyChanged -= PrintPreviewSession_PropertyChanged;
        Closed -= PrintPreviewWindow_Closed;
    }
}

internal static class IoFatVisualTreeExtensions
{
    public static IEnumerable<T> Descendants<T>(this DependencyObject parent) where T : DependencyObject
    {
        if (parent == null)
            yield break;
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;
            foreach (var nested in child.Descendants<T>())
                yield return nested;
        }
    }
}
