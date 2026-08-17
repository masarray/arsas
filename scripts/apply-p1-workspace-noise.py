from pathlib import Path

MAIN = Path('MainWindow.xaml')
GOOSE = Path('MainWindow.GooseSubscriber.cs')
MODELS = Path('Models/MonitorModels.cs')

main = MAIN.read_text(encoding='utf-8')
goose_code = GOOSE.read_text(encoding='utf-8')
models = MODELS.read_text(encoding='utf-8')


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected one match, found {count}')
    return text.replace(old, new, 1)


def replace_section(text: str, start: str, end: str, replacement: str, label: str) -> str:
    a = text.find(start)
    if a < 0:
        raise SystemExit(f'{label}: start marker missing')
    b = text.find(end, a + len(start))
    if b < 0:
        raise SystemExit(f'{label}: end marker missing')
    return text[:a] + replacement + text[b:]

# --- MainWindow resources: contained quality and diagnostic severity cues. ---
if 'x:Key="EventQualityBadgeTemplate"' not in main:
    resources = r'''
        <DataTemplate x:Key="EventQualityBadgeTemplate">
            <Border x:Name="QualityBadge" CornerRadius="8" Padding="7,2" MinWidth="72"
                    HorizontalAlignment="Left" Background="#F3F6F9" BorderBrush="#D5DEE8" BorderThickness="1">
                <TextBlock x:Name="QualityText" Text="{Binding Quality}" FontSize="10.4" FontWeight="SemiBold"
                           Foreground="#617286" TextTrimming="CharacterEllipsis" ToolTip="{Binding Quality}"/>
            </Border>
            <DataTemplate.Triggers>
                <DataTrigger Binding="{Binding QualityTone}" Value="Attention">
                    <Setter TargetName="QualityBadge" Property="Background" Value="#FFF8E6"/>
                    <Setter TargetName="QualityBadge" Property="BorderBrush" Value="#F2D28A"/>
                    <Setter TargetName="QualityText" Property="Foreground" Value="#946200"/>
                </DataTrigger>
                <DataTrigger Binding="{Binding QualityTone}" Value="Bad">
                    <Setter TargetName="QualityBadge" Property="Background" Value="#FFF1F2"/>
                    <Setter TargetName="QualityBadge" Property="BorderBrush" Value="#FDA29B"/>
                    <Setter TargetName="QualityText" Property="Foreground" Value="#B42318"/>
                </DataTrigger>
                <DataTrigger Binding="{Binding QualityTone}" Value="Good">
                    <Setter TargetName="QualityBadge" Property="Background" Value="#F3F6F9"/>
                    <Setter TargetName="QualityBadge" Property="BorderBrush" Value="#D5DEE8"/>
                    <Setter TargetName="QualityText" Property="Foreground" Value="#52677E"/>
                </DataTrigger>
            </DataTemplate.Triggers>
        </DataTemplate>

        <DataTemplate x:Key="DiagnosticLevelBadgeTemplate">
            <Border x:Name="LevelBadge" CornerRadius="8" Padding="7,2" MinWidth="58"
                    HorizontalAlignment="Left" Background="#EEF4FF" BorderBrush="#C9D9F1" BorderThickness="1">
                <TextBlock x:Name="LevelText" Text="{Binding Level}" FontSize="10.2" FontWeight="Bold" Foreground="#45648E"/>
            </Border>
            <DataTemplate.Triggers>
                <DataTrigger Binding="{Binding Level}" Value="WARN">
                    <Setter TargetName="LevelBadge" Property="Background" Value="#FFF8E6"/>
                    <Setter TargetName="LevelBadge" Property="BorderBrush" Value="#F2D28A"/>
                    <Setter TargetName="LevelText" Property="Foreground" Value="#946200"/>
                </DataTrigger>
                <DataTrigger Binding="{Binding Level}" Value="ERROR">
                    <Setter TargetName="LevelBadge" Property="Background" Value="#FFF1F2"/>
                    <Setter TargetName="LevelBadge" Property="BorderBrush" Value="#FDA29B"/>
                    <Setter TargetName="LevelText" Property="Foreground" Value="#B42318"/>
                </DataTrigger>
            </DataTemplate.Triggers>
        </DataTemplate>
'''
    main = replace_once(main, '    </Window.Resources>', resources + '    </Window.Resources>', 'window presentation templates')

