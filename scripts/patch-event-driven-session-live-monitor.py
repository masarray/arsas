from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected one match, got {count}: {old[:100]!r}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8', newline='\n')

# 1) Alarm Annunciator: retain SOE as edge/history authority, but reconcile current
# process state from the already-authoritative live point stream after every UI flush.
alarm_path = 'MainWindow.AlarmAnnunciator.cs'
replace_once(
    alarm_path,
    '''/// Trigger authority is the same SOE/Event Log stream shown to the operator. The
/// annunciator never invents a second IEC 61850 acquisition path. A momentary alarm
/// edge therefore remains visible even after the process value has returned to normal.
''',
    '''/// SOE/Event Log remains the occurrence/history authority. Current live point snapshots
/// are reconciled separately so a restored project immediately shows the physical alarm state
/// after its initial read without fabricating an SOE entry. A momentary alarm edge remains
/// latched even after the process value has returned to normal.
''')
replace_once(
    alarm_path,
    '''    private void AlarmRuntime_EventRaised(Iec61850EventEntry entry)
        => _pendingAnnunciatorEvents.Enqueue(entry);

    private void AnnunciatorUiTimer_Tick(object? sender, EventArgs e)
''',
    '''    private void AlarmRuntime_EventRaised(Iec61850EventEntry entry)
        => _pendingAnnunciatorEvents.Enqueue(entry);

    /// <summary>
    /// Reconciles the annunciator fascia from the same point snapshot already accepted by
    /// the Live Monitor. This never creates an Event Log entry and never starts another
    /// acquisition path; SOE edges remain responsible for occurrence history/latching.
    /// </summary>
    private void ReconcileAnnunciatorFromLivePoint(Iec61850MonitorPoint point)
    {
        if (!_annunciatorInitialized ||
            !point.CanUseAsAnnunciator ||
            !IsAnnunciatorConfigured(point.DeviceId, point.IecReference))
        {
            return;
        }

        var item = EnsureAnnunciatorItem(point);
        item.InitializeFromPoint(point);
        RefreshAnnunciatorDeviceGroup(point.DeviceId);
    }

    private void AnnunciatorUiTimer_Tick(object? sender, EventArgs e)
''')
replace_once(
    alarm_path,
    'item.MarkUnavailable(device.IsConnected ? "Waiting for live SOE" : "Offline / saved configuration");',
    'item.MarkUnavailable(device.IsConnected ? "Waiting for live value" : "Offline / saved configuration");')

# Call reconciliation only after the complete point snapshot (value, quality, source, timestamp)
# has been copied to the UI model.
main_cs = 'MainWindow.xaml.cs'
replace_once(
    main_cs,
    '''                UpdateCommandFeedbackFromLivePoint(point);
''',
    '''                UpdateCommandFeedbackFromLivePoint(point);
                ReconcileAnnunciatorFromLivePoint(point);
''')

