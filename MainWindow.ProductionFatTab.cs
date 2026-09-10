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

        // The seventh FAT destination is now canonical MainWindow XAML. Wait only for the
        // native FAT state contribution to bind its compatibility fields; do not create,
        // restyle, or re-own navigation here.
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

        _nativeFatTab.Content = BuildProductionFatPermanentHost();

        // M2 prewarm starts as soon as the canonical host exists. It is deliberately
        // independent of the currently selected tab so a valid Engineering SCL/DataSet
        // can prepare the exact production surface before the operator first opens FAT.
        QueueProductionFatEngineeringBootstrap();

        // MainWindow.xaml owns the FAT button's SegmentedNavButton style and NavButton_Click.
        // Production FAT reacts to tab selection below; it must not install a second click owner.
        _nativeFatNavButton.ToolTip = "Production FAT workspace · automatic Value 1 / Value 2 evidence capture";

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

    private FrameworkElement BuildProductionFatPermanentHost()
    {
        // This is a stable shell slot, not a launcher/form. With a valid Engineering
        // static DataSet it is replaced in the background by the exact production FAT
        // workspace before first navigation. With no eligible IED it remains a quiet
        // contextual empty state and never creates an alternate FAT workflow.
        var root = new Grid { Margin = new Thickness(0) };
        root.Children.Add(new TextBlock
        {
            Text = "FAT · awaiting an Engineering IED with static DataSet scope",
            FontSize = 12,
            Foreground = TryFindResource("Muted") as Brush ?? Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        return root;
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
        if (_nativeFatTab != null)
            _nativeFatTab.Content = BuildProductionFatPermanentHost();
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
