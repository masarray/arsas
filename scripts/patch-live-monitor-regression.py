from pathlib import Path

xaml_path = Path("MainWindow.xaml")
main_path = Path("MainWindow.xaml.cs")
runtime_path = Path("Services/Iec61850MonitorRuntime.cs")

xaml = xaml_path.read_text(encoding="utf-8")
main = main_path.read_text(encoding="utf-8")
runtime = runtime_path.read_text(encoding="utf-8")

old_grid = '''                            <Border Grid.Row="0" Style="{StaticResource Card}" Padding="10">
                                <Grid>
                                    <DataGrid ItemsSource="{Binding SelectedDevice.Points}" IsReadOnly="True"'''
new_grid = '''                            <Border Grid.Row="0" Style="{StaticResource Card}" Padding="10">
                                <Grid>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="Auto"/>
                                        <RowDefinition Height="8"/>
                                        <RowDefinition Height="*"/>
                                    </Grid.RowDefinitions>

                                    <Grid Grid.Row="0">
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="Auto"/>
                                        </Grid.ColumnDefinitions>
                                        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                            <Ellipse Width="7" Height="7" Fill="{StaticResource Accent}" Margin="0,0,7,0"/>
                                            <TextBlock Text="LIVE SIGNAL VALUES" FontSize="10.5" FontWeight="Bold" Foreground="#536275" VerticalAlignment="Center"/>
                                            <TextBlock Text="{Binding SelectedDevice.Points.Count, StringFormat= {0} signals}" FontSize="9.8" FontWeight="SemiBold" Foreground="#8B98A8" Margin="7,0,0,0" VerticalAlignment="Center"/>
                                        </StackPanel>

                                        <Border Grid.Column="1" Width="350" Height="34" CornerRadius="9"
                                                Background="#F8FAFC" BorderBrush="#C8D2DE" BorderThickness="1">
                                            <Grid>
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="32"/>
                                                    <ColumnDefinition Width="*"/>
                                                    <ColumnDefinition Width="30"/>
                                                </Grid.ColumnDefinitions>
                                                <Viewbox Width="14" Height="14" HorizontalAlignment="Center" VerticalAlignment="Center">
                                                    <Path Data="{StaticResource LucideSearch}" Style="{StaticResource LucideIcon}" Stroke="#738398"/>
                                                </Viewbox>
                                                <TextBox x:Name="ExplorerLiveSearchBox" Grid.Column="1"
                                                         BorderThickness="0" Background="Transparent" Foreground="#263445"
                                                         CaretBrush="#2563EB" FontSize="11.5" Padding="0"
                                                         VerticalContentAlignment="Center" FocusVisualStyle="{x:Null}"
                                                         TextChanged="ExplorerLiveSearch_TextChanged"
                                                         ToolTip="Filter the selected IED live values without changing the monitored point set."/>
                                                <TextBlock Grid.Column="1" Text="Search signal, IEC reference, value, quality or acquisition"
                                                           Foreground="#8B98A8" FontSize="11.2" VerticalAlignment="Center"
                                                           IsHitTestVisible="False">
                                                    <TextBlock.Style>
                                                        <Style TargetType="TextBlock">
                                                            <Setter Property="Visibility" Value="Collapsed"/>
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding Text, ElementName=ExplorerLiveSearchBox}" Value="">
                                                                    <Setter Property="Visibility" Value="Visible"/>
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </TextBlock.Style>
                                                </TextBlock>
                                                <Button Grid.Column="2" Click="ExplorerLiveSearchClear_Click"
                                                        Width="24" Height="24" Padding="0" Margin="0"
                                                        Background="Transparent" BorderThickness="0" Cursor="Hand"
                                                        ToolTip="Clear Live Signal search" FocusVisualStyle="{x:Null}">
                                                    <Button.Style>
                                                        <Style TargetType="Button">
                                                            <Setter Property="Visibility" Value="Visible"/>
                                                            <Setter Property="Template">
                                                                <Setter.Value>
                                                                    <ControlTemplate TargetType="Button">
                                                                        <Border x:Name="Chrome" Background="{TemplateBinding Background}" CornerRadius="7">
                                                                            <Viewbox Width="13" Height="13" HorizontalAlignment="Center" VerticalAlignment="Center">
                                                                                <Path Data="{StaticResource LucideX}" Style="{StaticResource LucideIcon}" Stroke="#607086"/>
                                                                            </Viewbox>
                                                                        </Border>
                                                                        <ControlTemplate.Triggers>
                                                                            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Chrome" Property="Background" Value="#E7EEF7"/></Trigger>
                                                                            <Trigger Property="IsPressed" Value="True"><Setter TargetName="Chrome" Property="Background" Value="#DCE7F5"/></Trigger>
                                                                        </ControlTemplate.Triggers>
                                                                    </ControlTemplate>
                                                                </Setter.Value>
                                                            </Setter>
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding Text, ElementName=ExplorerLiveSearchBox}" Value="">
                                                                    <Setter Property="Visibility" Value="Collapsed"/>
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </Button.Style>
                                                </Button>
                                            </Grid>
                                        </Border>
                                    </Grid>

                                    <DataGrid Grid.Row="2" ItemsSource="{Binding SelectedDevice.Points}" IsReadOnly="True"'''
