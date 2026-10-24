using System;
using System.Windows;

namespace ArIED61850Tester;

/// <summary>
/// First-paint-only bounds for the interactive workstation windows.
/// Never intercept user move, resize, maximize or restore.
/// Hidden IO FAT donor windows use Manual placement and remain untouched.
/// </summary>
internal static class WorkstationWindowStartupBounds
{
    internal static void Apply(Window window)
    {
        if (window is not MainWindow && window is not IoListTestingWindow)
            return;

        // The embedded FAT donor is intentionally hidden offscreen.
        if (window.WindowStartupLocation == WindowStartupLocation.Manual)
            return;

        var work = SystemParameters.WorkArea; // WPF device-independent units.
        const double inset = 24d;
        var maxWidth = Math.Max(window.MinWidth, work.Width - 2d * inset);
        var maxHeight = Math.Max(window.MinHeight, work.Height - 2d * inset);

        window.Width = Math.Clamp(window.Width, window.MinWidth, maxWidth);
        window.Height = Math.Clamp(window.Height, window.MinHeight, maxHeight);
        window.Left = work.Left + Math.Max(0d, (work.Width - window.Width) / 2d);
        window.Top = work.Top + Math.Max(0d, (work.Height - window.Height) / 2d);
    }
}
