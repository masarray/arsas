// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;

namespace ArIED61850Tester;

public partial class MainWindow
{
    /// <summary>
    /// Makes every unqualified Show() call in the MainWindow partial class lifecycle-safe.
    ///
    /// The IO FAT workspace is an owned window. When the application exits, WPF closes
    /// the owner and its owned FAT window as one native-window chain. The FAT Closed
    /// callback must not try to make the owner visible again while that owner is already
    /// inside Window.VerifyNotClosing(). During normal Engineering ↔ FAT switching this
    /// method delegates directly to Window.Show(), preserving the instant hide/show flow.
    /// </summary>
    public new void Show()
    {
        if (IsApplicationWindowShutdownInProgress())
            return;

        try
        {
            base.Show();
        }
        catch (InvalidOperationException) when (IsApplicationWindowShutdownInProgress())
        {
            // Shutdown can begin between the guard above and Window.Show(). This is a
            // benign owned-window teardown race, not an application error.
        }
    }

    private bool IsApplicationWindowShutdownInProgress()
    {
        if (_shutdownStarted || _allowClose || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return true;

        var applicationDispatcher = Application.Current?.Dispatcher;
        return applicationDispatcher is not null &&
               (applicationDispatcher.HasShutdownStarted || applicationDispatcher.HasShutdownFinished);
    }
}
