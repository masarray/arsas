// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class SmvViewerWindow
{
    private GooseAdapterOption? _fastWorkflowPreferredAdapter;
    private string _fastWorkflowRouteDetail = string.Empty;
    private bool _fastWorkflowConfigured;
    private bool _fastWorkflowStarted;

    internal void EnableFastWorkflow(GooseAdapterOption? preferredAdapter, string? routeDetail)
    {
        _fastWorkflowPreferredAdapter = preferredAdapter;
        _fastWorkflowRouteDetail = routeDetail?.Trim() ?? string.Empty;
        _fastWorkflowConfigured = true;
        Loaded -= FastWorkflowWindow_Loaded;
        Loaded += FastWorkflowWindow_Loaded;

        if (IsLoaded)
            ScheduleFastWorkflowStart();
    }

    private void FastWorkflowWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= FastWorkflowWindow_Loaded;
        ScheduleFastWorkflowStart();
    }

    private void ScheduleFastWorkflowStart()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(StartConfiguredFastWorkflow));
    }

    private void StartConfiguredFastWorkflow()
    {
        if (!_fastWorkflowConfigured || _fastWorkflowStarted || !IsLoaded)
            return;

        if (Streams.Count == 0)
        {
            CaptureStatusText = "No configured or discovered SMV stream is available for automatic capture.";
            StatusText =
                "SMV workspace opened, but this IED has no Sampled Value stream definition. Load the correct SCL/live model before capturing.";
            return;
        }

        SelectedStream ??= Streams.FirstOrDefault();
        if (SelectedStream is null)
            return;

        if (_fastWorkflowPreferredAdapter is null)
        {
            SelectedAdapter = null;
            CaptureStatusText = "Automatic adapter selection was not unique. Select the process/station-bus adapter and press Capture snapshot.";
            StatusText = string.IsNullOrWhiteSpace(_fastWorkflowRouteDetail)
                ? "SMV workspace is ready for manual adapter selection."
                : $"SMV workspace is ready, but automatic capture was not started. {_fastWorkflowRouteDetail}";
            return;
        }

        var matchedAdapter = AdapterOptions.FirstOrDefault(adapter =>
            adapter.Name.Equals(_fastWorkflowPreferredAdapter.Name, StringComparison.OrdinalIgnoreCase) ||
            adapter.Selector.Equals(_fastWorkflowPreferredAdapter.Selector, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(adapter.MacAddress) &&
             adapter.MacAddress.Equals(_fastWorkflowPreferredAdapter.MacAddress, StringComparison.OrdinalIgnoreCase)));
        if (matchedAdapter is null)
        {
            SelectedAdapter = null;
            CaptureStatusText = "The routed Windows adapter was not found in the active Npcap catalog.";
            StatusText =
                $"SMV workspace opened for {DeviceName}, but Npcap could not map the routed adapter. Refresh Npcap or select the adapter manually.";
            return;
        }

        SelectedAdapter = matchedAdapter;
        StreamGrid.SelectedItem = SelectedStream;
        StreamGrid.ScrollIntoView(SelectedStream);

        if (!CaptureButton.IsEnabled)
        {
            CaptureStatusText = "SMV capture could not auto-start because the capture control is not ready.";
            return;
        }

        _fastWorkflowStarted = true;
        CaptureStatusText = $"Starting two-cycle capture for {SelectedStream.StreamId}…";
        StatusText =
            $"Fast SMV workflow selected {matchedAdapter.DisplayText} for {DeviceName}. Waiting for the first complete two-cycle waveform…";

        // Raising the real button event preserves all existing P0 stream-identity,
        // continuity and evidence-capture guards before the async capture begins.
        CaptureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, CaptureButton));
    }
}