if xaml.count(old_grid) != 1:
    raise SystemExit(f"Explorer grid anchor count={xaml.count(old_grid)}")
xaml = xaml.replace(old_grid, new_grid, 1)

old_empty = '''                                    <Border Visibility="{Binding SelectedDeviceNoLivePointsVisibility}" Background="#FAFCFF" CornerRadius="16"'''
new_empty = '''                                    <Border Grid.Row="2" Visibility="{Binding SelectedDeviceNoLivePointsVisibility}" Background="#FAFCFF" CornerRadius="16"'''
if xaml.count(old_empty) != 1:
    raise SystemExit(f"Empty-state anchor count={xaml.count(old_empty)}")
xaml = xaml.replace(old_empty, new_empty, 1)

old_selected = '''            TryAutoExpandCommandPanelOnce(_selectedDevice);
            // ctlModel inspection is preloaded independently of the Expander. Avoid'''
new_selected = '''            TryAutoExpandCommandPanelOnce(_selectedDevice);
            ApplyExplorerLiveSearchFilter();
            // ctlModel inspection is preloaded independently of the Expander. Avoid'''
if main.count(old_selected) != 1:
    raise SystemExit(f"SelectedDevice anchor count={main.count(old_selected)}")
main = main.replace(old_selected, new_selected, 1)

old_policy = '''    private static int GetVerificationPollIntervalMs(
        Iec61850MonitorPoint point,
        RuntimePointState state,
        bool reportAssigned)
    {
        if (!reportAssigned) return point.PollingIntervalMs;
        if (state.ReportTrafficSeen)
        {
            var minimum = state.ReportChangeVerified ? (IsFastPoint(point) ? 10000 : 30000) : (IsFastPoint(point) ? 5000 : 15000);
            return Math.Clamp(Math.Max(point.PollingIntervalMs * 15, minimum), minimum, 60000);
        }
        var awaitingReportMinimum = IsFastPoint(point) ? 2000 : 5000;
        return Math.Clamp(Math.Max(point.PollingIntervalMs * 3, awaitingReportMinimum), awaitingReportMinimum, 15000);
    }'''
new_policy = '''    private static int GetVerificationPollIntervalMs(
        Iec61850MonitorPoint point,
        RuntimePointState state,
        bool reportAssigned)
        => ReportVerificationPollingPolicy.GetIntervalMs(
            point.PollingIntervalMs,
            IsFastPoint(point),
            reportAssigned,
            state.ReportTrafficSeen,
            state.ReportChangeVerified);'''
if runtime.count(old_policy) != 1:
    raise SystemExit(f"Polling policy anchor count={runtime.count(old_policy)}")
runtime = runtime.replace(old_policy, new_policy, 1)

xaml_path.write_text(xaml, encoding="utf-8", newline="\n")
main_path.write_text(main, encoding="utf-8", newline="\n")
runtime_path.write_text(runtime, encoding="utf-8", newline="\n")
print("Restored Live Signal search and fail-safe MMS verification cadence.")
