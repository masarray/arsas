using System.Windows;
using System.Windows.Controls;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private static readonly bool P0FatPresentationRegistered = RegisterP0FatPresentation();
    private bool _p0FatPresentationInstalled;

    private static bool RegisterP0FatPresentation()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0FatPresentation_Loaded));
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0FatCaptureButton_Loaded));
        return true;
    }

    private static void P0FatPresentation_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window || window._p0FatPresentationInstalled)
            return;

        window._p0FatPresentationInstalled = true;

        // The bind-time image may come directly from ARIEC as lowercase Boolean text.
        // Canonicalize it before the operator starts interacting with FAT. Subsequent live
        // snapshots pass through the same formatter in MainWindow.IoFatRuntimeAuthority.
        foreach (var point in window.Project.Ieds.SelectMany(ied => ied.TestPoints))
            point.Runtime.CurrentValue = IoFatValuePresentation.Canonicalize(point.Runtime.CurrentValue);
    }

    private static void P0FatCaptureButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !Equals(button.Content, "✓ Capture") ||
            Window.GetWindow(button) is not IoListTestingWindow)
        {
            return;
        }

        // Analog Value 1 / Value 2 is automatic in the normal FAT workflow. Keep the
        // explicit Recapture context-menu path as an audit/operator override, but never
        // expose a per-cell manual Capture button.
        button.Visibility = Visibility.Collapsed;
        button.IsTabStop = false;
        button.IsHitTestVisible = false;
    }
}
