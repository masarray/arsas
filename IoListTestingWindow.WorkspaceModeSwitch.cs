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

        var originalEngineeringButton = EngineeringButton;
        if (originalEngineeringButton?.Parent is not WrapPanel actions)
            return;

        var originalIndex = actions.Children.IndexOf(originalEngineeringButton);
        var engineeringButton = new Button
        {
            Content = CreateToolbarLabel("Engineering Workspace", "LucideWrench", FatWorkspaceBrush("#24324A")),
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
            Background = FatWorkspaceBrush("#EDF4FF"),
            BorderBrush = FatWorkspaceBrush("#AFC6EE"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = CreateToolbarLabel(
                "IO LIST FAT · LOADED",
                "LucideCheckCircle",
                FatWorkspaceBrush("#285FB8"),
                10.5,
                FontWeights.Bold)
        };

        actions.Children.Insert(Math.Max(0, originalIndex), selectedMode);
        actions.Children.Insert(Math.Max(0, originalIndex + 1), engineeringButton);
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

    private StackPanel CreateToolbarLabel(
        string text,
        string iconResource,
        Brush foreground,
        double fontSize = 12.8,
        FontWeight? fontWeight = null)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = new System.Windows.Shapes.Path
        {
            Data = TryFindResource(iconResource) as Geometry,
            Stroke = foreground,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent
        };
        panel.Children.Add(new Viewbox
        {
            Width = 15,
            Height = 15,
            Margin = new Thickness(0, 0, 7, 0),
            Child = icon
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = fontSize,
            FontWeight = fontWeight ?? FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
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
