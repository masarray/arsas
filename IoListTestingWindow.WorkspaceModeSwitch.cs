// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private const string FatWorkspaceModeTag = "ARSAS_FAT_WORKSPACE_MODE";
    private static readonly bool FatWorkspaceModeRegistered = RegisterFatWorkspaceMode();
    private bool _fatWorkspaceModeInstalled;

    private static bool RegisterFatWorkspaceMode()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(FatWorkspaceMode_Loaded));
        return true;
    }

    private static void FatWorkspaceMode_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is IoListTestingWindow window)
            window.InstallFatWorkspaceModeSwitch();
    }

    private void InstallFatWorkspaceModeSwitch()
    {
        if (_fatWorkspaceModeInstalled)
        {
            if (Owner is MainWindow existingOwner)
                existingOwner.RegisterLoadedIoFatWindow(this);
            return;
        }

        var originalEngineeringButton = VisualDescendants<Button>(this)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Engineering", StringComparison.Ordinal));
        if (originalEngineeringButton?.Parent is not WrapPanel actions)
            return;

        var originalIndex = actions.Children.IndexOf(originalEngineeringButton);
        var engineeringButton = new Button
        {
            Content = "Engineering Workspace",
            Style = originalEngineeringButton.Style,
            Padding = originalEngineeringButton.Padding,
            Margin = new Thickness(0),
            FontSize = originalEngineeringButton.FontSize,
            FontWeight = originalEngineeringButton.FontWeight,
            ToolTip = "Return to Engineering without unloading this FAT project",
            VerticalAlignment = originalEngineeringButton.VerticalAlignment,
            HorizontalAlignment = originalEngineeringButton.HorizontalAlignment
        };
        engineeringButton.Click += EngineeringWorkspaceMode_Click;

        actions.Children.Remove(originalEngineeringButton);

        var selectedMode = new Border
        {
            Tag = FatWorkspaceModeTag,
            Background = TryFindResource("Accent") as Brush ?? FatWorkspaceBrush("#2563EB"),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(11, 7, 11, 7),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "IO LIST FAT · LOADED",
                Foreground = Brushes.White,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        actions.Children.Insert(Math.Max(0, originalIndex), selectedMode);
        actions.Children.Insert(Math.Max(0, originalIndex + 1), engineeringButton);
        Closed += FatWorkspace_Closed;
        _fatWorkspaceModeInstalled = true;

        if (Owner is MainWindow owner)
            owner.RegisterLoadedIoFatWindow(this);
    }

    private void EngineeringWorkspaceMode_Click(object sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow owner)
        {
            owner.ShowEngineeringWorkspaceFromFat(this);
            return;
        }

        Storage?.ScheduleSave();
        Hide();
    }

    private void FatWorkspace_Closed(object? sender, EventArgs e)
    {
        Closed -= FatWorkspace_Closed;
        if (Owner is MainWindow owner)
            owner.RegisterLoadedIoFatWindow(this);
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in VisualDescendants<T>(child))
                yield return descendant;
        }
    }

    private static SolidColorBrush FatWorkspaceBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
