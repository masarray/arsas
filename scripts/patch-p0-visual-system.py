from pathlib import Path
import re


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


def regex_once(text: str, pattern: str, replacement: str, label: str) -> str:
    result, count = re.subn(pattern, replacement, text, count=1, flags=re.S)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one regex match, found {count}")
    return result


app_path = Path("App.xaml")
main_path = Path("MainWindow.xaml")
grid_path = Path("GridUxBehavior.cs")
search_path = Path("MainWindow.GlobalLiveSearch.cs")
sas_path = Path("SasOperationalUiPolicy.cs")

app = app_path.read_text(encoding="utf-8")
main = main_path.read_text(encoding="utf-8")
grid = grid_path.read_text(encoding="utf-8")
search = search_path.read_text(encoding="utf-8")
sas = sas_path.read_text(encoding="utf-8")

# ---------------------------------------------------------------------------
# App.xaml — compact typography, workspace surfaces, stable navigation.
# ---------------------------------------------------------------------------
old_typography = '''        <!-- Compact P2 typography scale: restrained sizes for dense engineering workstations. -->
        <Style x:Key="SectionTitle" TargetType="TextBlock">
            <Setter Property="FontSize" Value="15"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Foreground" Value="{StaticResource Ink}"/>
            <Setter Property="TextOptions.TextFormattingMode" Value="Display"/>
        </Style>

        <Style x:Key="BodyText" TargetType="TextBlock">
            <Setter Property="FontSize" Value="12.2"/>
            <Setter Property="Foreground" Value="{StaticResource Ink}"/>
            <Setter Property="TextOptions.TextFormattingMode" Value="Display"/>
        </Style>

        <Style x:Key="Caption" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{StaticResource Muted}"/>
            <Setter Property="FontSize" Value="11.8"/>
            <Setter Property="TextOptions.TextFormattingMode" Value="Display"/>
        </Style>

        <Style x:Key="MicroLabel" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{StaticResource Muted}"/>
            <Setter Property="FontSize" Value="10.6"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="TextOptions.TextFormattingMode" Value="Display"/>
        </Style>
'''
new_typography = '''        <!-- P0 workstation typography: five levels only for fast engineering scan paths. -->
        <Style x:Key="WorkspaceTitle" TargetType="TextBlock">
            <Setter Property="FontSize" Value="16"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Foreground" Value="{StaticResource Ink}"/>
            <Setter Property="TextOptions.TextFormattingMode" Value="Display"/>
        </Style>

        <Style x:Key="WorkspaceSubtitle" TargetType="TextBlock">
            <Setter Property="FontSize" Value="11"/>
            <Setter Property="Foreground" Value="{StaticResource Muted}"/>
            <Setter Property="TextOptions.TextFormattingMode" Value="Display"/>
        </Style>

        <Style x:Key="SectionTitle" TargetType="TextBlock">
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Foreground" Value="{StaticResource Ink}"/>
            <Setter Property="TextOptions.TextFormattingMode" Value="Display"/>
        </Style>

        <Style x:Key="BodyText" TargetType="TextBlock">
            <Setter Property="FontSize" Value="12"/>
            <Setter Property="Foreground" Value="{StaticResource Ink}"/>
            <Setter Property="TextOptions.TextFormattingMode" Value="Display"/>
        </Style>

        <Style x:Key="Caption" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{StaticResource Muted}"/>
            <Setter Property="FontSize" Value="11"/>
            <Setter Property="TextOptions.TextFormattingMode" Value="Display"/>
        </Style>

        <Style x:Key="MicroLabel" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{StaticResource Muted}"/>
            <Setter Property="FontSize" Value="10"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="TextOptions.TextFormattingMode" Value="Display"/>
        </Style>

        <!-- Main-window workspace surfaces deliberately avoid card shadows. The data is the hierarchy. -->
        <Style x:Key="WorkspaceCard" TargetType="Border">
            <Setter Property="Background" Value="{StaticResource PremiumSurface}"/>
            <Setter Property="CornerRadius" Value="12"/>
            <Setter Property="Padding" Value="12"/>
            <Setter Property="BorderBrush" Value="{StaticResource BorderSubtle}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Effect" Value="{x:Null}"/>
        </Style>

        <Style x:Key="SearchSurface" TargetType="Border">
            <Setter Property="Height" Value="36"/>
            <Setter Property="Background" Value="#F8FAFC"/>
            <Setter Property="BorderBrush" Value="#D7E1EE"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="CornerRadius" Value="10"/>
        </Style>
'''
app = replace_once(app, old_typography, new_typography, "typography block")

