using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Hosts the proven IoListTestingWindow production workspace inside MainWindow's FAT tab.
/// The production Window remains loaded-but-hidden so its existing session controller,
/// auto-capture lifecycle, persistence and report-preview code stay the single FAT authority.
/// Only its central workspace + FAT status footer are re-parented into Engineering.
/// </summary>
public partial class IoListTestingWindow
{
    private bool _engineeringEmbeddedMountQueued;
    private bool _engineeringEmbeddedMounted;
    private FrameworkElement? _engineeringEmbeddedSurface;

    [ModuleInitializer]
    internal static void RegisterEmbeddedEngineeringFatHost()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(EmbeddedEngineeringFatHost_Loaded),
            handledEventsToo: true);
    }

    internal void PrepareForEmbeddedEngineeringHost()
    {
        // Must run before Window.Show(). A transparent/off-screen, non-activating donor
        // cannot produce the historical black/blank compositor frame while its exact
        // production center is being re-parented into MainWindow.
        ShowActivated = false;
        ShowInTaskbar = false;
        Opacity = 0d;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000d;
        Top = -32000d;
    }

    private static void EmbeddedEngineeringFatHost_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window ||
            !ReferenceEquals(e.OriginalSource, window) ||
            window._engineeringEmbeddedMounted ||
            window._engineeringEmbeddedMountQueued ||
            window.Owner is not MainWindow owner ||
            !owner.ProductionFatTabReady)
        {
            return;
        }

        window._engineeringEmbeddedMountQueued = true;

        // MainWindow's legacy launcher still calls Show() on this Window. Make that bootstrap
        // surface invisible immediately; the actual production visual is moved into Engineering
        // on the next Loaded-priority dispatcher turn, before ContextIdle command-panel work.
        window.PrepareForEmbeddedEngineeringHost();

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => window.TryMountIntoEngineering(owner)));
    }

    private void TryMountIntoEngineering(MainWindow owner)
    {
        _engineeringEmbeddedMountQueued = false;
        if (_engineeringEmbeddedMounted || !IsLoaded || !ReferenceEquals(Owner, owner) || !owner.ProductionFatTabReady)
            return;

        try
        {
            EnsureProductionFatPresentationForEmbeddedHost();
            DisableLegacyEmbeddedCommandPanel();
            var surface = DetachProductionFatCentralWorkspace();
            if (surface == null)
                return;

            _engineeringEmbeddedSurface = surface;
            _engineeringEmbeddedMounted = owner.MountProductionFatWorkspace(this, surface);
            if (!_engineeringEmbeddedMounted)
                return;

            // The central FAT view now belongs to MainWindow. Keep this Window loaded and
            // hidden because existing controller/session/event code is intentionally reused.
            Hide();
        }
        catch (Exception ex)
        {
            Opacity = 1d;
            ShowInTaskbar = true;
            ShowActivated = true;
            MessageBox.Show(
                owner,
                $"ARSAS could not embed the production FAT workspace. The standalone FAT window will remain available.\n\n{ex.Message}",
                "FAT workspace host",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void EnsureProductionFatPresentationForEmbeddedHost()
    {
        // P1 normally installs this during Loaded. Calling it explicitly is safe/idempotent
        // and guarantees the first embedded frame is the same V2 FAT grid as the old Window.
        InstallFatV2WorkspaceUx();

        // PrintPreview historically installs from OnContentRendered. The bootstrap Window is
        // intentionally transparent, so install the exact same production preview explicitly
        // before the central workspace is detached. This preserves full-center mode switching.
        if (!_printPreviewInstalled)
        {
            InstallPerIedPrintPreview();
            PropertyChanged += PrintPreviewWindow_PropertyChanged;
            Session.PropertyChanged += PrintPreviewSession_PropertyChanged;
            Closed += PrintPreviewWindow_Closed;
            _printPreviewInstalled = true;
        }

        // The embedded center already declares WorkspacePreviewToggle in XAML. Make that
        // visible button the production toggle authority instead of the hidden Window header.
        if (WorkspacePreviewToggle != null)
            _printPreviewToggle = WorkspacePreviewToggle;
    }

    private void DisableLegacyEmbeddedCommandPanel()
    {
        // Engineering already owns one shared Command Dock. Prevent the old FAT window's
        // duplicate command panel from being created/refreshed while embedded. A non-null
        // sentinel makes the queued legacy installer return immediately; null row/summary
        // references make its queued refresh a no-op. No command runtime semantics change.
        DetachFatCommandDevice();
        if (_fatCommandPanelShell?.Parent is Grid existingHost)
        {
            var row = Grid.GetRow(_fatCommandPanelShell);
            existingHost.Children.Remove(_fatCommandPanelShell);
            if (row >= 0 && row < existingHost.RowDefinitions.Count)
                existingHost.RowDefinitions[row].Height = new GridLength(0);
            if (row - 1 >= 0 && row - 1 < existingHost.RowDefinitions.Count)
                existingHost.RowDefinitions[row - 1].Height = new GridLength(0);
        }

        _fatCommandPanelShell ??= new Border { Visibility = Visibility.Collapsed };
        _fatCommandRows = null;
        _fatCommandSummary = null;
        _fatCommandEmptyState = null;
    }

    private FrameworkElement? DetachProductionFatCentralWorkspace()
    {
        if (Content is not Grid root)
            return null;

        var middle = root.Children
            .OfType<Grid>()
            .FirstOrDefault(child => Grid.GetRow(child) == 2);
        var workspaceBorder = middle?.Children
            .OfType<Border>()
            .FirstOrDefault(child => Grid.GetColumn(child) == 2);
        if (middle == null || workspaceBorder == null)
            return null;

        var footer = root.Children
            .OfType<Border>()
            .FirstOrDefault(child => Grid.GetRow(child) == 4);

        middle.Children.Remove(workspaceBorder);
        if (footer != null)
            root.Children.Remove(footer);

        var host = new Grid
        {
            DataContext = this,
            Margin = new Thickness(0)
        };
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        if (footer != null)
        {
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        workspaceBorder.Margin = new Thickness(0);
        Grid.SetRow(workspaceBorder, 0);
        Grid.SetColumn(workspaceBorder, 0);
        host.Children.Add(workspaceBorder);

        if (footer != null)
        {
            footer.Margin = new Thickness(0);
            Grid.SetRow(footer, 2);
            Grid.SetColumn(footer, 0);
            host.Children.Add(footer);
        }

        return host;
    }

    internal void SelectEngineeringDeviceForEmbeddedFat(Iec61850MonitorDevice? device)
    {
        if (!_engineeringEmbeddedMounted || device == null)
            return;

        var match = Project.Ieds.FirstOrDefault(ied =>
            (!string.IsNullOrWhiteSpace(ied.LiveDeviceId) &&
             ied.LiveDeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(ied.IpAddress) &&
             ied.IpAddress.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase)) ||
            ied.IedName.Equals(device.SclIedName, StringComparison.OrdinalIgnoreCase) ||
            ied.IedName.Equals(device.Name, StringComparison.OrdinalIgnoreCase));
        if (match == null || ReferenceEquals(SelectedIed, match))
            return;

        // M3 viewed-device contract: the persistent Engineering IED Explorer owns
        // what the FAT grid displays. SelectedIed/Session.SelectContext changes only that
        // projection; an active capture remains latched inside its per-IED controller.
        if (!CanSelectIed)
            return;

        SelectedIed = match;
    }

    internal void NotifyEmbeddedHostActivated()
    {
        if (!_engineeringEmbeddedMounted)
            return;

        RefreshFatV2WorkspaceUx(refreshRows: true);
        if (_printPreviewActive)
            RefreshPrintPreview();
    }

    private void EmbeddedEngineeringFatHost_Closed(object? sender, EventArgs e)
    {
        if (Owner is MainWindow owner)
            owner.UnmountProductionFatWorkspace(this);
        Closed -= EmbeddedEngineeringFatHost_Closed;
        _engineeringEmbeddedMounted = false;
        _engineeringEmbeddedSurface = null;
    }

    // Field initializer cannot attach an instance event. Hook cleanup once the embedded
    // surface has actually been mounted; this helper is called from the production owner.
    internal void RegisterEmbeddedHostCloseCleanup()
    {
        Closed -= EmbeddedEngineeringFatHost_Closed;
        Closed += EmbeddedEngineeringFatHost_Closed;
    }
}
