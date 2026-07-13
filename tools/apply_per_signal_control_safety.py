from __future__ import annotations

from pathlib import Path
import re
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]


def read_source(relative_path: str) -> tuple[Path, str, str]:
    path = ROOT / relative_path
    raw = path.read_bytes()
    newline = "\r\n" if b"\r\n" in raw else "\n"
    text = raw.decode("utf-8").replace("\r\n", "\n")
    return path, text, newline


def write_source(path: Path, text: str, newline: str) -> None:
    normalized = text.replace("\r\n", "\n")
    path.write_bytes(normalized.replace("\n", newline).encode("utf-8"))


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


def patch_signal_definition() -> None:
    path, text, newline = read_source("Models/SignalDefinition.cs")

    text = replace_once(
        text,
        """    private string _controlLastResult = string.Empty;
    private bool _controlIsBusy;
""",
        """    private string _controlLastResult = string.Empty;
    private bool _controlIsBusy;
    private bool _controlInterlockCheck = true;
    private bool _controlSynchroCheck;
    private bool _controlTestMode;
    private bool _controlConfirmationPending;
    private string _controlPendingValue = string.Empty;
    private string _controlPendingAction = string.Empty;
""",
        "SignalDefinition safety fields",
    )

    text = replace_once(
        text,
        """            if (!Set(ref _controlIsBusy, value))
                return;

            if (value)
""",
        """            if (!Set(ref _controlIsBusy, value))
                return;

            Raise(nameof(ControlCanConfirm));

            if (value)
""",
        "SignalDefinition busy state",
    )

    text = replace_once(
        text,
        """    private bool CanExposeControlActions => !_controlModelResolved || ControlSupportsOperate;
""",
        """    public bool ControlInterlockCheck
    {
        get => _controlInterlockCheck;
        set => Set(ref _controlInterlockCheck, value);
    }

    public bool ControlSynchroCheck
    {
        get => _controlSynchroCheck;
        set => Set(ref _controlSynchroCheck, value);
    }

    public bool ControlTestMode
    {
        get => _controlTestMode;
        set => Set(ref _controlTestMode, value);
    }

    public bool ControlConfirmationPending => _controlConfirmationPending;
    public string ControlPendingValue => _controlPendingValue;
    public string ControlPendingAction => _controlPendingAction;
    public string ControlPendingConfirmationLabel =>
        string.IsNullOrWhiteSpace(_controlPendingAction) ? "Confirm command" : $"Confirm {_controlPendingAction}";
    public bool ControlCanConfirm => _controlConfirmationPending && ControlSupportsOperate && !ControlIsBusy;

    public void StageControlConfirmation(string requestedValue, string actionLabel)
    {
        var normalizedValue = requestedValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedValue))
            return;

        Set(ref _controlPendingValue, normalizedValue, nameof(ControlPendingValue));
        if (Set(ref _controlPendingAction, actionLabel?.Trim() ?? string.Empty, nameof(ControlPendingAction)))
            Raise(nameof(ControlPendingConfirmationLabel));
        if (Set(ref _controlConfirmationPending, true, nameof(ControlConfirmationPending)))
            Raise(nameof(ControlCanConfirm));
    }

    public void ClearControlConfirmation()
    {
        Set(ref _controlPendingValue, string.Empty, nameof(ControlPendingValue));
        if (Set(ref _controlPendingAction, string.Empty, nameof(ControlPendingAction)))
            Raise(nameof(ControlPendingConfirmationLabel));
        if (Set(ref _controlConfirmationPending, false, nameof(ControlConfirmationPending)))
            Raise(nameof(ControlCanConfirm));
    }

    private bool CanExposeControlActions => !_controlModelResolved || ControlSupportsOperate;
""",
        "SignalDefinition per-signal safety properties",
    )

    text = replace_once(
        text,
        """        Raise(nameof(ControlSupportsOperate));
        Raise(nameof(IsReadOnlyControl));
""",
        """        Raise(nameof(ControlSupportsOperate));
        Raise(nameof(ControlCanConfirm));
        Raise(nameof(IsReadOnlyControl));
""",
        "SignalDefinition confirmation readiness refresh",
    )

    write_source(path, text, newline)


