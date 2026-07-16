from pathlib import Path
import re


def apply(path: str, pattern: str, replacement: str, label: str, flags: int = re.S) -> None:
    file_path = Path(path)
    text = file_path.read_text(encoding="utf-8")
    updated, count = re.subn(pattern, replacement, text, count=1, flags=flags)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    file_path.write_text(updated, encoding="utf-8")
    print(f"applied: {label}")


# Append timeline rows at the tail so WPF does not shift every realized row.
apply(
    "MainWindow.GooseTimeline.cs",
    r"GooseEvents\.Insert\(0, eventRow\);\s*while \(GooseEvents\.Count > MaxGooseTimelineEvents\)\s*GooseEvents\.RemoveAt\(GooseEvents\.Count - 1\);",
    """// Append in capture order. Tail insertion avoids shifting every realized row.\n            GooseEvents.Add(eventRow);\n            while (GooseEvents.Count > MaxGooseTimelineEvents)\n                GooseEvents.RemoveAt(0);""",
    "append-only timeline")

apply(
    "MainWindow.GooseTimeline.cs",
    r"var changed = stream\.Leaves.*?return string\.Join\(\" • \", changed\) \+ suffix;\s*}",
    """var changed = stream.Leaves
            .Where(leaf => leaf.IsChanged)
            .Take(2)
            .Select(leaf =>
            {
                var current = ShortenGooseText(GooseEngineeringValueFormatter.Format(leaf.Value), 30);
                return IsGenericGooseLeafName(leaf.SignalName)
                    ? current
                    : $\"{ShortenGooseText(leaf.SignalName, 22)}: {current}\";
            })
            .ToArray();
        if (changed.Length > 0)
        {
            var suffix = stream.ChangedValueCount > changed.Length
                ? $\" • +{stream.ChangedValueCount - changed.Length:N0}\"
                : string.Empty;
            return string.Join(\" • \", changed) + suffix;
        }""",
    "concise current values")

apply(
    "MainWindow.GooseTimeline.cs",
    r"\n    private static string FriendlySequenceStatus\(string value\)",
    """
    private static bool IsGenericGooseLeafName(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(text) ||
               text.StartsWith(\"Leaf \", StringComparison.OrdinalIgnoreCase);
    }

    private static string FriendlySequenceStatus(string value)""",
    "generic leaf-name detection")

# Cache the latest frame per stream and re-apply names as soon as SCL/live discovery appears.
apply(
    "MainWindow.GooseSubscriber.cs",
    r"(private readonly ConcurrentDictionary<string, GooseSubscriberFrameSnapshot> _pendingGooseFrames = new\(StringComparer\.OrdinalIgnoreCase\);)",
    r"\1\n    private readonly ConcurrentDictionary<string, GooseSubscriberFrameSnapshot> _latestGooseFrames = new(StringComparer.OrdinalIgnoreCase);",
    "latest frame cache")

apply(
    "MainWindow.GooseSubscriber.cs",
    r"(private bool _gooseWorkspaceActivationScheduled;)",
    r"\1\n    private bool _gooseBindingRefreshScheduled;",
    "binding refresh state")

apply(
    "MainWindow.GooseSubscriber.cs",
    r"_pendingGooseFrames\.Clear\(\);\s*_gooseStreamIndex\.Clear\(\);",
    """_pendingGooseFrames.Clear();
        _latestGooseFrames.Clear();
        _gooseStreamIndex.Clear();""",
    "clear latest frame cache")

apply(
    "MainWindow.GooseSubscriber.cs",
    r"private void RefreshGooseBindingPreview\(\)\s*\{\s*if \(IsGooseCapturing\)\s*return;\s*\n\s*try\s*\{\s*_gooseBindingCatalog = BuildGooseBindingCatalog\(\);\s*GooseBindingText = _gooseBindingCatalog\.Summary;",
    """private void RefreshGooseBindingPreview()
    {
        try
        {
            _gooseBindingCatalog = BuildGooseBindingCatalog();
            GooseBindingText = _gooseBindingCatalog.Summary;
            RebindGooseRowsFromLatestFrames();""",
    "refresh names during active capture")

