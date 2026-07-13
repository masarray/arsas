from __future__ import annotations

from pathlib import Path
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]


def read_source(relative_path: str) -> tuple[Path, str, str]:
    path = ROOT / relative_path
    raw = path.read_bytes()
    newline = "\r\n" if b"\r\n" in raw else "\n"
    text = raw.decode("utf-8").replace("\r\n", "\n")
    return path, text, newline


def write_source(path: Path, text: str, newline: str) -> None:
    normalized = text.replace("\r\n", "\n")
    path.write_bytes(normalized.replace("\n", newline).encode("utf-8"))


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


def patch_main_window_xaml() -> None:
    path, text, newline = read_source("MainWindow.xaml")

    text = replace_once(
        text,
        '                       Text="Fast workflow: add IED → discover live model → choose signals → monitor each IED independently"',
        '                       Text="Fast workflow: add IED or open SCL → verify live model → choose signals → monitor independently"',
        "header workflow text",
    )

    old_actions = """                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="6"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>
                                    <Button Grid.Column="0" Click="ConnectAllIeds_Click" Style="{StaticResource SoftButton}" Padding="8,7" FontSize="11.5"
                                            ToolTip="Fast-connect every saved IED and start live monitoring for its saved selections">
                                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                                            <Viewbox Width="13" Height="13" Margin="0,0,4,0">
                                                <Canvas Width="24" Height="24">
                                                    <Path Data="M5,3 L19,12 L5,21 Z" Stroke="#16A34A" StrokeThickness="2" StrokeLineJoin="Round" Fill="Transparent"/>
                                                </Canvas>
                                            </Viewbox>
                                            <TextBlock Text="Connect All" FontWeight="SemiBold"/>
                                        </StackPanel>
                                    </Button>
                                    <Button Grid.Column="2" Click="AddRelay_Click" Style="{StaticResource PrimaryButton}" Padding="8,7" FontSize="11.5"
                                            ToolTip="Add another IEC 61850 IED">
                                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                                            <TextBlock Text="＋" FontSize="15" FontWeight="SemiBold" Margin="0,-1,5,0"/>
                                            <TextBlock Text="Add IED" FontWeight="SemiBold"/>
                                        </StackPanel>
                                    </Button>
                                </Grid>
"""

    new_actions = """                                <Grid>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="Auto"/>
                                        <RowDefinition Height="6"/>
                                        <RowDefinition Height="Auto"/>
                                    </Grid.RowDefinitions>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="6"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>

                                    <Button Grid.Row="0" Grid.ColumnSpan="3" Click="ConnectAllIeds_Click"
                                            Style="{StaticResource SoftButton}" Padding="8,7" FontSize="11.5"
                                            ToolTip="Fast-connect every saved or SCL-imported IED and start live monitoring for saved selections">
                                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                                            <Viewbox Width="13" Height="13" Margin="0,0,4,0">
                                                <Canvas Width="24" Height="24">
                                                    <Path Data="M5,3 L19,12 L5,21 Z" Stroke="#16A34A" StrokeThickness="2" StrokeLineJoin="Round" Fill="Transparent"/>
                                                </Canvas>
                                            </Viewbox>
                                            <TextBlock Text="Connect All" FontWeight="SemiBold"/>
                                        </StackPanel>
                                    </Button>

                                    <Button Grid.Row="2" Grid.Column="0" Click="OpenScl_Click"
                                            Style="{StaticResource SoftButton}" Padding="7,7" FontSize="11.2"
                                            ToolTip="Import one or more IED endpoints from SCD, CID, ICD, IID, SSD, or XML SCL files">
                                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                                            <Viewbox Width="13" Height="13" Margin="0,0,4,0">
                                                <Canvas Width="24" Height="24">
                                                    <Path Data="M6,2 H14 L19,7 V22 H6 Z M14,2 V7 H19" Stroke="#2563EB" StrokeThickness="1.8" StrokeLineJoin="Round" Fill="Transparent"/>
                                                </Canvas>
                                            </Viewbox>
                                            <TextBlock Text="Open SCL" FontWeight="SemiBold"/>
                                        </StackPanel>
                                    </Button>

                                    <Button Grid.Row="2" Grid.Column="2" Click="AddRelay_Click"
                                            Style="{StaticResource PrimaryButton}" Padding="7,7" FontSize="11.2"
                                            ToolTip="Add an IEC 61850 IED by IP address">
                                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                                            <TextBlock Text="＋" FontSize="15" FontWeight="SemiBold" Margin="0,-1,4,0"/>
                                            <TextBlock Text="Add IED" FontWeight="SemiBold"/>
                                        </StackPanel>
                                    </Button>
                                </Grid>
"""

    text = replace_once(text, old_actions, new_actions, "IED Explorer fast actions")
    write_source(path, text, newline)