# --- Event Log: keep process state separate; contain attention inside Quality. ---
event_block = r'''<!-- EVENT LOG -->
            <TabItem Header="Event Log">
                <Border Style="{StaticResource WorkspaceCard}" Padding="12">
                    <DockPanel>
                        <Grid DockPanel.Dock="Top" Margin="0,0,0,10">
                            <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                            <StackPanel>
                                <TextBlock Text="IEC 61850 Sequence of Events" Style="{StaticResource WorkspaceTitle}"/>
                                <TextBlock Text="SOE edges • process state and signal quality are shown separately." Style="{StaticResource WorkspaceSubtitle}" Margin="0,2,0,0"/>
                            </StackPanel>
                            <WrapPanel Grid.Column="1">
                                <Button Content="Export CSV" Style="{StaticResource SoftButton}" Click="ExportEvents_Click" Margin="0,0,8,0"/>
                                <Button Content="Clear" Style="{StaticResource SoftButton}" Click="ClearEvents_Click"/>
                            </WrapPanel>
                        </Grid>
                        <DataGrid ItemsSource="{Binding Events}" IsReadOnly="True" Style="{StaticResource ModernDataGrid}"
                                  FrozenColumnCount="4" EnableRowVirtualization="True" EnableColumnVirtualization="True"
                                  VirtualizingPanel.IsVirtualizing="True" VirtualizingPanel.VirtualizationMode="Recycling"
                                  ScrollViewer.CanContentScroll="True" ScrollViewer.HorizontalScrollBarVisibility="Auto">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="#" Binding="{Binding Sequence}" Width="65"/>
                                <DataGridTextColumn Header="IED Timestamp" Binding="{Binding DeviceTimestamp}" Width="185"/>
                                <DataGridTextColumn Header="IED" Binding="{Binding DeviceName}" Width="120"/>
                                <DataGridTextColumn Header="Signal" Binding="{Binding SignalName}" Width="190"/>
                                <DataGridTextColumn Header="IEC Telegram" Binding="{Binding IecTelegram}" Width="300"/>
                                <DataGridTemplateColumn Header="Value" Width="150" CellTemplate="{StaticResource ProcessValueBadgeTemplate}"/>
                                <DataGridTemplateColumn Header="Quality" Width="135" CellTemplate="{StaticResource EventQualityBadgeTemplate}"/>
                                <DataGridTextColumn Header="Acquisition" Binding="{Binding SourceMode}" Width="190"/>
                            </DataGrid.Columns>
                        </DataGrid>
                    </DockPanel>
                </Border>
            </TabItem>

            '''
main = replace_section(main, '<!-- EVENT LOG -->', '<!-- EVENT-LATCHED ALARM ANNUNCIATOR -->', event_block, 'event log section')

# --- Alarm: remove cryptic/tiny helper copy, preserve actual latched state colors. ---
alarm_start = main.find('<!-- EVENT-LATCHED ALARM ANNUNCIATOR -->')
alarm_end = main.find('<!-- SCL / DISCOVERY-AWARE GOOSE SUBSCRIBER -->', alarm_start)
if alarm_start < 0 or alarm_end < 0:
    raise SystemExit('alarm section markers missing')
alarm = main[alarm_start:alarm_end]
alarm = replace_once(
    alarm,
    'Text="FLASH = UNACK • STEADY = ACK • RTN = RETURNED"\n                                               Style="{StaticResource MicroLabel}" Foreground="#7A8797" Margin="0,2,0,0"',
    'Text="Unacknowledged flashes • acknowledged steady • returned awaits ACK"\n                                               Style="{StaticResource Caption}" Foreground="#667085" Margin="0,2,0,0"',
    'alarm legend')
alarm = replace_once(
    alarm,
    '<StackPanel>\n                                                    <TextBlock Text="IED ALARMS" FontSize="10" FontWeight="Bold" Foreground="#DCE3EC"/>\n                                                    <TextBlock Text="Select fascia" FontSize="9.2" Foreground="#8794A5" Margin="0,2,0,0"/>\n                                                </StackPanel>',
    '<StackPanel>\n                                                    <TextBlock Text="IEDs" FontSize="10.8" FontWeight="SemiBold" Foreground="#DCE3EC"/>\n                                                </StackPanel>',
    'alarm IED rail heading')
