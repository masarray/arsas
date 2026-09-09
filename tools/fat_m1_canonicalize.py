from pathlib import Path


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly 1 match in {path}, found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"patched {label}")


xaml = Path("MainWindow.xaml")
cs = Path("MainWindow.xaml.cs")
native = Path("MainWindow.NativeFatWorkspace.cs")

# --- MainWindow.xaml: one canonical seven-destination navigation shell. ---
replace_once(
    xaml,
    '''                    <Grid.ColumnDefinitions>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                    </Grid.ColumnDefinitions>\n                    <Border x:Name="WorkflowPill" Grid.ColumnSpan="6" Width="148" Height="34"''',
    '''                    <Grid.ColumnDefinitions>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                    </Grid.ColumnDefinitions>\n                    <Border x:Name="WorkflowPill" Grid.ColumnSpan="7" Width="112" Height="34"''',
    "seven navigation columns and pill span",
)

replace_once(
    xaml,
    '''                    </Button>\n                </Grid>\n            </Border>\n\n            <!-- Runtime counts intentionally live only in the bottom status bar.''',
    '''                    </Button>\n                    <Button x:Name="NavNativeFatButton" Grid.Column="6" Content="FAT" Tag="6" Click="NavButton_Click" Style="{StaticResource SegmentedNavButton}"/>\n                </Grid>\n            </Border>\n\n            <!-- Runtime counts intentionally live only in the bottom status bar.''',
    "literal FAT navigation sibling",
)

replace_once(
    xaml,
    '''            </TabItem>\n                </TabControl>\n\n        <Border Grid.Row="2"''',
    '''            </TabItem>\n\n            <!-- FAT: permanent seventh Engineering workspace host. Production presentation is mounted here. -->\n            <TabItem x:Name="NativeFatTab" Header="FAT">\n                <Grid/>\n            </TabItem>\n        </TabControl>\n\n        <Border Grid.Row="2"''',
    "literal FAT tab sibling",
)

# --- MainWindow.xaml.cs: canonical navigation owns index 0..6 and geometry. ---
replace_once(
    cs,
    '''    private void NavButton_Click(object sender, RoutedEventArgs e)\n    {\n        if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var index))\n            return;\n        index = Math.Clamp(index, 0, 5);\n        MainTabs.SelectedIndex = index;\n        UpdateNavigationVisuals(index, animate: true);\n    }''',
    '''    private void NavButton_Click(object sender, RoutedEventArgs e)\n    {\n        if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var index))\n            return;\n        index = Math.Clamp(index, 0, NativeFatWorkspaceIndex);\n        MainTabs.SelectedIndex = index;\n        UpdateNavigationVisuals(index, animate: true);\n    }''',
    "canonical seven-index nav click",
)

replace_once(
    cs,
    '''    private void UpdateNavigationVisuals(int index, bool animate)\n    {\n        if (WorkflowPillTranslate == null)\n            return;\n\n        var target = Math.Clamp(index, 0, 5) * 150d;\n        if (animate)\n        {\n            var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(190))\n            {\n                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }\n            };\n            WorkflowPillTranslate.BeginAnimation(TranslateTransform.XProperty, animation);\n        }\n        else\n        {\n            WorkflowPillTranslate.BeginAnimation(TranslateTransform.XProperty, null);\n            WorkflowPillTranslate.X = target;\n        }\n\n        var buttons = new[] { NavExplorerButton, NavLiveButton, NavEventsButton, NavAlarmButton, NavGooseButton, NavDiagnosticsButton };\n        for (var i = 0; i < buttons.Length; i++)\n            buttons[i].Foreground = i == index ? Brushes.White : new SolidColorBrush(Color.FromRgb(71, 84, 103));\n    }''',
    '''    private void UpdateNavigationVisuals(int index, bool animate)\n    {\n        if (WorkflowPillTranslate == null)\n            return;\n\n        index = Math.Clamp(index, 0, NativeFatWorkspaceIndex);\n\n        // MainWindow is the single owner of all seven Engineering destinations.\n        // Keep the same density used by the proven workstation shell while allowing\n        // enough width for the Explorer label and the new canonical FAT sibling.\n        var availableWidth = ActualWidth > 0d ? ActualWidth : 1480d;\n        var shellWidth = availableWidth >= 1700d ? 1085d : availableWidth >= 1380d ? 995d : 805d;\n        WorkflowNavShell.Width = shellWidth;\n        WorkflowNavShell.MinWidth = shellWidth;\n\n        var contentWidth = Math.Max(0d, shellWidth - WorkflowNavShell.Padding.Left - WorkflowNavShell.Padding.Right);\n        var cellWidth = contentWidth / 7d;\n        WorkflowPill.Width = Math.Max(1d, cellWidth - 2d);\n        var target = index * cellWidth;\n\n        if (animate)\n        {\n            var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(190))\n            {\n                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }\n            };\n            WorkflowPillTranslate.BeginAnimation(TranslateTransform.XProperty, animation);\n        }\n        else\n        {\n            WorkflowPillTranslate.BeginAnimation(TranslateTransform.XProperty, null);\n            WorkflowPillTranslate.X = target;\n        }\n\n        var buttons = new[]\n        {\n            NavExplorerButton,\n            NavLiveButton,\n            NavEventsButton,\n            NavAlarmButton,\n            NavGooseButton,\n            NavDiagnosticsButton,\n            NavNativeFatButton\n        };\n        for (var i = 0; i < buttons.Length; i++)\n            buttons[i].Foreground = i == index ? Brushes.White : new SolidColorBrush(Color.FromRgb(71, 84, 103));\n    }''',
    "canonical seven-slot visual owner",
)