def patch_main_window_code() -> None:
    path, text, newline = read_source("MainWindow.xaml.cs")

    anchor = """    private async void AddRelay_Click(object sender, RoutedEventArgs e)
    {
        var initialIp = SelectedDevice?.IpAddress ?? NewDeviceIp;
        var initialPort = SelectedDevice?.Port ?? 102;
        var wizard = new IpConnectWizardWindow(initialIp, initialPort) { Owner = this };
        if (wizard.ShowDialog() != true)
            return;

        NewDeviceIp = wizard.RelayIpAddress;
        NewDevicePort = wizard.MmsPort.ToString(CultureInfo.InvariantCulture);
        await AddOrDiscoverEndpointAsync(wizard.RelayIpAddress, wizard.MmsPort);
    }


"""

    replacement = anchor + """    private async void OpenScl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open IEC 61850 SCL",
            Filter = "IEC 61850 SCL (*.scd;*.cid;*.icd;*.iid;*.ssd)|*.scd;*.cid;*.icd;*.iid;*.ssd|XML SCL (*.xml)|*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var sourceName = Path.GetFileName(dialog.FileName);
        SetStatus($"Reading IED endpoints from {sourceName}…");
        try
        {
            var result = await SclImportService.LoadAsync(dialog.FileName, _applicationCancellation.Token);
            foreach (var warning in result.Warnings.Take(25))
                AddLog("WARN", "SCL", warning);
            if (result.Warnings.Count > 25)
                AddLog("WARN", "SCL", $"{result.Warnings.Count - 25} additional SCL warning(s) were omitted from the live log.");

            if (result.Endpoints.Count == 0)
            {
                var reason = result.ConnectedAccessPointCount == 0
                    ? "No ConnectedAP communication entries were found."
                    : "ConnectedAP entries were found, but none contained a valid IP address.";
                SetStatus($"{sourceName}: no usable IEC 61850 MMS endpoints. {reason}");
                AddLog("WARN", "SCL", $"{sourceName}: {reason}");
                MessageBox.Show(
                    this,
                    $"No usable IEC 61850 MMS endpoint was found in {sourceName}.\n\n{reason}\n\nThe file may contain only an IED template without a Communication section.",
                    "Open SCL",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var added = 0;
            var refreshed = 0;
            var retained = 0;
            Iec61850MonitorDevice? firstImported = null;

            foreach (var endpoint in result.Endpoints)
            {
                var device = Devices.FirstOrDefault(item =>
                    item.IpAddress.Equals(endpoint.IpAddress, StringComparison.OrdinalIgnoreCase) &&
                    item.Port == endpoint.Port);

                if (device == null)
                {
                    device = new Iec61850MonitorDevice
                    {
                        Name = endpoint.IedName,
                        IdentitySource = $"SCL • {sourceName}",
                        LogicalDeviceSummary = BuildSclEndpointSummary(endpoint),
                        IpAddress = endpoint.IpAddress,
                        Port = endpoint.Port,
                        AllowDynamicDataSetWrites = true,
                        Status = "SCL endpoint ready",
                        Detail = $"Imported from {sourceName}. Press Play to connect and verify the live IEC 61850 model.",
                        AcquisitionMode = "SCL • live discovery pending"
                    };
                    Devices.Add(device);
                    added++;
                }
                else if (!device.IsConnected && !device.IsBusy && !device.HasDiscoveryCache)
                {
                    if (string.IsNullOrWhiteSpace(device.Name) ||
                        device.Name.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase))
                    {
                        device.Name = endpoint.IedName;
                    }
                    device.IdentitySource = $"SCL • {sourceName}";
                    device.LogicalDeviceSummary = BuildSclEndpointSummary(endpoint);
                    device.Status = "SCL endpoint ready";
                    device.Detail = $"Endpoint refreshed from {sourceName}. Press Play to connect and verify the live IEC 61850 model.";
                    device.AcquisitionMode = "SCL • live discovery pending";
                    device.RefreshComputed();
                    refreshed++;
                }
                else
                {
                    // Preserve active sessions and successful discovery caches. SCL is an
                    // endpoint-import path, never authority over a verified live model.
                    retained++;
                }

                firstImported ??= device;
            }

            if (firstImported != null)
                SelectedDevice = firstImported;
            MainTabs.SelectedIndex = 0;
            UpdateNavigationVisuals(0, animate: true);
            RaiseWorkspaceCounts();

            var warningText = result.Warnings.Count == 0 ? string.Empty : $", {result.Warnings.Count} warning(s)";
            var status = $"{sourceName}: {result.Endpoints.Count} SCL endpoint(s) read — {added} added, {refreshed} refreshed, {retained} existing retained{warningText}. Use Play or Connect All for live verification.";
            SetStatus(status);
            AddLog("INFO", "SCL", status);
        }
        catch (OperationCanceledException)
        {
            SetStatus($"{sourceName}: SCL import cancelled.");
        }
        catch (Exception ex)
        {
            AddLog("ERROR", "SCL", $"Could not open {sourceName}: {ex.Message}");
            SetStatus($"{sourceName}: SCL import failed. Diagnostics is marked with !.");
            MarkDiagnosticAlert();
            MessageBox.Show(
                this,
                $"ArIED could not read this SCL file.\n\n{ex.Message}",
                "Open SCL",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string BuildSclEndpointSummary(SclIedEndpoint endpoint)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(endpoint.AccessPointName))
            parts.Add($"AP {endpoint.AccessPointName}");
        if (!string.IsNullOrWhiteSpace(endpoint.SubNetworkName))
            parts.Add(endpoint.SubNetworkName);
        return parts.Count == 0 ? "SCL endpoint" : string.Join(" • ", parts);
    }


"""

    text = replace_once(text, anchor, replacement, "Open SCL command handler")
    write_source(path, text, newline)


