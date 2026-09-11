using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Engineering FAT pivot: the permanent XAML FAT tab hosts the proven production
/// IoListTestingWindow workspace rather than a second/manual FAT implementation.
/// The global Engineering IED Explorer and shared Command Dock remain authoritative.
/// </summary>
public partial class MainWindow
{
    private bool _productionFatTabInstalled;
    private IoListTestingWindow? _productionFatWindow;
    private FrameworkElement? _productionFatSurface;
    private DispatcherTimer? _productionFatInstallRetry;

    internal bool ProductionFatTabReady => _productionFatTabInstalled && NativeFatTab != null;

    [ModuleInitializer]
    internal static void RegisterProductionFatTabPivot()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ProductionFatTab_MainWindowLoaded),
            handledEventsToo: true);
    }

    private static void ProductionFatTab_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._productionFatTabInstalled)
            return;

        // MainWindow.Loaded runs after XAML has materialized the canonical seventh tab but
        // before the first normal render. Install the permanent FAT shell immediately so the
        // operator never sees an empty/black seventh workspace while idle-dispatcher work waits.
        window.TryInstallProductionFatTabPivot();
    }

    private void TryInstallProductionFatTabPivot()
    {
        if (_productionFatTabInstalled || !IsLoaded)
            return;

        // M7: MainWindow.xaml is the sole owner of the seventh destination. Wait only
        // until the canonical XAML tab is present; there is no native FAT runtime to install.
        if (MainTabs.Items.Count <= NativeFatWorkspaceIndex ||
            !ReferenceEquals(MainTabs.Items[NativeFatWorkspaceIndex], NativeFatTab))
        {
            _productionFatInstallRetry ??= new DispatcherTimer(DispatcherPriority.Loaded)
            {
                Interval = TimeSpan.FromMilliseconds(60)
            };
            _productionFatInstallRetry.Tick -= ProductionFatInstallRetry_Tick;
            _productionFatInstallRetry.Tick += ProductionFatInstallRetry_Tick;
            _productionFatInstallRetry.Start();
            return;
        }

        _productionFatInstallRetry?.Stop();
        _productionFatTabInstalled = true;
        NativeFatTab.Content = BuildProductionFatPermanentHost();

        // Bootstrap remains navigation-gated. Queueing here is harmless because the
        // Engineering bootstrap itself refuses to build FAT unless the FAT tab is active.
        QueueProductionFatEngineeringBootstrap();

        // MainWindow.xaml owns both style and click routing for the seventh nav button.
        NavNativeFatButton.ToolTip = "Production FAT workspace · automatic Value 1 / Value 2 evidence capture";

        PropertyChanged += ProductionFat_MainWindowPropertyChanged;
        MainTabs.SelectionChanged += ProductionFat_MainTabsSelectionChanged;
        Closed += ProductionFat_MainWindowClosed;

        QueueNativeFatNavigationGeometry();
    }

    private void ProductionFatInstallRetry_Tick(object? sender, EventArgs e)
    {
        _productionFatInstallRetry?.Stop();
        TryInstallProductionFatTabPivot();
    }

    private FrameworkElement BuildProductionFatPermanentHost(
        string? statusText = null,
        bool isBusy = false)
    {
        var root = new Grid { Margin = new Thickness(0) };
        var card = new Border
        {
            MaxWidth = 520,
            Padding = new Thickness(28, 24, 28, 22),
            CornerRadius = new CornerRadius(18),
            Background = TryFindResource("CardBackground") as Brush ?? Brushes.White,
            BorderBrush = TryFindResource("CardBorder") as Brush ?? new SolidColorBrush(Color.FromRgb(220, 228, 239)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        panel.Children.Add(new TextBlock
        {
            Text = "IEC 61850 FAT",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryFindResource("Accent") as Brush ?? Brushes.RoyalBlue,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = isBusy ? "Preparing production workspace" : "Production FAT workspace",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryFindResource("Ink") as Brush ?? Brushes.Black,
            Margin = new Thickness(0, 5, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(statusText)
                ? "Select an Engineering IED with static DataSet scope, then open FAT."
                : statusText,
            FontSize = 11.5,
            Foreground = TryFindResource("Muted") as Brush ?? Brushes.DimGray,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        if (isBusy)
        {
            panel.Children.Add(new ProgressBar
            {
                Height = 4,
                IsIndeterminate = true,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 18, 0, 0),
                Foreground = TryFindResource("Accent") as Brush ?? Brushes.RoyalBlue,
                Background = new SolidColorBrush(Color.FromRgb(220, 231, 248))
            });
        }

        card.Child = panel;
        root.Children.Add(card);
        return root;
    }

    internal void ShowProductionFatBootstrapState(string message, bool isBusy)
    {
        if (!ProductionFatTabReady || _productionFatWindow is { IsLoaded: true })
            return;

        NativeFatTab.Content = BuildProductionFatPermanentHost(message, isBusy);
    }

    private void ProductionFat_MainTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs))
            return;

        QueueNativeFatNavigationGeometry();
        if (MainTabs.SelectedIndex == NativeFatWorkspaceIndex)
        {
            SynchronizeProductionFatSelectedIed();
            _productionFatWindow?.NotifyEmbeddedHostActivated();
        }
        else
        {
            _productionFatWindow?.Storage?.ScheduleSave();
        }
    }

    private void ProductionFat_MainWindowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedDevice))
            SynchronizeProductionFatSelectedIed();
    }

    private void SynchronizeProductionFatSelectedIed()
        => _productionFatWindow?.SelectEngineeringDeviceForEmbeddedFat(SelectedDevice);

    internal bool MountProductionFatWorkspace(IoListTestingWindow window, FrameworkElement surface)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(surface);
        if (!ProductionFatTabReady)
            return false;

        _productionFatWindow = window;
        _productionFatSurface = surface;
        surface.DataContext = window;
        NativeFatTab.Content = surface;
        if (_persistentWorkbench != null)
            _persistentWorkbench.DockExpandedByWorkspace[NativeFatWorkspaceIndex] = true;

        window.Closed -= ProductionFatWindow_Closed;
        window.Closed += ProductionFatWindow_Closed;
        SynchronizeProductionFatSelectedIed();

        // Passive mount: prewarming must never navigate, hide/show, activate, or steal
        // focus from the operator's current Engineering destination.
        window.RegisterEmbeddedHostCloseCleanup();
        QueueNativeFatNavigationGeometry();

        if (MainTabs.SelectedIndex == NativeFatWorkspaceIndex)
            SetStatus($"FAT ready in Engineering tab · {window.Project.Ieds.Count} IED · production auto-capture workflow.");
        return true;
    }

    internal void UnmountProductionFatWorkspace(IoListTestingWindow window)
    {
        if (!ReferenceEquals(_productionFatWindow, window))
            return;

        window.Closed -= ProductionFatWindow_Closed;
        _productionFatWindow = null;
        _productionFatSurface = null;
        NativeFatTab.Content = BuildProductionFatPermanentHost();
    }

    private void ProductionFatWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is IoListTestingWindow window)
            UnmountProductionFatWorkspace(window);
    }

    private void ProductionFat_MainWindowClosed(object? sender, EventArgs e)
    {
        PropertyChanged -= ProductionFat_MainWindowPropertyChanged;
        MainTabs.SelectionChanged -= ProductionFat_MainTabsSelectionChanged;
        Closed -= ProductionFat_MainWindowClosed;
        _productionFatInstallRetry?.Stop();
        _productionFatInstallRetry = null;
        _productionFatWindow = null;
        _productionFatSurface = null;
    }
}
