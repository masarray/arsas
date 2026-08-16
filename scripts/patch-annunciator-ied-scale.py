from pathlib import Path
import re

path = Path("MainWindow.xaml")
text = path.read_text(encoding="utf-8")

start_marker = "            <!-- EVENT-LATCHED ALARM ANNUNCIATOR -->"
end_marker = "            <!-- SCL / DISCOVERY-AWARE GOOSE SUBSCRIBER -->"
pattern = re.compile(re.escape(start_marker) + r".*?" + re.escape(end_marker), re.S)

replacement = r'''            <!-- EVENT-LATCHED ALARM ANNUNCIATOR -->
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
                                    <TextBlock Text="IED FASCIA • FLASH = UNACK • STEADY = ACK • RTN = RETURNED"
                                               FontSize="9.8" FontWeight="SemiBold" Foreground="#7A8797" Margin="0,2,0,0"/>
                                </StackPanel>
                            </StackPanel>
                            <Border Grid.Column="1" Background="#F8FAFC" BorderBrush="{StaticResource Line}" BorderThickness="1" CornerRadius="11" Padding="10,6">
                                <TextBlock Text="{Binding AnnunciatorSummaryText}" FontSize="10.8" FontWeight="SemiBold" Foreground="#475467"/>
                            </Border>
                        </Grid>

                        <!-- IED-first annunciator: the rail scales to hundreds of configured
                             devices while only the selected IED's fascia windows are rendered. -->
                        <Border Grid.Row="2" Background="#292E35" BorderBrush="#1E242B" BorderThickness="1"
                                CornerRadius="16" Padding="10">
                            <Grid>
                                <Border Visibility="{Binding AnnunciatorEmptyVisibility}" Background="#343A43" BorderBrush="#4A535F"
                                        BorderThickness="1" CornerRadius="14" Padding="34"
                                        HorizontalAlignment="Center" VerticalAlignment="Center" MaxWidth="620">
                                    <StackPanel>
                                        <Ellipse Width="16" Height="16" Fill="#68727E" Stroke="#EEF2F6" StrokeThickness="1.5" HorizontalAlignment="Center"/>
                                        <TextBlock Text="No annunciator IED configured" FontSize="17" FontWeight="SemiBold" Foreground="#F8FAFC"
                                                   HorizontalAlignment="Center" Margin="0,12,0,0"/>
                                        <TextBlock Text="Select Alarm in IEC 61850 Explorer for the ST points that must latch on SOE events."
                                                   TextWrapping="Wrap" TextAlignment="Center" FontSize="11.5" Foreground="#B8C2CF" Margin="0,7,0,0"/>
                                    </StackPanel>
                                </Border>

                                <Grid Visibility="{Binding AnnunciatorContentVisibility}">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="222"/>
                                        <ColumnDefinition Width="10"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>

                                    <!-- Stable IED rail. Do not auto-sort on alarm state: preserving
                                         position lets an operator build spatial memory across a large SAS. -->
                                    <Border Grid.Column="0" Background="#23282F" BorderBrush="#414A55" BorderThickness="1" CornerRadius="12" Padding="7">
                                        <Grid>
                                            <Grid.RowDefinitions>
                                                <RowDefinition Height="Auto"/>
                                                <RowDefinition Height="7"/>
                                                <RowDefinition Height="*"/>
                                            </Grid.RowDefinitions>
                                            <Grid>
                                                <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                                                <StackPanel>
                                                    <TextBlock Text="IED ALARMS" FontSize="10" FontWeight="Bold" Foreground="#DCE3EC"/>
                                                    <TextBlock Text="Select fascia" FontSize="9.2" Foreground="#8794A5" Margin="0,2,0,0"/>
                                                </StackPanel>
                                                <TextBlock Grid.Column="1" Text="{Binding AnnunciatorDeviceCount}" FontSize="11" FontWeight="Bold" Foreground="#AFC2DA" VerticalAlignment="Center"/>
                                            </Grid>

                                            <ListBox Grid.Row="2" ItemsSource="{Binding AnnunciatorDevices}"
                                                     SelectedItem="{Binding SelectedAnnunciatorDevice, Mode=TwoWay}"
                                                     Background="Transparent" BorderThickness="0" Padding="0"
                                                     HorizontalContentAlignment="Stretch"
                                                     ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                                                     ScrollViewer.VerticalScrollBarVisibility="Auto"
                                                     ScrollViewer.CanContentScroll="True"
                                                     VirtualizingPanel.IsVirtualizing="True"
                                                     VirtualizingPanel.VirtualizationMode="Recycling">
                                                <ListBox.ItemContainerStyle>
                                                    <Style TargetType="ListBoxItem">
                                                        <Setter Property="Padding" Value="0"/>
                                                        <Setter Property="Margin" Value="0,0,0,5"/>
                                                        <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
                                                        <Setter Property="Background" Value="Transparent"/>
                                                        <Setter Property="BorderBrush" Value="Transparent"/>
                                                        <Setter Property="BorderThickness" Value="1.5"/>
                                                        <Setter Property="Template">
                                                            <Setter.Value>
                                                                <ControlTemplate TargetType="ListBoxItem">
                                                                    <Border x:Name="IedAlarmRailItem" Background="{TemplateBinding Background}"
                                                                            BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                                                                            CornerRadius="9" Padding="2">
                                                                        <ContentPresenter/>
                                                                    </Border>
                                                                    <ControlTemplate.Triggers>
                                                                        <Trigger Property="IsMouseOver" Value="True">
                                                                            <Setter TargetName="IedAlarmRailItem" Property="Background" Value="#303843"/>
                                                                            <Setter TargetName="IedAlarmRailItem" Property="BorderBrush" Value="#596675"/>
                                                                        </Trigger>
                                                                        <Trigger Property="IsSelected" Value="True">
                                                                            <Setter TargetName="IedAlarmRailItem" Property="Background" Value="#2D3948"/>
                                                                            <Setter TargetName="IedAlarmRailItem" Property="BorderBrush" Value="#6EA8FF"/>
                                                                        </Trigger>
                                                                    </ControlTemplate.Triggers>
                                                                </ControlTemplate>
                                                            </Setter.Value>
                                                        </Setter>
                                                    </Style>
                                                </ListBox.ItemContainerStyle>
                                                <ListBox.ItemTemplate>
                                                    <DataTemplate>
                                                        <Border Height="54" CornerRadius="7" Padding="7,5" BorderThickness="1">
                                                            <Border.Style>
                                                                <Style TargetType="Border">
                                                                    <Setter Property="Background" Value="#2D333B"/>
                                                                    <Setter Property="BorderBrush" Value="#414A55"/>
                                                                    <Style.Triggers>
                                                                        <DataTrigger Binding="{Binding VisualState}" Value="Unacknowledged">
                                                                            <Setter Property="Background" Value="#3B292E"/>
                                                                            <Setter Property="BorderBrush" Value="#8D4650"/>
                                                                        </DataTrigger>
                                                                        <DataTrigger Binding="{Binding VisualState}" Value="Active">
                                                                            <Setter Property="Background" Value="#3A3129"/>
                                                                            <Setter Property="BorderBrush" Value="#84603C"/>
                                                                        </DataTrigger>
                                                                    </Style.Triggers>
                                                                </Style>
                                                            </Border.Style>
                                                            <Grid>
                                                                <Grid.ColumnDefinitions><ColumnDefinition Width="28"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                                                                <Ellipse Width="19" Height="19" Stroke="#F8FAFC" StrokeThickness="1.3"
                                                                         HorizontalAlignment="Left" VerticalAlignment="Center"
                                                                         Opacity="{Binding LampOpacity}">
                                                                    <Ellipse.Style>
                                                                        <Style TargetType="Ellipse">
                                                                            <Setter Property="Fill" Value="#66717D"/>
                                                                            <Style.Triggers>
                                                                                <DataTrigger Binding="{Binding VisualState}" Value="Unacknowledged"><Setter Property="Fill" Value="#EF233C"/></DataTrigger>
                                                                                <DataTrigger Binding="{Binding VisualState}" Value="Active"><Setter Property="Fill" Value="#F97316"/></DataTrigger>
                                                                            </Style.Triggers>
                                                                        </Style>
                                                                    </Ellipse.Style>
                                                                </Ellipse>
                                                                <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                                                    <TextBlock Text="{Binding DeviceName}" FontSize="11.8" FontWeight="SemiBold" Foreground="#F3F6FA"
                                                                               TextTrimming="CharacterEllipsis"/>
                                                                    <TextBlock Text="{Binding StatusText}" FontSize="9.1" Margin="0,2,0,0" TextTrimming="CharacterEllipsis">
                                                                        <TextBlock.Style>
                                                                            <Style TargetType="TextBlock">
                                                                                <Setter Property="Foreground" Value="#8E9BAA"/>
                                                                                <Style.Triggers>
                                                                                    <DataTrigger Binding="{Binding VisualState}" Value="Unacknowledged"><Setter Property="Foreground" Value="#FF9AA5"/></DataTrigger>
                                                                                    <DataTrigger Binding="{Binding VisualState}" Value="Active"><Setter Property="Foreground" Value="#FFB47B"/></DataTrigger>
                                                                                </Style.Triggers>
                                                                            </Style>
                                                                        </TextBlock.Style>
                                                                    </TextBlock>
                                                                </StackPanel>
                                                            </Grid>
                                                        </Border>
                                                    </DataTemplate>
                                                </ListBox.ItemTemplate>
                                            </ListBox>
                                        </Grid>
                                    </Border>

                                    <!-- Selected IED fascia. Alarm windows fill top-to-bottom first,
                                         then wrap into the next column to match relay LED-list reading. -->
                                    <Grid Grid.Column="2">
                                        <Grid.RowDefinitions>
                                            <RowDefinition Height="Auto"/>
                                            <RowDefinition Height="7"/>
                                            <RowDefinition Height="*"/>
                                        </Grid.RowDefinitions>

                                        <Border Background="#323942" BorderBrush="#46515E" BorderThickness="1" CornerRadius="10" Padding="10,7">
                                            <Grid>
                                                <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                                                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                                    <TextBlock Text="{Binding SelectedAnnunciatorDevice.DeviceName}" FontSize="14.5" FontWeight="Bold" Foreground="#F8FAFC"/>
                                                    <TextBlock Text="{Binding SelectedAnnunciatorDevice.StatusText}" FontSize="9.8" Foreground="#9EABB9" Margin="10,1,0,0" VerticalAlignment="Center"/>
                                                </StackPanel>
                                                <Button Grid.Column="1" Content="ACK IED" Click="AcknowledgeAllAlarms_Click"
                                                        Style="{StaticResource PrimaryButton}" Padding="11,5" FontSize="10"
                                                        IsEnabled="{Binding SelectedAnnunciatorDevice.HasUnacknowledged}"
                                                        ToolTip="Acknowledge all unacknowledged alarm occurrences for the selected IED only."/>
                                            </Grid>
                                        </Border>

                                        <ScrollViewer x:Name="AnnunciatorAlarmScroller" Grid.Row="2"
                                                      HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled"
                                                      PanningMode="HorizontalOnly" CanContentScroll="False">
                                            <ItemsControl ItemsSource="{Binding SelectedAnnunciatorDevice.Alarms}"
                                                          Height="{Binding ViewportHeight, ElementName=AnnunciatorAlarmScroller}">
                                                <ItemsControl.ItemsPanel>
                                                    <ItemsPanelTemplate>
                                                        <WrapPanel Orientation="Vertical"/>
                                                    </ItemsPanelTemplate>
                                                </ItemsControl.ItemsPanel>
                                                <ItemsControl.ItemTemplate>
                                                    <DataTemplate>
                                                        <Grid Width="250" Height="64" Margin="4,3">
                                                            <Grid.ColumnDefinitions>
                                                                <ColumnDefinition Width="34"/>
                                                                <ColumnDefinition Width="*"/>
                                                            </Grid.ColumnDefinitions>

                                                            <Ellipse Width="22" Height="22" HorizontalAlignment="Left" VerticalAlignment="Center"
                                                                     Stroke="#F8FAFC" StrokeThickness="1.5" Opacity="{Binding LampOpacity}">
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

                                                            <Border Grid.Column="1" CornerRadius="7" Padding="9,5" BorderThickness="1.4">
                                                                <Border.Style>
                                                                    <Style TargetType="Border">
                                                                        <Setter Property="Background" Value="#F7F9FC"/>
                                                                        <Setter Property="BorderBrush" Value="#C7D0DB"/>
                                                                        <Style.Triggers>
                                                                            <DataTrigger Binding="{Binding VisualState}" Value="ActiveUnacknowledged">
                                                                                <Setter Property="Background" Value="#FFE8EA"/>
                                                                                <Setter Property="BorderBrush" Value="#EF233C"/>
                                                                                <Setter Property="BorderThickness" Value="2"/>
                                                                            </DataTrigger>
                                                                            <DataTrigger Binding="{Binding VisualState}" Value="ActiveAcknowledged">
                                                                                <Setter Property="Background" Value="#FFF0DE"/>
                                                                                <Setter Property="BorderBrush" Value="#F97316"/>
                                                                                <Setter Property="BorderThickness" Value="1.8"/>
                                                                            </DataTrigger>
                                                                            <DataTrigger Binding="{Binding VisualState}" Value="ReturnedUnacknowledged">
                                                                                <Setter Property="Background" Value="#FFF6CF"/>
                                                                                <Setter Property="BorderBrush" Value="#E6A700"/>
                                                                                <Setter Property="BorderThickness" Value="1.8"/>
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
                                                                    <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
                                                                    <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                                                                    <TextBlock Text="{Binding SignalName}" FontSize="11.6" FontWeight="SemiBold" Foreground="#26313F"
                                                                               TextTrimming="CharacterEllipsis" Margin="0,0,5,0"/>
                                                                    <Border Grid.Column="1" CornerRadius="6" Padding="5,1" BorderThickness="1">
                                                                        <Border.Style>
                                                                            <Style TargetType="Border">
                                                                                <Setter Property="Visibility" Value="Collapsed"/>
                                                                                <Setter Property="Background" Value="#ECEFF3"/>
                                                                                <Setter Property="BorderBrush" Value="#CCD4DE"/>
                                                                                <Style.Triggers>
                                                                                    <DataTrigger Binding="{Binding VisualState}" Value="ActiveUnacknowledged"><Setter Property="Visibility" Value="Visible"/><Setter Property="Background" Value="#FFD7DC"/><Setter Property="BorderBrush" Value="#EF9AA5"/></DataTrigger>
                                                                                    <DataTrigger Binding="{Binding VisualState}" Value="ActiveAcknowledged"><Setter Property="Visibility" Value="Visible"/><Setter Property="Background" Value="#FFE4CB"/><Setter Property="BorderBrush" Value="#F2A064"/></DataTrigger>
                                                                                    <DataTrigger Binding="{Binding VisualState}" Value="ReturnedUnacknowledged"><Setter Property="Visibility" Value="Visible"/><Setter Property="Background" Value="#FFEDB2"/><Setter Property="BorderBrush" Value="#DDB44B"/></DataTrigger>
                                                                                </Style.Triggers>
                                                                            </Style>
                                                                        </Border.Style>
                                                                        <TextBlock Text="{Binding CompactStateText}" FontSize="8.2" FontWeight="Bold" Foreground="#5B4750"/>
                                                                    </Border>
                                                                    <TextBlock Grid.Row="1" Text="{Binding CurrentValue}" FontSize="18" FontWeight="Bold" Foreground="#111827"
                                                                               VerticalAlignment="Center" TextTrimming="CharacterEllipsis" ToolTip="{Binding CurrentValue}"/>
                                                                    <Button Grid.Row="1" Grid.Column="1" Content="ACK" Tag="{Binding}" Click="AcknowledgeAlarm_Click"
                                                                            Padding="7,2" Margin="7,0,0,0" FontSize="8.8" MinHeight="22" VerticalAlignment="Center">
                                                                        <Button.Style>
                                                                            <Style TargetType="Button" BasedOn="{StaticResource SoftButton}">
                                                                                <Setter Property="Visibility" Value="Collapsed"/>
                                                                                <Style.Triggers>
                                                                                    <DataTrigger Binding="{Binding CanAcknowledge}" Value="True"><Setter Property="Visibility" Value="Visible"/></DataTrigger>
                                                                                </Style.Triggers>
                                                                            </Style>
                                                                        </Button.Style>
                                                                    </Button>
                                                                </Grid>
                                                            </Border>
                                                        </Grid>
                                                    </DataTemplate>
                                                </ItemsControl.ItemTemplate>
                                            </ItemsControl>
                                        </ScrollViewer>
                                    </Grid>
                                </Grid>
                            </Grid>
                        </Border>
                    </Grid>
                </Border>
            </TabItem>

            <!-- SCL / DISCOVERY-AWARE GOOSE SUBSCRIBER -->'''

new_text, count = pattern.subn(replacement, text, count=1)
if count != 1:
    raise SystemExit(f"annunciator section: expected one match, found {count}")

path.write_text(new_text, encoding="utf-8")