def patch_readme() -> None:
    path, text, newline = read_source("README.md")
    text = replace_once(
        text,
        """Add or open IED
→ discover or restore cached model
""",
        """Add IED by IP, open a project, or import SCL
→ connect and verify the live model, or restore a cached model
""",
        "README core workflow",
    )
    text = replace_once(
        text,
        """- Real IEDName and Logical Device boundary resolution.
- Multi-IED independent connections and monitoring.
""",
        """- Real IEDName and Logical Device boundary resolution.
- SCL endpoint import from SCD, CID, ICD, IID, SSD, or XML files, including multi-IED ConnectedAP/IP extraction and duplicate protection.
- Multi-IED independent connections and monitoring.
""",
        "README SCL capability",
    )
    write_source(path, text, newline)


def validate() -> None:
    ET.parse(ROOT / "MainWindow.xaml")
    main_window = (ROOT / "MainWindow.xaml.cs").read_text(encoding="utf-8")
    service = (ROOT / "Services" / "SclImportService.cs").read_text(encoding="utf-8")
    required = [
        "OpenScl_Click",
        "SclImportService.LoadAsync",
        "BuildSclEndpointSummary",
        "SCL endpoint ready",
        "DtdProcessing.Prohibit",
        "ConnectedAP",
    ]
    combined = main_window + service
    missing = [token for token in required if token not in combined]
    if missing:
        raise RuntimeError(f"SCL import validation missing: {', '.join(missing)}")


if __name__ == "__main__":
    patch_main_window_xaml()
    patch_main_window_code()
    patch_readme()
    validate()
    print("SCL import source patch and validation completed")
