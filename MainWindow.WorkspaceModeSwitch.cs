// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private const string WorkspaceModeSwitchTag = "ARSAS_WORKSPACE_MODE_SWITCH";
    private static readonly bool WorkspaceModeSwitchRegistered = RegisterWorkspaceModeSwitch();
    private Button? _workspaceFatButton;
    private IoListTestingWindow? _loadedIoFatWindow;

    private static bool RegisterWorkspaceModeSwitch()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(WorkspaceModeSwitch_Loaded));
        return true;
    }

    private static void WorkspaceModeSwitch_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.InstallWorkspaceModeSwitch();
    }

    private void InstallWorkspaceModeSwitch()
    {
        if (Content is not Grid root)
            return;

        var header = root.Children.OfType<Grid>().FirstOrDefault(child => Grid.GetRow(child) == 0);
        if (header == null || header.Children.OfType<FrameworkElement>()
                .Any(child => Equals(child.Tag, WorkspaceModeSwitchTag)))
            return;

        var shell = new Border
        {
            Tag = WorkspaceModeSwitchTag,
            Background = WorkspaceBrush("#E7ECF5"),
            BorderBrush = WorkspaceBrush("#D5DEEB"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(4),
            Margin = new Thickness(10, 0, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Switch between Engineering and IO List FAT workspaces"
        };
        Grid.SetColumn(shell, 1);

        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        modes.Children.Add(new Border
        {
            Background = TryFindResource("Accent") as Brush ?? WorkspaceBrush("#2563EB"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 7, 12, 7),
            Child = new TextBlock
            {
                Text = "ENGINEERING",
                Foreground = Brushes.White,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            }
        });

        _workspaceFatButton = new Button
        {
            Content = "IO LIST FAT",
            Style = TryFindResource("SoftButton") as Style,
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(4, 0, 0, 0),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand,
            ToolTip = "Open or return to the IO List FAT workspace"
        };
        _workspaceFatButton.Click += OpenOrResumeIoFatWorkspace_Click;
        modes.Children.Add(_workspaceFatButton);

        var menuButton = new Button
        {
            Content = "▾",
            Style = TryFindResource("SoftButton") as Style,
            Padding = new Thickness(8, 7, 8, 7),
            Margin = new Thickness(2, 0, 0, 0),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand,
            ToolTip = "Load SCL/CID, IO List workbook, or portable ARSAS project"
        };
        menuButton.Click += OpenIoFatWorkspaceMenu_Click;
        modes.Children.Add(menuButton);

        shell.Child = modes;
        header.Children.Add(shell);
        UpdateIoFatWorkspaceModeState();
    }

    internal void RegisterLoadedIoFatWindow(IoListTestingWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (ReferenceEquals(_loadedIoFatWindow, window))
            return;

        if (_loadedIoFatWindow != null)
            _loadedIoFatWindow.Closed -= LoadedIoFatWindow_Closed;

        _loadedIoFatWindow = window;
        _loadedIoFatWindow.Closed += LoadedIoFatWindow_Closed;
        UpdateIoFatWorkspaceModeState();
    }

    internal void ShowEngineeringWorkspaceFromFat(IoListTestingWindow window)
    {
        if (!ReferenceEquals(_loadedIoFatWindow, window))
            RegisterLoadedIoFatWindow(window);

        window.Storage?.ScheduleSave();
        window.Hide();
        IsEnabled = true;
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        SetStatus($"Engineering workspace active · IO List FAT project '{window.Project.ProjectName}' remains loaded.");
        UpdateIoFatWorkspaceModeState();
    }

    private void OpenOrResumeIoFatWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (ShowLoadedIoFatWorkspace())
            return;

        if (sender is Button anchor)
            OpenIoFatWorkspaceMenu(anchor);
    }

    private bool ShowLoadedIoFatWorkspace()
    {
        var window = _loadedIoFatWindow;
        if (window == null || !window.IsLoaded)
            return false;

        SetStatus($"Returning to loaded IO List FAT project '{window.Project.ProjectName}'.");
        IsEnabled = false;
        Hide();
        window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
        return true;
    }

    private void OpenIoFatWorkspaceMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button anchor)
            OpenIoFatWorkspaceMenu(anchor);
    }

    private void OpenIoFatWorkspaceMenu(Button anchor)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 5,
            StaysOpen = false
        };

        if (_loadedIoFatWindow is { IsLoaded: true } loaded)
        {
            var resume = new MenuItem
            {
                Header = $"Continue loaded FAT project · {loaded.Project.ProjectName}",
                FontWeight = FontWeights.SemiBold
            };
            resume.Click += (_, _) => ShowLoadedIoFatWorkspace();
            menu.Items.Add(resume);
            menu.Items.Add(new Separator());
        }

        var importScl = new MenuItem
        {
            Header = _loadedIoFatWindow is { IsLoaded: true }
                ? "Add SCL / CID to loaded FAT workspace"
                : "Import SCL / CID files"
        };
        importScl.Click += (_, _) =>
        {
            if (_loadedIoFatWindow is { IsLoaded: true } loaded)
            {
                // P0.4: SCL is additive while a FAT workspace is loaded. Existing IED
                // connections/session evidence stay alive; replacement is reserved for
                // explicit workbook/project open flows below.
                _ = OpenSclForLoadedFatAppendAsync(loaded);
                return;
            }

            OpenSclFatTesting_Click(this, new RoutedEventArgs());
        };

        var importWorkbook = new MenuItem
        {
            Header = _loadedIoFatWindow == null
                ? "Import IO List Excel workbook"
                : "Import another IO List Excel workbook"
        };
        importWorkbook.Click += (_, _) => QueueIoFatWorkspaceReplacement(
            () => OpenIoListTesting_Click(this, new RoutedEventArgs()));

        var openProject = new MenuItem
        {
            Header = _loadedIoFatWindow == null
                ? "Open portable .arsas project"
                : "Open another portable .arsas project"
        };
        openProject.Click += (_, _) => QueueIoFatWorkspaceReplacement(
            () => OpenIoListPackage_Click(this, new RoutedEventArgs()));

        menu.Items.Add(importScl);
        menu.Items.Add(importWorkbook);
        menu.Items.Add(openProject);
        menu.IsOpen = true;
    }

    private void QueueIoFatWorkspaceReplacement(Action openReplacement)
    {
        ArgumentNullException.ThrowIfNull(openReplacement);
        var loaded = _loadedIoFatWindow;
        if (loaded == null || !loaded.IsLoaded)
        {
            Dispatcher.BeginInvoke(openReplacement, DispatcherPriority.ContextIdle);
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"IO List FAT project '{loaded.Project.ProjectName}' is still loaded.\n\nSave and close that workspace before loading another project?",
            "Load another IO List FAT project",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        loaded.Close();
        if (ReferenceEquals(_loadedIoFatWindow, loaded))
            return;

        Dispatcher.BeginInvoke(openReplacement, DispatcherPriority.ContextIdle);
    }

    private void LoadedIoFatWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is IoListTestingWindow window)
            window.Closed -= LoadedIoFatWindow_Closed;
        if (ReferenceEquals(_loadedIoFatWindow, sender))
            _loadedIoFatWindow = null;
        IsEnabled = true;
        UpdateIoFatWorkspaceModeState();
    }

    private void UpdateIoFatWorkspaceModeState()
    {
        if (_workspaceFatButton == null)
            return;

        var loaded = _loadedIoFatWindow is { IsLoaded: true };
        _workspaceFatButton.Content = loaded ? "IO LIST FAT · LOADED" : "IO LIST FAT";
        _workspaceFatButton.ToolTip = loaded
            ? "Return instantly to the loaded IO List FAT workspace"
            : "Open an IO List FAT workbook or portable ARSAS project";
    }

    private static SolidColorBrush WorkspaceBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
