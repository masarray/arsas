from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one anchor, found {count}")
    return text.replace(old, new, 1)


main_path = Path("MainWindow.xaml")
main = main_path.read_text(encoding="utf-8")

main = replace_once(
    main,
    '<Button x:Name="NavAlarmButton" Grid.Column="3" Content="Alarm Annunciator" Tag="3" Click="NavButton_Click" Style="{StaticResource SegmentedNavButton}"/>',
    '<Button x:Name="NavAlarmButton" Grid.Column="3" Content="Alarm" Tag="3" Click="NavButton_Click" Style="{StaticResource SegmentedNavButton}"/>',
    "compact Alarm navbar label",
)

start_marker = "            <!-- EVENT-LATCHED ALARM ANNUNCIATOR -->"
end_marker = "            <!-- SCL / DISCOVERY-AWARE GOOSE SUBSCRIBER -->"
start = main.find(start_marker)
end = main.find(end_marker)
if start < 0 or end < 0 or end <= start:
    raise SystemExit("annunciator XAML markers were not found in the expected order")

new_block = r'''            <!-- EVENT-LATCHED ALARM ANNUNCIATOR -->
            <TabItem Header="Alarm Annunciator">
                <Border Style="{StaticResource Card}" Padding="12">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="10"/>
                            <RowDefinition Height="*"/>
                        </Grid.RowDefinitions>

                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                <Border Width="32" Height="32" CornerRadius="10" Background="#FFF0F0" BorderBrush="#F5B8B8" BorderThickness="1" Margin="0,0,10,0">
                                    <Ellipse Width="12" Height="12" Fill="#DC2626"
                                             Opacity="{Binding AnnunciatorBeaconOpacity}"
                                             HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
                                <StackPanel>
                                    <TextBlock Text="Alarm Annunciator" FontSize="16" FontWeight="SemiBold" Foreground="{StaticResource Ink}"/>
                                    <TextBlock Text="FLASH = UNACK   •   STEADY = ACK   •   RTN = RETURNED"
                                               FontSize="9.8" FontWeight="SemiBold" Foreground="#7A8797" Margin="0,2,0,0"/>
                                </StackPanel>
                            </StackPanel>
                            <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
                                <Border Background="#F8FAFC" BorderBrush="{StaticResource Line}" BorderThickness="1" CornerRadius="11" Padding="10,6" Margin="0,0,8,0">
                                    <TextBlock Text="{Binding AnnunciatorSummaryText}" FontSize="10.8" FontWeight="SemiBold" Foreground="#475467"/>
                                </Border>
                                <Button Content="ACK ALL" Style="{StaticResource PrimaryButton}" Padding="13,7"
                                        Click="AcknowledgeAllAlarms_Click"
                                        ToolTip="Acknowledge every unacknowledged alarm. Active conditions remain indicated until they return to normal."/>
                            </StackPanel>
                        </Grid>

                        <!-- Modern annunciator faceplate: fixed windows and independent lamps keep
                             the primary scan path identical to a conventional substation panel. -->
                        <Border Grid.Row="2" Background="#292E35" BorderBrush="#1E242B" BorderThickness="1"
                                CornerRadius="16" Padding="12">
                            <Grid>
                                <ScrollViewer Visibility="{Binding AnnunciatorContentVisibility}"
                                              VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                                    <ItemsControl ItemsSource="{Binding AnnunciatorAlarms}">
                                        <ItemsControl.ItemsPanel>
                                            <ItemsPanelTemplate>
                                                <WrapPanel Orientation="Horizontal"/>
                                            </ItemsPanelTemplate>
                                        </ItemsControl.ItemsPanel>
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate>
                                                <Grid Width="278" Height="116" Margin="5">
                                                    <Grid.ColumnDefinitions>
                                                        <ColumnDefinition Width="28"/>
                                                        <ColumnDefinition Width="*"/>
                                                    </Grid.ColumnDefinitions>

                                                    <!-- Dedicated annunciator lamp. Only this element flashes;
                                                         caption/value remain readable continuously. -->
                                                    <Grid Grid.Column="0" VerticalAlignment="Stretch">
                                                        <Ellipse Width="17" Height="17" VerticalAlignment="Top" Margin="0,15,0,0"
                                                                 Stroke="#F8FAFC" StrokeThickness="1.4" Opacity="{Binding LampOpacity}">
                                                            <Ellipse.Style>
                                                                <Style TargetType="Ellipse">
                                                                    <Setter Property="Fill" Value="#68727E"/>
                                                                    <Style.Triggers>
                                                                        <DataTrigger Binding="{Binding VisualState}" Value="ActiveUnacknowledged"><Setter Property="Fill" Value="#EF233C"/></DataTrigger>
                                                                        <DataTrigger Binding="{Binding VisualState}" Value="ActiveAcknowledged"><Setter Property="Fill" Value="#F97316"/></DataTrigger>
                                                                        <DataTrigger Binding="{Binding VisualState}" Value="ReturnedUnacknowledged"><Setter Property="Fill" Value="#F6C344"/></DataTrigger>
                                                                    </Style.Triggers>
                                                                </Style>
                                                            </Ellipse.Style>
                                                        </Ellipse>
                                                    </Grid>

                                                    <!-- Alarm window: signal + current value dominate. Engineering
                                                         metadata remains available in the tooltip instead of the scan path. -->
                                                    <Border Grid.Column="1" CornerRadius="10" Padding="12,9" BorderThickness="1.5">
                                                        <Border.Style>
                                                            <Style TargetType="Border">
                                                                <Setter Property="Background" Value="#F8FAFC"/>
                                                                <Setter Property="BorderBrush" Value="#C9D1DC"/>
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding="{Binding VisualState}" Value="ActiveUnacknowledged">
                                                                        <Setter Property="Background" Value="#FFE8EA"/>
                                                                        <Setter Property="BorderBrush" Value="#EF233C"/>
                                                                        <Setter Property="BorderThickness" Value="2.2"/>
                                                                    </DataTrigger>
                                                                    <DataTrigger Binding="{Binding VisualState}" Value="ActiveAcknowledged">
                                                                        <Setter Property="Background" Value="#FFF0DE"/>
                                                                        <Setter Property="BorderBrush" Value="#F97316"/>
                                                                        <Setter Property="BorderThickness" Value="2"/>
                                                                    </DataTrigger>
                                                                    <DataTrigger Binding="{Binding VisualState}" Value="ReturnedUnacknowledged">
                                                                        <Setter Property="Background" Value="#FFF6CF"/>
                                                                        <Setter Property="BorderBrush" Value="#E6A700"/>
                                                                        <Setter Property="BorderThickness" Value="2"/>
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </Border.Style>
                                                        <Border.ToolTip>
                                                            <StackPanel MaxWidth="430">
                                                                <TextBlock Text="{Binding SignalName}" FontWeight="Bold" Margin="0,0,0,5"/>
                                                                <TextBlock Text="{Binding DeviceName, StringFormat=IED: {0}}"/>
                                                                <TextBlock Text="{Binding IecReference, StringFormat=IEC: {0}}" TextWrapping="Wrap"/>
                                                                <TextBlock Text="{Binding LastEventTimestamp, StringFormat=Last SOE: {0}}"/>
                                                                <TextBlock Text="{Binding Quality, StringFormat=Quality: {0}}"/>
                                                                <TextBlock Text="{Binding ActivationCountText}"/>
                                                            </StackPanel>
                                                        </Border.ToolTip>
                                                        <Grid>
                                                            <Grid.RowDefinitions>
                                                                <RowDefinition Height="Auto"/>
                                                                <RowDefinition Height="*"/>
                                                                <RowDefinition Height="Auto"/>
                                                            </Grid.RowDefinitions>

                                                            <Grid>
                                                                <Grid.ColumnDefinitions>
                                                                    <ColumnDefinition Width="*"/>
                                                                    <ColumnDefinition Width="Auto"/>
                                                                </Grid.ColumnDefinitions>
                                                                <TextBlock Text="{Binding SignalName}" FontSize="13.6" FontWeight="Bold"
                                                                           Foreground="#1F2937" TextTrimming="CharacterEllipsis"/>
                                                                <Border Grid.Column="1" CornerRadius="7" Padding="6,2" Margin="8,0,0,0"
                                                                        Background="#E9EEF4" BorderBrush="#CCD5E0" BorderThickness="1">
                                                                    <TextBlock Text="{Binding StateText}" FontSize="8.8" FontWeight="Bold" Foreground="#445164"/>
                                                                </Border>
                                                            </Grid>

                                                            <StackPanel Grid.Row="1" VerticalAlignment="Center" Margin="0,4,0,3">
                                                                <TextBlock Text="VALUE" FontSize="8.7" FontWeight="Bold" Foreground="#7A8797"/>
                                                                <TextBlock Text="{Binding CurrentValue}" FontSize="24" FontWeight="Bold" Foreground="#111827"
                                                                           TextTrimming="CharacterEllipsis" ToolTip="{Binding CurrentValue}"/>
                                                            </StackPanel>

                                                            <Grid Grid.Row="2">
                                                                <Grid.ColumnDefinitions>
                                                                    <ColumnDefinition Width="*"/>
                                                                    <ColumnDefinition Width="Auto"/>
                                                                </Grid.ColumnDefinitions>
                                                                <TextBlock Text="{Binding DeviceName}" FontSize="9.4" FontWeight="SemiBold" Foreground="#6B7788"
                                                                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
                                                                <Button Grid.Column="1" Content="ACK" Tag="{Binding}" Click="AcknowledgeAlarm_Click"
                                                                        Padding="8,3" Margin="8,0,0,0" FontSize="9.5">
                                                                    <Button.Style>
                                                                        <Style TargetType="Button" BasedOn="{StaticResource SoftButton}">
                                                                            <Setter Property="Visibility" Value="Collapsed"/>
                                                                            <Setter Property="MinHeight" Value="0"/>
                                                                            <Style.Triggers>
                                                                                <DataTrigger Binding="{Binding CanAcknowledge}" Value="True">
                                                                                    <Setter Property="Visibility" Value="Visible"/>
                                                                                </DataTrigger>
                                                                            </Style.Triggers>
                                                                        </Style>
                                                                    </Button.Style>
                                                                </Button>
                                                            </Grid>
                                                        </Grid>
                                                    </Border>
                                                </Grid>
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                </ScrollViewer>

                                <Border Visibility="{Binding AnnunciatorEmptyVisibility}" Background="#343A43" BorderBrush="#4A535F"
                                        BorderThickness="1" CornerRadius="14" Padding="34"
                                        HorizontalAlignment="Center" VerticalAlignment="Center" MaxWidth="620">
                                    <StackPanel>
                                        <Ellipse Width="14" Height="14" Fill="#68727E" Stroke="#EEF2F6" StrokeThickness="1.5" HorizontalAlignment="Center"/>
                                        <TextBlock Text="No annunciator windows configured" FontSize="17" FontWeight="SemiBold" Foreground="#F8FAFC"
                                                   HorizontalAlignment="Center" Margin="0,12,0,0"/>
                                        <TextBlock Text="Select Alarm in IEC 61850 Explorer for the ST points that must latch on SOE events."
                                                   TextWrapping="Wrap" TextAlignment="Center" FontSize="11.5" Foreground="#B8C2CF" Margin="0,7,0,0"/>
                                    </StackPanel>
                                </Border>
                            </Grid>
                        </Border>
                    </Grid>
                </Border>
            </TabItem>

'''

main = main[:start] + new_block + main[end:]
main_path.write_text(main, encoding="utf-8")

nav_path = Path("MainWindow.NavigationLayoutFix.cs")
nav = nav_path.read_text(encoding="utf-8")
nav = replace_once(nav, '        "Alarm Annunciator",', '        "Alarm",', "responsive Alarm label")
nav_path.write_text(nav, encoding="utf-8")

controller_path = Path("MainWindow.AlarmAnnunciator.cs")
controller = controller_path.read_text(encoding="utf-8")
controller = replace_once(
    controller,
    '    public string AnnunciatorSummaryText => $"{AnnunciatorConfiguredCount} configured • {AnnunciatorActiveCount} active • {AnnunciatorUnacknowledgedCount} unacknowledged";',
    '    public string AnnunciatorSummaryText => $"{AnnunciatorActiveCount} ACTIVE • {AnnunciatorUnacknowledgedCount} UNACK • {AnnunciatorConfiguredCount} WINDOWS";',
    "compact annunciator summary",
)
controller_path.write_text(controller, encoding="utf-8")

print("Alarm annunciator visual hierarchy patch applied.")
