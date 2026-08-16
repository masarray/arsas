from pathlib import Path

model_path = Path("Models/MonitorModels.cs")
runtime_path = Path("Services/Iec61850MonitorRuntime.cs")
xaml_path = Path("MainWindow.xaml")

model = model_path.read_text(encoding="utf-8")
runtime = runtime_path.read_text(encoding="utf-8")
xaml = xaml_path.read_text(encoding="utf-8")

# --- Model: one presentation classifier for live points and SOE entries. ---
old = '    public string Value { get => _value; set => Set(ref _value, string.IsNullOrWhiteSpace(value) ? "-" : value); }'
new = '''    public string Value
    {
        get => _value;
        set
        {
            if (Set(ref _value, string.IsNullOrWhiteSpace(value) ? "-" : value))
            {
                Raise(nameof(DisplayValue));
                Raise(nameof(ValueTone));
            }
        }
    }
    public string DisplayValue => Value;
    public string ValueTone => Iec61850ValueStatePresentation.Classify(Value, IecDataType);'''
if model.count(old) != 1:
    raise SystemExit(f"monitor Value property anchor count={model.count(old)}")
model = model.replace(old, new, 1)

old = '''    public string IecReference { get; init; } = string.Empty;
    public string OldValue { get; init; } = "-";'''
new = '''    public string IecReference { get; init; } = string.Empty;
    public string IecDataType { get; init; } = string.Empty;
    public string OldValue { get; init; } = "-";'''
if model.count(old) != 1:
    raise SystemExit(f"event type anchor count={model.count(old)}")
model = model.replace(old, new, 1)

old = '''    public string ChangeText => $"{EdgeType} · {OldValue} → {NewValue}";
    public string EventValue => string.IsNullOrWhiteSpace(NewValue) ? "-" : NewValue;
    public string ValueTone
    {
        get
        {
            var text = EventValue.Trim().ToLowerInvariant();
            if (text.Contains("closed") ||
                text is "true" or "on" or "active" or "asserted" or "1" or "1.0")
                return "Energized";
            if (text.Contains("open") ||
                text is "false" or "off" or "inactive" or "deasserted" or "0" or "0.0")
                return "Deenergized";
            if (text.Contains("intermediate") || text.Contains("bad") ||
                text.Contains("00") || text.Contains("11"))
                return "Abnormal";
            return "Neutral";
        }
    }'''
new = '''    public string ChangeText => $"{EdgeType} · {OldValue} → {NewValue}";
    public string EventValue => string.IsNullOrWhiteSpace(NewValue) ? "-" : NewValue;
    public string DisplayValue => EventValue;
    public string ValueTone => Iec61850ValueStatePresentation.Classify(EventValue, IecDataType);'''
if model.count(old) != 1:
    raise SystemExit(f"event ValueTone anchor count={model.count(old)}")
model = model.replace(old, new, 1)

# --- Runtime: preserve the discovered IEC type on each SOE entry. ---
old = '''            SignalName = point.SignalName,
            IecReference = point.IecReference,
            OldValue = oldValue,'''
new = '''            SignalName = point.SignalName,
            IecReference = point.IecReference,
            IecDataType = point.IecDataType,
            OldValue = oldValue,'''
if runtime.count(old) != 1:
    raise SystemExit(f"event runtime type anchor count={runtime.count(old)}")
runtime = runtime.replace(old, new, 1)

# --- MainWindow: reusable premium state badge. ---
old = '''        Icon="Assets/app-icon.ico" Closing="Window_Closing" PreviewKeyDown="MainWindow_PreviewKeyDown">
    <Grid Margin="12">'''