def patch_main_window_code() -> None:
    path, text, newline = read_source("MainWindow.xaml.cs")

    text = replace_once(
        text,
        """    private bool _connectAllInProgress;
    private bool _liveControlArmed;
    private bool _commandInterlockCheck = true;
    private bool _commandSynchroCheck;
    private bool _commandTestMode;
""",
        """    private bool _connectAllInProgress;
""",
        "MainWindow global command state fields",
    )

    text = replace_once(
        text,
        """    public string LastStatusText { get => _lastStatusText; set => Set(ref _lastStatusText, value); }
    public bool LiveControlArmed { get => _liveControlArmed; set => Set(ref _liveControlArmed, value); }
    public bool CommandInterlockCheck { get => _commandInterlockCheck; set => Set(ref _commandInterlockCheck, value); }
    public bool CommandSynchroCheck { get => _commandSynchroCheck; set => Set(ref _commandSynchroCheck, value); }
    public bool CommandTestMode { get => _commandTestMode; set => Set(ref _commandTestMode, value); }

""",
        """    public string LastStatusText { get => _lastStatusText; set => Set(ref _lastStatusText, value); }

""",
        "MainWindow global command state properties",
    )

    text = replace_once(
        text,
        """            if (_selectedDevice != null) _selectedDevice.IsActive = false;
            LiveControlArmed = false;
            _selectedDevice = value;
""",
        """            if (_selectedDevice != null)
            {
                _selectedDevice.IsActive = false;
                ClearPendingControlConfirmations(_selectedDevice);
            }
            _selectedDevice = value;
""",
        "MainWindow selected IED arm reset",
    )

    text = replace_once(
        text,
        """            if (_selectedDevice != null)
            {
                _selectedDevice.IsActive = true;
                NewDeviceIp = _selectedDevice.IpAddress;
""",
        """            if (_selectedDevice != null)
            {
                ClearPendingControlConfirmations(_selectedDevice);
                _selectedDevice.IsActive = true;
                NewDeviceIp = _selectedDevice.IpAddress;
""",
        "MainWindow selected IED pending reset",
    )

    handlers = """    private void ControlStageAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not SignalDefinition signal || !signal.IsPositionControl)
            return;
        if (signal.ControlIsBusy || !signal.ControlSupportsOperate)
            return;

        var requestedValue = button.CommandParameter?.ToString()?.Trim() ?? string.Empty;
        var actionLabel = button.Content?.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(requestedValue) || string.IsNullOrWhiteSpace(actionLabel))
            return;

        signal.StageControlConfirmation(requestedValue, actionLabel);
        var device = _signalOwners.TryGetValue(signal, out var owner) ? owner : SelectedDevice;
        SetStatus($"{device?.Name ?? "IED"}: review {signal.Name} — {signal.ControlPendingConfirmationLabel}, or Cancel.");
    }

    private async void ControlConfirmAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not SignalDefinition signal)
            return;
        if (!signal.ControlCanConfirm || string.IsNullOrWhiteSpace(signal.ControlPendingValue))
            return;

        var requestedValue = signal.ControlPendingValue;
        signal.ClearControlConfirmation();
        await ExecuteQuickControlAsync(signal, requestedValue);
    }

    private void ControlCancelAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not SignalDefinition signal)
            return;

        var action = signal.ControlPendingAction;
        signal.ClearControlConfirmation();
        var device = _signalOwners.TryGetValue(signal, out var owner) ? owner : SelectedDevice;
        SetStatus($"{device?.Name ?? "IED"}: {signal.Name} {action} cancelled before dispatch.");
    }

"""
    text = replace_once(
        text,
        """    private async void ControlQuickAction_Click(object sender, RoutedEventArgs e)
""",
        handlers + """    private async void ControlQuickAction_Click(object sender, RoutedEventArgs e)
""",
        "MainWindow confirmation handlers",
    )

    text = replace_once(
        text,
        """        if (!CommandTestMode && !LiveControlArmed)
        {
            signal.ControlLastResult = "Enable Live control armed before sending a command.";
            SetStatus("Live control is not armed. Review the selected IED and enable the Command Panel safety switch.");
            return;
        }

""",
        "",
        "MainWindow global live-arm gate",
    )

    text = replace_once(
        text,
        """            $"Control click accepted: {signal.ObjectReference} value={requestedValue}; test={CommandTestMode}; interlock={CommandInterlockCheck}; synchro={CommandSynchroCheck}.");
""",
        """            $"Control click accepted: {signal.ObjectReference} value={requestedValue}; test={signal.ControlTestMode}; interlock={signal.ControlInterlockCheck}; synchro={signal.ControlSynchroCheck}.");
""",
        "MainWindow per-signal control log",
    )

    text = replace_once(
        text,
        """                    InterlockCheck = CommandInterlockCheck,
                    SynchroCheck = CommandSynchroCheck,
                    TestMode = CommandTestMode,
""",
        """                    InterlockCheck = signal.ControlInterlockCheck,
                    SynchroCheck = signal.ControlSynchroCheck,
                    TestMode = signal.ControlTestMode,
""",
        "MainWindow per-signal control request",
    )

    text = replace_once(
        text,
        """        finally
        {
            signal.ControlIsBusy = false;
        }
    }

    private static string BuildQuickControlResult""",
        """        finally
        {
            signal.ControlIsBusy = false;
            signal.ClearControlConfirmation();
        }
    }

    private static string BuildQuickControlResult""",
        "MainWindow clear staged confirmation",
    )

    text = replace_once(
        text,
        """    private static string ControlFeedbackKey(string deviceId, string reference)
        => $"{deviceId}|{NormalizeReference(reference)}";

    private async void IedRescan_Click""",
        """    private static string ControlFeedbackKey(string deviceId, string reference)
        => $"{deviceId}|{NormalizeReference(reference)}";

    private static void ClearPendingControlConfirmations(Iec61850MonitorDevice? device)
    {
        if (device == null)
            return;

        foreach (var signal in device.Signals.Where(item => item.IsControlSignal))
            signal.ClearControlConfirmation();
    }

    private async void IedRescan_Click""",
        "MainWindow pending confirmation reset helper",
    )

    write_source(path, text, newline)


