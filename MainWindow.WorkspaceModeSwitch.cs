// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private const string WorkspaceModeSwitchTag = "ARSAS_WORKSPACE_MODE_SWITCH";
    private static readonly bool WorkspaceModeSwitchRegistered = RegisterWorkspaceModeSwitch();

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
            Margin = new Thickness(10, 0),
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
            Padding = new Thickness(12, 7),
            Child = new TextBlock
            {
                Text = "ENGINEERING",
                Foreground = Brushes.White,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            }
        });

        var fatButton = new Button
        {
            Content = "IO LIST FAT",
            Style = TryFindResource("SoftButton") as Style,
            Padding = new Thickness(12, 7),
            Margin = new Thickness(4, 0, 0, 0),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand,
            ToolTip = "Open an IO List FAT workspace"
        };
        fatButton.Click += OpenIoFatWorkspaceMenu_Click;
        modes.Children.Add(fatButton);

        shell.Child = modes;
        header.Children.Add(shell);
    }

    private void OpenIoFatWorkspaceMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button anchor)
            return;

        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 5,
            StaysOpen = false
        };

        var importWorkbook = new MenuItem { Header = "Import IO List Excel workbook" };
        importWorkbook.Click += OpenIoListTesting_Click;
        var openProject = new MenuItem { Header = "Open portable .arsas project" };
        openProject.Click += OpenIoListPackage_Click;

        menu.Items.Add(importWorkbook);
        menu.Items.Add(openProject);
        menu.IsOpen = true;
    }

    private static SolidColorBrush WorkspaceBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