for old, new, label in [
    ('FontSize="9.1" Margin="0,2,0,0"', 'FontSize="10" Margin="0,2,0,0"', 'alarm device status size'),
    ('FontSize="9.8" Foreground="#9EABB9"', 'FontSize="10.4" Foreground="#9EABB9"', 'selected IED status size'),
    ('FontSize="8.2" FontWeight="Bold"', 'FontSize="9.2" FontWeight="Bold"', 'alarm state badge size'),
    ('FontSize="8.8" MinHeight="22"', 'FontSize="9.4" MinHeight="22"', 'alarm ACK size'),
]:
    alarm = replace_once(alarm, old, new, label)
main = main[:alarm_start] + alarm + main[alarm_end:]

# --- GOOSE: one primary idle surface, technical options collapsed until requested. ---
goose_start = main.find('<!-- SCL / DISCOVERY-AWARE GOOSE SUBSCRIBER -->')
goose_end = main.find('<!-- DIAGNOSTICS -->', goose_start)
if goose_start < 0 or goose_end < 0:
    raise SystemExit('GOOSE section markers missing')
goose = main[goose_start:goose_end]
goose = replace_once(
    goose,
    '<RowDefinition Height="0.9*" MinHeight="205"/>\n                        <RowDefinition Height="8"/>\n                        <RowDefinition Height="1.1*" MinHeight="230"/>',
    '<RowDefinition Height="*" MinHeight="260"/>\n                        <RowDefinition Height="8"/>\n                        <RowDefinition Height="Auto"/>',
    'GOOSE progressive rows')

top_start = goose.find('<Border Grid.Row="0" Style="{StaticResource WorkspaceCard}" Padding="10">')
top_end = goose.find('<Border Grid.Row="2" Style="{StaticResource WorkspaceCard}" Padding="9">', top_start)
if top_start < 0 or top_end < 0:
    raise SystemExit('GOOSE top/streams card markers missing')