def patch_command_panel_ux() -> None:
    path, text, newline = read_source("MainWindow.CommandPanelUx.cs")

    text = replace_once(
        text,
        """        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var liveArmed = values.ElementAtOrDefault(0) is true;
            var testMode = values.ElementAtOrDefault(1) is true;
            var busy = values.ElementAtOrDefault(2) is true;
            var supportsOperate = values.ElementAtOrDefault(3) is true;
            // Never disable Open/Close from the last displayed process value. Report and
            // UI batching can be stale for a short time, which previously made the first
            // click disappear. The live MMS preflight in the control engine is the only
            // authority that may suppress a redundant command.
            return (liveArmed || testMode) && supportsOperate && !busy;
        }
""",
        """        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var busy = values.ElementAtOrDefault(0) is true;
            var supportsOperate = values.ElementAtOrDefault(1) is true;
            // Fast workflow is always armed. Runtime ctlModel/type validation remains the
            // authority, while Open/Close gains an explicit row-level confirmation step.
            return supportsOperate && !busy;
        }
""",
        "CommandPanelUx always-armed converter",
    )

    text = replace_once(
        text,
        """        enabledBinding.Bindings.Add(new Binding(nameof(LiveControlArmed)) { Source = this });
        enabledBinding.Bindings.Add(new Binding(nameof(CommandTestMode)) { Source = this });
        enabledBinding.Bindings.Add(new Binding(nameof(SignalDefinition.ControlIsBusy)));
        enabledBinding.Bindings.Add(new Binding(nameof(SignalDefinition.ControlSupportsOperate)));
""",
        """        enabledBinding.Bindings.Add(new Binding(nameof(SignalDefinition.ControlIsBusy)));
        enabledBinding.Bindings.Add(new Binding(nameof(SignalDefinition.ControlSupportsOperate)));
""",
        "CommandPanelUx per-row enabled binding",
    )

    write_source(path, text, newline)


