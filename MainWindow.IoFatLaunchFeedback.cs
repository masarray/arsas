using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ArIED61850Tester;

/// <summary>
/// P1 first-visible feedback for FAT workspace launch.
///
/// Import/bootstrap already runs asynchronously, but the final WPF workspace construction can
/// still take measurable time on large projects. Keep a lightweight engineering progress
/// surface visible before that synchronous first-paint work begins so the operator never sees
/// an apparently frozen Engineering window. No import, persistence, acquisition, or FAT model
/// work is moved between threads here.
/// </summary>
public partial class MainWindow
{
    private Border? _ioFatLaunchOverlay;
    private TextBlock? _ioFatLaunchTitle;
    private TextBlock? _ioFatLaunchDetail;
    private bool _ioFatLaunchFeedbackInstalled;

    [ModuleInitializer]
    internal static void RegisterIoFatLaunchFeedback()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(IoFatLaunchFeedback_MainWindowLoaded),
            true);
    }

    private static void IoFatLaunchFeedback_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.InstallIoFatLaunchFeedback();
    }

    private void InstallIoFatLaunchFeedback()
    {
        if (_ioFatLaunchFeedbackInstalled || Content is not Grid root)
            return;

        _ioFatLaunchFeedbackInstalled = true;

        var title = new TextBlock
        {
            Text = "Preparing FAT workspace",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryFindResource("Ink") as Brush ?? new SolidColorBrush(Color.FromRgb(0x20, 0x30, 0x4A))
        };
        var detail = new TextBlock
        {
            Text = "Loading project data…",
            Margin = new Thickness(0, 4, 0, 11),
            FontSize = 11.2,
            Foreground = TryFindResource("Muted") as Brush ?? new SolidColorBrush(Color.FromRgb(0x66, 0x75, 0x8B)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 470
        };
        var progress = new ProgressBar
        {
            Height = 5,
            IsIndeterminate = true,
            Foreground = TryFindResource("Accent") as Brush ?? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            Background = TryFindResource("Line") as Brush ?? new SolidColorBrush(Color.FromRgb(0xDD, 0xE5, 0xF0))
        };
        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(detail);
        stack.Children.Add(progress);

        var card = new Border
        {
            Width = 430,
            Padding = new Thickness(18, 15, 18, 15),
            Background = TryFindResource("SurfaceElevated") as Brush ?? Brushes.White,
            BorderBrush = TryFindResource("BorderSubtle") as Brush ?? new SolidColorBrush(Color.FromRgb(0xDD, 0xE5, 0xF0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 5,
                Opacity = 0.14,
                Color = Color.FromRgb(0x17, 0x20, 0x33)
            },
            Child = stack
        };

        var overlay = new Border
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(178, 238, 243, 249)),
            Child = card
        };
        Grid.SetRow(overlay, 1);
        Grid.SetColumn(overlay, 0);
        Panel.SetZIndex(overlay, 900);
        root.Children.Add(overlay);

        _ioFatLaunchOverlay = overlay;
        _ioFatLaunchTitle = title;
        _ioFatLaunchDetail = detail;

        PropertyChanged += IoFatLaunchFeedback_PropertyChanged;
        IsVisibleChanged += IoFatLaunchFeedback_IsVisibleChanged;
        Closed += IoFatLaunchFeedback_Closed;
    }

    private void IoFatLaunchFeedback_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LastStatusText) || _ioFatLaunchOverlay == null)
            return;

        var status = LastStatusText ?? string.Empty;
        if (IsIoFatLaunchStartStatus(status))
        {
            ShowIoFatLaunchFeedback(status);
            return;
        }

        // Keep the already-painted surface through the final synchronous Window/XAML first
        // paint. MainWindow.Hide() collapses it immediately before the FAT window is shown.
        if (_ioFatLaunchOverlay.Visibility == Visibility.Visible &&
            status.StartsWith("FAT ready:", StringComparison.OrdinalIgnoreCase))
        {
            _ioFatLaunchTitle!.Text = "Opening FAT workspace";
            _ioFatLaunchDetail!.Text = "Preparing the test grid, restored evidence state, and workspace controls…";
            return;
        }

        if (IsIoFatLaunchTerminalStatus(status))
            HideIoFatLaunchFeedback();
    }

    private static bool IsIoFatLaunchStartStatus(string status)
        => status.Contains("Building shared Engineering/FAT workspace", StringComparison.OrdinalIgnoreCase) ||
           status.Contains("Importing IO List test plan", StringComparison.OrdinalIgnoreCase) ||
           status.Contains("Opening ARSAS IO FAT project", StringComparison.OrdinalIgnoreCase) ||
           status.Contains("Opening IO FAT project", StringComparison.OrdinalIgnoreCase);

    private static bool IsIoFatLaunchTerminalStatus(string status)
        => status.Contains("FAT import cancelled", StringComparison.OrdinalIgnoreCase) ||
           status.Contains("FAT import failed", StringComparison.OrdinalIgnoreCase) ||
           status.Contains("IO List import was rejected", StringComparison.OrdinalIgnoreCase) ||
           status.Contains("IO List import cancelled", StringComparison.OrdinalIgnoreCase) ||
           status.Contains("IO List import failed", StringComparison.OrdinalIgnoreCase) ||
           status.Contains("project import cancelled", StringComparison.OrdinalIgnoreCase) ||
           status.Contains("project import failed", StringComparison.OrdinalIgnoreCase) ||
           status.Contains("no static DataSet members", StringComparison.OrdinalIgnoreCase);

    private void ShowIoFatLaunchFeedback(string status)
    {
        if (_ioFatLaunchOverlay == null || _ioFatLaunchTitle == null || _ioFatLaunchDetail == null)
            return;

        _ioFatLaunchOverlay.Visibility = Visibility.Visible;
        if (status.Contains("IO List", StringComparison.OrdinalIgnoreCase))
        {
            _ioFatLaunchTitle.Text = "Loading IO FAT project";
            _ioFatLaunchDetail.Text = "Reading the approved workbook and restoring the FAT test plan…";
        }
        else if (status.Contains("ARSAS", StringComparison.OrdinalIgnoreCase) &&
                 status.Contains("project", StringComparison.OrdinalIgnoreCase))
        {
            _ioFatLaunchTitle.Text = "Opening ARSAS FAT project";
            _ioFatLaunchDetail.Text = "Restoring saved scope, evidence metadata, and workspace state…";
        }
        else
        {
            _ioFatLaunchTitle.Text = "Building FAT workspace";
            _ioFatLaunchDetail.Text = "Reading SCL sources and preparing the shared Engineering/FAT signal authority…";
        }
    }

    private void HideIoFatLaunchFeedback()
    {
        if (_ioFatLaunchOverlay != null)
            _ioFatLaunchOverlay.Visibility = Visibility.Collapsed;
    }

    private void IoFatLaunchFeedback_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // The Engineering window is hidden only after FAT window construction has completed.
        // Clearing here guarantees the progress layer is not still present when Engineering
        // becomes visible again after the FAT workspace closes.
        if (e.NewValue is false)
            HideIoFatLaunchFeedback();
    }

    private void IoFatLaunchFeedback_Closed(object? sender, EventArgs e)
    {
        PropertyChanged -= IoFatLaunchFeedback_PropertyChanged;
        IsVisibleChanged -= IoFatLaunchFeedback_IsVisibleChanged;
        Closed -= IoFatLaunchFeedback_Closed;
        _ioFatLaunchFeedbackInstalled = false;
        _ioFatLaunchOverlay = null;
        _ioFatLaunchTitle = null;
        _ioFatLaunchDetail = null;
    }
}