goose_top = r'''<Border Grid.Row="0" Style="{StaticResource WorkspaceCard}" Padding="10">
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="6"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="4"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>

                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                    <ColumnDefinition Width="6"/>
                                    <ColumnDefinition Width="Auto"/>
                                    <ColumnDefinition Width="6"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                    <Ellipse Width="8" Height="8" Fill="#16A34A" Margin="0,0,8,0"/>
                                    <TextBlock Text="GOOSE Subscriber" Style="{StaticResource SectionTitle}" VerticalAlignment="Center"/>
                                    <Border Margin="8,0,0,0" Background="#EEF4FF" BorderBrush="#C9D9F1" BorderThickness="1" CornerRadius="8" Padding="6,2"
                                            ToolTip="Read-only IEC 61850-8-1 Ethernet capture">
                                        <TextBlock Text="READ ONLY" FontSize="8.7" FontWeight="Bold" Foreground="#45648E"/>
                                    </Border>
                                </StackPanel>

                                <Button Grid.Column="1" Style="{StaticResource CommandOpenButton}" Click="StartGooseSubscriber_Click"
                                        Visibility="{Binding GooseStartVisibility}" IsEnabled="{Binding CanStartGooseSubscriber}" Padding="13,6" MinHeight="34">
                                    <StackPanel Orientation="Horizontal">
                                        <Viewbox Width="13" Height="13" Margin="0,0,6,0"><Path Data="{StaticResource LucidePlay}" Style="{StaticResource LucideIcon}" Stroke="White"/></Viewbox>
                                        <TextBlock Text="Start" Foreground="White"/>
                                    </StackPanel>
                                </Button>
                                <Button Grid.Column="3" Style="{StaticResource SoftButton}" Click="StopGooseSubscriber_Click"
                                        Visibility="{Binding GooseStopVisibility}" IsEnabled="{Binding CanStopGooseSubscriber}" Padding="11,6" MinHeight="34">
                                    <StackPanel Orientation="Horizontal">
                                        <Viewbox Width="13" Height="13" Margin="0,0,6,0"><Path Data="{StaticResource LucideSquare}" Style="{StaticResource LucideIcon}" Stroke="{StaticResource Warning}"/></Viewbox>
                                        <TextBlock Text="Stop"/>
                                    </StackPanel>
                                </Button>
                                <Button Grid.Column="5" Style="{StaticResource SoftButton}" Click="ClearGooseSubscriber_Click"
                                        Visibility="{Binding GooseClearVisibility}" Padding="11,6" MinHeight="34" ToolTip="Clear captured streams">
                                    <StackPanel Orientation="Horizontal">
                                        <Viewbox Width="13" Height="13" Margin="0,0,6,0"><Path Data="{StaticResource LucideX}" Style="{StaticResource LucideIcon}" Stroke="{StaticResource Danger}"/></Viewbox>
                                        <TextBlock Text="Clear"/>
                                    </StackPanel>
                                </Button>
                            </Grid>

                            <Grid Grid.Row="2">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="330"/>
                                    <ColumnDefinition Width="4"/>
                                    <ColumnDefinition Width="Auto"/>
                                    <ColumnDefinition Width="12"/>
                                    <ColumnDefinition Width="Auto"/>
                                    <ColumnDefinition Width="10"/>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <ComboBox ItemsSource="{Binding GooseAdapters}" SelectedItem="{Binding SelectedGooseAdapter, Mode=TwoWay}"
                                          TextSearch.TextPath="DisplayText" Style="{StaticResource ModernComboBox}" MinHeight="34" Height="34" Padding="10,4"
                                          IsEnabled="{Binding CanRefreshGooseConfiguration}" ToolTip="{Binding SelectedGooseAdapterDetail}">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate><TextBlock Text="{Binding DisplayText}" TextTrimming="CharacterEllipsis"/></DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                                <Button Grid.Column="2" Style="{StaticResource IedIconButton}" Click="RefreshGooseAdapters_Click"
                                        IsEnabled="{Binding CanRefreshGooseConfiguration}" ToolTip="Refresh adapters">
                                    <Viewbox Width="15" Height="15"><Path Data="{StaticResource LucideRefreshCw}" Style="{StaticResource LucideIcon}"/></Viewbox>
                                </Button>

                                <Border Grid.Column="4" CornerRadius="9" Padding="8,3" BorderThickness="1">
                                    <Border.Style>
                                        <Style TargetType="Border">
                                            <Setter Property="Background" Value="#F2F4F7"/>
                                            <Setter Property="BorderBrush" Value="#D0D5DD"/>
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding IsGooseCapturing}" Value="True">
                                                    <Setter Property="Background" Value="#E8F8EF"/>
                                                    <Setter Property="BorderBrush" Value="#86D3A7"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <TextBlock Text="{Binding GooseCaptureStateText}" FontSize="9.8" FontWeight="Bold">
                                        <TextBlock.Style>
                                            <Style TargetType="TextBlock">
                                                <Setter Property="Foreground" Value="#667085"/>
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding IsGooseCapturing}" Value="True"><Setter Property="Foreground" Value="#067647"/></DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </TextBlock.Style>
                                    </TextBlock>
                                </Border>
                                <TextBlock Grid.Column="6" Text="{Binding GooseStatusText}" FontSize="10.7" Foreground="{StaticResource Muted}"
                                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis" ToolTip="{Binding GooseBindingText}"/>
                                <TextBlock Grid.Column="7" Text="{Binding GooseCounterText}" FontSize="10.4" FontWeight="SemiBold" Foreground="#45648E"
                                           VerticalAlignment="Center" HorizontalAlignment="Right"/>
                            </Grid>

                            <Expander x:Name="GooseCaptureOptionsExpander" Grid.Row="4" Header="Capture options" IsExpanded="False"
                                      Foreground="#52677E" FontSize="10.5" FontWeight="SemiBold">
                                <Grid Margin="0,6,0,0">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="82"/>
                                        <ColumnDefinition Width="330"/>
                                        <ColumnDefinition Width="6"/>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="12"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Text="BPF filter" Style="{StaticResource Caption}" VerticalAlignment="Center"/>
                                    <TextBox Grid.Column="1" Text="{Binding GooseCaptureFilter, UpdateSourceTrigger=PropertyChanged}"
                                             MinHeight="32" Height="32" Padding="9,4" FontFamily="Cascadia Mono, Consolas" FontSize="10.2"
                                             IsEnabled="{Binding CanRefreshGooseConfiguration}" ToolTip="GOOSE BPF capture filter"/>
                                    <Button Grid.Column="3" Style="{StaticResource IedIconButton}" Click="RefreshGooseModels_Click"
                                            IsEnabled="{Binding CanRefreshGooseConfiguration}" ToolTip="Refresh SCL/live model bindings">
                                        <Viewbox Width="15" Height="15"><Path Data="{StaticResource LucideSlidersHorizontal}" Style="{StaticResource LucideIcon}"/></Viewbox>
                                    </Button>
                                    <TextBlock Grid.Column="5" Text="{Binding GooseBindingText}" Style="{StaticResource Caption}"
                                               VerticalAlignment="Center" TextTrimming="CharacterEllipsis" ToolTip="{Binding GooseBindingText}"/>
                                </Grid>
                            </Expander>
                        </Grid>
                    </Border>

                    '''