# 2) Live Monitor header: remove duplicated monitoring badge and add a full-width global search.
xaml_path = 'MainWindow.xaml'
old_header = '''                        <Grid DockPanel.Dock="Top" Margin="0,0,0,10">
                            <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                            <StackPanel>
                                <TextBlock Text="Global Multi-IED Live Monitor" FontSize="15.5" FontWeight="SemiBold" Foreground="{StaticResource Ink}"/>
                                <TextBlock Text="Every IED keeps its own connection, report subscription, validation reads, and start/stop state."
                                           FontSize="12.2" Foreground="{StaticResource Muted}" Margin="0,3,0,0"/>
                            </StackPanel>
                            <Border Grid.Column="1" Background="#F8FAFC" BorderBrush="{StaticResource Line}" BorderThickness="1" CornerRadius="15" Padding="10,6">
                                <TextBlock Text="{Binding MonitoringInsightText}" FontSize="12.4" Foreground="{StaticResource Ink}"/>
                            </Border>
                        </Grid>
'''
new_header = '''                        <Grid DockPanel.Dock="Top" Margin="0,0,0,10">
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>
                            <StackPanel>
                                <TextBlock Text="Global Multi-IED Live Monitor" FontSize="15.5" FontWeight="SemiBold" Foreground="{StaticResource Ink}"/>
                                <TextBlock Text="Every IED keeps its own connection, report subscription, validated reads, and event-driven RCB state."
                                           FontSize="12.2" Foreground="{StaticResource Muted}" Margin="0,3,0,0"/>
                            </StackPanel>
                            <Border Grid.Row="1" Margin="0,10,0,0" Height="40" Background="#F8FAFC"
                                    BorderBrush="#D7E1EE" BorderThickness="1" CornerRadius="10"
                                    HorizontalAlignment="Stretch">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="38"/>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="38"/>
                                    </Grid.ColumnDefinitions>
                                    <Viewbox Width="16" Height="16" HorizontalAlignment="Center" VerticalAlignment="Center">
                                        <Path Data="{StaticResource LucideSearch}" Style="{StaticResource LucideIcon}" Stroke="#607086"/>
                                    </Viewbox>
                                    <Grid Grid.Column="1">
                                        <TextBox x:Name="GlobalLiveSearchBox"
                                                 TextChanged="GlobalLiveSearch_TextChanged"
                                                 Background="Transparent" BorderThickness="0" Padding="0"
                                                 VerticalContentAlignment="Center" FontSize="12.5"
                                                 Foreground="{StaticResource Ink}" CaretBrush="{StaticResource Accent}"
                                                 ToolTip="Fast search across all monitored IED signals"/>
                                        <TextBlock Text="Search all live signals — IED, signal, IEC reference, value, quality, acquisition…"
                                                   Margin="1,0,0,0" VerticalAlignment="Center" IsHitTestVisible="False"
                                                   FontSize="12.2" Foreground="#98A2B3">
                                            <TextBlock.Style>
                                                <Style TargetType="TextBlock">
                                                    <Setter Property="Visibility" Value="Collapsed"/>
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding Text, ElementName=GlobalLiveSearchBox}" Value="">
                                                            <Setter Property="Visibility" Value="Visible"/>
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </TextBlock.Style>
                                        </TextBlock>
                                    </Grid>
                                    <Button Grid.Column="2" Click="GlobalLiveSearchClear_Click" Background="Transparent"
                                            BorderThickness="0" Padding="8" Cursor="Hand" ToolTip="Clear search">
                                        <Button.Style>
                                            <Style TargetType="Button">
                                                <Setter Property="Visibility" Value="Visible"/>
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding Text, ElementName=GlobalLiveSearchBox}" Value="">
                                                        <Setter Property="Visibility" Value="Collapsed"/>
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </Button.Style>
                                        <Viewbox Width="13" Height="13"><Path Data="{StaticResource LucideX}" Style="{StaticResource LucideIcon}" Stroke="#607086"/></Viewbox>
                                    </Button>
                                </Grid>
                            </Border>
                        </Grid>
'''
replace_once(xaml_path, old_header, new_header)

# 3) Integrate global search with the existing per-column rapid filter instead of replacing
# ICollectionView.Filter. Also bind header content width to the actual DataGridColumn width.
grid_path = 'GridUxBehavior.cs'
replace_once(
    grid_path,
    '''    private sealed class GlobalRapidFilterState
    {
        public required DataGrid Grid { get; init; }
        public required ICollectionView View { get; init; }
        public required DispatcherTimer RefreshTimer { get; init; }
        public Dictionary<string, string> Filters { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
''',
    '''    private sealed class GlobalRapidFilterState
    {
        public required DataGrid Grid { get; init; }
        public required ICollectionView View { get; init; }
        public required DispatcherTimer RefreshTimer { get; init; }
        public Dictionary<string, string> Filters { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string SearchQuery { get; set; } = string.Empty;
    }
''')
replace_once(
    grid_path,
    '        state.View.Filter = item => FilterGlobalPoint(item, state.Filters);',
    '        state.View.Filter = item => FilterGlobalPoint(item, state.Filters, state.SearchQuery);')
