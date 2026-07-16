from pathlib import Path
import re


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)


# Models/GooseSubscriberModels.cs
path = Path("Models/GooseSubscriberModels.cs")
text = path.read_text(encoding="utf-8")
text = replace_once(
    text,
    '''    public string MacAddress { get; init; } = string.Empty;
    public string Selector => Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string DisplayText => $"[{Index}] {(!string.IsNullOrWhiteSpace(Description) ? Description : Name)}";
    public string DetailText => string.IsNullOrWhiteSpace(MacAddress) ? Name : $"{MacAddress} • {Name}";''',
    '''    public string MacAddress { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = string.Empty;
    public string Selector => Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string DisplayText => $"[{Index}] {FirstReadable(FriendlyName, Description, Name, "Network adapter")}";
    public string DetailText => string.Join(" • ", new[] { FriendlyName, Description, MacAddress, Name }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string FirstReadable(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "Network adapter";''',
    "adapter presentation",
)
text = replace_once(
    text,
    '''    private string _bindingSource = "Unbound";
    private bool _isChanged;''',
    '''    private string _bindingSource = "Unbound";
    private bool _isChanged;
    private bool _isHighlighted;
    private DateTimeOffset _highlightUntilUtc;''',
    "leaf highlight fields",
)
text = replace_once(
    text,
    '''    public bool IsChanged { get => _isChanged; set => Set(ref _isChanged, value); }
    public string TypeText => string.Join(" / ", new[] { Cdc, BType }.Where(item => !string.IsNullOrWhiteSpace(item)));''',
    '''    public bool IsChanged { get => _isChanged; set => Set(ref _isChanged, value); }
    public bool IsHighlighted { get => _isHighlighted; private set => Set(ref _isHighlighted, value); }
    public string TypeText => string.Join(" / ", new[] { Cdc, BType }.Where(item => !string.IsNullOrWhiteSpace(item)));

    public bool ExpireHighlight(DateTimeOffset nowUtc)
    {
        if (!IsHighlighted || nowUtc < _highlightUntilUtc)
            return false;
        IsHighlighted = false;
        return true;
    }''',
    "leaf highlight property",
)
text = replace_once(
    text,
    '''        BindingSource = snapshot.BindingSource;
        IsChanged = snapshot.IsChanged;
        Raise(nameof(TypeText));''',
    '''        BindingSource = snapshot.BindingSource;
        IsChanged = snapshot.IsChanged;
        if (snapshot.IsChanged)
        {
            _highlightUntilUtc = DateTimeOffset.UtcNow.AddSeconds(5);
            IsHighlighted = true;
        }
        Raise(nameof(TypeText));''',
    "leaf highlight arm",
)
path.write_text(text, encoding="utf-8")

# Models/GoosePresentationModels.cs
path = Path("Models/GoosePresentationModels.cs")
text = path.read_text(encoding="utf-8")
text = replace_once(text, "public sealed class GooseEventRow\n{", "public sealed class GooseEventRow : ObservableObject\n{", "event observable")
text = replace_once(
    text,
    '''    public required string Summary { get; init; }

    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);''',
    '''    public required string Summary { get; init; }

    private bool _isRecent = true;
    public bool IsRecent { get => _isRecent; private set => Set(ref _isRecent, value); }
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public bool ExpireHighlight(DateTimeOffset nowUtc)
    {
        if (!IsRecent || nowUtc - Timestamp < TimeSpan.FromSeconds(5))
            return false;
        IsRecent = false;
        return true;
    }''',
    "event recent expiry",
)
path.write_text(text, encoding="utf-8")

# MainWindow.GooseTimeline.cs
path = Path("MainWindow.GooseTimeline.cs")
text = path.read_text(encoding="utf-8")
text = replace_once(text, "private const int MaxGooseTimelineEvents = 1000;\n    private const int MaxPendingGooseTimelineEvents = 4096;", "private const int MaxGooseTimelineEvents = 300;\n    private const int MaxPendingGooseTimelineEvents = 512;", "timeline limits")
text = replace_once(
    text,
    '''    private GooseEventRow? _selectedGooseEvent;
    private bool _goosePresentationInstalled;''',
    '''    private GooseEventRow? _selectedGooseEvent;
    private bool _goosePresentationInstalled;
    private DateTimeOffset _nextGooseHighlightExpiryCheckUtc = DateTimeOffset.MinValue;''',
    "timeline expiry field",
)
old = '''    private void GooseTimelineUiFlushTimer_Tick(object? sender, EventArgs args)
    {
        if (_pendingGooseTimeline.IsEmpty)
        {
            Raise(nameof(GoosePublisherCountText));
            Raise(nameof(GooseSelectedLeafCountText));
            return;
        }

        var processed = 0;
        while (processed < 256 && _pendingGooseTimeline.TryDequeue(out var captured))'''
