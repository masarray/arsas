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
        // Use almost all available desktop space on first launch, without
        // maximizing. The old 1440×840 cap truncated timestamps and the
        // Command Dock on 1600–1920px engineering workstations.
        const double inset = 8d;
        var maxWidth = Math.Max(0d, work.Width - 2d * inset);
        var maxHeight = Math.Max(0d, work.Height - 2d * inset);
        if (maxWidth <= 0d || maxHeight <= 0d)
            return;

        // MinWidth/MinHeight are allowed only when the actual monitor can
        // support them. Preserve native dragging and future user resizing.
        window.Width = Math.Min(maxWidth, Math.Max(window.Width, Math.Min(window.MinWidth, maxWidth)));
        window.Height = Math.Min(maxHeight, Math.Max(window.Height, Math.Min(window.MinHeight, maxHeight)));
        window.Left = work.Left + Math.Max(0d, (work.Width - window.Width) / 2d);
        window.Top = work.Top + Math.Max(0d, (work.Height - window.Height) / 2d);
    }
}