replace_once(
    grid_path,
    '''        foreach (var column in grid.Columns)
        {
            var caption = column.Header?.ToString() ?? string.Empty;
            column.Header = BuildRapidFilterHeader(state, caption);
        }
    }
''',
    '''        foreach (var column in grid.Columns)
        {
            var caption = column.Header?.ToString() ?? string.Empty;
            column.Header = BuildRapidFilterHeader(state, column, caption);
        }
    }

    internal static void SetGlobalRapidSearch(MainWindow owner, string? query)
    {
        var normalized = query?.Trim() ?? string.Empty;
        foreach (var grid in FindVisualChildren<DataGrid>(owner).Where(IsGlobalLiveGrid))
        {
            if (!GlobalGrids.TryGetValue(grid, out var state))
                continue;

            state.SearchQuery = normalized;
            state.RefreshTimer.Stop();
            state.RefreshTimer.Start();
        }
    }
''')
replace_once(
    grid_path,
    '    private static FrameworkElement BuildRapidFilterHeader(GlobalRapidFilterState state, string caption)\n',
    '    private static FrameworkElement BuildRapidFilterHeader(GlobalRapidFilterState state, DataGridColumn column, string caption)\n')
replace_once(
    grid_path,
    '''        var root = new Grid
        {
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true
        };
''',
    '''        var root = new Grid
        {
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 0
        };
        root.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(DataGridColumn.ActualWidth))
        {
            Source = column,
            Mode = BindingMode.OneWay
        });
''')
replace_once(
    grid_path,
    '    private static bool FilterGlobalPoint(object item, IReadOnlyDictionary<string, string> filters)\n',
    '    private static bool FilterGlobalPoint(object item, IReadOnlyDictionary<string, string> filters, string? searchQuery)\n')
replace_once(
    grid_path,
    '''        foreach (var (key, rawFilter) in filters)
        {
''',
    '''        var globalTokens = Tokenize(searchQuery);
        if (globalTokens.Length > 0)
        {
            var searchable = string.Join(" ", new[]
            {
                point.DeviceName,
                point.SignalName,
                point.IecTelegram,
                point.IecReference,
                point.DisplayValue,
                point.Quality,
                point.DeviceTimestamp,
                point.SourceMode,
                point.Category,
                point.IecDataType
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            if (!globalTokens.All(token => searchable.Contains(token, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        foreach (var (key, rawFilter) in filters)
        {
''')

# 4) Create a tiny code-behind bridge for the Live Monitor search box.
Path('MainWindow.GlobalLiveSearch.cs').write_text('''using System.Windows;\nusing System.Windows.Controls;\n\nnamespace ArIED61850Tester;\n\npublic partial class MainWindow\n{\n    private void GlobalLiveSearch_TextChanged(object sender, TextChangedEventArgs e)\n        => GridUxBehavior.SetGlobalRapidSearch(this, GlobalLiveSearchBox?.Text);\n\n    private void GlobalLiveSearchClear_Click(object sender, RoutedEventArgs e)\n    {\n        if (GlobalLiveSearchBox == null)\n            return;\n\n        GlobalLiveSearchBox.Clear();\n        GlobalLiveSearchBox.Focus();\n    }\n}\n''', encoding='utf-8', newline='\n')