new = '''    private void GooseTimelineUiFlushTimer_Tick(object? sender, EventArgs args)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        if (nowUtc >= _nextGooseHighlightExpiryCheckUtc)
        {
            ExpireGooseHighlights(nowUtc);
            _nextGooseHighlightExpiryCheckUtc = nowUtc.AddSeconds(1);
        }

        if (_pendingGooseTimeline.IsEmpty)
            return;

        var processed = 0;
        while (processed < 48 && _pendingGooseTimeline.TryDequeue(out var captured))'''
text = replace_once(text, old, new, "timeline flush")
insert_anchor = '''    private GooseEventRow BuildGooseEventRow(
        GooseSubscriberFrameSnapshot captured,
        GooseStreamSnapshot stream)'''
insert_code = '''    private void ExpireGooseHighlights(DateTimeOffset nowUtc)
    {
        foreach (var eventRow in GooseEvents)
            eventRow.ExpireHighlight(nowUtc);

        foreach (var stream in GooseStreams)
        {
            foreach (var leaf in stream.Leaves)
                leaf.ExpireHighlight(nowUtc);
        }
    }

''' + insert_anchor
text = replace_once(text, insert_anchor, insert_code, "highlight expiry method")
text = replace_once(
    text,
    '''        _lastGooseTimelineTimestamp.Clear();
        GooseEvents.Clear();''',
    '''        _lastGooseTimelineTimestamp.Clear();
        _nextGooseHighlightExpiryCheckUtc = DateTimeOffset.MinValue;
        GooseEvents.Clear();''',
    "reset expiry",
)
path.write_text(text, encoding="utf-8")

# MainWindow.GooseSubscriber.cs: semantic values and fixed internal filter
path = Path("MainWindow.GooseSubscriber.cs")
text = path.read_text(encoding="utf-8")
text = replace_once(text, '    private string _gooseCaptureFilter = GooseSubscriberRuntime.DefaultCaptureFilter;\n', '', "remove editable filter field")
text = replace_once(text, '    public string GooseCaptureFilter { get => _gooseCaptureFilter; set => Set(ref _gooseCaptureFilter, value ?? string.Empty); }\n', '', "remove editable filter property")
text = replace_once(text, '                GooseCaptureFilter,\n', '                GooseSubscriberRuntime.DefaultCaptureFilter,\n', "fixed capture filter")
old_value = '''            var value = index < rawValueCount
                ? decoded?.DisplayValue ?? MmsDataValueRenderer.ToCompactString(frame.Pdu.Values[index], signalReference)
                : "<missing in frame>";

            leaves.Add(new GooseLeafValueSnapshot('''
new_value = '''            var rawValue = index < rawValueCount
                ? decoded?.DisplayValue ?? MmsDataValueRenderer.ToCompactString(frame.Pdu.Values[index], signalReference)
                : "<missing in frame>";
            var value = InterpretGooseLeafValue(rawValue, definition?.Cdc, definition?.BType);
            var previousValue = InterpretGooseLeafValue(decoded?.PreviousDisplayValue ?? string.Empty, definition?.Cdc, definition?.BType);

            leaves.Add(new GooseLeafValueSnapshot('''
text = replace_once(text, old_value, new_value, "semantic value decode")
text = replace_once(text, '                decoded?.PreviousDisplayValue ?? string.Empty,\n', '                previousValue,\n', "semantic previous value")
anchor = '''    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;'''
helper = '''    private static string InterpretGooseLeafValue(string? rawValue, string? cdc, string? bType)
    {
        var text = rawValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || text == "<missing in frame>")
            return text;

        var bitString = Regex.Match(text, @"^bits\((?<hex>[0-9A-Fa-f]{2}),\s*unused=(?<unused>[67])\)$", RegexOptions.CultureInvariant);
        if (!bitString.Success || !byte.TryParse(bitString.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return text;

        var unused = bitString.Groups["unused"].Value;
        if (unused == "7")
            return (value & 0x80) == 0 ? "false" : "true";

        var doublePoint = (value >> 6) & 0x03;
        return doublePoint switch
        {
            0 => "Intermediate [00]",
            1 => "Open [01]",
            2 => "Closed [10]",
            _ => "Invalid [11]"
        };
    }

''' + anchor
text = replace_once(text, anchor, helper, "semantic interpreter helper")
path.write_text(text, encoding="utf-8")