apply(
    "MainWindow.GooseSubscriber.cs",
    r"\n    private void ApplyGooseWorkspaceFallback\(string context, Exception exception\)",
    """
    private void ScheduleGooseBindingRefreshFromWorkspace()
    {
        if ((!_goosePresentationInstalled && !IsGooseCapturing) ||
            _gooseBindingRefreshScheduled || Dispatcher.HasShutdownStarted)
            return;

        _gooseBindingRefreshScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _gooseBindingRefreshScheduled = false;
            RefreshGooseBindingPreview();
        }));
    }

    private void RebindGooseRowsFromLatestFrames()
    {
        foreach (var captured in _latestGooseFrames.Values.Take(256))
        {
            if (_gooseStreamIndex.TryGetValue(captured.StreamKey, out var row))
                row.Apply(BuildGooseStreamSnapshot(captured, _gooseBindingCatalog));
        }

        Raise(nameof(GooseSelectedStreamText));
        Raise(nameof(GooseNoLeafValuesVisibility));
        Raise(nameof(GooseSelectedLeafCountText));
    }

    private void ApplyGooseWorkspaceFallback(string context, Exception exception)""",
    "automatic model rebinding")

apply(
    "MainWindow.GooseSubscriber.cs",
    r"private void GooseSubscriberRuntime_FrameReceived\(GooseSubscriberFrameSnapshot snapshot\)\s*=> _pendingGooseFrames\[snapshot\.StreamKey\] = snapshot;",
    """private void GooseSubscriberRuntime_FrameReceived(GooseSubscriberFrameSnapshot snapshot)
    {
        _latestGooseFrames[snapshot.StreamKey] = snapshot;
        _pendingGooseFrames[snapshot.StreamKey] = snapshot;
    }""",
    "store latest stream frame")

# Notify the GOOSE binding layer after both SCL import and live discovery.
apply(
    "MainWindow.xaml.cs",
    r"device\.Signals\.AddRange\(signals\);\s*device\.RecountSelectedSignals\(\);\s*device\.RefreshComputed\(\);\s*}\s*\n\s*private static string BuildSclWorkspaceSummary",
    """device.Signals.AddRange(signals);
        device.RecountSelectedSignals();
        device.RefreshComputed();
        ScheduleGooseBindingRefreshFromWorkspace();
    }

    private static string BuildSclWorkspaceSummary""",
    "SCL-to-GOOSE refresh")

apply(
    "MainWindow.xaml.cs",
    r"device\.Signals\.AddRange\(signals\);\s*device\.HasDiscoveryCache = signals\.Count > 0;\s*ApplySclLiveComparison\(device, signals\);",
    """device.Signals.AddRange(signals);
            device.HasDiscoveryCache = signals.Count > 0;
            ApplySclLiveComparison(device, signals);
            ScheduleGooseBindingRefreshFromWorkspace();""",
    "live-discovery-to-GOOSE refresh")

apply(
    "MainWindow.xaml.cs",
    r"WaitForDiscoveryProgressAnimationAsync\(device, TimeSpan\.FromMilliseconds\(1800\)\)",
    "WaitForDiscoveryProgressAnimationAsync(device, TimeSpan.FromMilliseconds(650))",
    "lighter discovery completion")

# Signal Selection remains owned and above the main window, but no longer disables it.
apply(
    "SignalSelectionWizardWindow.xaml.cs",
    r"public event PropertyChangedEventHandler\? PropertyChanged;\s*\n\s*public ICollectionView SignalsView",
    """public event PropertyChangedEventHandler? PropertyChanged;

    public bool Accepted => _accepted;
    public ICollectionView SignalsView""",
    "expose modeless result")