# 5) Regression contract: session restore/current state, Live Monitor search/filter sizing,
# and the existing ARIEC hybrid static/dynamic-before-polling path must remain explicit.
Path('tests/ARSAS.Tests/EventDrivenSessionLiveMonitorRegressionTests.cs').write_text(r'''namespace ARSAS.Tests;

public sealed class EventDrivenSessionLiveMonitorRegressionTests
{
    [Fact]
    public void Annunciator_ReconcilesCurrentLiveSnapshot_WithoutCreatingSecondSoePath()
    {
        var alarm = File.ReadAllText(FindRepoFile("MainWindow.AlarmAnnunciator.cs"));
        var main = File.ReadAllText(FindRepoFile("MainWindow.xaml.cs"));

        Assert.Contains("ReconcileAnnunciatorFromLivePoint", alarm, StringComparison.Ordinal);
        Assert.Contains("item.InitializeFromPoint(point)", alarm, StringComparison.Ordinal);
        Assert.Contains("Waiting for live value", alarm, StringComparison.Ordinal);
        Assert.DoesNotContain("Waiting for live SOE", alarm, StringComparison.Ordinal);
        Assert.Contains("ReconcileAnnunciatorFromLivePoint(point);", main, StringComparison.Ordinal);
        Assert.DoesNotContain("Events.Add", alarm, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadValueAsync", alarm, StringComparison.Ordinal);
        Assert.DoesNotContain("StartDeviceAsync", alarm, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveMonitor_HasFullWidthGlobalSearch_AndNoDuplicateSummaryBadge()
    {
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        var behavior = File.ReadAllText(FindRepoFile("GridUxBehavior.cs"));
        var bridge = File.ReadAllText(FindRepoFile("MainWindow.GlobalLiveSearch.cs"));
        var section = Slice(xaml, "<!-- GLOBAL MULTI-IED LIVE MONITOR -->", "<!-- EVENT LOG -->");

        Assert.Contains("GlobalLiveSearchBox", section, StringComparison.Ordinal);
        Assert.Contains("GlobalLiveSearch_TextChanged", section, StringComparison.Ordinal);
        Assert.Contains("GlobalLiveSearchClear_Click", section, StringComparison.Ordinal);
        Assert.DoesNotContain("MonitoringInsightText", section, StringComparison.Ordinal);
        Assert.Contains("SetGlobalRapidSearch", bridge, StringComparison.Ordinal);
        Assert.Contains("SearchQuery", behavior, StringComparison.Ordinal);
        Assert.Contains("FilterGlobalPoint(item, state.Filters, state.SearchQuery)", behavior, StringComparison.Ordinal);
        Assert.Contains("nameof(DataGridColumn.ActualWidth)", behavior, StringComparison.Ordinal);
        Assert.Contains("Source = column", behavior, StringComparison.Ordinal);
    }

    [Fact]
    public void SclMonitoring_UsesAriecHybridStaticAndDynamicReports_BeforeResidualPolling()
    {
        var bridge = File.ReadAllText(FindRepoFile(Path.Combine("Services", "NativeIec61850Client.HybridReporting.cs")));
        var models = File.ReadAllText(FindRepoFile(Path.Combine("Models", "MonitorModels.cs")));

        Assert.Contains("AllowStaticBrcb = true", bridge, StringComparison.Ordinal);
        Assert.Contains("AllowStaticUrcb = true", bridge, StringComparison.Ordinal);
        Assert.Contains("AllowDynamicBrcb = device.AllowDynamicDataSetWrites", bridge, StringComparison.Ordinal);
        Assert.Contains("AllowDynamicUrcb = device.AllowDynamicDataSetWrites", bridge, StringComparison.Ordinal);
        Assert.Contains("MmsHybridReportAcquisitionPlanner.Build", bridge, StringComparison.Ordinal);
        Assert.Contains("StartPersistentReportMonitorAsync", bridge, StringComparison.Ordinal);
        Assert.Contains("MmsPollingFallback", bridge, StringComparison.Ordinal);
        Assert.Contains("AllowDynamicDataSetWrites { get; set; } = true", models, StringComparison.Ordinal);
    }

    private static string Slice(string source, string start, string end)
    {
        var a = source.IndexOf(start, StringComparison.Ordinal);
        var b = source.IndexOf(end, StringComparison.Ordinal);
        Assert.True(a >= 0 && b > a);
        return source[a..b];
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
''', encoding='utf-8', newline='\n')

print('Patched alarm live-state reconciliation, Live Monitor search/filter UX, and regression contracts.')