new = '''        Icon="Assets/app-icon.ico" Closing="Window_Closing" PreviewKeyDown="MainWindow_PreviewKeyDown">
    <Window.Resources>
        <!-- Process state is intentionally separate from health/severity. Active is
             ARSAS blue, inactive is slate, abnormal/intermediate is amber. Red/green
             remain reserved for faults, bad quality, availability and success. -->
        <DataTemplate x:Key="ProcessValueBadgeTemplate">
            <Border x:Name="ProcessValueBadge"
                    CornerRadius="9" Padding="8,3" MinWidth="62"
                    HorizontalAlignment="Left"
                    Background="#F6F8FB" BorderBrush="#DCE3EC" BorderThickness="1">
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <Ellipse x:Name="ProcessValueDot" Width="7" Height="7" Margin="0,0,6,0"
                             Fill="#94A3B8" Stroke="#94A3B8" StrokeThickness="1.2"
                             VerticalAlignment="Center"/>
                    <TextBlock x:Name="ProcessValueText"
                               Text="{Binding DisplayValue}" ToolTip="{Binding DisplayValue}"
                               FontSize="11.8" FontWeight="SemiBold" Foreground="#344054"
                               TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
                </StackPanel>
            </Border>
            <DataTemplate.Triggers>
                <DataTrigger Binding="{Binding ValueTone}" Value="Active">
                    <Setter TargetName="ProcessValueBadge" Property="Background" Value="#EAF4FF"/>
                    <Setter TargetName="ProcessValueBadge" Property="BorderBrush" Value="#B8D8F5"/>
                    <Setter TargetName="ProcessValueText" Property="Foreground" Value="#245F9E"/>
                    <Setter TargetName="ProcessValueDot" Property="Fill" Value="#2F80ED"/>
                    <Setter TargetName="ProcessValueDot" Property="Stroke" Value="#2F80ED"/>
                </DataTrigger>
                <DataTrigger Binding="{Binding ValueTone}" Value="Inactive">
                    <Setter TargetName="ProcessValueBadge" Property="Background" Value="#F3F6F9"/>
                    <Setter TargetName="ProcessValueBadge" Property="BorderBrush" Value="#D5DEE8"/>
                    <Setter TargetName="ProcessValueText" Property="Foreground" Value="#617286"/>
                    <Setter TargetName="ProcessValueDot" Property="Fill" Value="Transparent"/>
                    <Setter TargetName="ProcessValueDot" Property="Stroke" Value="#7C8FA3"/>
                    <Setter TargetName="ProcessValueDot" Property="StrokeThickness" Value="1.7"/>
                </DataTrigger>
                <DataTrigger Binding="{Binding ValueTone}" Value="Abnormal">
                    <Setter TargetName="ProcessValueBadge" Property="Background" Value="#FFF8E6"/>
                    <Setter TargetName="ProcessValueBadge" Property="BorderBrush" Value="#F2D28A"/>
                    <Setter TargetName="ProcessValueText" Property="Foreground" Value="#946200"/>
                    <Setter TargetName="ProcessValueDot" Property="Fill" Value="#F59E0B"/>
                    <Setter TargetName="ProcessValueDot" Property="Stroke" Value="#F59E0B"/>
                </DataTrigger>
            </DataTemplate.Triggers>
        </DataTemplate>
    </Window.Resources>

    <Grid Margin="12">'''
if xaml.count(old) != 1:
    raise SystemExit(f"Window.Resources anchor count={xaml.count(old)}")
xaml = xaml.replace(old, new, 1)

# Explorer and global live monitor now share the same state badge.
replacements = [
    (
        '                                            <DataGridTextColumn Header="Value" Binding="{Binding Value}" Width="0.8*" MinWidth="125"/>',
        '                                            <DataGridTemplateColumn Header="Value" Width="0.8*" MinWidth="125" CellTemplate="{StaticResource ProcessValueBadgeTemplate}"/>'
    ),
    (
        '                                <DataGridTextColumn Header="Value" Binding="{Binding Value}" Width="125"/>',
        '                                <DataGridTemplateColumn Header="Value" Width="125" CellTemplate="{StaticResource ProcessValueBadgeTemplate}"/>'
    ),
]
for old, new in replacements:
    if xaml.count(old) != 1:
        raise SystemExit(f"live Value column anchor count={xaml.count(old)}: {old}")
    xaml = xaml.replace(old, new, 1)

old = '''                                <TextBlock Text="SCADA/SAS SOE: each row shows the new process value directly. Closed/ON/true is red, Open/OFF/false is green, and intermediate/bad states are amber. Non-events stay out."
                                           FontSize="12.2" Foreground="{StaticResource Muted}" Margin="0,3,0,0"/>'''
new = '''                                <TextBlock Text="SCADA/SAS SOE: state values use ARSAS blue for active and slate for inactive; intermediate/bad states use amber. Color describes state, not alarm severity. Non-events stay out."
                                           FontSize="12.2" Foreground="{StaticResource Muted}" Margin="0,3,0,0"/>'''