# Services/GooseSubscriberRuntime.cs: real Windows adapter name
path = Path("Services/GooseSubscriberRuntime.cs")
text = path.read_text(encoding="utf-8")
text = replace_once(text, "using System.Diagnostics;\n", "using System.Diagnostics;\nusing System.Net.NetworkInformation;\nusing System.Text.RegularExpressions;\n", "network imports")
old_list = '''    public IReadOnlyList<GooseAdapterOption> ListAdapters()
        => NpcapAdapterCatalog.ListAdapters()
            .Select(adapter => new GooseAdapterOption
            {
                Index = adapter.Index,
                Name = adapter.Name,
                Description = adapter.Description,
                MacAddress = adapter.MacAddress?.ToString() ?? string.Empty
            })
            .ToArray();'''
new_list = '''    public IReadOnlyList<GooseAdapterOption> ListAdapters()
    {
        var windowsAdapters = NetworkInterface.GetAllNetworkInterfaces();
        return NpcapAdapterCatalog.ListAdapters()
            .Select(adapter =>
            {
                var macAddress = adapter.MacAddress?.ToString() ?? string.Empty;
                return new GooseAdapterOption
                {
                    Index = adapter.Index,
                    Name = adapter.Name,
                    Description = adapter.Description,
                    MacAddress = macAddress,
                    FriendlyName = ResolveAdapterFriendlyName(adapter.Name, adapter.Description, macAddress, windowsAdapters)
                };
            })
            .ToArray();
    }

    private static string ResolveAdapterFriendlyName(
        string captureName,
        string captureDescription,
        string macAddress,
        IReadOnlyList<NetworkInterface> windowsAdapters)
    {
        var normalizedMac = NormalizeMac(macAddress);
        var captureId = ExtractAdapterId(captureName);
        var match = windowsAdapters.FirstOrDefault(adapter =>
            (!string.IsNullOrWhiteSpace(normalizedMac) && NormalizeMac(adapter.GetPhysicalAddress().ToString()) == normalizedMac) ||
            (!string.IsNullOrWhiteSpace(captureId) && adapter.Id.Equals(captureId, StringComparison.OrdinalIgnoreCase)));

        if (match is not null)
        {
            var windowsName = CleanAdapterLabel(match.Name);
            if (!string.IsNullOrWhiteSpace(windowsName))
                return windowsName;
            var windowsDescription = CleanAdapterLabel(match.Description);
            if (!string.IsNullOrWhiteSpace(windowsDescription))
                return windowsDescription;
        }

        return FirstAdapterLabel(CleanAdapterLabel(captureDescription), CleanAdapterLabel(captureName), "Network adapter");
    }

    private static string ExtractAdapterId(string? value)
    {
        var match = Regex.Match(value ?? string.Empty, @"\{(?<id>[0-9A-Fa-f-]{36})\}");
        return match.Success ? match.Groups["id"].Value : string.Empty;
    }

    private static string NormalizeMac(string? value)
        => Regex.Replace(value ?? string.Empty, "[^0-9A-Fa-f]", string.Empty).ToUpperInvariant();

    private static string CleanAdapterLabel(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Equals("ArIED61850", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("ArIED 61850", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return text;
    }

    private static string FirstAdapterLabel(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Network adapter";'''
text = replace_once(text, old_list, new_list, "adapter friendly mapping")
path.write_text(text, encoding="utf-8")

