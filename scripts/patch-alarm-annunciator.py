from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one anchor, found {count}")
    return text.replace(old, new, 1)

# -----------------------------------------------------------------------------
# MainWindow.xaml — remove redundant top status chips, add sixth navigation tab,
# Alarm selection checkbox and the annunciator workspace.
# -----------------------------------------------------------------------------
xaml_path = Path("MainWindow.xaml")
xaml = xaml_path.read_text(encoding="utf-8")

xaml = replace_once(
    xaml,
    '''                    <Grid.ColumnDefinitions>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                    </Grid.ColumnDefinitions>''',
    '''                    <Grid.ColumnDefinitions>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                        <ColumnDefinition Width="*"/>\n                    </Grid.ColumnDefinitions>''',
    "six navigation columns")

xaml = replace_once(
    xaml,
    '''                    <Border x:Name="WorkflowPill" Grid.ColumnSpan="5" Width="148" Height="34"''',
    '''                    <Border x:Name="WorkflowPill" Grid.ColumnSpan="6" Width="148" Height="34"''',
    "six-column navigation pill")

xaml = replace_once(
    xaml,
    '''                    <Button x:Name="NavEventsButton" Grid.Column="2" Content="Event Log" Tag="2" Click="NavButton_Click" Style="{StaticResource SegmentedNavButton}"/>\n                    <Button x:Name="NavGooseButton" Grid.Column="3" Content="GOOSE Subscriber" Tag="3" Click="NavButton_Click" Style="{StaticResource SegmentedNavButton}"/>\n                    <Button x:Name="NavDiagnosticsButton" Grid.Column="4" Tag="4" Click="NavButton_Click" Style="{StaticResource SegmentedNavButton}">''',
    '''                    <Button x:Name="NavEventsButton" Grid.Column="2" Content="Event Log" Tag="2" Click="NavButton_Click" Style="{StaticResource SegmentedNavButton}"/>\n                    <Button x:Name="NavAlarmButton" Grid.Column="3" Content="Alarm Annunciator" Tag="3" Click="NavButton_Click" Style="{StaticResource SegmentedNavButton}"/>\n                    <Button x:Name="NavGooseButton" Grid.Column="4" Content="GOOSE Subscriber" Tag="4" Click="NavButton_Click" Style="{StaticResource SegmentedNavButton}"/>\n                    <Button x:Name="NavDiagnosticsButton" Grid.Column="5" Tag="5" Click="NavButton_Click" Style="{StaticResource SegmentedNavButton}">''',
    "alarm navigation button")

status_chips = '''\n            <WrapPanel Grid.Column="2" HorizontalAlignment="Right" VerticalAlignment="Center">\n                <Border Background="{StaticResource PremiumSurface}" BorderBrush="{StaticResource BorderSubtle}" BorderThickness="1" CornerRadius="15" Padding="10,6" Margin="0,0,8,0">\n                    <TextBlock Text="{Binding ConnectionInsightText}" FontSize="12.5" Foreground="{StaticResource Ink}"/>\n                </Border>\n                <Border Background="{StaticResource PremiumSurface}" BorderBrush="{StaticResource BorderSubtle}" BorderThickness="1" CornerRadius="15" Padding="10,6" Margin="0,0,8,0">\n                    <TextBlock Text="{Binding MonitoringInsightText}" FontSize="12.5" Foreground="{StaticResource Ink}"/>\n                </Border>\n                <Border Background="{StaticResource PremiumSurface}" BorderBrush="{StaticResource BorderSubtle}" BorderThickness="1" CornerRadius="15" Padding="10,6">\n                    <TextBlock Text="{Binding EventInsightText}" FontSize="12.5" Foreground="{StaticResource Ink}"/>\n                </Border>\n            </WrapPanel>'''
xaml = replace_once(
    xaml,
    status_chips,
    '''\n            <!-- Runtime counts intentionally live only in the bottom status bar.\n                 Keeping the header free of duplicate status chips reduces visual noise\n                 and reserves width for the six workflow destinations. -->''',
    "remove redundant top runtime chips")