def patch_main_window_xaml() -> None:
    path, text, newline = read_source("MainWindow.xaml")

    text = replace_once(
        text,
        """                                            <WrapPanel Grid.Column="1" Margin="12,0,0,0" VerticalAlignment="Center">
                                                <CheckBox Content="Live control armed" IsChecked="{Binding LiveControlArmed}" FontWeight="SemiBold"
                                                          Foreground="#B42318" VerticalAlignment="Center" Margin="0,0,14,0"
                                                          ToolTip="Safety arm for direct row commands. It resets when another IED is selected."/>
                                                <CheckBox Content="Interlock" IsChecked="{Binding CommandInterlockCheck}" VerticalAlignment="Center" Margin="0,0,12,0"/>
                                                <CheckBox Content="Synchrocheck" IsChecked="{Binding CommandSynchroCheck}" VerticalAlignment="Center" Margin="0,0,12,0"/>
                                                <CheckBox Content="Test" IsChecked="{Binding CommandTestMode}" VerticalAlignment="Center" Margin="0,0,12,0"/>
                                                <Button Content="Refresh values" Click="RefreshCommandValues_Click" Style="{StaticResource SoftButton}" Padding="11,6" Margin="0,0,8,0"/>
                                                <Button Content="Edit signals" Tag="{Binding SelectedDevice}" Click="IedConfigureSignals_Click"
                                                        Style="{StaticResource SoftButton}" Padding="11,6" IsEnabled="{Binding SelectedDevice.CanEditSignals}"/>
                                            </WrapPanel>
""",
        """                                            <WrapPanel Grid.Column="1" Margin="12,0,0,0" VerticalAlignment="Center">
                                                <Button Content="Refresh values" Click="RefreshCommandValues_Click" Style="{StaticResource SoftButton}" Padding="11,6" Margin="0,0,8,0"/>
                                                <Button Content="Edit signals" Tag="{Binding SelectedDevice}" Click="IedConfigureSignals_Click"
                                                        Style="{StaticResource SoftButton}" Padding="11,6" IsEnabled="{Binding SelectedDevice.CanEditSignals}"/>
                                            </WrapPanel>
""",
        "MainWindow.xaml global command switches",
    )

    checks_column = """                                                    <DataGridTextColumn Header="Control model" Binding="{Binding ControlModelText}" Width="1.35*" MinWidth="175" IsReadOnly="True"/>
                                                    <DataGridTemplateColumn Header="Checks" Width="1.35*" MinWidth="210">
                                                        <DataGridTemplateColumn.CellTemplate>
                                                            <DataTemplate>
                                                                <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="0,2">
                                                                    <CheckBox Content="Interlock"
                                                                              IsChecked="{Binding ControlInterlockCheck, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                                                              Margin="0,0,10,0"
                                                                              ToolTip="Set the IEC 61850 interlock check flag for this signal only."/>
                                                                    <CheckBox Content="Sync"
                                                                              IsChecked="{Binding ControlSynchroCheck, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                                                              Margin="0,0,10,0"
                                                                              ToolTip="Set the IEC 61850 synchrocheck flag for this signal only."/>
                                                                    <CheckBox Content="Test"
                                                                              IsChecked="{Binding ControlTestMode, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                                                              ToolTip="Send this signal in IEC 61850 Test mode."/>
                                                                </StackPanel>
                                                            </DataTemplate>
                                                        </DataGridTemplateColumn.CellTemplate>
                                                    </DataGridTemplateColumn>
                                                    <DataGridTemplateColumn Header="Control" Width="1.7*" MinWidth="250">
"""
    text = replace_once(
        text,
        """                                                    <DataGridTextColumn Header="Control model" Binding="{Binding ControlModelText}" Width="1.35*" MinWidth="175" IsReadOnly="True"/>
                                                    <DataGridTemplateColumn Header="Control" Width="1.45*" MinWidth="200">
""",
        checks_column,
        "MainWindow.xaml per-row safety checks",
    )

    old_position = """                                                                        <StackPanel Orientation="Horizontal">
                                                                            <StackPanel.Style>
                                                                                <Style TargetType="StackPanel"><Setter Property="Visibility" Value="Collapsed"/>
                                                                                    <Style.Triggers><DataTrigger Binding="{Binding IsPositionControl}" Value="True"><Setter Property="Visibility" Value="Visible"/></DataTrigger></Style.Triggers>
                                                                                </Style>
                                                                            </StackPanel.Style>
                                                                            <Button Content="Open" Tag="{Binding}" CommandParameter="Open [01]" Click="ControlQuickAction_Click" Style="{StaticResource CommandOpenButton}" Margin="0,0,7,0"
                                                                                    IsEnabled="{Binding DataContext.LiveControlArmed, RelativeSource={RelativeSource AncestorType=Window}}"/>
                                                                            <Button Content="Close" Tag="{Binding}" CommandParameter="Closed [10]" Click="ControlQuickAction_Click" Style="{StaticResource CommandCloseButton}" Margin="0,0,7,0"
                                                                                    IsEnabled="{Binding DataContext.LiveControlArmed, RelativeSource={RelativeSource AncestorType=Window}}"/>
                                                                        </StackPanel>
"""
    new_position = """                                                                        <StackPanel Orientation="Horizontal">
                                                                            <StackPanel.Style>
                                                                                <Style TargetType="StackPanel">
                                                                                    <Setter Property="Visibility" Value="Collapsed"/>
                                                                                    <Style.Triggers>
                                                                                        <MultiDataTrigger>
                                                                                            <MultiDataTrigger.Conditions>
                                                                                                <Condition Binding="{Binding IsPositionControl}" Value="True"/>
                                                                                                <Condition Binding="{Binding ControlConfirmationPending}" Value="False"/>
                                                                                            </MultiDataTrigger.Conditions>
                                                                                            <Setter Property="Visibility" Value="Visible"/>
                                                                                        </MultiDataTrigger>
                                                                                    </Style.Triggers>
                                                                                </Style>
                                                                            </StackPanel.Style>
                                                                            <Button Content="Open" Tag="{Binding}" CommandParameter="Open [01]" Click="ControlStageAction_Click"
                                                                                    Style="{StaticResource CommandOpenButton}" Margin="0,0,7,0"
                                                                                    IsEnabled="{Binding ControlSupportsOperate}"/>
                                                                            <Button Content="Close" Tag="{Binding}" CommandParameter="Closed [10]" Click="ControlStageAction_Click"
                                                                                    Style="{StaticResource CommandCloseButton}" Margin="0,0,7,0"
                                                                                    IsEnabled="{Binding ControlSupportsOperate}"/>
                                                                        </StackPanel>

                                                                        <StackPanel Orientation="Horizontal">
                                                                            <StackPanel.Style>
                                                                                <Style TargetType="StackPanel">
                                                                                    <Setter Property="Visibility" Value="Collapsed"/>
                                                                                    <Style.Triggers>
                                                                                        <MultiDataTrigger>
                                                                                            <MultiDataTrigger.Conditions>
                                                                                                <Condition Binding="{Binding IsPositionControl}" Value="True"/>
                                                                                                <Condition Binding="{Binding ControlConfirmationPending}" Value="True"/>
                                                                                            </MultiDataTrigger.Conditions>
                                                                                            <Setter Property="Visibility" Value="Visible"/>
                                                                                        </MultiDataTrigger>
                                                                                    </Style.Triggers>
                                                                                </Style>
                                                                            </StackPanel.Style>
                                                                            <Button Content="Confirm Open" Tag="{Binding}" Click="ControlConfirmAction_Click"
                                                                                    IsEnabled="{Binding ControlCanConfirm}" Margin="0,0,7,0">
                                                                                <Button.Style>
                                                                                    <Style TargetType="Button" BasedOn="{StaticResource CommandOpenButton}">
                                                                                        <Setter Property="Visibility" Value="Collapsed"/>
                                                                                        <Style.Triggers>
                                                                                            <DataTrigger Binding="{Binding ControlPendingAction}" Value="Open">
                                                                                                <Setter Property="Visibility" Value="Visible"/>
                                                                                            </DataTrigger>
                                                                                        </Style.Triggers>
                                                                                    </Style>
                                                                                </Button.Style>
                                                                            </Button>
                                                                            <Button Content="Confirm Close" Tag="{Binding}" Click="ControlConfirmAction_Click"
                                                                                    IsEnabled="{Binding ControlCanConfirm}" Margin="0,0,7,0">
                                                                                <Button.Style>
                                                                                    <Style TargetType="Button" BasedOn="{StaticResource CommandCloseButton}">
                                                                                        <Setter Property="Visibility" Value="Collapsed"/>
                                                                                        <Style.Triggers>
                                                                                            <DataTrigger Binding="{Binding ControlPendingAction}" Value="Close">
                                                                                                <Setter Property="Visibility" Value="Visible"/>
                                                                                            </DataTrigger>
                                                                                        </Style.Triggers>
                                                                                    </Style>
                                                                                </Button.Style>
                                                                            </Button>
                                                                            <Button Content="Cancel" Tag="{Binding}" Click="ControlCancelAction_Click"
                                                                                    Style="{StaticResource SoftButton}" Padding="12,6"/>
                                                                        </StackPanel>
"""
    text = replace_once(text, old_position, new_position, "MainWindow.xaml staged Open/Close")

    old_enabled = 'IsEnabled="{Binding DataContext.LiveControlArmed, RelativeSource={RelativeSource AncestorType=Window}}"'
    count = text.count(old_enabled)
    if count != 7:
        raise RuntimeError(f"MainWindow.xaml remaining global arm bindings: expected 7, found {count}")
    text = text.replace(old_enabled, 'IsEnabled="{Binding ControlSupportsOperate}"')

    if "Live control armed" in text or "LiveControlArmed" in text:
        raise RuntimeError("MainWindow.xaml still contains the removed global live-control arm")

    write_source(path, text, newline)


