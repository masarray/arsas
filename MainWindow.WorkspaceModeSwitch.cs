// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Threading;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private IoListTestingWindow? _loadedIoFatWindow;

    internal void RegisterLoadedIoFatWindow(IoListTestingWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (ReferenceEquals(_loadedIoFatWindow, window))
            return;

        if (_loadedIoFatWindow != null)
            _loadedIoFatWindow.Closed -= LoadedIoFatWindow_Closed;

        _loadedIoFatWindow = window;
        _loadedIoFatWindow.Closed += LoadedIoFatWindow_Closed;
    }

    internal void ShowEngineeringWorkspaceFromFat(IoListTestingWindow window)
    {
        if (!ReferenceEquals(_loadedIoFatWindow, window))
            RegisterLoadedIoFatWindow(window);

        window.Storage?.ScheduleSave();
        window.Hide();
        IsEnabled = true;
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        SetStatus($"Engineering workspace active · IO List FAT project '{window.Project.ProjectName}' remains loaded.");
    }

    private void QueueIoFatWorkspaceReplacement(Action openReplacement)
    {
        ArgumentNullException.ThrowIfNull(openReplacement);
        var loaded = _loadedIoFatWindow;
        if (loaded == null || !loaded.IsLoaded)
        {
            Dispatcher.BeginInvoke(openReplacement, DispatcherPriority.ContextIdle);
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"IO List FAT project '{loaded.Project.ProjectName}' is still loaded.\n\nSave and close that workspace before loading another project?",
            "Load another IO List FAT project",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        loaded.Close();
        if (ReferenceEquals(_loadedIoFatWindow, loaded))
            return;

        Dispatcher.BeginInvoke(openReplacement, DispatcherPriority.ContextIdle);
    }

    private void LoadedIoFatWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is IoListTestingWindow window)
            window.Closed -= LoadedIoFatWindow_Closed;
        if (ReferenceEquals(_loadedIoFatWindow, sender))
            _loadedIoFatWindow = null;
        IsEnabled = true;
    }
}