xaml = replace_once(
    xaml,
    '''                                            <DataGridTemplateColumn Header="Value" Width="0.8*" MinWidth="125" CellTemplate="{StaticResource ProcessValueBadgeTemplate}"/>\n                                            <DataGridTextColumn Header="Quality" Binding="{Binding Quality}" Width="0.72*" MinWidth="105"/>''',
    '''                                            <DataGridTemplateColumn Header="Value" Width="0.8*" MinWidth="125" CellTemplate="{StaticResource ProcessValueBadgeTemplate}"/>\n                                            <DataGridTemplateColumn Header="Alarm" Width="72" MinWidth="68">\n                                                <DataGridTemplateColumn.CellTemplate>\n                                                    <DataTemplate>\n                                                        <CheckBox IsChecked="{Binding IsAnnunciatorSelected, Mode=OneWay}"\n                                                                  IsEnabled="{Binding CanUseAsAnnunciator}"\n                                                                  Tag="{Binding}" Click="AnnunciatorSelection_Click"\n                                                                  HorizontalAlignment="Center" VerticalAlignment="Center"\n                                                                  ToolTip="{Binding AnnunciatorSelectionToolTip}"\n                                                                  FocusVisualStyle="{x:Null}"/>\n                                                    </DataTemplate>\n                                                </DataGridTemplateColumn.CellTemplate>\n                                            </DataGridTemplateColumn>\n                                            <DataGridTextColumn Header="Quality" Binding="{Binding Quality}" Width="0.72*" MinWidth="105"/>''',
    "explorer alarm checkbox")

