using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Engineering FAT pivot. The normal FAT destination is a native view over the exact
/// Engineering live-row collection. The legacy IoListTestingWindow host remains available
/// only for explicit/manual compatibility workflows and is never bootstrapped by navigation.
/// </summary>
public partial class MainWindow
{
    private bool _productionFatTabInstalled;
    private IoListTestingWindow? _productionFatWindow;
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

        window.TryInstallProductionFatTabPivot();
    }

    private void TryInstallProductionFatTabPivot()
    {
        if (_productionFatTabInstalled || !IsLoaded)
            return;

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

        // P5 normal-entry boundary: installing/navigating FAT is only a canonical view bind.
        // No Engineering -> IoTest projection/bootstrap module exists on this path.
        SynchronizeProductionFatSelectedIed();

        NavNativeFatButton.ToolTip = "Factory Acceptance Test · canonical Engineering rows + sparse evidence";

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
        var host = BuildNativeFatCanonicalWorkspace();
        ConvergeNativeFatWorkspaceShell(host);
        InstallNativeFatEvidenceBindingRuntime();
        InstallNativeFatDiagnosticButtons();

        return new Border
        {
            Style = TryFindResource("WorkspaceCard") as Style,
            Padding = new Thickness(12),
            Child = host
        };
    }

    private void ConvergeNativeFatWorkspaceShell(FrameworkElement host)
    {
        if (host is not Grid root)
            return;

        // Match Event Log / Live Monitor workspace geometry: one WorkspaceCard owns the
        // surface, its content uses 12 px inset, a flat header, a 10 px header-to-grid gap,
        // then the shared ModernDataGrid. FAT-specific nested card chrome made this tab look
        // like a separate application even though it is a sibling Engineering destination.
        root.Margin = new Thickness(0);

        var header = root.Children
            .OfType<Border>()
            .FirstOrDefault(child => Grid.GetRow(child) == 0);
        if (header != null)
        {
            header.Padding = new Thickness(0);
            header.CornerRadius = new CornerRadius(0);
            header.Background = null;
            header.BorderBrush = null;
            header.BorderThickness = new Thickness(0);
        }

        if (_nativeFatIedText != null && TryFindResource("WorkspaceTitle") is Style titleStyle)
        {
            _nativeFatIedText.Style = titleStyle;
            _nativeFatIedText.ClearValue(TextBlock.FontSizeProperty);
            _nativeFatIedText.ClearValue(TextBlock.FontWeightProperty);
            _nativeFatIedText.ClearValue(TextBlock.ForegroundProperty);
        }

        if (_nativeFatStatusText != null && TryFindResource("WorkspaceSubtitle") is Style subtitleStyle)
        {
            _nativeFatStatusText.Style = subtitleStyle;
            _nativeFatStatusText.Margin = new Thickness(0, 2, 0, 0);
            _nativeFatStatusText.ClearValue(TextBlock.FontSizeProperty);
            _nativeFatStatusText.ClearValue(TextBlock.ForegroundProperty);
        }
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
            _nativeFatCanonicalGrid?.Focus();
        }
        else
        {
            SaveNativeFatSessionState();
            _productionFatWindow?.Storage?.ScheduleSave();
        }
    }

    private void ProductionFat_MainWindowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedDevice))
            SynchronizeProductionFatSelectedIed();
    }

    private void SynchronizeProductionFatSelectedIed()
    {
        if (_productionFatWindow is { IsLoaded: true })
        {
            _productionFatWindow.SelectEngineeringDeviceForEmbeddedFat(SelectedDevice);
            return;
        }

        BindNativeFatCanonicalRows();
        BindNativeFatDiagnostics(SelectedDevice);
        RefreshNativeFatEvidenceBindingRuntime();
    }

    // Compatibility host contract: the global Engineering IED Explorer and shared Command Dock remain authoritative.
    // Mounting the explicit/manual IoListTestingWindow center must never replace those workstation shell owners.
    internal bool MountProductionFatWorkspace(IoListTestingWindow window, FrameworkElement surface)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(surface);
        if (!ProductionFatTabReady)
            return false;

        SaveNativeFatSessionState();
        _productionFatWindow = window;
        surface.DataContext = window;
        NativeFatTab.Content = surface;
        if (_persistentWorkbench != null)
            _persistentWorkbench.DockExpandedByWorkspace[NativeFatWorkspaceIndex] = true;

        window.Closed -= ProductionFatWindow_Closed;
        window.Closed += ProductionFatWindow_Closed;
        SynchronizeProductionFatSelectedIed();

        window.RegisterEmbeddedHostCloseCleanup();
        QueueNativeFatNavigationGeometry();

        if (MainTabs.SelectedIndex == NativeFatWorkspaceIndex)
            SetStatus($"FAT compatibility workspace · {window.Project.Ieds.Count} IED.");
        return true;
    }

    internal void UnmountProductionFatWorkspace(IoListTestingWindow window)
    {
        if (!ReferenceEquals(_productionFatWindow, window))
            return;

        window.Closed -= ProductionFatWindow_Closed;
        _productionFatWindow = null;
        NativeFatTab.Content = BuildProductionFatPermanentHost();
        SynchronizeProductionFatSelectedIed();
    }

    private void ProductionFatWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is IoListTestingWindow window)
            UnmountProductionFatWorkspace(window);
    }

    private void ProductionFat_MainWindowClosed(object? sender, EventArgs e)
    {
        // Persistence is fail-closed: write the latest sparse snapshot before any debounce
        // cancellation or service disposal can discard the final FAT transition.
        FlushNativeFatEvidenceBeforeShutdown();

        PropertyChanged -= ProductionFat_MainWindowPropertyChanged;
        MainTabs.SelectionChanged -= ProductionFat_MainTabsSelectionChanged;
        Closed -= ProductionFat_MainWindowClosed;
        _productionFatInstallRetry?.Stop();
        _productionFatInstallRetry = null;
        _productionFatWindow = null;
        if (_nativeFatCanonicalGrid != null)
            _nativeFatCanonicalGrid.CellEditEnding -= NativeFatCanonicalGrid_CellEditEnding;
        DisposeNativeFatEvidenceBindingRuntime();
        DisposeNativeFatDiagnostics();
        DisposeNativeFatArmCoordinator();
        _nativeFatCanonicalGrid = null;
        _nativeFatIedText = null;
        _nativeFatRowCountText = null;
        _nativeFatStatusText = null;
        _nativeFatSessionByIed.Clear();
        _nativeFatBoundIedKey = null;
    }
}