goose = goose[:top_start] + goose_top + goose[top_end:]
goose = replace_once(
    goose,
    '<Border Grid.Row="4" Style="{StaticResource WorkspaceCard}" Padding="9">',
    '<Border Grid.Row="4" Style="{StaticResource WorkspaceCard}" Padding="9" Height="280"\n                            Visibility="{Binding GooseDataSetInspectorVisibility}">',
    'GOOSE DataSet progressive card')
main = main[:goose_start] + goose + main[goose_end:]

# --- Diagnostics: level badge + left rail only; never flood every cell with severity fill. ---
diag_block = r'''<!-- DIAGNOSTICS -->
            <TabItem Header="Diagnostics">
                <Border Style="{StaticResource WorkspaceCard}" Padding="12">
                    <DockPanel>
                        <Grid DockPanel.Dock="Top" Margin="0,0,0,10">
                            <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                            <StackPanel>
                                <TextBlock Text="Diagnostics &amp; Communication Journal" Style="{StaticResource WorkspaceTitle}"/>
                                <TextBlock Text="Protocol and connection journal • WARN and ERROR are called out without tinting the whole row." Style="{StaticResource WorkspaceSubtitle}" Margin="0,2,0,0"/>
                            </StackPanel>
                            <StackPanel Grid.Column="1" Orientation="Horizontal">
                                <Button x:Name="CopyDiagnosticButton" Content="Copy Diagnostic" Style="{StaticResource PrimaryButton}"
                                        Margin="0,0,8,0" ToolTip="Copy app, engine, network, IED-session, TCP-probe, and recent journal details for support analysis"
                                        Click="CopyDiagnostics_Click"/>
                                <Button Content="Clear Log" Style="{StaticResource SoftButton}" Click="ClearDiagnostics_Click"/>
                            </StackPanel>
                        </Grid>
                        <DataGrid ItemsSource="{Binding Logs}" IsReadOnly="True" Style="{StaticResource ModernDataGrid}"
                                  EnableRowVirtualization="True" EnableColumnVirtualization="True"
                                  VirtualizingPanel.IsVirtualizing="True" VirtualizingPanel.VirtualizationMode="Recycling"
                                  ScrollViewer.CanContentScroll="True">
                            <DataGrid.RowStyle>
                                <Style TargetType="DataGridRow" BasedOn="{StaticResource {x:Type DataGridRow}}">
                                    <Setter Property="BorderBrush" Value="Transparent"/>
                                    <Setter Property="BorderThickness" Value="0"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding Level}" Value="ERROR">
                                            <Setter Property="BorderBrush" Value="#EF4444"/>
                                            <Setter Property="BorderThickness" Value="3,0,0,0"/>
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding Level}" Value="WARN">
                                            <Setter Property="BorderBrush" Value="#F59E0B"/>
                                            <Setter Property="BorderThickness" Value="3,0,0,0"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </DataGrid.RowStyle>
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="Time" Binding="{Binding Time, StringFormat=yyyy-MM-dd HH:mm:ss.fff}" Width="190"/>
                                <DataGridTemplateColumn Header="Level" Width="90" CellTemplate="{StaticResource DiagnosticLevelBadgeTemplate}"/>
                                <DataGridTextColumn Header="Source" Binding="{Binding Source}" Width="145"/>
                                <DataGridTextColumn Header="Message" Binding="{Binding Message}" Width="*"/>
                            </DataGrid.Columns>
                        </DataGrid>
                    </DockPanel>
                </Border>
            </TabItem>
        '''