alarm_tab = '''\n\n            <!-- EVENT-LATCHED ALARM ANNUNCIATOR -->\n            <TabItem Header="Alarm Annunciator">\n                <Border Style="{StaticResource Card}" Padding="14">\n                    <Grid>\n                        <Grid.RowDefinitions>\n                            <RowDefinition Height="Auto"/>\n                            <RowDefinition Height="12"/>\n                            <RowDefinition Height="*"/>\n                        </Grid.RowDefinitions>\n\n                        <Grid>\n                            <Grid.ColumnDefinitions>\n                                <ColumnDefinition Width="*"/>\n                                <ColumnDefinition Width="Auto"/>\n                            </Grid.ColumnDefinitions>\n                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">\n                                <Border Width="34" Height="34" CornerRadius="12" Background="#FFF1F0" BorderBrush="#FECACA" BorderThickness="1" Margin="0,0,11,0">\n                                    <Ellipse Width="12" Height="12" Fill="#DC2626"\n                                             Opacity="{Binding AnnunciatorBeaconOpacity}"\n                                             HorizontalAlignment="Center" VerticalAlignment="Center"/>\n                                </Border>\n                                <StackPanel>\n                                    <TextBlock Text="Alarm Annunciator" FontSize="16" FontWeight="SemiBold" Foreground="{StaticResource Ink}"/>\n                                    <TextBlock Text="SOE-latched alarm windows • momentary relay pulses remain visible until acknowledgement"\n                                               FontSize="12.1" Foreground="{StaticResource Muted}" Margin="0,3,0,0"/>\n                                </StackPanel>\n                            </StackPanel>\n                            <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">\n                                <Border Background="#F8FAFC" BorderBrush="{StaticResource Line}" BorderThickness="1" CornerRadius="12" Padding="10,6" Margin="0,0,9,0">\n                                    <TextBlock Text="{Binding AnnunciatorSummaryText}" FontSize="11.8" Foreground="{StaticResource Ink}"/>\n                                </Border>\n                                <Button Content="ACK ALL" Style="{StaticResource PrimaryButton}" Padding="14,7"\n                                        Click="AcknowledgeAllAlarms_Click"\n                                        ToolTip="Acknowledge every unacknowledged annunciator occurrence. Active process conditions remain steadily indicated until they return to normal."/>\n                            </StackPanel>\n                        </Grid>\n\n                        <Grid Grid.Row="2">\n                            <ScrollViewer Visibility="{Binding AnnunciatorContentVisibility}" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">\n                                <ItemsControl ItemsSource="{Binding AnnunciatorAlarms}">\n                                    <ItemsControl.ItemsPanel>\n                                        <ItemsPanelTemplate>\n                                            <WrapPanel Orientation="Horizontal"/>\n                                        </ItemsPanelTemplate>\n                                    </ItemsControl.ItemsPanel>\n                                    <ItemsControl.ItemTemplate>\n                                        <DataTemplate>\n                                            <Border Width="286" MinHeight="158" Margin="0,0,10,10" Padding="14" CornerRadius="15" BorderThickness="1.2">\n                                                <Border.Style>\n                                                    <Style TargetType="Border">\n                                                        <Setter Property="Background" Value="#F8FAFC"/>\n                                                        <Setter Property="BorderBrush" Value="#D8E1EC"/>\n                                                        <Style.Triggers>\n                                                            <DataTrigger Binding="{Binding VisualState}" Value="ActiveUnacknowledged">\n                                                                <Setter Property="Background" Value="#FFF1F0"/>\n                                                                <Setter Property="BorderBrush" Value="#FCA5A5"/>\n                                                            </DataTrigger>\n                                                            <DataTrigger Binding="{Binding VisualState}" Value="ActiveAcknowledged">\n                                                                <Setter Property="Background" Value="#FFF7ED"/>\n                                                                <Setter Property="BorderBrush" Value="#FDBA74"/>\n                                                            </DataTrigger>\n                                                            <DataTrigger Binding="{Binding VisualState}" Value="ReturnedUnacknowledged">\n                                                                <Setter Property="Background" Value="#FFF8E7"/>\n                                                                <Setter Property="BorderBrush" Value="#F5C76B"/>\n                                                            </DataTrigger>\n                                                        </Style.Triggers>\n                                                    </Style>\n                                                </Border.Style>\n                                                <Grid>\n                                                    <Grid.RowDefinitions>\n                                                        <RowDefinition Height="Auto"/>\n                                                        <RowDefinition Height="Auto"/>\n                                                        <RowDefinition Height="Auto"/>\n                                                        <RowDefinition Height="Auto"/>\n                                                    </Grid.RowDefinitions>\n                                                    <Grid>\n                                                        <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>\n                                                        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">\n                                                            <Ellipse Width="11" Height="11" Margin="0,0,8,0" Opacity="{Binding LampOpacity}">\n                                                                <Ellipse.Style>\n                                                                    <Style TargetType="Ellipse">\n                                                                        <Setter Property="Fill" Value="#94A3B8"/>\n                                                                        <Style.Triggers>\n                                                                            <DataTrigger Binding="{Binding VisualState}" Value="ActiveUnacknowledged"><Setter Property="Fill" Value="#DC2626"/></DataTrigger>\n                                                                            <DataTrigger Binding="{Binding VisualState}" Value="ActiveAcknowledged"><Setter Property="Fill" Value="#EA580C"/></DataTrigger>\n                                                                            <DataTrigger Binding="{Binding VisualState}" Value="ReturnedUnacknowledged"><Setter Property="Fill" Value="#D97706"/></DataTrigger>\n                                                                        </Style.Triggers>\n                                                                    </Style>\n                                                                </Ellipse.Style>\n                                                            </Ellipse>\n                                                            <TextBlock Text="{Binding SignalName}" FontSize="13.3" FontWeight="SemiBold" Foreground="#1D2939" TextTrimming="CharacterEllipsis" MaxWidth="178"/>\n                                                        </StackPanel>\n                                                        <Border Grid.Column="1" CornerRadius="8" Padding="7,3" BorderThickness="1">\n                                                            <Border.Style>\n                                                                <Style TargetType="Border">\n                                                                    <Setter Property="Background" Value="#EEF2F6"/>\n                                                                    <Setter Property="BorderBrush" Value="#D8E1EA"/>\n                                                                    <Style.Triggers>\n                                                                        <DataTrigger Binding="{Binding VisualState}" Value="ActiveUnacknowledged"><Setter Property="Background" Value="#FEE2E2"/><Setter Property="BorderBrush" Value="#FCA5A5"/></DataTrigger>\n                                                                        <DataTrigger Binding="{Binding VisualState}" Value="ActiveAcknowledged"><Setter Property="Background" Value="#FFEDD5"/><Setter Property="BorderBrush" Value="#FDBA74"/></DataTrigger>\n                                                                        <DataTrigger Binding="{Binding VisualState}" Value="ReturnedUnacknowledged"><Setter Property="Background" Value="#FEF3C7"/><Setter Property="BorderBrush" Value="#F5C76B"/></DataTrigger>\n                                                                    </Style.Triggers>\n                                                                </Style>\n                                                            </Border.Style>\n                                                            <TextBlock Text="{Binding StateText}" FontSize="9.7" FontWeight="Bold" Foreground="#475467"/>\n                                                        </Border>\n                                                    </Grid>\n                                                    <StackPanel Grid.Row="1" Margin="19,6,0,0">\n                                                        <TextBlock Text="{Binding DeviceName}" FontSize="10.8" Foreground="#475467" FontWeight="SemiBold"/>\n                                                        <TextBlock Text="{Binding IecTelegram}" FontSize="10.1" Foreground="#667085" TextTrimming="CharacterEllipsis" ToolTip="{Binding IecReference}"/>\n                                                    </StackPanel>\n                                                    <Grid Grid.Row="2" Margin="19,9,0,0">\n                                                        <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>\n                                                        <StackPanel>\n                                                            <TextBlock Text="{Binding StateDetail}" FontSize="10.4" Foreground="#475467" TextWrapping="Wrap" MaxWidth="190"/>\n                                                            <TextBlock Text="{Binding LastEventTimestamp, StringFormat=Last SOE {0}}" FontSize="9.7" Foreground="#98A2B3" Margin="0,4,0,0"/>\n                                                        </StackPanel>\n                                                        <Button Grid.Column="1" Content="ACK" Tag="{Binding}" Click="AcknowledgeAlarm_Click"\n                                                                Style="{StaticResource SoftButton}" Padding="10,5" Margin="8,0,0,0"\n                                                                IsEnabled="{Binding CanAcknowledge}" VerticalAlignment="Bottom"/>\n                                                    </Grid>\n                                                    <Grid Grid.Row="3" Margin="19,9,0,0">\n                                                        <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>\n                                                        <TextBlock Text="{Binding CurrentValue, StringFormat=Value {0}}" FontSize="10" FontWeight="SemiBold" Foreground="#344054"/>\n                                                        <TextBlock Grid.Column="1" Text="{Binding ActivationCountText}" FontSize="9.7" Foreground="#667085"/>\n                                                    </Grid>\n                                                </Grid>\n                                            </Border>\n                                        </DataTemplate>\n                                    </ItemsControl.ItemTemplate>\n                                </ItemsControl>\n                            </ScrollViewer>\n\n                            <Border Visibility="{Binding AnnunciatorEmptyVisibility}" Background="#FAFCFF" BorderBrush="#D9E4F1" BorderThickness="1" CornerRadius="18" Padding="28"\n                                    HorizontalAlignment="Center" VerticalAlignment="Center" MaxWidth="620">\n                                <StackPanel>\n                                    <Border Width="48" Height="48" CornerRadius="16" Background="#EEF4FF" BorderBrush="#C9D9F1" BorderThickness="1" HorizontalAlignment="Center">\n                                        <Viewbox Width="22" Height="22" HorizontalAlignment="Center" VerticalAlignment="Center">\n                                            <Path Data="{StaticResource LucideBell}" Style="{StaticResource LucideIcon}" Stroke="#2563EB"/>\n                                        </Viewbox>\n                                    </Border>\n                                    <TextBlock Text="No annunciator signals configured" FontSize="18" FontWeight="SemiBold" Foreground="{StaticResource Ink}" HorizontalAlignment="Center" Margin="0,14,0,0"/>\n                                    <TextBlock Text="In IEC 61850 Explorer, tick Alarm for the ST/protection status points you want to retain. Alarm state is driven by Event Log/SOE edges, so short relay pulses remain latched until ACK."\n                                               TextWrapping="Wrap" TextAlignment="Center" FontSize="12.4" Foreground="{StaticResource Muted}" Margin="0,8,0,0"/>\n                                </StackPanel>\n                            </Border>\n                        </Grid>\n                    </Grid>\n                </Border>\n            </TabItem>'''

