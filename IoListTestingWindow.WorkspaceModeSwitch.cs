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
        var engineeringButton = VisualDescendants<Button>(this)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Engineering", StringComparison.Ordinal));
        if (engineeringButton?.Parent is not WrapPanel actions ||
            actions.Children.OfType<FrameworkElement>().Any(child => Equals(child.Tag, FatWorkspaceModeTag)))
            return;

        engineeringButton.Content = "Engineering Workspace";
        engineeringButton.ToolTip = "Save this FAT workspace and return to the main Engineering workspace";
        engineeringButton.Margin = new Thickness(0);

        var selectedMode = new Border
        {
            Tag = FatWorkspaceModeTag,
            Background = TryFindResource("Accent") as Brush ?? FatWorkspaceBrush("#2563EB"),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(11, 7),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "IO LIST FAT",
                Foreground = Brushes.White,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var index = actions.Children.IndexOf(engineeringButton);
        actions.Children.Insert(Math.Max(0, index), selectedMode);
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
