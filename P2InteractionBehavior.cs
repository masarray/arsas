using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Smooth scrolling only.
///
/// This behavior deliberately does not install keyboard shortcuts, search UI,
/// collection filters, result navigation, or runtime virtualization mutations.
/// Pixel scrolling is configured on application styles before MainWindow is
/// materialized; after layout starts, the only behavior here is wheel easing.
/// </summary>
internal static class P2InteractionBehavior
{
    private sealed class SmoothScrollState
    {
        public required ScrollViewer Viewer { get; init; }
        public required DispatcherTimer Timer { get; init; }
        public double TargetOffset { get; set; }
    }

    private static readonly ConditionalWeakTable<ScrollViewer, SmoothScrollState> SmoothScrollers = new();
    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        // Install physical pixel scrolling before StartupUri creates MainWindow.
        // Never mutate VirtualizationMode/IsVirtualizing after an ItemsHostPanel
        // has entered Measure; WPF explicitly rejects that and can break the UI.
        ConfigurePixelScrollStylesBeforeWindowCreation();

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Mouse.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(MainWindow_PreviewMouseWheel),
            true);
    }

    private static void ConfigurePixelScrollStylesBeforeWindowCreation()
    {
        var resources = Application.Current?.Resources;
        if (resources == null)
            return;

        // Existing keyed engineering styles (ModernDataGrid, CommandDataGrid, etc.)
        // are still unsealed here because MainWindow has not been created yet.
        foreach (DictionaryEntry entry in resources)
        {
            if (entry.Value is not Style style ||
                style.IsSealed ||
                style.TargetType == null ||
                !typeof(ItemsControl).IsAssignableFrom(style.TargetType))
            {
                continue;
            }

            AddPhysicalScrollSetters(style);
        }

        // The Explorer/Annunciator rails use plain ListBox/ListView/TreeView controls.
        // Supply only scrolling properties; no templates, colors, sizes or input bindings.
        EnsureImplicitPhysicalScrollStyle(resources, typeof(ListBox));
        EnsureImplicitPhysicalScrollStyle(resources, typeof(ListView));
        EnsureImplicitPhysicalScrollStyle(resources, typeof(TreeView));
    }

    private static void EnsureImplicitPhysicalScrollStyle(ResourceDictionary resources, Type targetType)
    {
        if (resources.Contains(targetType))
        {
            if (resources[targetType] is Style existing && !existing.IsSealed)
                AddPhysicalScrollSetters(existing);
            return;
        }

        var style = new Style(targetType);
        AddPhysicalScrollSetters(style);
        resources[targetType] = style;
    }

    private static void AddPhysicalScrollSetters(Style style)
    {
        AddSetterIfMissing(style, VirtualizingPanel.ScrollUnitProperty, ScrollUnit.Pixel);
        AddSetterIfMissing(style, ScrollViewer.CanContentScrollProperty, true);
        AddSetterIfMissing(style, ScrollViewer.IsDeferredScrollingEnabledProperty, false);
    }

    private static void AddSetterIfMissing(Style style, DependencyProperty property, object value)
    {
        foreach (var setterBase in style.Setters)
        {
            if (setterBase is Setter setter && setter.Property == property)
                return;
        }

        style.Setters.Add(new Setter(property, value));
    }

    private static void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not MainWindow || e.OriginalSource is not DependencyObject source)
            return;

        // Preserve normal Windows gestures and controls that own their wheel input.
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None)
            return;
        if (FindAncestor<ScrollBar>(source) != null)
            return;
        if (FindAncestor<ComboBox>(source) is { IsDropDownOpen: true })
            return;
        if (FindAncestor<TextBox>(source) is { AcceptsReturn: true })
            return;

        var viewer = FindScrollableViewer(source, e.Delta);
        if (viewer == null || viewer.ScrollableHeight <= 0.5d)
            return;

        // Intercept only physical/pixel scrolling. Any control that intentionally
        // remains in logical item mode keeps its native WPF wheel behavior.
        var items = FindAncestor<ItemsControl>(source);
        if (items != null && VirtualizingPanel.GetScrollUnit(items) != ScrollUnit.Pixel)
            return;
        if (items == null && viewer.CanContentScroll)
            return;

        var state = SmoothScrollers.GetValue(viewer, CreateSmoothScrollState);
        if (!state.Timer.IsEnabled ||
            Math.Abs(state.TargetOffset - viewer.VerticalOffset) > Math.Max(240d, viewer.ViewportHeight * 1.5d))
        {
            state.TargetOffset = viewer.VerticalOffset;
        }

        var wheelLines = SystemParameters.WheelScrollLines;
        var distancePerNotch = wheelLines < 0
            ? Math.Max(96d, viewer.ViewportHeight * 0.82d)
            : Math.Clamp(wheelLines, 1, 6) * 30d;

        // Keep high-resolution mouse/touchpad deltas proportional instead of
        // quantizing every input to a full 120-delta wheel notch.
        var deltaPixels = -(e.Delta / 120d) * distancePerNotch;
        var nextTarget = Math.Clamp(state.TargetOffset + deltaPixels, 0d, viewer.ScrollableHeight);
        if (Math.Abs(nextTarget - state.TargetOffset) < 0.1d)
            return;

        state.TargetOffset = nextTarget;
        if (!state.Timer.IsEnabled)
            state.Timer.Start();

        e.Handled = true;
    }

    private static SmoothScrollState CreateSmoothScrollState(ScrollViewer viewer)
    {
        SmoothScrollState? state = null;
        var timer = new DispatcherTimer(DispatcherPriority.Render, viewer.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        state = new SmoothScrollState
        {
            Viewer = viewer,
            Timer = timer,
            TargetOffset = viewer.VerticalOffset
        };

        timer.Tick += (_, _) =>
        {
            if (!viewer.IsVisible || viewer.ScrollableHeight <= 0.5d)
            {
                timer.Stop();
                state.TargetOffset = viewer.VerticalOffset;
                return;
            }

            state.TargetOffset = Math.Clamp(state.TargetOffset, 0d, viewer.ScrollableHeight);
            var remaining = state.TargetOffset - viewer.VerticalOffset;
            if (Math.Abs(remaining) <= 0.45d)
            {
                viewer.ScrollToVerticalOffset(state.TargetOffset);
                timer.Stop();
                return;
            }

            // Fast response with a calm landing. This does not alter item selection,
            // filtering, focus, keyboard input, IEC 61850 data, or collection order.
            var step = remaining * 0.24d;
            if (Math.Abs(step) < 0.7d)
                step = Math.CopySign(0.7d, remaining);
            if (Math.Abs(step) > Math.Abs(remaining))
                step = remaining;

            viewer.ScrollToVerticalOffset(
                Math.Clamp(viewer.VerticalOffset + step, 0d, viewer.ScrollableHeight));
        };

        viewer.Unloaded += (_, _) => timer.Stop();
        return state;
    }

    private static ScrollViewer? FindScrollableViewer(DependencyObject source, int wheelDelta)
    {
        for (DependencyObject? current = source; current != null; current = GetParent(current))
        {
            if (current is not ScrollViewer viewer || viewer.ScrollableHeight <= 0.5d)
                continue;

            var canMove = wheelDelta < 0
                ? viewer.VerticalOffset < viewer.ScrollableHeight - 0.5d
                : viewer.VerticalOffset > 0.5d;
            if (canMove)
                return viewer;
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;
            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is ContentElement content)
            return ContentOperations.GetParent(content) ?? LogicalTreeHelper.GetParent(content);

        try
        {
            return VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }
        catch (InvalidOperationException)
        {
            return LogicalTreeHelper.GetParent(current);
        }
    }
}