xaml = replace_once(
    xaml,
    '''            </TabItem>\n\n            <!-- SCL / DISCOVERY-AWARE GOOSE SUBSCRIBER -->''',
    '''            </TabItem>''' + alarm_tab + '''\n\n            <!-- SCL / DISCOVERY-AWARE GOOSE SUBSCRIBER -->''',
    "insert Alarm Annunciator tab")

xaml_path.write_text(xaml, encoding="utf-8", newline="\n")

# -----------------------------------------------------------------------------
# MonitorModels.cs — live-point selection state + project persistence field.
# -----------------------------------------------------------------------------
models_path = Path("Models/MonitorModels.cs")
models = models_path.read_text(encoding="utf-8")
models = replace_once(
    models,
    '''    private long _sequence;\n    private bool _isRecentlyChanged;''',
    '''    private long _sequence;\n    private bool _isRecentlyChanged;\n    private bool _isAnnunciatorSelected;''',
    "monitor point annunciator backing field")
models = replace_once(
    models,
    '''    public long Sequence { get => _sequence; set => Set(ref _sequence, value); }\n    public bool IsRecentlyChanged { get => _isRecentlyChanged; set => Set(ref _isRecentlyChanged, value); }''',
    '''    public long Sequence { get => _sequence; set => Set(ref _sequence, value); }\n    public bool IsRecentlyChanged { get => _isRecentlyChanged; set => Set(ref _isRecentlyChanged, value); }\n    public bool IsAnnunciatorSelected { get => _isAnnunciatorSelected; set => Set(ref _isAnnunciatorSelected, value); }\n    public bool CanUseAsAnnunciator => FunctionalConstraint.Equals("ST", StringComparison.OrdinalIgnoreCase);\n    public string AnnunciatorSelectionToolTip => CanUseAsAnnunciator\n        ? "Latch this IEC 61850 ST point in Alarm Annunciator using SOE/Event Log edges."\n        : "Alarm Annunciator selection is limited to IEC 61850 ST status points.";''',
    "monitor point annunciator properties")