nav_pattern = r'''        <Style x:Key="SegmentedNavButton" TargetType="Button">.*?        </Style>\n\n        <Style x:Key="RuntimeSegmentButton"'''
nav_replacement = '''        <Style x:Key="SegmentedNavButton" TargetType="Button">
            <Setter Property="Foreground" Value="#475467"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderBrush" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Padding" Value="0"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="FontSize" Value="12.5"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="SnapsToDevicePixels" Value="True"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Grid SnapsToDevicePixels="True">
                            <Border Background="Transparent" CornerRadius="10" Margin="2"/>
                            <Border x:Name="KeyboardFocusRing" Margin="2" CornerRadius="10"
                                    BorderBrush="#7EA9EA" BorderThickness="1.5" Opacity="0"
                                    IsHitTestVisible="False"/>
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"
                                              RecognizesAccessKey="True"
                                              TextElement.Foreground="{TemplateBinding Foreground}"/>
                        </Grid>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter Property="Foreground" Value="#344054"/>
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter Property="Opacity" Value="0.82"/>
                            </Trigger>
                            <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter TargetName="KeyboardFocusRing" Property="Opacity" Value="1"/>
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Opacity" Value="0.45"/>
                                <Setter Property="Cursor" Value="Arrow"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="RuntimeSegmentButton"'''
app = regex_once(app, nav_pattern, nav_replacement, "segmented nav style")

# Dense data baseline: keep engineering grids compact and consistent.
app = replace_once(app, '<Setter Property="RowHeight" Value="34"/>\n            <Setter Property="ColumnHeaderHeight" Value="34"/>\n            <Setter Property="AlternatingRowBackground" Value="#F5F8FD"/>\n            <Setter Property="FontSize" Value="12.5"/>',
                   '<Setter Property="RowHeight" Value="32"/>\n            <Setter Property="ColumnHeaderHeight" Value="34"/>\n            <Setter Property="AlternatingRowBackground" Value="#F5F8FD"/>\n            <Setter Property="FontSize" Value="12.2"/>',
                   "modern datagrid density")
app = replace_once(app, '<Setter Property="FontWeight" Value="SemiBold"/>\n            <Setter Property="Padding" Value="10,0"/>\n            <Setter Property="HorizontalContentAlignment" Value="Left"/>',
                   '<Setter Property="FontWeight" Value="SemiBold"/>\n            <Setter Property="FontSize" Value="12"/>\n            <Setter Property="Padding" Value="9,0"/>\n            <Setter Property="HorizontalContentAlignment" Value="Left"/>',
                   "datagrid header typography")

# ---------------------------------------------------------------------------
# MainWindow.xaml — shared surfaces/header hierarchy + progressive filters.
# ---------------------------------------------------------------------------
main = main.replace('Style="{StaticResource Card}"', 'Style="{StaticResource WorkspaceCard}"')
main = replace_once(main, 'x:Name="WorkflowNavShell" Width="760" Height="56"', 'x:Name="WorkflowNavShell" Width="900" Height="56"', "nav initial width")
main = replace_once(main, 'Background="#E7ECF5" CornerRadius="20" Padding="5" Effect="{StaticResource SoftShadow}"',
                    'Background="#E7ECF5" CornerRadius="14" Padding="5" Effect="{StaticResource SoftShadow}"', "nav shell radius")