# --- Native FAT bootstrap: consume the XAML-owned FAT tab/button; do not create shell UI. ---
replace_once(
    native,
    '''        _nativeFatTab = new TabItem\n        {\n            Header = "FAT",\n            Content = BuildNativeFatWorkspaceContent()\n        };\n        MainTabs.Items.Add(_nativeFatTab);\n\n        // The command dock is deliberately shared, not cloned. FAT is command-centric,\n        // therefore start with the same expanded behavior as Explorer/Event Log.\n        _persistentWorkbench.DockExpandedByWorkspace[NativeFatWorkspaceIndex] = true;\n\n        InstallNativeFatNavigationButton();''',
    '''        // M1: MainWindow.xaml owns the seventh tab and navigation button. The native\n        // FAT implementation temporarily supplies only content/state until the production\n        // reusable FAT surface replaces it in M2; it no longer creates workstation shell UI.\n        _nativeFatTab = NativeFatTab;\n        _nativeFatNavButton = NavNativeFatButton;\n        _nativeFatTab.Content = BuildNativeFatWorkspaceContent();\n\n        // The command dock is deliberately shared, not cloned. FAT is command-centric,\n        // therefore start with the same expanded behavior as Explorer/Event Log.\n        _persistentWorkbench.DockExpandedByWorkspace[NativeFatWorkspaceIndex] = true;''',
    "bind Native FAT to canonical XAML shell",
)

start = native.read_text(encoding="utf-8")
method_start = start.find("    private void InstallNativeFatNavigationButton()")
method_end_marker = "    private void InstallNativeFatTimers()"
method_end = start.find(method_end_marker, method_start)
if method_start < 0 or method_end < 0:
    raise SystemExit("dynamic FAT navigation method markers not found exactly once")
start = start[:method_start] + start[method_end:]
native.write_text(start, encoding="utf-8")
print("removed dynamic FAT nav button creation")

# Compatibility queue stays because production partials call it, but it delegates to the\n# canonical MainWindow navigation owner instead of maintaining a second geometry algorithm.
text = native.read_text(encoding="utf-8")
queue_start = text.find("    private void QueueNativeFatNavigationGeometry()")
apply_marker = "    private void NativeFat_MainWindowPropertyChanged"
queue_end = text.find(apply_marker, queue_start)
if queue_start < 0 or queue_end < 0:
    raise SystemExit("Native FAT navigation geometry block markers not found")
replacement = '''    private void QueueNativeFatNavigationGeometry()\n    {\n        if (!_nativeFatInstalled)\n            return;\n\n        Dispatcher.BeginInvoke(\n            DispatcherPriority.ApplicationIdle,\n            new Action(() => UpdateNavigationVisuals(MainTabs.SelectedIndex, animate: false)));\n    }\n\n'''
text = text[:queue_start] + replacement + text[queue_end:]
native.write_text(text, encoding="utf-8")
print("delegated Native FAT nav queue to canonical MainWindow owner")

# Guard against accidental survival of the old shell creators.
native_text = native.read_text(encoding="utf-8")
for forbidden in ("MainTabs.Items.Add(_nativeFatTab)", "InstallNativeFatNavigationButton()", 'Name = "NavNativeFatButton"'):
    if forbidden in native_text:
        raise SystemExit(f"forbidden dynamic shell pattern survived: {forbidden}")

xaml_text = xaml.read_text(encoding="utf-8")
if xaml_text.count('x:Name="NavNativeFatButton"') != 1:
    raise SystemExit("canonical FAT nav button count is not exactly one")
if xaml_text.count('x:Name="NativeFatTab"') != 1:
    raise SystemExit("canonical FAT TabItem count is not exactly one")

print("FAT M1 canonical-shell migration complete")