if xaml.count(old) != 1:
    raise SystemExit(f"SOE explanatory copy anchor count={xaml.count(old)}")
xaml = xaml.replace(old, new, 1)

old = '''                                <DataGridTemplateColumn Header="Value" Width="150">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <Border x:Name="ValueBadge" CornerRadius="9" Padding="10,4" HorizontalAlignment="Left"
                                                    Background="#F1F5F9" BorderBrush="#D8E0EA" BorderThickness="1">
                                                <TextBlock x:Name="ValueText" Text="{Binding EventValue}" FontWeight="SemiBold" Foreground="#344054"/>
                                            </Border>
                                            <DataTemplate.Triggers>
                                                <DataTrigger Binding="{Binding ValueTone}" Value="Energized">
                                                    <Setter TargetName="ValueBadge" Property="Background" Value="#FEE4E2"/>
                                                    <Setter TargetName="ValueBadge" Property="BorderBrush" Value="#FDA29B"/>
                                                    <Setter TargetName="ValueText" Property="Foreground" Value="#B42318"/>
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding ValueTone}" Value="Deenergized">
                                                    <Setter TargetName="ValueBadge" Property="Background" Value="#ECFDF3"/>
                                                    <Setter TargetName="ValueBadge" Property="BorderBrush" Value="#86EFAC"/>
                                                    <Setter TargetName="ValueText" Property="Foreground" Value="#067647"/>
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding ValueTone}" Value="Abnormal">
                                                    <Setter TargetName="ValueBadge" Property="Background" Value="#FFFAEB"/>
                                                    <Setter TargetName="ValueBadge" Property="BorderBrush" Value="#FEC84B"/>
                                                    <Setter TargetName="ValueText" Property="Foreground" Value="#B54708"/>
                                                </DataTrigger>
                                            </DataTemplate.Triggers>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>'''
new = '''                                <DataGridTemplateColumn Header="Value" Width="150" CellTemplate="{StaticResource ProcessValueBadgeTemplate}"/>'''
if xaml.count(old) != 1:
    raise SystemExit(f"Event Value badge anchor count={xaml.count(old)}")
xaml = xaml.replace(old, new, 1)

# Command-panel current state follows the same state palette. Control action button
# colors are intentionally untouched because they represent actions, not read values.
command_replacements = [
    ('<Setter Property="Background" Value="#FEF3F2"/>\n                                                                                    <Setter Property="BorderBrush" Value="#FECDCA"/>',
     '<Setter Property="Background" Value="#EAF4FF"/>\n                                                                                    <Setter Property="BorderBrush" Value="#B8D8F5"/>'),
    ('<Setter Property="Background" Value="#ECFDF3"/>\n                                                                                    <Setter Property="BorderBrush" Value="#ABEFC6"/>',
     '<Setter Property="Background" Value="#F3F6F9"/>\n                                                                                    <Setter Property="BorderBrush" Value="#D5DEE8"/>'),
    ('<DataTrigger Binding="{Binding ControlCurrentTone}" Value="Energized"><Setter Property="Foreground" Value="#B42318"/></DataTrigger>',
     '<DataTrigger Binding="{Binding ControlCurrentTone}" Value="Energized"><Setter Property="Foreground" Value="#245F9E"/></DataTrigger>'),
    ('<DataTrigger Binding="{Binding ControlCurrentTone}" Value="Deenergized"><Setter Property="Foreground" Value="#067647"/></DataTrigger>',
     '<DataTrigger Binding="{Binding ControlCurrentTone}" Value="Deenergized"><Setter Property="Foreground" Value="#617286"/></DataTrigger>'),
]
for old, new in command_replacements:
    if xaml.count(old) != 1:
        raise SystemExit(f"command value palette anchor count={xaml.count(old)}: {old[:90]}")
    xaml = xaml.replace(old, new, 1)

model_path.write_text(model, encoding="utf-8", newline="\n")
runtime_path.write_text(runtime, encoding="utf-8", newline="\n")
xaml_path.write_text(xaml, encoding="utf-8", newline="\n")
print("Applied semantic premium process-value badges across Explorer, Live Monitor, Event Log and command current-value display.")