# Views/GooseSubscriberView.xaml: remove advanced filter and use 5-second transient highlights
path = Path("Views/GooseSubscriberView.xaml")
text = path.read_text(encoding="utf-8")
text = replace_once(
    text,
    '''                        <ColumnDefinition Width="280"/>
                        <ColumnDefinition Width="4"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="12"/>
                        <ColumnDefinition Width="225"/>
                        <ColumnDefinition Width="4"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="6"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="6"/>
                        <ColumnDefinition Width="Auto"/>''',
    '''                        <ColumnDefinition Width="340"/>
                        <ColumnDefinition Width="4"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="10"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="6"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="6"/>
                        <ColumnDefinition Width="Auto"/>''',
    "capture bar columns",
)
old_controls = '''                    <ComboBox Grid.Column="2" ItemsSource="{Binding GooseAdapters}"
                              SelectedItem="{Binding SelectedGooseAdapter, Mode=TwoWay}"
                              DisplayMemberPath="DisplayText" Style="{StaticResource ModernComboBox}"
                              MinHeight="34" Height="34" Padding="10,4"
                              IsEnabled="{Binding CanRefreshGooseConfiguration}"
                              ToolTip="{Binding SelectedGooseAdapterDetail}"/>
                    <Button Grid.Column="4" Style="{StaticResource IedIconButton}"
                            Click="RefreshAdapters_Click"
                            IsEnabled="{Binding CanRefreshGooseConfiguration}" ToolTip="Refresh network adapters">
                        <Viewbox Width="15" Height="15">
                            <Path Data="{StaticResource LucideRefreshCw}" Style="{StaticResource LucideIcon}"/>
                        </Viewbox>
                    </Button>

                    <TextBox Grid.Column="6" Text="{Binding GooseCaptureFilter, UpdateSourceTrigger=PropertyChanged}"
                             MinHeight="34" Height="34" Padding="10,4"
                             FontFamily="Cascadia Mono, Consolas" FontSize="10.2"
                             IsEnabled="{Binding CanRefreshGooseConfiguration}"
                             ToolTip="Advanced BPF filter for GOOSE Ethernet frames"/>
                    <Button Grid.Column="8" Style="{StaticResource IedIconButton}"
                            Click="RefreshModels_Click"
                            IsEnabled="{Binding CanRefreshGooseConfiguration}"
                            ToolTip="Refresh signal names from loaded SCL and live IED discovery">
                        <Viewbox Width="15" Height="15">
                            <Path Data="{StaticResource LucideSlidersHorizontal}" Style="{StaticResource LucideIcon}"/>
                        </Viewbox>
                    </Button>

                    <Button Grid.Column="10" Style="{StaticResource CommandOpenButton}"
                            Click="StartCapture_Click" IsEnabled="{Binding CanStartGooseSubscriber}"'''
new_controls = '''                    <ComboBox Grid.Column="2" ItemsSource="{Binding GooseAdapters}"
                              SelectedItem="{Binding SelectedGooseAdapter, Mode=TwoWay}"
                              Style="{StaticResource ModernComboBox}"
                              MinHeight="34" Height="34" Padding="10,4"
                              IsHitTestVisible="{Binding CanRefreshGooseConfiguration}"
                              Focusable="{Binding CanRefreshGooseConfiguration}"
                              ToolTip="{Binding SelectedGooseAdapterDetail}">
                        <ComboBox.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding DisplayText}" TextTrimming="CharacterEllipsis"/>
                            </DataTemplate>
                        </ComboBox.ItemTemplate>
                    </ComboBox>
                    <Button Grid.Column="4" Style="{StaticResource IedIconButton}"
                            Click="RefreshAdapters_Click"
                            IsEnabled="{Binding CanRefreshGooseConfiguration}" ToolTip="Refresh network adapters">
                        <Viewbox Width="15" Height="15">
                            <Path Data="{StaticResource LucideRefreshCw}" Style="{StaticResource LucideIcon}"/>
                        </Viewbox>
                    </Button>

                    <Button Grid.Column="6" Style="{StaticResource IedIconButton}"
                            Click="RefreshModels_Click"
                            IsEnabled="{Binding CanRefreshGooseConfiguration}"
                            ToolTip="Refresh signal names from loaded SCL and live IED discovery">
                        <Viewbox Width="15" Height="15">
                            <Path Data="{StaticResource LucideSlidersHorizontal}" Style="{StaticResource LucideIcon}"/>
                        </Viewbox>
                    </Button>

                    <Button Grid.Column="8" Style="{StaticResource CommandOpenButton}"
                            Click="StartCapture_Click" IsEnabled="{Binding CanStartGooseSubscriber}"'''
