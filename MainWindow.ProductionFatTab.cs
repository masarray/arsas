using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// P2 FAT pivot: the Engineering FAT tab is a host for the proven production
/// IoListTestingWindow workspace rather than a second/manual FAT implementation.
/// The global Engineering IED Explorer and shared Command Dock remain authoritative.
/// </summary>
public partial class MainWindow
{
    private bool _productionFatTabInstalled;
    private IoListTestingWindow? _productionFatWindow;
    private FrameworkElement? _productionFatSurface;
    private DispatcherTimer? _productionFatInstallRetry;

    internal bool ProductionFatTabReady => _productionFatTabInstalled && _nativeFatTab != null;

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

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(window.TryInstallProductionFatTabPivot));
    }

    private void TryInstallProductionFatTabPivot()
    {
        if (_productionFatTabInstalled || !IsLoaded)
            return;

        // PR #290 creates the seventh navigation slot. Wait until that shell contribution
        // exists, then replace only its FAT content/handlers; P0/P1 shell geometry is untouched.
        if (!_nativeFatInstalled || _nativeFatTab == null || _nativeFatNavButton == null)
        {
            _productionFatInstallRetry ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _productionFatInstallRetry.Tick -= ProductionFatInstallRetry_Tick;
            _productionFatInstallRetry.Tick += ProductionFatInstallRetry_Tick;
            _productionFatInstallRetry.Start();
            return;
        }

        _productionFatInstallRetry?.Stop();
        _productionFatTabInstalled = true;

        // Retire the experimental manual-capture surface. Keep its code isolated in the
        // branch for now so this pivot is reversible while production FAT parity is verified.
        _nativeFatReconcileTimer?.Stop();
        _nativeFatSaveTimer?.Stop();
        AttachNativeFatObservedDevice(null);
        PropertyChanged -= NativeFat_MainWindowPropertyChanged;
        MainTabs.SelectionChanged -= NativeFat_MainTabsSelectionChanged;
        _nativeFatNavButton.Click -= NativeFatNavButton_Click;

        _nativeFatTab.Content = BuildProductionFatLauncher();

        // FAT must be visually indistinguishable from the existing workflow tabs.
        _nativeFatNavButton.Style = NavDiagnosticsButton.Style;
        _nativeFatNavButton.Padding = NavDiagnosticsButton.Padding;
        _nativeFatNavButton.Margin = NavDiagnosticsButton.Margin;
        _nativeFatNavButton.HorizontalContentAlignment = NavDiagnosticsButton.HorizontalContentAlignment;
        _nativeFatNavButton.VerticalContentAlignment = NavDiagnosticsButton.VerticalContentAlignment;
        _nativeFatNavButton.ToolTip = "Production FAT workspace · automatic Value 1 / Value 2 evidence capture";
        _nativeFatNavButton.Click += ProductionFatNavButton_Click;

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

    private FrameworkElement BuildProductionFatLauncher()
    {
        var root = new Grid { Margin = new Thickness(0) };
        var card = new Border
        {
            MaxWidth = 720,
            Padding = new Thickness(28, 24, 28, 24),
            CornerRadius = new CornerRadius(16),
            Background = TryFindResource("Surface") as Brush ?? Brushes.White,
            BorderBrush = TryFindResource("Line") as Brush ?? new SolidColorBrush(Color.FromRgb(0xDC, 0xE4, 0xEF)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "FAT WORKSPACE",
            Style = TryFindResource("MicroLabel") as Style,
            Foreground = TryFindResource("Accent") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = "Production FAT inside Engineering",
            Margin = new Thickness(0, 7, 0, 0),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryFindResource("Ink") as Brush ?? Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = "Open an SCL FAT project to use the proven automatic capture / completion workflow. The global IED Explorer stays visible at left and the shared Command Dock remains the only command surface.",
            Margin = new Thickness(0, 10, 0, 18),
            MaxWidth = 610,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.2,
            Foreground = TryFindResource("Muted") as Brush ?? Brushes.DimGray
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var openScl = new Button
        {
            Content = "Open SCL for FAT",
            Style = TryFindResource("PrimaryButton") as Style,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        openScl.Click += OpenSclFatTesting_Click;
        actions.Children.Add(openScl);

        var openProject = new Button
        {
            Content = "Open ARSAS Project",
            Style = TryFindResource("SoftButton") as Style,
            Padding = new Thickness(14, 8, 14, 8)
        };
        openProject.Click += OpenIoListPackage_Click;
        actions.Children.Add(openProject);
        content.Children.Add(actions);

        content.Children.Add(new TextBlock
        {
            Text = "Excel workflow is intentionally not part of this primary FAT tab.",
            Margin = new Thickness(0, 14, 0, 0),
            FontSize = 10.6,
            Foreground = TryFindResource("Muted") as Brush ?? Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        card.Child = content;
        root.Children.Add(card);
        return root;
    }

    private void ProductionFatNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (MainTabs.Items.Count <= NativeFatWorkspaceIndex)
            return;

        MainTabs.SelectedIndex = NativeFatWorkspaceIndex;
        QueueNativeFatNavigationGeometry();
        SynchronizeProductionFatSelectedIed();
        _productionFatWindow?.NotifyEmbeddedHostActivated();
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
        if (!ProductionFatTabReady || _nativeFatTab == null)
            return false;

        _productionFatWindow = window;
        _productionFatSurface = surface;
        surface.DataContext = window;
        _nativeFatTab.Content = surface;
        if (_persistentWorkbench != null)
            _persistentWorkbench.DockExpandedByWorkspace[NativeFatWorkspaceIndex] = true;

        window.Closed -= ProductionFatWindow_Closed;
        window.Closed += ProductionFatWindow_Closed;
        SynchronizeProductionFatSelectedIed();

        MainTabs.SelectedIndex = NativeFatWorkspaceIndex;
        QueueNativeFatNavigationGeometry();

        // The legacy launcher hides Engineering before showing IoListTestingWindow.
        // Once its proven central workspace is re-parented here, restore Engineering and
        // keep the legacy Window loaded-but-hidden solely as the production controller owner.
        IsEnabled = true;
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();

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
        if (_nativeFatTab != null)
            _nativeFatTab.Content = BuildProductionFatLauncher();
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