def patch_readme() -> None:
    path, text, newline = read_source("README.md")

    text = replace_once(
        text,
        """- Optional **Details** window for the full ctlModel, sequence, checks, and protocol evidence
""",
        """- Per-signal **Interlock**, **Synchrocheck**, and **Test** flags directly in each command row
- Two-step breaker safety: **Open/Close** becomes **Confirm Open/Confirm Close** plus **Cancel** before dispatch
- Optional **Details** window for the full ctlModel, sequence, checks, and protocol evidence
""",
        "README control workflow bullets",
    )

    write_source(path, text, newline)


def validate() -> None:
    ET.parse(ROOT / "App.xaml")
    ET.parse(ROOT / "MainWindow.xaml")

    main_xaml = (ROOT / "MainWindow.xaml").read_text(encoding="utf-8")
    main_cs = (ROOT / "MainWindow.xaml.cs").read_text(encoding="utf-8")
    signal_cs = (ROOT / "Models/SignalDefinition.cs").read_text(encoding="utf-8")
    ux_cs = (ROOT / "MainWindow.CommandPanelUx.cs").read_text(encoding="utf-8")

    required = {
        "per-row checks": "ControlInterlockCheck",
        "staged command": "ControlStageAction_Click",
        "confirm command": "ControlConfirmAction_Click",
        "cancel command": "ControlCancelAction_Click",
        "confirm UI": "Confirm Close",
    }
    combined = "\n".join((main_xaml, main_cs, signal_cs, ux_cs))
    missing = [name for name, token in required.items() if token not in combined]
    if missing:
        raise RuntimeError("Missing expected implementation tokens: " + ", ".join(missing))

    forbidden = ("Live control armed", "LiveControlArmed", "CommandInterlockCheck = CommandInterlockCheck")
    present = [token for token in forbidden if token in combined]
    if present:
        raise RuntimeError("Removed global control state is still present: " + ", ".join(present))


def main() -> None:
    patch_signal_definition()
    patch_main_window_code()
    patch_command_panel_ux()
    patch_main_window_xaml()
    patch_readme()
    validate()
    print("Per-signal control safety UX patch applied and validated.")


if __name__ == "__main__":
    main()
