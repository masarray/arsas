// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Installs a compact Connect action beside the selected IED workflow controls without
/// changing the evidence-session contract. The button follows SelectedIed, so the
/// operator can start IED B while IED A is still preparing; each card continues to show
/// its own progress and live state.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool MultiIedConnectionUxRegistered = RegisterMultiIedConnectionUx();
    private Button? _selectedIedConnectButton;

    private static bool RegisterMultiIedConnectionUx()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MultiIedConnectionUx_Loaded));
        return true;
    }

    private static void MultiIedConnectionUx_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window)
            return;

        window.Dispatcher.BeginInvoke(
            new Action(window.InstallSelectedIedConnectButton),
            DispatcherPriority.Loaded);
    }

    private void InstallSelectedIedConnectButton()
    {
        if (_selectedIedConnectButton != null ||
            LogicalTreeHelper.GetParent(WorkspacePreviewToggle) is not Panel actionBar)
        {
            return;
        }

        var button = new Button
        {
            Style = FindResource("SoftButton") as Style,
            Padding = new Thickness(11, 8, 11, 8),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Connect or refresh the selected IED independently. Other IED connection workflows keep running."
        };
        button.SetBinding(
            FrameworkElement.DataContextProperty,
            new Binding(nameof(SelectedIed)) { Source = this });
        button.SetBinding(
            ContentControl.ContentProperty,
            new Binding(nameof(IoTestIedPlan.ConnectionActionText)));
        button.SetBinding(
            UIElement.IsEnabledProperty,
            new Binding(nameof(IoTestIedPlan.CanPrepareConnection)));
        button.Click += ConnectIed_Click;

        var previewIndex = actionBar.Children.IndexOf(WorkspacePreviewToggle);
        actionBar.Children.Insert(Math.Max(0, previewIndex + 1), button);
        _selectedIedConnectButton = button;
        Closed += MultiIedConnectionUx_Closed;
    }

    private void MultiIedConnectionUx_Closed(object? sender, EventArgs e)
    {
        Closed -= MultiIedConnectionUx_Closed;
        if (_selectedIedConnectButton == null)
            return;

        _selectedIedConnectButton.Click -= ConnectIed_Click;
        _selectedIedConnectButton = null;
    }
}