models = replace_once(
    models,
    '''    public List<string> SelectedReferences { get; set; } = new();\n    public List<Iec61850CachedSignalProfile> CachedSignals { get; set; } = new();''',
    '''    public List<string> SelectedReferences { get; set; } = new();\n    public List<string> AnnunciatorReferences { get; set; } = new();\n    public List<Iec61850CachedSignalProfile> CachedSignals { get; set; } = new();''',
    "project annunciator references")
models_path.write_text(models, encoding="utf-8", newline="\n")

# -----------------------------------------------------------------------------
# MainWindow.xaml.cs — sixth-tab indices, project persistence and diagnostics index.
# -----------------------------------------------------------------------------
cs_path = Path("MainWindow.xaml.cs")
cs = cs_path.read_text(encoding="utf-8")
cs = replace_once(cs, '''        index = Math.Clamp(index, 0, 4);''', '''        index = Math.Clamp(index, 0, 5);''', "nav clamp")
cs = replace_once(
    cs,
    '''        else if (MainTabs.SelectedIndex == 3)\n        {\n            // Defer optional Npcap/model work until after the selected tab has rendered.\n            // An unavailable capture dependency must never leave the workspace blank.\n            ActivateGooseSubscriberWorkspace();\n        }\n        else if (MainTabs.SelectedIndex == 4)\n        {\n            ClearDiagnosticAlert();\n        }''',
    '''        else if (MainTabs.SelectedIndex == 4)\n        {\n            // Defer optional Npcap/model work until after the selected tab has rendered.\n            // An unavailable capture dependency must never leave the workspace blank.\n            ActivateGooseSubscriberWorkspace();\n        }\n        else if (MainTabs.SelectedIndex == 5)\n        {\n            ClearDiagnosticAlert();\n        }''',
    "shift Goose and Diagnostics tab indices")
cs = replace_once(
    cs,
    '''        var target = Math.Clamp(index, 0, 4) * 150d;''',
    '''        var target = Math.Clamp(index, 0, 5) * 150d;''',
    "legacy pill clamp")
cs = replace_once(
    cs,
    '''        var buttons = new[] { NavExplorerButton, NavLiveButton, NavEventsButton, NavGooseButton, NavDiagnosticsButton };''',
    '''        var buttons = new[] { NavExplorerButton, NavLiveButton, NavEventsButton, NavAlarmButton, NavGooseButton, NavDiagnosticsButton };''',
    "six nav buttons")
