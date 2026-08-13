using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// P2.1 operator-workstation layer. It adds rapid IED/signal search, consistent
/// Lucide-compatible action affordances and tighter industrial chrome without
/// changing any IEC 61850 acquisition, FAT scope, evidence or command semantics.
/// </summary>
internal static class P2IndustrialWorkstationUx
{
    private static readonly object ResourceSync = new();
    private static bool _resourcesInstalled;
    private static readonly ConditionalWeakTable<MainWindow, MainSearchState> MainStates = new();
    private static readonly ConditionalWeakTable<IoListTestingWindow, FatSearchState> FatStates = new();

    public static void Apply(Window window)
    {
        EnsureResources();

        switch (window)
        {
            case MainWindow main:
                ApplyMain(main);
                break;
            case IoListTestingWindow fat:
                ApplyFat(fat);
                break;
        }
    }

    private static void EnsureResources()
    {
        lock (ResourceSync)
        {
            if (_resourcesInstalled || Application.Current == null)
                return;

            var dictionary = new ResourceDictionary
            {
                Source = new Uri("/ARSAS;component/Resources/P2IndustrialControls.xaml", UriKind.Relative)
            };
            foreach (DictionaryEntry entry in dictionary)
                Application.Current.Resources[entry.Key] = entry.Value;

            _resourcesInstalled = true;
        }
    }

    private static void ApplyMain(MainWindow window)
    {
        if (window.FindName("WorkflowNavShell") is Border navShell)
        {
            navShell.CornerRadius = new CornerRadius(6);
            navShell.BorderThickness = new Thickness(1);
            navShell.Height = 52;
        }

        if (window.FindName("WorkflowPill") is Border pill)
        {
            pill.CornerRadius = new CornerRadius(5);
            pill.Height = 34;
        }

        InstallMainIedSearch(window);
        DecorateMainNavigation(window);
        DecorateTextButtons(window);
        ApplyIndustrialGridHeaders(window, skipCommandGrid: true);
        IntegrateHeroWithWorkstation(window);
    }