main = replace_once(main,
'''                            <StackPanel>
                                <TextBlock Text="Global Multi-IED Live Monitor" FontSize="15.5" FontWeight="SemiBold" Foreground="{StaticResource Ink}"/>
                                <TextBlock Text="Every IED keeps its own connection, report subscription, validated reads, and event-driven RCB state."
                                           FontSize="12.2" Foreground="{StaticResource Muted}" Margin="0,3,0,0"/>
                            </StackPanel>''',
'''                            <StackPanel>
                                <TextBlock Text="Global Multi-IED Live Monitor" Style="{StaticResource WorkspaceTitle}"/>
                                <TextBlock Text="Live values across all monitored IEDs." Style="{StaticResource WorkspaceSubtitle}" Margin="0,2,0,0"/>
                            </StackPanel>''', "live monitor header")

live_search_pattern = r'''                            <Border Grid.Row="1" Margin="0,10,0,0" Height="40" Background="#F8FAFC".*?                            </Border>\n                        </Grid>\n                        <DataGrid x:Name="GlobalLiveGrid"'''
live_search_replacement = '''                            <Grid Grid.Row="1" Margin="0,8,0,0">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="8"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <Border Style="{StaticResource SearchSurface}" HorizontalAlignment="Stretch">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="36"/>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="34"/>
                                        </Grid.ColumnDefinitions>
                                        <Viewbox Width="15" Height="15" HorizontalAlignment="Center" VerticalAlignment="Center">
                                            <Path Data="{StaticResource LucideSearch}" Style="{StaticResource LucideIcon}" Stroke="#607086"/>
                                        </Viewbox>
                                        <Grid Grid.Column="1">
                                            <TextBox x:Name="GlobalLiveSearchBox"
                                                     TextChanged="GlobalLiveSearch_TextChanged"
                                                     Background="Transparent" BorderThickness="0" Padding="0"
                                                     VerticalContentAlignment="Center" FontSize="12"
                                                     Foreground="{StaticResource Ink}" CaretBrush="{StaticResource Accent}"
                                                     ToolTip="Fast search across all monitored IED signals"/>
                                            <TextBlock Text="Search IED, signal, IEC reference, value, quality or acquisition"
                                                       VerticalAlignment="Center" IsHitTestVisible="False"
                                                       Style="{StaticResource Caption}" Foreground="#98A2B3">
                                                <TextBlock.Style>
                                                    <Style TargetType="TextBlock" BasedOn="{StaticResource Caption}">
                                                        <Setter Property="Foreground" Value="#98A2B3"/>
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
                                                BorderThickness="0" Padding="7" Cursor="Hand" ToolTip="Clear search" FocusVisualStyle="{x:Null}">
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
                                <Button x:Name="GlobalLiveFiltersButton" Grid.Column="2"
                                        Click="GlobalLiveFilters_Click" Style="{StaticResource SoftButton}"
                                        Padding="10,6" MinHeight="36" ToolTip="Show or hide per-column filters">
                                    <StackPanel Orientation="Horizontal">
                                        <Viewbox Width="14" Height="14" Margin="0,0,6,0">
                                            <Path Data="{StaticResource LucideSlidersHorizontal}" Style="{StaticResource LucideIcon}"/>
                                        </Viewbox>
                                        <TextBlock x:Name="GlobalLiveFiltersLabel" Text="Filters" FontWeight="SemiBold"/>
                                    </StackPanel>
                                </Button>
                            </Grid>
                        </Grid>
                        <DataGrid x:Name="GlobalLiveGrid"'''
main = regex_once(main, live_search_pattern, live_search_replacement, "live search/filter toolbar")

main = replace_once(main,
'''                            <StackPanel>
                                <TextBlock Text="IEC 61850 Sequence of Events" FontSize="15.5" FontWeight="SemiBold" Foreground="{StaticResource Ink}"/>
                                <TextBlock Text="SCADA/SAS SOE: state values use ARSAS blue for active and slate for inactive; intermediate/bad states use amber. Color describes state, not alarm severity. Non-events stay out."
                                           FontSize="12.2" Foreground="{StaticResource Muted}" Margin="0,3,0,0"/>
                            </StackPanel>''',
'''                            <StackPanel>
                                <TextBlock Text="IEC 61850 Sequence of Events" Style="{StaticResource WorkspaceTitle}"/>
                                <TextBlock Text="SOE edges only • state color is not alarm severity." Style="{StaticResource WorkspaceSubtitle}" Margin="0,2,0,0"/>
                            </StackPanel>''', "event log header")