cs = replace_once(
    cs,
    '''                SelectedReferences = device.Signals\n                    .Where(signal => signal.IsSelected)\n                    .Select(signal => NormalizeReference(signal.ObjectReference))\n                    .Distinct(StringComparer.OrdinalIgnoreCase)\n                    .ToList(),\n                CachedSignals = device.HasDiscoveryCache''',
    '''                SelectedReferences = device.Signals\n                    .Where(signal => signal.IsSelected)\n                    .Select(signal => NormalizeReference(signal.ObjectReference))\n                    .Distinct(StringComparer.OrdinalIgnoreCase)\n                    .ToList(),\n                AnnunciatorReferences = GetAnnunciatorReferencesForDevice(device),\n                CachedSignals = device.HasDiscoveryCache''',
    "save annunciator project references")
cs = replace_once(
    cs,
    '''                else\n                {\n                    _pendingProjectSelections[device.DeviceId] = selectedReferences;\n                }\n            }''',
    '''                else\n                {\n                    _pendingProjectSelections[device.DeviceId] = selectedReferences;\n                }\n\n                RestoreAnnunciatorReferences(device, profile.AnnunciatorReferences);\n            }''',
    "restore annunciator project references")
cs = replace_once(
    cs,
    '''        if (MainTabs?.SelectedIndex == 4 || _hasUnreadDiagnosticError)''',
    '''        if (MainTabs?.SelectedIndex == 5 || _hasUnreadDiagnosticError)''',
    "diagnostics alert selected index")
cs_path.write_text(cs, encoding="utf-8", newline="\n")

# -----------------------------------------------------------------------------
# Responsive header: six cells and wider nav now that redundant chips are gone.
# -----------------------------------------------------------------------------
nav_path = Path("MainWindow.NavigationLayoutFix.cs")
nav = nav_path.read_text(encoding="utf-8")
nav = nav.replace("five equal columns", "six equal columns")
nav = nav.replace("five columns", "six columns")
nav = replace_once(nav, "private const double WideNavWidth = 840d;", "private const double WideNavWidth = 990d;", "wide nav width")
nav = replace_once(nav, "private const double MediumNavWidth = 720d;", "private const double MediumNavWidth = 900d;", "medium nav width")
nav = replace_once(nav, "private const double CompactNavWidth = 580d;", "private const double CompactNavWidth = 720d;", "compact nav width")
nav = replace_once(
    nav,
    '''        "Event Log",\n        "GOOSE Subscriber"''',
    '''        "Event Log",\n        "Alarm Annunciator",\n        "GOOSE Subscriber"''',
    "full alarm nav label")
nav = replace_once(
    nav,
    '''        "Events",\n        "GOOSE"''',
    '''        "Events",\n        "Alarm",\n        "GOOSE"''',
    "compact alarm nav label")
nav = replace_once(
    nav,
    '''        if (button.Name is not ("NavExplorerButton" or "NavLiveButton" or "NavEventsButton" or "NavGooseButton" or "NavDiagnosticsButton"))''',
    '''        if (button.Name is not ("NavExplorerButton" or "NavLiveButton" or "NavEventsButton" or "NavAlarmButton" or "NavGooseButton" or "NavDiagnosticsButton"))''',
    "alarm nav click correction")
nav = replace_once(
    nav,
    '''            window.FindName("NavEventsButton") as Button,\n            window.FindName("NavGooseButton") as Button,''',
    '''            window.FindName("NavEventsButton") as Button,\n            window.FindName("NavAlarmButton") as Button,\n            window.FindName("NavGooseButton") as Button,''',
    "alarm navigation button array")
if nav.count("contentWidth / 5d") != 2:
    raise SystemExit(f"navigation cell width: expected 2 occurrences, found {nav.count('contentWidth / 5d')}")
nav = nav.replace("contentWidth / 5d", "contentWidth / 6d")
nav = replace_once(nav, "Math.Clamp(tabs.SelectedIndex, 0, 4)", "Math.Clamp(tabs.SelectedIndex, 0, 5)", "responsive tab clamp")
nav_path.write_text(nav, encoding="utf-8", newline="\n")

print("Alarm Annunciator integration patch applied.")