text = replace_once(text, old_controls, new_controls, "remove advanced filter")
text = text.replace('Grid.Column="12" Style="{StaticResource SoftButton}"', 'Grid.Column="10" Style="{StaticResource SoftButton}"', 1)
text = text.replace('Grid.Column="14" Style="{StaticResource SoftButton}"', 'Grid.Column="12" Style="{StaticResource SoftButton}"', 1)
old_rows = '''                                        <DataTrigger Binding="{Binding EventTone}" Value="Warning">
                                            <Setter Property="Background" Value="#FFFAEB"/>
                                            <Setter Property="BorderBrush" Value="#FEC84B"/>
                                            <Setter Property="BorderThickness" Value="3,0,0,1"/>
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding EventTone}" Value="Change">
                                            <Setter Property="Background" Value="#EEF5FF"/>
                                            <Setter Property="BorderBrush" Value="#84ADFF"/>
                                            <Setter Property="BorderThickness" Value="3,0,0,1"/>
                                        </DataTrigger>'''
new_rows = '''                                        <MultiDataTrigger>
                                            <MultiDataTrigger.Conditions>
                                                <Condition Binding="{Binding EventTone}" Value="Warning"/>
                                                <Condition Binding="{Binding IsRecent}" Value="True"/>
                                            </MultiDataTrigger.Conditions>
                                            <Setter Property="Background" Value="#FFFAEB"/>
                                            <Setter Property="BorderBrush" Value="#FEC84B"/>
                                            <Setter Property="BorderThickness" Value="3,0,0,1"/>
                                        </MultiDataTrigger>
                                        <MultiDataTrigger>
                                            <MultiDataTrigger.Conditions>
                                                <Condition Binding="{Binding EventTone}" Value="Change"/>
                                                <Condition Binding="{Binding IsRecent}" Value="True"/>
                                            </MultiDataTrigger.Conditions>
                                            <Setter Property="Background" Value="#EEF5FF"/>
                                            <Setter Property="BorderBrush" Value="#84ADFF"/>
                                            <Setter Property="BorderThickness" Value="3,0,0,1"/>
                                        </MultiDataTrigger>'''
text = replace_once(text, old_rows, new_rows, "transient event highlight")
text = replace_once(text, '<DataTrigger Binding="{Binding IsChanged}" Value="True">\n                                                    <Setter TargetName="LeafValueBadge"', '<DataTrigger Binding="{Binding IsHighlighted}" Value="True">\n                                                    <Setter TargetName="LeafValueBadge"', "transient leaf highlight")
path.write_text(text, encoding="utf-8")

# CI invariants
path = Path(".github/workflows/build.yml")
text = path.read_text(encoding="utf-8")
text = replace_once(
    text,
    '''          $gooseModels = Get-Content .\\ArIED61850Tester\\Models\\GooseSubscriberModels.cs -Raw
''',
    '''          $gooseModels = Get-Content .\\ArIED61850Tester\\Models\\GooseSubscriberModels.cs -Raw
          $goosePresentation = Get-Content .\\ArIED61850Tester\\Models\\GoosePresentationModels.cs -Raw
          $gooseTimeline = Get-Content .\\ArIED61850Tester\\MainWindow.GooseTimeline.cs -Raw
          $gooseView = Get-Content .\\ArIED61850Tester\\Views\\GooseSubscriberView.xaml -Raw
''',
    "ci presentation sources",
)
anchor = '''          if ($sasPolicy -notmatch 'public static bool IsVisible' -or'''
check = '''          if ($gooseView -match 'GooseCaptureFilter' -or
              $gooseView -notmatch 'Binding IsRecent' -or
              $gooseView -notmatch 'Binding IsHighlighted' -or
              $gooseTimeline -notmatch 'MaxGooseTimelineEvents = 300' -or
              $gooseTimeline -notmatch 'ExpireGooseHighlights' -or
              $goosePresentation -notmatch 'TimeSpan.FromSeconds\\(5\\)' -or
              $gooseModels -notmatch 'FriendlyName' -or
              $gooseRuntime -notmatch 'ResolveAdapterFriendlyName' -or
              $gooseUi -notmatch 'InterpretGooseLeafValue' -or
              $gooseUi -notmatch 'Open \\[01\\]' -or
              $gooseUi -notmatch 'Closed \\[10\\]') {
            throw "Lightweight GOOSE presentation, five-second highlights, semantic values, or adapter identity regressed."
          }

''' + anchor
text = replace_once(text, anchor, check, "ci lightweight checks")
path.write_text(text, encoding="utf-8")

print("Applied lightweight GOOSE workspace corrections.")