main = replace_once(main, '<TextBlock Text="Alarm Annunciator" FontSize="16" FontWeight="SemiBold" Foreground="{StaticResource Ink}"/>',
                    '<TextBlock Text="Alarm Annunciator" Style="{StaticResource WorkspaceTitle}"/>', "alarm title")
main = replace_once(main, 'Text="IED FASCIA • FLASH = UNACK • STEADY = ACK • RTN = RETURNED"\n                                               FontSize="9.8" FontWeight="SemiBold" Foreground="#7A8797" Margin="0,2,0,0"/',
                    'Text="FLASH = UNACK • STEADY = ACK • RTN = RETURNED"\n                                               Style="{StaticResource MicroLabel}" Foreground="#7A8797" Margin="0,2,0,0"/', "alarm legend")

main = replace_once(main,
'''                                <TextBlock Text="Diagnostics &amp; Communication Journal" FontSize="15.5" FontWeight="SemiBold" Foreground="{StaticResource Ink}"/>
                                <TextBlock Text="Connection, discovery, IED identity, RCB/DataSet setup, report verification, fallback, reconnect, and protocol faults."
                                           FontSize="12.2" Foreground="{StaticResource Muted}" Margin="0,3,0,0"/>''',
'''                                <TextBlock Text="Diagnostics &amp; Communication Journal" Style="{StaticResource WorkspaceTitle}"/>
                                <TextBlock Text="Connection, reporting, fallback and protocol journal." Style="{StaticResource WorkspaceSubtitle}" Margin="0,2,0,0"/>''', "diagnostics header")

# GOOSE primary title follows the same section-level typography rather than an arbitrary size.
main = replace_once(main, '<TextBlock Text="GOOSE Subscriber" FontSize="14.5" FontWeight="SemiBold" Foreground="{StaticResource Ink}" VerticalAlignment="Center"/>',
                    '<TextBlock Text="GOOSE Subscriber" Style="{StaticResource SectionTitle}" VerticalAlignment="Center"/>', "goose title")

# ---------------------------------------------------------------------------
# GridUxBehavior.cs — global search is primary; filters expand only on demand.
# ---------------------------------------------------------------------------
grid = replace_once(grid,
'''        public Dictionary<string, string> Filters { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string SearchQuery { get; set; } = string.Empty;''',
'''        public Dictionary<string, string> Filters { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Grid> HeaderRoots { get; } = new();
        public string SearchQuery { get; set; } = string.Empty;
        public bool FiltersExpanded { get; set; }''', "rapid filter state")

grid = replace_once(grid,
'''        headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(220, 227, 236))));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        headerStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 74d));
        grid.ColumnHeaderStyle = headerStyle;
        grid.ColumnHeaderHeight = 74;''',
'''        headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(220, 227, 236))));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        grid.ColumnHeaderStyle = headerStyle;
        grid.ColumnHeaderHeight = 34;''', "collapsed header baseline")

grid = replace_once(grid,
'''    internal static void SetGlobalRapidSearch(DataGrid grid, string? query)
    {
        if (!GlobalGrids.TryGetValue(grid, out var state))
            return;

        state.SearchQuery = query?.Trim() ?? string.Empty;
        state.RefreshTimer.Stop();
        state.RefreshTimer.Start();
    }
''',
'''    internal static void SetGlobalRapidSearch(DataGrid grid, string? query)
    {
        if (!GlobalGrids.TryGetValue(grid, out var state))
            return;

        state.SearchQuery = query?.Trim() ?? string.Empty;
        state.RefreshTimer.Stop();
        state.RefreshTimer.Start();
    }

    internal static void SetGlobalRapidFiltersExpanded(DataGrid grid, bool expanded)
    {
        if (!GlobalGrids.TryGetValue(grid, out var state))
            return;

        state.FiltersExpanded = expanded;
        foreach (var root in state.HeaderRoots)
        {
            if (root.RowDefinitions.Count < 2)
                continue;
            root.RowDefinitions[1].Height = expanded ? new GridLength(34) : new GridLength(0);
        }
        grid.ColumnHeaderHeight = expanded ? 68 : 34;
        grid.UpdateLayout();
    }
''', "filters disclosure method")