main = replace_section(main, '<!-- DIAGNOSTICS -->', '        </TabControl>', diag_block, 'diagnostics section')

# --- Event entry exposes presentation-only quality tone. ---
models = replace_once(
    models,
    'public string ValueTone => Iec61850ValueStatePresentation.Classify(EventValue, IecDataType);',
    'public string ValueTone => Iec61850ValueStatePresentation.Classify(EventValue, IecDataType);\n    public string QualityTone => Iec61850QualityPresentation.Classify(Quality);',
    'event quality tone')

# --- GOOSE presentation visibilities; no capture/runtime semantics changed. ---
goose_code = replace_once(
    goose_code,
    'Raise(nameof(GooseSelectedStreamText));\n            Raise(nameof(GooseNoLeafValuesVisibility));',
    'Raise(nameof(GooseSelectedStreamText));\n            Raise(nameof(GooseNoLeafValuesVisibility));\n            Raise(nameof(GooseDataSetInspectorVisibility));',
    'GOOSE selected stream visibility raise')
goose_code = replace_once(
    goose_code,
    'Raise(nameof(CanRefreshGooseConfiguration));\n            Raise(nameof(GooseCaptureStateText));',
    'Raise(nameof(CanRefreshGooseConfiguration));\n            Raise(nameof(GooseCaptureStateText));\n            Raise(nameof(GooseStartVisibility));\n            Raise(nameof(GooseStopVisibility));',
    'GOOSE capture action visibility raise')
goose_code = replace_once(
    goose_code,
    'public Visibility GooseNoStreamsVisibility => GooseStreams.Count == 0 ? Visibility.Visible : Visibility.Collapsed;\n    public Visibility GooseNoLeafValuesVisibility => SelectedGooseStream?.Leaves.Count > 0 ? Visibility.Collapsed : Visibility.Visible;',
    'public Visibility GooseNoStreamsVisibility => GooseStreams.Count == 0 ? Visibility.Visible : Visibility.Collapsed;\n    public Visibility GooseNoLeafValuesVisibility => SelectedGooseStream?.Leaves.Count > 0 ? Visibility.Collapsed : Visibility.Visible;\n    public Visibility GooseDataSetInspectorVisibility => SelectedGooseStream is null ? Visibility.Collapsed : Visibility.Visible;\n    public Visibility GooseStartVisibility => IsGooseCapturing ? Visibility.Collapsed : Visibility.Visible;\n    public Visibility GooseStopVisibility => IsGooseCapturing ? Visibility.Visible : Visibility.Collapsed;\n    public Visibility GooseClearVisibility => GooseStreams.Count > 0 ? Visibility.Visible : Visibility.Collapsed;',
    'GOOSE progressive visibility properties')
goose_code = replace_once(
    goose_code,
    'Raise(nameof(GooseNoStreamsVisibility));\n        Raise(nameof(GooseNoLeafValuesVisibility));\n        Raise(nameof(GooseCounterText));',
    'Raise(nameof(GooseNoStreamsVisibility));\n        Raise(nameof(GooseNoLeafValuesVisibility));\n        Raise(nameof(GooseDataSetInspectorVisibility));\n        Raise(nameof(GooseClearVisibility));\n        Raise(nameof(GooseCounterText));',
    'GOOSE reset visibility raises')
goose_code = replace_once(
    goose_code,
    'Raise(nameof(GooseCounterText));\n        Raise(nameof(GooseNoStreamsVisibility));\n        Raise(nameof(GooseNoLeafValuesVisibility));\n        Raise(nameof(GooseSelectedStreamText));',
    'Raise(nameof(GooseCounterText));\n        Raise(nameof(GooseNoStreamsVisibility));\n        Raise(nameof(GooseNoLeafValuesVisibility));\n        Raise(nameof(GooseDataSetInspectorVisibility));\n        Raise(nameof(GooseClearVisibility));\n        Raise(nameof(GooseSelectedStreamText));',
    'GOOSE flush visibility raises')

MAIN.write_text(main, encoding='utf-8', newline='\n')
GOOSE.write_text(goose_code, encoding='utf-8', newline='\n')
MODELS.write_text(models, encoding='utf-8', newline='\n')
print('Applied P1 workspace noise cleanup to presentation source.')