    private static void InstallMainIedSearch(MainWindow window)
    {
        if (MainStates.TryGetValue(window, out _))
            return;

        var list = Descendants<ListBox>(window)
            .FirstOrDefault(candidate => ReferenceEquals(candidate.ItemsSource, window.Devices));
        if (list == null || list.Parent is not DockPanel dock)
            return;

        var search = CreateSearchBox(window, "Search IED name, SCL name or IP");
        search.Margin = new Thickness(0, 0, 0, 9);
        DockPanel.SetDock(search, Dock.Top);
        var listIndex = dock.Children.IndexOf(list);
        dock.Children.Insert(Math.Max(0, listIndex), search);

        var view = CollectionViewSource.GetDefaultView(window.Devices);
        var state = new MainSearchState(search, view);
        MainStates.Add(window, state);

        search.TextChanged += (_, _) =>
        {
            var query = search.Text.Trim();
            view.Filter = item => item is Iec61850MonitorDevice device && MatchesDevice(device, query);
            view.Refresh();
        };

        window.PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                search.Focus();
                search.SelectAll();
                e.Handled = true;
            }
        };
    }

    private static void ApplyFat(IoListTestingWindow window)
    {
        InstallFatSearch(window);
        DecorateTextButtons(window);
        DecorateFatActionBar(window);
        ApplyIndustrialGridHeaders(window, skipCommandGrid: false);

        // Connect / Clock Sync / supplemental evidence controls are installed from
        // Loaded callbacks. Repeat the visual pass once that queue has drained.
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            DecorateTextButtons(window);
            DecorateFatActionBar(window);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void InstallFatSearch(IoListTestingWindow window)
    {
        if (FatStates.TryGetValue(window, out var existing))
        {
            existing.AttachSignalView(window.SelectedIed);
            return;
        }

        var iedList = Descendants<ListBox>(window)
            .FirstOrDefault(candidate => ReferenceEquals(candidate.ItemsSource, window.Project.Ieds));
        if (iedList == null || iedList.Parent is not DockPanel dock)
            return;

        var iedSearch = CreateSearchBox(window, "Search workbook IED or IP");
        iedSearch.Margin = new Thickness(5, 0, 5, 10);
        DockPanel.SetDock(iedSearch, Dock.Top);
        var listIndex = dock.Children.IndexOf(iedList);
        dock.Children.Insert(Math.Max(0, listIndex), iedSearch);

        if (window.FindName("WorkspacePreviewToggle") is not Button preview || preview.Parent is not Panel actionBar)
            return;

        var signalSearch = CreateSearchBox(window, "Search signal, IEC reference or test point");
        signalSearch.Width = 285;
        signalSearch.Margin = new Thickness(0, 0, 10, 0);
        signalSearch.VerticalAlignment = VerticalAlignment.Center;
        var previewIndex = actionBar.Children.IndexOf(preview);
        actionBar.Children.Insert(Math.Max(0, previewIndex), signalSearch);

        var iedView = CollectionViewSource.GetDefaultView(window.Project.Ieds);
        var state = new FatSearchState(iedSearch, signalSearch, iedView);
        FatStates.Add(window, state);

        iedSearch.TextChanged += (_, _) =>
        {
            var query = iedSearch.Text.Trim();
            iedView.Filter = item => item is IoTestIedPlan ied && MatchesFatIed(ied, query);
            iedView.Refresh();
        };

        signalSearch.TextChanged += (_, _) => state.RefreshSignalFilter(window.SelectedIed);
        state.AttachSignalView(window.SelectedIed);

        PropertyChangedEventHandler selectedChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(IoListTestingWindow.SelectedIed))
                state.AttachSignalView(window.SelectedIed);
        };
        window.PropertyChanged += selectedChanged;
        window.Closed += (_, _) => window.PropertyChanged -= selectedChanged;

        window.PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                signalSearch.Focus();
                signalSearch.SelectAll();
                e.Handled = true;
            }
        };
    }

    private static TextBox CreateSearchBox(FrameworkElement owner, string watermark)
        => new()
        {
            Style = Resource(owner, "IndustrialSearchTextBox") as Style,
            Tag = watermark,
            ToolTip = watermark
        };

    private static bool MatchesDevice(Iec61850MonitorDevice device, string query)
    {
        if (query.Length == 0)
            return true;

        return Contains(device.Name, query) ||
               Contains(device.SclIedName, query) ||
               Contains(device.IpAddress, query) ||
               Contains(device.EndpointText, query) ||
               Contains(device.IdentitySource, query);
    }

    private static bool MatchesFatIed(IoTestIedPlan ied, string query)
    {
        if (query.Length == 0)
            return true;

        return Contains(ied.IedName, query) ||
               Contains(ied.IpAddress, query) ||
               Contains(ied.IedRole, query) ||
               Contains(ied.LiveStatusText, query);
    }

    private static bool MatchesPoint(IoTestPointPlan point, string query)
    {
        if (query.Length == 0)
            return true;

        return Contains(point.TestPointId, query) ||
               Contains(point.SignalName, query) ||
               Contains(point.ObjectReference, query) ||
               Contains(point.EventLogSearchReference, query) ||
               Contains(point.SourceIecReference, query) ||
               Contains(point.ReportDisplayReference, query) ||
               Contains(point.LogicalDevice, query) ||
               Contains(point.LogicalNode, query) ||
               Contains(point.DataObject, query) ||
               Contains(point.DataAttribute, query) ||
               Contains(point.Runtime.StateText, query) ||
               Contains(point.Runtime.CurrentValue, query);
    }

    private static bool Contains(string? value, string query)
        => !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static void DecorateMainNavigation(MainWindow window)
    {
        ApplyTemplate(window.FindName("NavExplorerButton") as Button, window, "IconExplorerNavContent");
        ApplyTemplate(window.FindName("NavLiveButton") as Button, window, "IconMonitorNavContent");
        ApplyTemplate(window.FindName("NavEventsButton") as Button, window, "IconEventsNavContent");
        ApplyTemplate(window.FindName("NavGooseButton") as Button, window, "IconGooseNavContent");
        ApplyTemplate(window.FindName("NavDiagnosticsButton") as Button, window, "IconDiagnosticsNavContent");
    }

    private static void DecorateFatActionBar(IoListTestingWindow window)
    {
        if (window.FindName("WorkspacePreviewToggle") is not Button preview || preview.Parent is not Panel actionBar)
            return;

        ApplyTemplate(preview, window, "IconPrintContent");

        foreach (var button in actionBar.Children.OfType<Button>())
        {
            var text = button.Content?.ToString()?.Trim() ?? string.Empty;
            if (text.Equals("Pause", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Resume", StringComparison.OrdinalIgnoreCase))
            {
                button.Visibility = Visibility.Collapsed;
                continue;
            }

            ApplyCaptionTemplate(button, window, text);
        }
    }

    private static void DecorateTextButtons(FrameworkElement root)
    {
        foreach (var button in Descendants<Button>(root))
        {
            if (button.Content is UIElement && button.ContentTemplate == null)
                continue; // Already has a hand-built icon/content layout.

            var text = button.Content?.ToString()?.Trim() ?? string.Empty;
            ApplyCaptionTemplate(button, root, text);
        }
    }

    private static void ApplyCaptionTemplate(Button button, FrameworkElement owner, string text)
    {
        if (text.Length == 0)
            return;

        if (text.Equals("Print Preview", StringComparison.OrdinalIgnoreCase))
            ApplyTemplate(button, owner, "IconPrintContent");
        else if (text.StartsWith("Time Sync", StringComparison.OrdinalIgnoreCase))
            ApplyTemplate(button, owner, "IconClockContent");
        else if (text.StartsWith("COMTRADE", StringComparison.OrdinalIgnoreCase))
            ApplyTemplate(button, owner, "IconComtradeContent");
        else if (text.Equals("Connect", StringComparison.OrdinalIgnoreCase) ||
                 text.StartsWith("Refresh", StringComparison.OrdinalIgnoreCase) ||
                 text.StartsWith("Prepare", StringComparison.OrdinalIgnoreCase))
            ApplyTemplate(button, owner, "IconConnectContent");
        else if (text.Contains("FAT", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("Connect & Start", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("Connect & Continue", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("Retest", StringComparison.OrdinalIgnoreCase))
            ApplyTemplate(button, owner, "IconStartFatContent");
        else if (text.Equals("Stop", StringComparison.OrdinalIgnoreCase))
            ApplyTemplate(button, owner, "IconStopContent");
        else if (text.Equals("Save", StringComparison.OrdinalIgnoreCase))
            ApplyTemplate(button, owner, "IconSaveContent");
        else if (text.Equals("Excel", StringComparison.OrdinalIgnoreCase))
            ApplyTemplate(button, owner, "IconExcelContent");
        else if (text.Equals("PDF", StringComparison.OrdinalIgnoreCase))
            ApplyTemplate(button, owner, "IconPdfContent");
        else if (text.Contains("Export .arsas", StringComparison.OrdinalIgnoreCase))
            ApplyTemplate(button, owner, "IconExportContent");
        else if (text.Equals("Engineering", StringComparison.OrdinalIgnoreCase) ||
                 text.Equals("Engineering Workspace", StringComparison.OrdinalIgnoreCase))
            ApplyTemplate(button, owner, "IconWorkspaceContent");
    }

    private static void ApplyTemplate(Button? button, FrameworkElement owner, string key)
    {
        if (button != null && Resource(owner, key) is DataTemplate template)
            button.ContentTemplate = template;
    }

    private static void ApplyIndustrialGridHeaders(FrameworkElement owner, bool skipCommandGrid)
    {
        var header = Resource(owner, "IndustrialGridHeader") as Style;
        if (header == null)
            return;

        var commandStyle = Resource(owner, "CommandDataGrid");
        foreach (var grid in Descendants<DataGrid>(owner))
        {
            if (skipCommandGrid && grid.Style == commandStyle)
                continue;
            grid.ColumnHeaderStyle = header;
        }
    }

    private static void IntegrateHeroWithWorkstation(MainWindow window)
    {
        var hero = Descendants<Image>(window).FirstOrDefault(image =>
            image.Source?.ToString()?.Contains("gateway-hero", StringComparison.OrdinalIgnoreCase) == true);
        if (hero == null)
            return;

        hero.Opacity = 0.76;
        if (hero.Parent is Grid parent && !parent.Children.OfType<Border>().Any(border => Equals(border.Tag, "P2IndustrialHeroTint")))
        {
            var tint = new Border
            {
                Tag = "P2IndustrialHeroTint",
                Background = new SolidColorBrush(Color.FromArgb(0x1E, 0x2A, 0x46, 0x5B)),
                IsHitTestVisible = false
            };
            Panel.SetZIndex(tint, 1);
            parent.Children.Insert(Math.Min(1, parent.Children.Count), tint);

            // Keep the existing workflow card above the restrained industrial tint.
            foreach (UIElement child in parent.Children)
            {
                if (!ReferenceEquals(child, hero) && !ReferenceEquals(child, tint))
                    Panel.SetZIndex(child, 2);
            }
        }
    }

    private static object? Resource(FrameworkElement owner, object key)
        => owner.TryFindResource(key) ?? Application.Current?.TryFindResource(key);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private sealed record MainSearchState(TextBox Search, ICollectionView View);

    private sealed class FatSearchState
    {
        private ICollectionView? _signalView;

        public FatSearchState(TextBox iedSearch, TextBox signalSearch, ICollectionView iedView)
        {
            IedSearch = iedSearch;
            SignalSearch = signalSearch;
            IedView = iedView;
        }

        public TextBox IedSearch { get; }
        public TextBox SignalSearch { get; }
        public ICollectionView IedView { get; }

        public void AttachSignalView(IoTestIedPlan? selectedIed)
        {
            if (_signalView != null)
            {
                _signalView.Filter = null;
                _signalView.Refresh();
            }

            if (selectedIed == null)
            {
                _signalView = null;
                SignalSearch.IsEnabled = false;
                return;
            }

            SignalSearch.IsEnabled = true;
            _signalView = CollectionViewSource.GetDefaultView(selectedIed.TestPoints);
            RefreshSignalFilter(selectedIed);
        }

        public void RefreshSignalFilter(IoTestIedPlan? selectedIed)
        {
            if (_signalView == null || selectedIed == null)
                return;

            var query = SignalSearch.Text.Trim();
            _signalView.Filter = item => item is IoTestPointPlan point && MatchesPoint(point, query);
            _signalView.Refresh();
        }
    }
}