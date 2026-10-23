using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// P1 FAT UI notification gate.
///
/// The multi-session coordinator exposes fine-grained property changes, but the legacy FAT
/// window used to translate every one of those changes into a full window/property refresh.
/// During Start/Continue, automatic Value 1/Value 2 capture and Stop that multiplied a small
/// evidence update into thousands of WPF binding/layout invalidations. Coalesce those legacy
/// wrapper notifications to at most one refresh per Dispatcher turn. Direct bindings to
/// Session.* continue to receive their normal targeted PropertyChanged events.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool P1NotificationThrottleRegistered = RegisterP1NotificationThrottle();

    private bool _p1NotificationThrottleInstalled;
    private int _p1WindowRefreshScheduled;

    private static bool RegisterP1NotificationThrottle()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P1NotificationThrottle_Loaded));
        return true;
    }

    private static void P1NotificationThrottle_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window ||
            window._p1NotificationThrottleInstalled ||
            !ReferenceEquals(e.OriginalSource, window))
        {
            return;
        }

        window._p1NotificationThrottleInstalled = true;

        // The constructor attached the compatibility handler before InitializeComponent.
        // Replace only that fan-out edge. Session itself remains fully observable, so nested
        // bindings such as Session.CanStop / Session.StateText are not delayed or hidden.
        window.Session.PropertyChanged -= window.Session_PropertyChanged;
        window.Session.PropertyChanged += window.P1Session_PropertyChanged;
        window.Closed += window.P1NotificationThrottle_Closed;
    }

    private void P1Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Interlocked.Exchange(ref _p1WindowRefreshScheduled, 1) != 0)
            return;

        try
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    Interlocked.Exchange(ref _p1WindowRefreshScheduled, 0);
                    if (!IsLoaded)
                        return;

                    // One coherent recompute is enough for the window-owned derived labels.
                    // Keep it below input/render priority so command buttons and scrolling
                    // never wait behind evidence metadata churn.
                    RaiseStatusProperties();
                    RaiseSelectedIedContextProperties();
                }),
                DispatcherPriority.Background);
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _p1WindowRefreshScheduled, 0);
        }
    }

    private void P1NotificationThrottle_Closed(object? sender, EventArgs e)
    {
        Closed -= P1NotificationThrottle_Closed;
        Session.PropertyChanged -= P1Session_PropertyChanged;
        Interlocked.Exchange(ref _p1WindowRefreshScheduled, 0);
    }
}