apply(
    "SignalSelectionWizardWindow.xaml.cs",
    r"private void Save_Click\(object sender, RoutedEventArgs e\)\s*\{\s*_accepted = true;\s*DialogResult = true;\s*}\s*\n\s*private void Cancel_Click\(object sender, RoutedEventArgs e\)\s*\{\s*RestoreOriginalSelection\(\);\s*DialogResult = false;\s*}",
    """private void Save_Click(object sender, RoutedEventArgs e)
    {
        _accepted = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => Close();""",
    "modeless save and cancel")

apply(
    "MainWindow.xaml.cs",
    r"var wizard = new SignalSelectionWizardWindow\(\s*device,\s*restoredSelectionCount < 0 \? device\.SelectedSignalCount : restoredSelectionCount\)\s*\{\s*Owner = this\s*};\s*\n\s*_signalSelectionWizardOpen = true;\s*try\s*\{\s*if \(wizard\.ShowDialog\(\) != true\)",
    """var wizard = new SignalSelectionWizardWindow(
            device,
            restoredSelectionCount < 0 ? device.SelectedSignalCount : restoredSelectionCount)
        {
            Owner = this,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        _signalSelectionWizardOpen = true;
        try
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void WizardClosed(object? sender, EventArgs args)
            {
                wizard.Closed -= WizardClosed;
                completion.TrySetResult(wizard.Accepted);
            }

            wizard.Closed += WizardClosed;
            wizard.Show();
            var accepted = await completion.Task;
            if (!accepted)""",
    "owned non-blocking signal selection")

# Remove command-count noise.
apply(
    "MainWindow.xaml",
    r"(<TextBlock Text=\"IED Command Panel\"[^>]*/>)\s*<Border Background=\"#EAF2FF\".*?</Border>",
    r"\1",
    "remove command count badge")

# Prevent the default ToggleButton hover chrome from covering the ComboBox selection.
apply(
    "App.xaml",
    r"<ToggleButton Background=\"Transparent\" BorderThickness=\"0\" Focusable=\"False\" Cursor=\"Hand\"\s*IsChecked=\"\{Binding IsDropDownOpen, RelativeSource=\{RelativeSource TemplatedParent\}, Mode=TwoWay\}\"/>",
    """<ToggleButton Background=\"Transparent\" BorderThickness=\"0\" Focusable=\"False\" Cursor=\"Hand\"
                                          IsChecked=\"{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}\">
                                <ToggleButton.Template>
                                    <ControlTemplate TargetType=\"ToggleButton\">
                                        <Border Background=\"Transparent\"/>
                                    </ControlTemplate>
                                </ToggleButton.Template>
                            </ToggleButton>""",
    "transparent ComboBox hit surface")

apply(
    "Views/GooseSubscriberLiteView.xaml",
    r"<ComboBox Grid\.Column=\"2\" ItemsSource=\"\{Binding GooseAdapters\}\".*?</ComboBox>",
    """<ComboBox Grid.Column=\"2\" ItemsSource=\"{Binding GooseAdapters}\"
                              SelectedItem=\"{Binding SelectedGooseAdapter, Mode=TwoWay}\"
                              DisplayMemberPath=\"DisplayText\" TextSearch.TextPath=\"DisplayText\"
                              Style=\"{StaticResource ModernComboBox}\" Height=\"34\" Padding=\"10,4\"
                              IsHitTestVisible=\"{Binding CanRefreshGooseConfiguration}\"
                              Focusable=\"{Binding CanRefreshGooseConfiguration}\"
                              ToolTip=\"{Binding SelectedGooseAdapterDetail}\"/>""",
    "adapter selection rendering")

apply(
    "Views/GooseSubscriberLiteView.xaml",
    r"Header=\"Changed values\"",
    "Header=\"Values\"",
    "concise Values header",
    flags=0)

print("GOOSE/discovery UX correction completed.")