grid = replace_once(grid,
'''        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });''',
'''        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });''', "filter header row geometry")
grid = replace_once(grid, '            FontSize = 12.5,\n            VerticalAlignment = VerticalAlignment.Center,',
                    '            FontSize = 12.0,\n            VerticalAlignment = VerticalAlignment.Center,', "rapid header title font")
grid = replace_once(grid,
'''        var filterBox = CreateRapidFilterTextBox(state, caption);
        Grid.SetRow(filterBox, 1);
        root.Children.Add(filterBox);

        return root;''',
'''        var filterBox = CreateRapidFilterTextBox(state, caption);
        Grid.SetRow(filterBox, 1);
        root.Children.Add(filterBox);
        state.HeaderRoots.Add(root);

        return root;''', "track filter header roots")
grid = replace_once(grid, '            Height = 36,\n            Padding = new Thickness(10, 0, 7, 0),\n            FontSize = 12.5,',
                    '            Height = 34,\n            Padding = new Thickness(9, 0, 7, 0),\n            FontSize = 12.0,', "rapid filter input density")
grid = replace_once(grid, '        watermark.SetValue(TextBlock.FontSizeProperty, 12.5d);',
                    '        watermark.SetValue(TextBlock.FontSizeProperty, 12.0d);', "rapid filter watermark font")

# ---------------------------------------------------------------------------
# Global Live search bridge — one-click filter disclosure.
# ---------------------------------------------------------------------------
search = '''using System.Windows;
using System.Windows.Controls;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private bool _globalLiveFiltersExpanded;

    private void GlobalLiveSearch_TextChanged(object sender, TextChangedEventArgs e)
        => GridUxBehavior.SetGlobalRapidSearch(GlobalLiveGrid, GlobalLiveSearchBox?.Text);

    private void GlobalLiveSearchClear_Click(object sender, RoutedEventArgs e)
    {
        if (GlobalLiveSearchBox == null)
            return;

        GlobalLiveSearchBox.Clear();
        GlobalLiveSearchBox.Focus();
    }

    private void GlobalLiveFilters_Click(object sender, RoutedEventArgs e)
    {
        _globalLiveFiltersExpanded = !_globalLiveFiltersExpanded;
        GridUxBehavior.SetGlobalRapidFiltersExpanded(GlobalLiveGrid, _globalLiveFiltersExpanded);
        if (GlobalLiveFiltersLabel != null)
            GlobalLiveFiltersLabel.Text = _globalLiveFiltersExpanded ? "Hide filters" : "Filters";
    }
}
'''

# ---------------------------------------------------------------------------
# Runtime nav policy must match the XAML/theme contract after Loaded.
# ---------------------------------------------------------------------------
sas = replace_once(sas, '        shell.Width = 760;\n        shell.Height = 56;\n        shell.Padding = new Thickness(5);\n        shell.CornerRadius = new CornerRadius(20);',
                   '        shell.Width = 900;\n        shell.Height = 56;\n        shell.Padding = new Thickness(5);\n        shell.CornerRadius = new CornerRadius(14);', "runtime nav shell")
sas = replace_once(sas, '            button.FontSize = 12.4;\n            button.PreviewMouseLeftButtonUp -= OnNavigationClick;',
                   '            button.FontSize = 12.5;\n            button.FocusVisualStyle = null;\n            button.PreviewMouseLeftButtonUp -= OnNavigationClick;', "runtime nav typography")
sas = sas.replace('CornerRadius="14"', 'CornerRadius="10"')

app_path.write_text(app, encoding="utf-8", newline="\n")
main_path.write_text(main, encoding="utf-8", newline="\n")
grid_path.write_text(grid, encoding="utf-8", newline="\n")
search_path.write_text(search, encoding="utf-8", newline="\n")
sas_path.write_text(sas, encoding="utf-8", newline="\n")

print("Applied P0 visual system unification: typography, surfaces, stable nav and progressive Live Monitor filters.")
