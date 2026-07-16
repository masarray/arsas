from pathlib import Path


def replace_once(path: str, old: str, new: str, label: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    file.write_text(text.replace(old, new, 1), encoding="utf-8")


replace_once(
    "MainWindow.xaml.cs",
    '''    public string HeaderStatusText => $"{Devices.Count} IED • {_runtime.MonitoringDeviceCount} monitoring";
    public string DeviceCountText => $"{Devices.Count} device(s)";
    public string RuntimeSummaryText => $"Connected {_runtime.ConnectedDeviceCount} • Monitoring {_runtime.MonitoringDeviceCount} • Values {GlobalPoints.Count} • Events {Events.Count}";
    public string ConnectionInsightText => $"{_runtime.ConnectedDeviceCount} connected / {Devices.Count} discovered";
    public string MonitoringInsightText => $"{_runtime.MonitoringDeviceCount} monitoring / {GlobalPoints.Count} values";''',
    '''    public string HeaderStatusText => $"{Devices.Count} IED • {(IsDemoMode ? Devices.Count(device => device.IsMonitoring) : _runtime.MonitoringDeviceCount)} monitoring";
    public string DeviceCountText => $"{Devices.Count} device(s)";
    public string RuntimeSummaryText => $"Connected {(IsDemoMode ? Devices.Count(device => device.IsConnected) : _runtime.ConnectedDeviceCount)} • Monitoring {(IsDemoMode ? Devices.Count(device => device.IsMonitoring) : _runtime.MonitoringDeviceCount)} • Values {GlobalPoints.Count} • Events {Events.Count}";
    public string ConnectionInsightText => $"{(IsDemoMode ? Devices.Count(device => device.IsConnected) : _runtime.ConnectedDeviceCount)} connected / {Devices.Count} discovered";
    public string MonitoringInsightText => $"{(IsDemoMode ? Devices.Count(device => device.IsMonitoring) : _runtime.MonitoringDeviceCount)} monitoring / {GlobalPoints.Count} values";''',
    "demo display counters",
)

replace_once(
    "MainWindow.xaml",
    'Icon="Assets/app-icon.ico" Closing="Window_Closing">',
    'Icon="Assets/app-icon.ico" Closing="Window_Closing" PreviewKeyDown="MainWindow_PreviewKeyDown">',
    "demo shortcut handler",
)

replace_once(
    "MainWindow.xaml",
    '''            <WrapPanel Grid.Column="2" HorizontalAlignment="Right" VerticalAlignment="Center">
                <Border Background="{StaticResource PremiumSurface}"''',
    '''            <WrapPanel Grid.Column="2" HorizontalAlignment="Right" VerticalAlignment="Center">
                <Border Visibility="{Binding DemoModeVisibility}" Background="#FFF4CC" BorderBrush="#F5B942" BorderThickness="1"
                        CornerRadius="15" Padding="10,6" Margin="0,0,8,0"
                        ToolTip="Synthetic read-only substation data. No MMS or Ethernet traffic is generated.">
                    <TextBlock Text="{Binding DemoModeText}" FontSize="11.8" FontWeight="SemiBold" Foreground="#8A4B08"/>
                </Border>
                <Border Background="{StaticResource PremiumSurface}"''',
    "demo mode badge",
)

replace_once(
    "Models/MonitorModels.cs",
    "    private bool _hasReportStream;\n",
    "    private bool _hasReportStream;\n    private bool _isDemo;\n",
    "demo model field",
)

report_property = '''    public bool HasReportStream
    {
        get => _hasReportStream;
        set
        {
            if (Set(ref _hasReportStream, value))
                RefreshComputed();
        }
    }
'''
replace_once(
    "Models/MonitorModels.cs",
    report_property,
    report_property + '''
    public bool IsDemo
    {
        get => _isDemo;
        set
        {
            if (Set(ref _isDemo, value))
                RefreshComputed();
        }
    }
''',
    "demo model property",
)

replacements = {
    "public bool IsActionEnabled => !IsBusy;": "public bool IsActionEnabled => !IsBusy && !IsDemo;",
    "public bool CanEditSignals => SignalCount > 0 && !IsBusy && !IsMonitoring;": "public bool CanEditSignals => SignalCount > 0 && !IsBusy && !IsMonitoring && !IsDemo;",
    "public bool CanStartMonitor => IsConnected && SelectedLiveSignalCount > 0 && !IsBusy;": "public bool CanStartMonitor => IsConnected && SelectedLiveSignalCount > 0 && !IsBusy && !IsDemo;",
    "public bool CanStartOrStopMonitor => !IsBusy && (IsMonitoring || SelectedLiveSignalCount > 0);": "public bool CanStartOrStopMonitor => !IsBusy && !IsDemo && (IsMonitoring || SelectedLiveSignalCount > 0);",
    "public bool CanPlayAction => !IsBusy && (!IsConnected || (!IsMonitoring && SelectedLiveSignalCount > 0));": "public bool CanPlayAction => !IsBusy && !IsDemo && (!IsConnected || (!IsMonitoring && SelectedLiveSignalCount > 0));",
    "public bool CanStopAction => !IsBusy && (IsConnected || IsMonitoring);": "public bool CanStopAction => !IsBusy && !IsDemo && (IsConnected || IsMonitoring);",
    "public bool CanRescan => !IsBusy && !IsMonitoring;": "public bool CanRescan => !IsBusy && !IsMonitoring && !IsDemo;",
    "public bool CanSaveScl => !IsBusy && (SclWorkspace != null || LiveDiscoveryModel != null);": "public bool CanSaveScl => !IsBusy && !IsDemo && (SclWorkspace != null || LiveDiscoveryModel != null);",
}
for old, new in replacements.items():
    replace_once("Models/MonitorModels.cs", old, new, old)
