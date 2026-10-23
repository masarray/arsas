using System.Runtime.CompilerServices;
using System.Windows;

namespace ArIED61850Tester;

/// <summary>
/// Relay-bench hardening for the shared Engineering/FAT command panel. The original P0
/// defaults are correct, but their registration lived behind an otherwise unreferenced
/// static field. ModuleInitializer guarantees the Loaded hook exists before any command row
/// can be realized; AttachP0CommandDefaults remains the single lifecycle/ownership authority.
/// </summary>
public partial class MainWindow
{
    [ModuleInitializer]
    internal static void RegisterP1CommandSafetyDefaultsAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P1CommandSafetyDefaults_Loaded),
            handledEventsToo: true);
    }

    private static void P1CommandSafetyDefaults_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.AttachP0CommandDefaults();
    }
}
