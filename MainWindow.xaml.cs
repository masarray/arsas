using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AR.Iec61850.Scl.Export;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly Iec61850MonitorRuntime _runtime = new();
    private readonly SclWorkspaceService _sclWorkspaceService = new();
    private readonly CancellationTokenSource _applicationCancellation = new();
    private readonly Dictionary<string, HashSet<string>> _pendingProjectSelections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SignalDefinition, Iec61850MonitorDevice> _signalOwners = new();
    private readonly ConcurrentDictionary<string, PendingPointUpdate> _pendingPointSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Iec61850MonitorPoint> _pointIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<Iec61850EventEntry> _pendingEvents = new();
    private readonly ConcurrentQueue<DiagnosticEntry> _pendingDiagnostics = new();
    private readonly Dictionary<string, DateTime> _reportPulseUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _pointHighlightUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SignalDefinition>> _controlFeedbackIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _uiFlushTimer;
    private readonly DispatcherTimer _progressAnimationTimer;
    private Iec61850MonitorDevice? _selectedDevice;
    private string _newDeviceIp = "192.168.1.10";
    private string _newDevicePort = "102";
    private int _pollingIntervalMs = 1000;
    private string _lastStatusText = "Ready. Add an IEC 61850 IED or open a saved ArIED project.";
    private bool _allowClose;
    private bool _shutdownStarted;
    private bool _hasUnreadDiagnosticError;
    private bool _signalSelectionWizardOpen;
    private bool _connectAllInProgress;
    private readonly HashSet<string> _autoExpandedCommandDevices = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<Iec61850MonitorDevice> Devices { get; } = new();
    public BulkObservableCollection<Iec61850MonitorPoint> GlobalPoints { get; } = new();
    public BulkObservableCollection<Iec61850EventEntry> Events { get; } = new();
    public BulkObservableCollection<DiagnosticEntry> Logs { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string NewDeviceIp { get => _newDeviceIp; set => Set(ref _newDeviceIp, value); }
    public string NewDevicePort { get => _newDevicePort; set => Set(ref _newDevicePort, value); }
    public int PollingIntervalMs { get => _pollingIntervalMs; set => Set(ref _pollingIntervalMs, Math.Clamp(value, 50, 600000)); }
    public string LastStatusText { get => _lastStatusText; set => Set(ref _lastStatusText, value); }

    public Iec61850MonitorDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (ReferenceEquals(_selectedDevice, value)) return;
            if (_selectedDevice != null)
            {
                _selectedDevice.IsActive = false;
                ClearPendingControlConfirmations(_selectedDevice);
            }
            _selectedDevice = value;
            if (_selectedDevice != null)
            {
                ClearPendingControlConfirmations(_selectedDevice);
                _selectedDevice.IsActive = true;
                NewDeviceIp = _selectedDevice.IpAddress;
                NewDevicePort = _selectedDevice.Port.ToString(CultureInfo.InvariantCulture);
                if (MainTabs != null && MainTabs.SelectedIndex == 2)
                    _selectedDevice.ClearUnreadEvents();
            }
            Raise();
            Raise(nameof(EmptyExplorerVisibility));
            Raise(nameof(SelectedExplorerVisibility));
            Raise(nameof(SelectedDeviceNoLivePointsVisibility));
            Raise(nameof(ActiveIedTitle));
            Raise(nameof(ActiveIedSubtitle));
            RaiseWorkspaceCounts();
            TryAutoExpandCommandPanelOnce(_selectedDevice);
            // ctlModel inspection is preloaded independently of the Expander. Avoid
            // changing the row set after the panel's first frame has already painted.
        }
    }

    public string HeaderStatusText => $"{Devices.Count} IED • {_runtime.MonitoringDeviceCount} monitoring";
    public string DeviceCountText => $"{Devices.Count} device(s)";
    public string RuntimeSummaryText => $"Connected {_runtime.ConnectedDeviceCount} • Monitoring {_runtime.MonitoringDeviceCount} • Values {GlobalPoints.Count} • Events {Events.Count}";
    public string ConnectionInsightText => $"{_runtime.ConnectedDeviceCount} connected / {Devices.Count} discovered";
    public string MonitoringInsightText => $"{_runtime.MonitoringDeviceCount} monitoring / {GlobalPoints.Count} values";
    public string EventInsightText => $"{Events.Count} event(s)";
    public Visibility DiagnosticsAlertVisibility => _hasUnreadDiagnosticError ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyExplorerVisibility => SelectedDevice == null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SelectedExplorerVisibility => SelectedDevice != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SelectedDeviceNoLivePointsVisibility => SelectedDevice != null && SelectedDevice.Points.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;
    public string ActiveIedTitle => SelectedDevice == null ? "IEC 61850 IED" : SelectedDevice.Name;
    public string ActiveIedSubtitle => SelectedDevice == null
        ? string.Empty
        : $"{SelectedDevice.EndpointText} • {SelectedDevice.LogicalDeviceSummary} • {SelectedDevice.ActivityText}";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _uiFlushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _uiFlushTimer.Tick += UiFlushTimer_Tick;

        _progressAnimationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _progressAnimationTimer.Tick += ProgressAnimationTimer_Tick;

        _runtime.Diagnostic += Runtime_Diagnostic;
        _runtime.PointUpdated += Runtime_PointUpdated;
        _runtime.EventRaised += Runtime_EventRaised;
        _uiFlushTimer.Start();
        _progressAnimationTimer.Start();

        AddLog("INFO", "System", "ArIED 61850 started — Smart IED Explorer & Monitor.");
        AddLog("INFO", "IEC61850", "Acquisition: static report → dynamic report → MMS verification/fallback; each IED remains independent.");
        InitializeGooseSubscriber();
        UpdateNavigationVisuals(0, animate: false);
    }

    private async void AddRelay_Click(object sender, RoutedEventArgs e)
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


    private async void OpenScl_Click(object sender, RoutedEventArgs e)
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
        SetStatus($"Opening {sourceName} as an offline IEC 61850 design model…");
        try
        {
            var document = await _sclWorkspaceService.OpenAsync(
                dialog.FileName,
                cancellationToken: _applicationCancellation.Token);
            LogSclFindings(sourceName, document.Findings);

            if (document.Ieds.Count == 0)
            {
                SetStatus($"{sourceName}: no IED model was found.");
                AddLog("WARN", "SCL", $"{sourceName}: the engine returned no IED workspace.");
                return;
            }

            var added = 0;
            var refreshed = 0;
            var retained = 0;
            Iec61850MonitorDevice? firstImported = null;

            foreach (var workspace in document.Ieds)
            {
                var device = Devices.FirstOrDefault(item =>
                    item.SclSourceSha256.Equals(document.SourceSha256, StringComparison.OrdinalIgnoreCase) &&
                    item.SclIedName.Equals(workspace.IedName, StringComparison.OrdinalIgnoreCase) &&
                    item.SclAccessPointName.Equals(workspace.AccessPointName, StringComparison.OrdinalIgnoreCase));

                if (device != null && (device.IsConnected || device.IsBusy || device.IsMonitoring))
                {
                    retained++;
                    firstImported ??= device;
                    continue;
                }

                var signals = SclWorkspaceSignalMapper.BuildSignals(workspace);
                if (device == null)
                {
                    device = new Iec61850MonitorDevice();
                    Devices.Add(device);
                    added++;
                }
                else
                {
                    refreshed++;
                }

                ApplySclWorkspaceToDevice(device, document, workspace, signals);
                firstImported ??= device;
            }

            if (firstImported != null)
                SelectedDevice = firstImported;
            MainTabs.SelectedIndex = 0;
            UpdateNavigationVisuals(0, animate: true);
            RaiseWorkspaceCounts();

            var offlineCount = document.Ieds.Count(item => item.CanBrowseOffline);
            var endpointCount = document.Ieds.Count(item => !item.RequiresEndpointBinding);
            var status = $"{sourceName}: {document.Ieds.Count} IED/AP workspace(s), {offlineCount} offline model(s), {endpointCount} MMS endpoint(s) — {added} added, {refreshed} refreshed, {retained} active retained.";
            SetStatus(status);
            AddLog("INFO", "SCL", status);

            if (firstImported != null && firstImported.Signals.Count > 0)
            {
                AddLog("INFO", "SCL", $"{firstImported.Name}: SCL model ready. Choose signals; saving the selection will continue to endpoint binding, connection, and monitoring.");
                await OpenSignalSelectionWizardAsync(firstImported);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus($"{sourceName}: SCL open cancelled.");
        }
        catch (Exception ex)
        {
            AddLog("ERROR", "SCL", $"Could not open {sourceName}: {ex.Message}");
            SetStatus($"{sourceName}: SCL open failed. Diagnostics is marked with !.");
            MarkDiagnosticAlert();
            MessageBox.Show(
                this,
                $"ArIED could not open this SCL file through the ARIEC61850 engine.\n\n{ex.Message}",
                "Open SCL",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ApplySclWorkspaceToDevice(
        Iec61850MonitorDevice device,
        SclWorkspaceDocument document,
        SclIedWorkspace workspace,
        IReadOnlyList<SignalDefinition> signals)
    {
        var previousSelection = device.Signals
            .Where(signal => signal.IsSelected)
            .Select(signal => NormalizeReference(signal.ObjectReference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        DetachSignalHandlers(device.Signals);
        device.Signals.Clear();
        device.RecountSelectedSignals();

        var endpoint = workspace.PreferredEndpoint;
        device.Name = workspace.IedName;
        device.IdentitySource = $"SCL design • {document.SourceName}";
        device.LogicalDeviceSummary = BuildSclWorkspaceSummary(workspace);
        if (endpoint?.HasUsableAddress == true)
        {
            device.IpAddress = endpoint.IpAddress;
            device.Port = endpoint.Port;
        }
        else if (string.IsNullOrWhiteSpace(device.IpAddress) || device.IpAddress == "192.168.1.10")
        {
            device.IpAddress = string.Empty;
            device.Port = 102;
        }

        var allowDynamicReporting = ShouldAllowDynamicReportingForScl(signals);
        device.AllowDynamicDataSetWrites = allowDynamicReporting;
        device.SclWorkspace = workspace;
        device.SclComparison = null;
        device.SclSourcePath = document.SourcePath;
        device.SclSourceSha256 = document.SourceSha256;
        device.SclIedName = workspace.IedName;
        device.SclAccessPointName = workspace.AccessPointName;
        device.HasDiscoveryCache = signals.Count > 0;
        device.Status = workspace.RequiresEndpointBinding ? "SCL model ready — bind endpoint" : "SCL model ready";
        device.Detail = allowDynamicReporting
            ? (workspace.RequiresEndpointBinding
                ? "LD/LN/DO/DA are available offline. Static report coverage is incomplete; after signal selection and endpoint binding, ArIED will create an association-scoped dynamic DataSet and use a safe free RCB before polling fallback."
                : "LD/LN/DO/DA were loaded offline. Static report coverage is incomplete; ArIED will use static coverage where available and create an association-scoped dynamic DataSet for uncovered selected signals before polling fallback.")
            : (workspace.RequiresEndpointBinding
                ? "LD/LN/DO/DA are available offline. Press Play to bind an MMS endpoint; no discovery traffic was sent while opening the file."
                : "LD/LN/DO/DA were loaded offline. Play performs a fast MMS association; Re-scan performs full design-versus-live verification.");
        device.AcquisitionMode = allowDynamicReporting
            ? "SCL design • Smart Dynamic reporting prepared"
            : "SCL offline design model";

        foreach (var signal in signals)
        {
            signal.IsSelected = previousSelection.Contains(NormalizeReference(signal.ObjectReference));
            signal.PropertyChanged += Signal_PropertyChanged;
            _signalOwners[signal] = device;
        }
        device.Signals.AddRange(signals);
        device.RecountSelectedSignals();
        device.RefreshComputed();
    }

    private static string BuildSclWorkspaceSummary(SclIedWorkspace workspace)
    {
        var coverage = workspace.DesignModel.Coverage;
        var ap = string.IsNullOrWhiteSpace(workspace.AccessPointName) ? "AP unassigned" : $"AP {workspace.AccessPointName}";
        return $"{ap} • {coverage.LogicalDeviceCount} LD • {coverage.LogicalNodeCount} LN • {coverage.DataObjectCount} DO • {coverage.DataAttributeCount} DA";
    }

    private void LogSclFindings(string sourceName, IReadOnlyList<SclWorkspaceFinding> findings)
    {
        var actionableFindings = findings
            .Where(finding => !IsSmartDynamicCapabilityHint(finding))
            .ToArray();
        if (actionableFindings.Length == 0)
            return;

        var groups = SclFindingAggregator.Group(actionableFindings);
        if (groups.Count != actionableFindings.Length)
        {
            AddLog(
                "INFO",
                "SCL",
                $"{sourceName} • grouped {actionableFindings.Length} actionable finding(s) into {groups.Count} diagnostic group(s). Full typed evidence remains attached to the SCL workspace.");
        }

        foreach (var group in groups.Take(40))
        {
            AddLog(
                SclFindingAggregator.ToLogLevel(group.Severity),
                "SCL",
                $"{sourceName} • {group.Code} [{group.Scope}]: {group.ToDiagnosticMessage()}");
        }

        if (groups.Count > 40)
        {
            var omittedRawCount = groups.Skip(40).Sum(group => group.Count);
            AddLog(
                "WARN",
                "SCL",
                $"{groups.Count - 40} additional diagnostic group(s), representing {omittedRawCount} actionable finding(s), were omitted from the live log.");
        }

        if (groups.Any(group => SclFindingAggregator.IsBlockingSeverity(group.Severity)))
            MarkDiagnosticAlert();
    }

    private static bool IsSmartDynamicCapabilityHint(SclWorkspaceFinding finding)
    {
        if (!finding.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase))
            return false;

        return finding.Code.Equals("SCL_REPORT_DATASET_UNASSIGNED", StringComparison.OrdinalIgnoreCase) ||
               finding.Code.Equals("SCL_REPORT_DATASET_UNRESOLVED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldAllowDynamicReportingForScl(IReadOnlyCollection<SignalDefinition> signals)
    {
        return signals.Any(signal =>
            signal.CanPublishAsSignal &&
            (string.IsNullOrWhiteSpace(signal.DataSetReference) ||
             string.IsNullOrWhiteSpace(signal.ReportControlReference)));
    }

    private void TryAutoExpandCommandPanelOnce(Iec61850MonitorDevice? device)
    {
        if (device == null || CommandPanelExpander == null || device.CommandSignals.Count == 0)
            return;
        if (!_autoExpandedCommandDevices.Add(device.DeviceId))
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (ReferenceEquals(SelectedDevice, device) && CommandPanelExpander != null)
                CommandPanelExpander.IsExpanded = true;
        }));
    }

    private bool EnsureSclEndpointBinding(Iec61850MonitorDevice device)
    {
        if (!device.RequiresEndpointBinding)
            return true;

        var initialIp = string.IsNullOrWhiteSpace(NewDeviceIp) ? "192.168.1.10" : NewDeviceIp;
        var wizard = new IpConnectWizardWindow(initialIp, device.Port <= 0 ? 102 : device.Port) { Owner = this };
        if (wizard.ShowDialog() != true)
        {
            SetStatus($"{device.Name}: endpoint binding cancelled; the SCL model remains available offline.");
            return false;
        }

        device.IpAddress = wizard.RelayIpAddress;
        device.Port = wizard.MmsPort;
        device.Status = "SCL model ready";
        device.Detail = device.AllowDynamicDataSetWrites
            ? "Endpoint bound locally. Saving the selected signals will connect and arm Smart Dynamic reporting with a safe free RCB before polling fallback."
            : "Endpoint bound locally. Play will fast-connect from the SCL design model; Re-scan performs full comparison.";
        device.RefreshComputed();
        NewDeviceIp = device.IpAddress;
        NewDevicePort = device.Port.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private async void ConnectAllIeds_Click(object sender, RoutedEventArgs e)
    {
        if (_connectAllInProgress)
            return;

        var candidates = Devices
            .Where(device => !device.IsBusy && !device.IsMonitoring)
            .ToArray();
        if (candidates.Length == 0)
        {
            SetStatus(Devices.Count == 0
                ? "Add or open an IED project before using Connect All."
                : "All available IEDs are already monitoring or currently busy.");
            return;
        }

        _connectAllInProgress = true;
        var originalSelection = SelectedDevice;
        SetStatus($"Connect All: starting {candidates.Length} independent IED workflow(s)…");
        try
        {
            // Start every independent session immediately. Each IED retains its own
            // card-local progress overlay, so slow/offline devices do not block others.
            var results = await Task.WhenAll(candidates.Select(ConnectAndStartWorkspaceDeviceAsync));
            var succeeded = results.Count(result => result);
            var monitoring = candidates.Count(device => device.IsMonitoring);
            var needsSelection = candidates.Count(device => device.IsConnected && device.SelectedLiveSignalCount == 0);

            if (originalSelection != null && Devices.Contains(originalSelection))
                SelectedDevice = originalSelection;

            SetStatus(needsSelection > 0
                ? $"Connect All complete: {succeeded}/{candidates.Length} connected, {monitoring} monitoring, {needsSelection} need signal selection."
                : $"Connect All complete: {succeeded}/{candidates.Length} connected and {monitoring} monitoring.");
        }
        finally
        {
            _connectAllInProgress = false;
            RaiseWorkspaceCounts();
        }
    }

    private async Task<bool> ConnectAndStartWorkspaceDeviceAsync(Iec61850MonitorDevice device)
    {
        try
        {
            if (device.IsMonitoring)
                return true;
            if (device.RequiresEndpointBinding)
            {
                device.Status = "SCL model ready — endpoint required";
                device.Detail = "Connect All skipped this offline SCL workspace because no MMS endpoint is bound.";
                device.RefreshComputed();
                AddLog("WARN", device.Name, "Connect All skipped the SCL workspace because its MMS endpoint is unassigned.");
                return false;
            }

            var connected = device.IsConnected;
            if (!connected)
            {
                connected = device.HasDiscoveryCache && device.Signals.Count > 0
                    ? await ConnectUsingSavedModelAsync(device, selectDevice: false)
                    : await ConnectAndConfigureDeviceAsync(device, openWizard: false, selectDevice: false);
            }

            if (!connected)
                return false;

            // A project with saved selections becomes live in one click. Newly discovered
            // IEDs without a selection remain connected and ready for the edit wizard.
            if (device.SelectedLiveSignalCount == 0)
            {
                device.Status = "Connected — choose signals";
                device.Detail = "Use the edit icon to choose signals; Apply & Start Live will start monitoring automatically.";
                device.RefreshComputed();
                return true;
            }

            return await StartDeviceMonitorAsync(device, navigateToExplorer: false);
        }
        catch (Exception ex)
        {
            AddLog("ERROR", device.Name, $"Connect All workflow failed: {ex.Message}");
            MarkDiagnosticAlert();
            return false;
        }
    }

    private async void ConnectAndScan_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadEndpoint(out var ip, out var port)) return;
        await AddOrDiscoverEndpointAsync(ip, port);
    }

    private async Task AddOrDiscoverEndpointAsync(string ip, int port)
    {
        var device = Devices.FirstOrDefault(item =>
            item.IpAddress.Equals(ip, StringComparison.OrdinalIgnoreCase) && item.Port == port);
        if (device == null)
        {
            device = new Iec61850MonitorDevice
            {
                // Until the live model reveals the real IEDName, use the endpoint as an
                // honest temporary identity. Do not leave a failed card named
                // "Discovering IED…" because initial discovery is not auto-retried.
                Name = ip,
                IpAddress = ip,
                Port = port,
                AllowDynamicDataSetWrites = true,
                Status = "Ready to connect",
                Detail = "Click Play to connect and discover the live IEC 61850 model."
            };
            Devices.Add(device);
        }

        SelectedDevice = device;
        MainTabs.SelectedIndex = 0;
        await ConnectAndConfigureDeviceAsync(device, openWizard: true);
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var index))
            return;
        index = Math.Clamp(index, 0, 4);
        MainTabs.SelectedIndex = index;
        UpdateNavigationVisuals(index, animate: true);
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs))
            return;

        if (MainTabs.SelectedIndex == 2)
        {
            foreach (var device in Devices)
                device.ClearUnreadEvents();
        }
        else if (MainTabs.SelectedIndex == 3)
        {
            // Defer optional Npcap/model work until after the selected tab has rendered.
            // An unavailable capture dependency must never leave the workspace blank.
            ActivateGooseSubscriberWorkspace();
        }
        else if (MainTabs.SelectedIndex == 4)
        {
            ClearDiagnosticAlert();
        }

        UpdateNavigationVisuals(MainTabs.SelectedIndex, animate: true);
    }

    private void UpdateNavigationVisuals(int index, bool animate)
    {
        if (WorkflowPillTranslate == null)
            return;

        var target = Math.Clamp(index, 0, 4) * 150d;
        if (animate)
        {
            var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            WorkflowPillTranslate.BeginAnimation(TranslateTransform.XProperty, animation);
        }
        else
        {
            WorkflowPillTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            WorkflowPillTranslate.X = target;
        }

        var buttons = new[] { NavExplorerButton, NavLiveButton, NavEventsButton, NavGooseButton, NavDiagnosticsButton };
        for (var i = 0; i < buttons.Length; i++)
            buttons[i].Foreground = i == index ? Brushes.White : new SolidColorBrush(Color.FromRgb(71, 84, 103));
    }

    private async Task<bool> ConnectAndConfigureDeviceAsync(
        Iec61850MonitorDevice device,
        bool openWizard,
        bool selectDevice = true)
    {
        if (device.IsBusy) return false;
        if (!EnsureSclEndpointBinding(device)) return false;

        RememberCurrentSelectionForReconnect(device);
        RemoveDevicePoints(device.DeviceId);
        device.Points.Clear();
        device.RefreshComputed();
        if (selectDevice)
            SelectedDevice = device;

        device.ResetDiscoveryProgress(
            $"Opening {device.EndpointText}…",
            "Discovering IED");
        device.IsBusy = true;
        await Dispatcher.Yield(DispatcherPriority.Render);

        // Progress is stored on the individual device model. Multiple IED discovery
        // operations can therefore advance independently without a global overlay.
        var progress = new Progress<IedDiscoveryProgress>(device.ApplyDiscoveryProgress);
        try
        {
            var signals = await _runtime.ConnectAndDiscoverAsync(
                device,
                _applicationCancellation.Token,
                progress);

            DetachSignalHandlers(device.Signals);
            device.Signals.Clear();
            device.RecountSelectedSignals();
            foreach (var signal in signals)
            {
                signal.PropertyChanged += Signal_PropertyChanged;
                _signalOwners[signal] = device;
            }
            device.Signals.AddRange(signals);
            device.HasDiscoveryCache = signals.Count > 0;
            ApplySclLiveComparison(device, signals);

            try
            {
                UserPreferenceStore.RecordSuccessfulEndpoint(device.IpAddress, device.Port, device.Name);
            }
            catch (Exception ex)
            {
                AddLog("WARN", device.Name, $"Could not update recent IED history: {ex.Message}");
            }

            var restoredCount = RestoreSignalSelection(device);
            device.RefreshComputed();
            RaiseWorkspaceCounts();
            SetStatus($"{device.Name}: discovery complete, {device.SignalCount} readable signal(s), {restoredCount} saved selection(s) restored.");

            // Let the card-local bar visibly settle at 100%, then release the card
            // before opening a modal wizard or starting live monitoring.
            await WaitForDiscoveryProgressAnimationAsync(device, TimeSpan.FromMilliseconds(1800));
            device.CompleteDiscoveryProgressAnimation();
            device.BusyStage = "Discovery complete";
            device.IsBusy = false;
            device.RefreshComputed();

            if (openWizard && device.SignalCount > 0)
            {
                if ((selectDevice || ReferenceEquals(SelectedDevice, device)) && !_signalSelectionWizardOpen)
                    await OpenSignalSelectionWizardAsync(device, restoredCount);
                else
                    SetStatus($"{device.Name}: discovery complete. Use the edit icon on its IED card to review {restoredCount} restored selection(s).");
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            try
            {
                await _runtime.StopDeviceAsync(device.DeviceId);
            }
            catch
            {
                // Cancellation cleanup is best effort.
            }

            device.IsConnected = false;
            device.Status = "Discovery cancelled";
            device.Detail = "Discovery was cancelled. Click Play to try again.";
            device.AcquisitionMode = "Not connected";
            device.MarkDiscoveryFailed("Discovery cancelled");
            SetStatus($"{device.Name}: discovery cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            try
            {
                await _runtime.StopDeviceAsync(device.DeviceId);
            }
            catch
            {
                // Failure cleanup must not hide the original discovery error.
            }

            device.IsConnected = false;
            if (string.IsNullOrWhiteSpace(device.Name) ||
                device.Name.Equals("IED", StringComparison.OrdinalIgnoreCase) ||
                device.Name.Equals("Discovering IED…", StringComparison.OrdinalIgnoreCase))
            {
                device.Name = device.IpAddress;
            }

            device.Status = "Connection failed";
            device.Detail = $"No live IEC 61850 session is active. Click Play to retry {device.EndpointText}.";
            device.AcquisitionMode = "Connection failed · click Play to retry";
            device.MarkDiscoveryFailed("Connection failed — click Play to retry");

            AddLog("ERROR", device.Name, ex.Message);
            SetStatus($"{device.Name}: connection/discovery failed. This endpoint is not auto-retried; use Play to retry. Diagnostics is marked with !.");
            MarkDiagnosticAlert();
            return false;
        }
        finally
        {
            if (device.IsConnected)
                device.CompleteDiscoveryProgressAnimation();
            device.BusyStage = device.IsConnected ? "Discovery complete" : device.BusyStage;
            device.IsBusy = false;
        }
    }

    private async Task<bool> ConnectUsingSavedModelAsync(
        Iec61850MonitorDevice device,
        bool selectDevice = true)
    {
        if (device.IsBusy) return false;
        if (!EnsureSclEndpointBinding(device)) return false;
        if (!device.HasDiscoveryCache || device.Signals.Count == 0)
            return await ConnectAndConfigureDeviceAsync(device, openWizard: true, selectDevice: selectDevice);

        RemoveDevicePoints(device.DeviceId);
        device.Points.Clear();
        device.RefreshComputed();
        if (selectDevice)
            SelectedDevice = device;
        device.ResetDiscoveryProgress(
            $"Opening {device.EndpointText}…",
            "Fast connect from saved model");
        device.IsBusy = true;
        await Dispatcher.Yield(DispatcherPriority.Render);

        var progress = new Progress<IedDiscoveryProgress>(device.ApplyDiscoveryProgress);
        try
        {
            await _runtime.ConnectUsingCachedModelAsync(
                device,
                _applicationCancellation.Token,
                progress);

            device.RecountSelectedSignals();
            await WaitForDiscoveryProgressAnimationAsync(device, TimeSpan.FromMilliseconds(900));
            RaiseWorkspaceCounts();
            if (device.HasSclDesignModel)
            {
                device.Status = "Connected — SCL design model";
                device.Detail = "MMS association is live and the SCL workspace remains the active model. Re-scan performs a complete design-versus-live comparison.";
                device.AcquisitionMode = "SCL design model • live association";
                SetStatus($"{device.Name}: fast connected from the SCL design model; full discovery skipped. Use Re-scan to compare the complete live model.");
            }
            else
            {
                SetStatus($"{device.Name}: fast connected from saved project model; full discovery skipped.");
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            device.IsConnected = false;
            device.Status = "Connection cancelled";
            device.Detail = "Saved model remains available. Click Play to retry.";
            device.AcquisitionMode = "Saved model • disconnected";
            device.MarkDiscoveryFailed("Connection cancelled");
            SetStatus($"{device.Name}: fast connect cancelled; saved model retained.");
            return false;
        }
        catch (Exception ex)
        {
            try
            {
                await _runtime.StopDeviceAsync(device.DeviceId);
            }
            catch
            {
                // Failure cleanup is best effort; keep the project cache intact.
            }

            device.IsConnected = false;
            device.Status = "Connection failed";
            device.Detail = "Saved signal model is still available. Retry Play or use Re-scan if the IED configuration changed.";
            device.AcquisitionMode = "Saved model • connection failed";
            device.MarkDiscoveryFailed("Connection failed — saved model retained");
            AddLog("ERROR", device.Name, ex.Message);
            MarkDiagnosticAlert();
            SetStatus($"{device.Name}: fast connect failed. The saved discovery model was retained; use Re-scan only if the IED model changed.");
            return false;
        }
        finally
        {
            if (device.IsConnected)
                device.CompleteDiscoveryProgressAnimation();
            device.IsBusy = false;
            device.RefreshComputed();
        }
    }

    private void ApplySclLiveComparison(Iec61850MonitorDevice device, IReadOnlyList<SignalDefinition> liveSignals)
    {
        if (device.SclWorkspace == null)
            return;

        var expectedModel = SclLiveSignalModelProjection.Build(
            device.SclWorkspace.IedName,
            device.SclWorkspace.AccessPointName,
            SclWorkspaceSignalMapper.BuildSignals(device.SclWorkspace));
        var observedModel = SclLiveSignalModelProjection.Build(
            device.Name,
            device.SclWorkspace.AccessPointName,
            liveSignals);
        var comparison = SclLiveModelComparer.Compare(expectedModel, observedModel);
        device.SclComparison = comparison;
        foreach (var finding in comparison.Findings.Take(30))
        {
            var level = finding.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase) ? "ERROR" : "INFO";
            AddLog(level, "SCL Compare", $"{finding.Kind} • {finding.Message}");
        }
        if (comparison.Findings.Count > 30)
            AddLog("WARN", "SCL Compare", $"{comparison.Findings.Count - 30} additional comparison finding(s) were omitted from the live log.");

        if (comparison.IsCompatible)
        {
            device.IdentitySource = $"SCL + live verified • {Path.GetFileName(device.SclSourcePath)}";
            device.AcquisitionMode = "SCL design • live model verified";
            device.Detail = $"SCL and live MMS structures are compatible: {comparison.MatchedAttributeCount}/{comparison.ExpectedAttributeCount} expected attributes matched.";
            AddLog("INFO", device.Name, device.Detail);
        }
        else
        {
            device.IdentitySource = $"SCL drift detected • {Path.GetFileName(device.SclSourcePath)}";
            device.AcquisitionMode = "Live discovery • SCL configuration drift";
            device.Detail = $"Live discovery found {comparison.BlockingFindingCount} blocking SCL mismatch(es). Live data is shown; review Diagnostics before testing control or reporting.";
            MarkDiagnosticAlert();
            AddLog("ERROR", device.Name, device.Detail);
        }
        device.RefreshComputed();
    }

    private async Task<bool> OpenSignalSelectionWizardAsync(
        Iec61850MonitorDevice device,
        int restoredSelectionCount = -1,
        bool autoStartAfterSave = true)
    {
        if (device.Signals.Count == 0)
        {
            SetStatus($"{device.Name}: no saved or live signal model is available. Run discovery first.");
            return false;
        }

        if (_signalSelectionWizardOpen)
        {
            SetStatus($"{device.Name}: another signal-selection wizard is open. Use this IED card's edit icon after closing it.");
            return false;
        }

        SelectedDevice = device;
        var wizard = new SignalSelectionWizardWindow(
            device,
            restoredSelectionCount < 0 ? device.SelectedSignalCount : restoredSelectionCount)
        {
            Owner = this
        };

        _signalSelectionWizardOpen = true;
        try
        {
            if (wizard.ShowDialog() != true)
            {
                device.RefreshComputed();
                RaiseWorkspaceCounts();
                SetStatus($"{device.Name}: signal selection unchanged.");
                return false;
            }

            SaveSignalSelectionMemory(device);
            device.RefreshComputed();
            RebuildControlFeedbackIndex(device);
            if (CommandPanelExpander?.IsExpanded == true && device.IsConnected)
                _ = RefreshControlValuesAsync(device);
            RaiseWorkspaceCounts();

            if (device.SelectedLiveSignalCount == 0)
            {
                if (device.IsMonitoring)
                    await StopDeviceMonitorAsync(device);
                SetStatus($"{device.Name}: selection saved with no live monitor points. Control objects remain saved, but choose at least one ST/MX signal to start monitoring.");
                return true;
            }

            if (!autoStartAfterSave)
            {
                SetStatus($"{device.Name}: saved {device.SelectedSignalCount} selection(s) — {device.SelectedLiveSignalCount} live, {device.SelectedControlSignalCount} control.");
                return true;
            }

            // Fast-workflow rule: applying a signal selection is the user's intent to
            // see live values. Restart an existing monitor or connect an offline cached
            // IED automatically instead of requiring another Play click.
            if (device.IsMonitoring)
                await StopDeviceMonitorAsync(device);

            if (!device.IsConnected)
            {
                var connected = device.HasDiscoveryCache && device.Signals.Count > 0
                    ? await ConnectUsingSavedModelAsync(device)
                    : await ConnectAndConfigureDeviceAsync(device, openWizard: false);
                if (!connected)
                    return false;
            }

            return await StartDeviceMonitorAsync(device);
        }
        finally
        {
            _signalSelectionWizardOpen = false;
        }
    }

    private async void IedPlayAction_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDeviceFromButton(sender, out var device) || device.IsBusy) return;
        SelectedDevice = device;

        if (!device.IsConnected)
        {
            var discoveryWillOpenWizard = !device.HasDiscoveryCache || device.Signals.Count == 0;
            var connected = device.HasDiscoveryCache && device.Signals.Count > 0
                ? await ConnectUsingSavedModelAsync(device)
                : await ConnectAndConfigureDeviceAsync(device, openWizard: discoveryWillOpenWizard);
            if (!connected || discoveryWillOpenWizard) return;
        }

        if (device.IsMonitoring) return;
        if (device.SelectedLiveSignalCount == 0)
        {
            await OpenSignalSelectionWizardAsync(device);
            return;
        }

        await StartDeviceMonitorAsync(device);
    }

    private async void IedStopAction_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDeviceFromButton(sender, out var device) || device.IsBusy) return;
        SelectedDevice = device;

        if (device.IsMonitoring)
            await StopDeviceMonitorAsync(device);
        else if (device.IsConnected)
            await StopDeviceConnectionAsync(device);
    }

    private async void IedConnectionAction_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDeviceFromButton(sender, out var device) || device.IsBusy) return;
        SelectedDevice = device;

        if (device.IsConnected)
        {
            SaveSignalSelectionMemory(device);
            await StopDeviceConnectionAsync(device);
        }
        else
        {
            if (device.HasDiscoveryCache && device.Signals.Count > 0)
                await ConnectUsingSavedModelAsync(device);
            else
                await ConnectAndConfigureDeviceAsync(device, openWizard: device.Signals.Count == 0);
        }
    }

    private async void IedMonitorAction_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDeviceFromButton(sender, out var device) || device.IsBusy) return;
        SelectedDevice = device;

        if (device.IsMonitoring)
        {
            await StopDeviceMonitorAsync(device);
            return;
        }

        if (!device.IsConnected)
        {
            var fullDiscovery = !device.HasDiscoveryCache || device.Signals.Count == 0;
            var connected = device.HasDiscoveryCache && device.Signals.Count > 0
                ? await ConnectUsingSavedModelAsync(device)
                : await ConnectAndConfigureDeviceAsync(device, openWizard: true);
            if (!connected || fullDiscovery) return;
        }

        if (device.SelectedLiveSignalCount == 0)
        {
            await OpenSignalSelectionWizardAsync(device);
            return;
        }

        await StartDeviceMonitorAsync(device);
    }

    private async void IedConfigureSignals_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDeviceFromButton(sender, out var device)) return;
        await OpenSignalSelectionWizardAsync(device);
    }

    private async void CommandPanel_Expanded(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice != null)
            await RefreshControlValuesAsync(SelectedDevice, force: true);
    }

    private async void RefreshCommandValues_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice != null)
            await RefreshControlValuesAsync(SelectedDevice, force: true);
    }

    private async Task RefreshControlValuesAsync(Iec61850MonitorDevice device, bool force = false)
    {
        if (!device.IsConnected || device.CommandSignals.Count == 0)
            return;

        var candidates = device.CommandSignals
            .Where(signal => force || signal.ControlCurrentValue == "-" || signal.ControlModelText == "Auto-detect")
            .ToArray();
        if (candidates.Length == 0)
        {
            RebuildControlFeedbackIndex(device);
            return;
        }

        using var throttle = new SemaphoreSlim(3, 3);
        await Task.WhenAll(candidates.Select(async signal =>
        {
            await throttle.WaitAsync(_applicationCancellation.Token);
            signal.ControlInspectionBusy = true;
            try
            {
                var capabilities = await _runtime.InspectControlAsync(
                    device.DeviceId,
                    signal,
                    _applicationCancellation.Token);
                if (!signal.ControlCommandBusy)
                {
                    signal.ControlCurrentValue = capabilities.CurrentValue;
                    signal.ControlLastResult = capabilities.SupportsOperate
                        ? capabilities.ControlModelText
                        : "Control unavailable";
                }
            }
            catch (OperationCanceledException)
            {
                // Application shutdown or device removal.
            }
            catch (Exception ex)
            {
                if (!signal.ControlCommandBusy)
                    signal.ControlLastResult = $"Status read failed: {ex.Message}";
            }
            finally
            {
                signal.ControlInspectionBusy = false;
                throttle.Release();
            }
        }));

        device.RefreshCommandSignalProjection();
        RebuildControlFeedbackIndex(device);
        TryAutoExpandCommandPanelOnce(device);
        SetStatus($"{device.Name}: refreshed {candidates.Length} control value(s).");
    }

    private void ControlStageAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not SignalDefinition signal || !signal.IsPositionControl)
            return;

        var device = _signalOwners.TryGetValue(signal, out var owner) ? owner : SelectedDevice;
        var requestedValue = button.CommandParameter?.ToString()?.Trim() ?? string.Empty;
        var actionLabel = button.Content?.ToString()?.Trim() ?? string.Empty;
        AddLog("INFO", device?.Name ?? "IED",
            $"Control confirmation stage click received: {signal.ObjectReference}; action={actionLabel}; value={requestedValue}; commandBusy={signal.ControlCommandBusy}; inspectionBusy={signal.ControlInspectionBusy}.");

        if (!signal.TryStageControlConfirmation(requestedValue, actionLabel, out var rejectionReason))
        {
            signal.ControlLastResult = $"Command not staged: {rejectionReason}.";
            AddLog("WARN", device?.Name ?? "IED",
                $"Control confirmation stage rejected: {signal.ObjectReference}; reason={rejectionReason}.");
            SetStatus($"{device?.Name ?? "IED"}: {signal.Name} command not staged — {rejectionReason}.");
            return;
        }

        AddLog("INFO", device?.Name ?? "IED",
            $"Control confirmation staged: {signal.ObjectReference}; action={actionLabel}; value={requestedValue}.");
        SetStatus($"{device?.Name ?? "IED"}: review {signal.Name} — {signal.ControlPendingConfirmationLabel}, or Cancel.");
    }

    private async void ControlConfirmAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not SignalDefinition signal)
            return;

        var device = _signalOwners.TryGetValue(signal, out var owner) ? owner : SelectedDevice;
        AddLog("INFO", device?.Name ?? "IED",
            $"Confirm click received: {signal.ObjectReference}; pending={signal.ControlConfirmationPending}; action={signal.ControlPendingAction}; value={signal.ControlPendingValue}; commandBusy={signal.ControlCommandBusy}; inspectionBusy={signal.ControlInspectionBusy}.");

        if (!signal.TryClaimControlConfirmation(out var claim, out var rejectionReason) || claim == null)
        {
            signal.ControlLastResult = $"Confirmation rejected: {rejectionReason}.";
            AddLog("WARN", device?.Name ?? "IED",
                $"Confirm rejected: {signal.ObjectReference}; reason={rejectionReason}.");
            SetStatus($"{device?.Name ?? "IED"}: {signal.Name} confirmation rejected — {rejectionReason}.");
            return;
        }

        AddLog("INFO", device?.Name ?? "IED",
            $"Confirm accepted: {signal.ObjectReference}; sequence={claim.Sequence}; action={claim.ActionLabel}; value={claim.RequestedValue}.");
        await ExecuteClaimedControlAsync(signal, claim);
    }

    private void ControlCancelAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not SignalDefinition signal)
            return;

        var action = signal.ControlPendingAction;
        signal.ClearControlConfirmation();
        var device = _signalOwners.TryGetValue(signal, out var owner) ? owner : SelectedDevice;
        AddLog("INFO", device?.Name ?? "IED",
            $"Control confirmation cancelled: {signal.ObjectReference}; action={action}.");
        SetStatus($"{device?.Name ?? "IED"}: {signal.Name} {action} cancelled before dispatch.");
    }

    private async void ControlQuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not SignalDefinition signal || !signal.IsControlSignal)
            return;

        var requestedValue = button.CommandParameter?.ToString();
        if (string.IsNullOrWhiteSpace(requestedValue))
            requestedValue = signal.ControlSetPointText;
        if (string.IsNullOrWhiteSpace(requestedValue))
        {
            signal.ControlLastResult = "Enter a setpoint value first.";
            return;
        }

        var device = _signalOwners.TryGetValue(signal, out var owner) ? owner : SelectedDevice;
        if (!signal.TryBeginDirectControlCommand(
                requestedValue,
                button.Content?.ToString()?.Trim() ?? requestedValue,
                out var claim,
                out var rejectionReason) || claim == null)
        {
            signal.ControlLastResult = $"Command rejected: {rejectionReason}.";
            AddLog("WARN", device?.Name ?? "IED",
                $"Direct control rejected: {signal.ObjectReference}; reason={rejectionReason}.");
            SetStatus($"{device?.Name ?? "IED"}: {signal.Name} command rejected — {rejectionReason}.");
            return;
        }

        await ExecuteClaimedControlAsync(signal, claim);
    }

    private async Task ExecuteClaimedControlAsync(SignalDefinition signal, ControlCommandClaim claim)
    {
        var device = _signalOwners.TryGetValue(signal, out var owner) ? owner : SelectedDevice;
        if (device == null)
        {
            signal.CompleteControlCommand(claim);
            return;
        }

        var clickStopwatch = Stopwatch.StartNew();
        signal.ControlLastResult = $"Dispatching {claim.RequestedValue}…";
        SetStatus($"{device.Name}: dispatching {signal.Name} = {claim.RequestedValue}…");
        AddLog("INFO", device.Name,
            $"Dispatch ownership acquired: {signal.ObjectReference}; sequence={claim.Sequence}; value={claim.RequestedValue}; test={signal.ControlTestMode}; interlock={signal.ControlInterlockCheck}; synchro={signal.ControlSynchroCheck}.");
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            if (!device.IsConnected)
            {
                SetStatus($"{device.Name}: connecting before control…");
                var connected = device.HasDiscoveryCache && device.Signals.Count > 0
                    ? await ConnectUsingSavedModelAsync(device)
                    : await ConnectAndConfigureDeviceAsync(device, openWizard: false);
                if (!connected)
                    return;
            }

            AddLog("INFO", device.Name,
                $"MMS command submitted: {signal.ObjectReference}; sequence={claim.Sequence}; value={claim.RequestedValue}.");
            var result = await _runtime.ExecuteControlAsync(
                device.DeviceId,
                new Iec61850ControlCommandRequest
                {
                    Signal = signal,
                    ValueText = claim.RequestedValue,
                    InterlockCheck = signal.ControlInterlockCheck,
                    SynchroCheck = signal.ControlSynchroCheck,
                    TestMode = signal.ControlTestMode,
                    FeedbackTimeoutMs = signal.IsPositionControl ? 12000 :
                        (signal.IsRaiseOnlyControl || signal.IsLowerOnlyControl || signal.IsRaiseLowerControl) ? 15000 : 8000,
                    CommandTerminationTimeoutMs = 10000,
                    OriginCategory = "Maintenance"
                },
                _applicationCancellation.Token);

            if (!string.IsNullOrWhiteSpace(result.ControlModelText))
                signal.ControlModelText = result.ControlModelText;
            if (!string.IsNullOrWhiteSpace(result.FeedbackValue) && result.FeedbackValue != "-")
                signal.ControlCurrentValue = result.FeedbackValue;

            signal.ControlLastResult = BuildQuickControlResult(result);
            SetStatus($"{device.Name}: {signal.Name} — {signal.ControlLastResult}");
            clickStopwatch.Stop();
            AddLog(result.IsSuccess ? "INFO" : "WARN", device.Name,
                $"Control UI timing: {signal.ObjectReference}; sequence={claim.Sequence}; click-to-result={clickStopwatch.Elapsed.TotalMilliseconds:0.###} ms; engine-total={result.TotalElapsedText}; serviceAccepted={result.ServiceAccepted}; stage={result.Stage}.");
        }
        catch (OperationCanceledException)
        {
            signal.ControlLastResult = "Command cancelled.";
            SetStatus($"{device.Name}: {signal.Name} command cancelled.");
        }
        catch (Exception ex)
        {
            signal.ControlLastResult = $"Command failed: {ex.Message}";
            AddLog("ERROR", device.Name, $"Quick control failed for {signal.ObjectReference}: {ex}");
            SetStatus($"{device.Name}: {signal.Name} command failed — {ex.Message}");
            MarkDiagnosticAlert();
        }
        finally
        {
            if (!signal.CompleteControlCommand(claim))
            {
                AddLog("ERROR", device.Name,
                    $"Control ownership release mismatch: {signal.ObjectReference}; sequence={claim.Sequence}.");
                MarkDiagnosticAlert();
            }
        }
    }

    private static string BuildQuickControlResult(Iec61850ControlCommandResult result)
    {
        var timing = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.ElapsedText) && result.ElapsedText != "-")
            timing.Add($"control {result.ElapsedText}");
        if (!string.IsNullOrWhiteSpace(result.FeedbackElapsedText) && result.FeedbackElapsedText != "-")
            timing.Add($"feedback {result.FeedbackElapsedText}");
        var suffix = timing.Count == 0 ? string.Empty : $" • {string.Join(" • ", timing)}";
        return result.IsSuccess
            ? $"{result.Stage}: {result.FeedbackValue}{suffix}"
            : $"{result.Stage}: {result.Message}{suffix}";
    }

    private async void ControlDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not SignalDefinition signal || !signal.IsControlSignal)
            return;

        var device = SelectedDevice;
        if (device == null)
            return;

        if (!device.IsConnected)
        {
            SetStatus($"{device.Name}: connecting before opening technical control details…");
            var connected = device.HasDiscoveryCache && device.Signals.Count > 0
                ? await ConnectUsingSavedModelAsync(device)
                : await ConnectAndConfigureDeviceAsync(device, openWizard: false);
            if (!connected)
                return;
        }

        var dialog = new ControlCommandWindow(_runtime, device, signal)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void RemoveControlFeedbackIndex(string deviceId)
    {
        var prefix = deviceId + "|";
        foreach (var key in _controlFeedbackIndex.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
            _controlFeedbackIndex.Remove(key);
    }

    private void RebuildControlFeedbackIndex(Iec61850MonitorDevice device)
    {
        RemoveControlFeedbackIndex(device.DeviceId);

        foreach (var signal in device.CommandSignals)
        {
            if (string.IsNullOrWhiteSpace(signal.ControlStatusReference))
                continue;
            var key = ControlFeedbackKey(device.DeviceId, signal.ControlStatusReference);
            if (!_controlFeedbackIndex.TryGetValue(key, out var signals))
                _controlFeedbackIndex[key] = signals = new List<SignalDefinition>();
            if (!signals.Contains(signal))
                signals.Add(signal);
        }
    }

    private void UpdateCommandFeedbackFromLivePoint(Iec61850MonitorPoint point)
    {
        var key = ControlFeedbackKey(point.DeviceId, point.IecReference);
        if (!_controlFeedbackIndex.TryGetValue(key, out var signals))
            return;
        foreach (var signal in signals)
            signal.ControlCurrentValue = point.Value;
    }

    private static string ControlFeedbackKey(string deviceId, string reference)
        => $"{deviceId}|{NormalizeReference(reference)}";

    private static void ClearPendingControlConfirmations(Iec61850MonitorDevice? device)
    {
        if (device == null)
            return;

        foreach (var signal in device.Signals.Where(item => item.IsControlSignal))
            signal.ClearControlConfirmation();
    }

    private async void IedRescan_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDeviceFromButton(sender, out var device) || device.IsBusy || device.IsMonitoring)
            return;

        SelectedDevice = device;
        if (!EnsureSclEndpointBinding(device))
            return;
        RememberCurrentSelectionForReconnect(device);
        if (device.IsConnected)
            await StopDeviceConnectionAsync(device);

        SetStatus(device.HasSclDesignModel
            ? $"{device.Name}: discovering the complete live model and comparing it with the SCL design model."
            : $"{device.Name}: running a forced full live-model discovery. The saved cache will be replaced only after success.");
        await ConnectAndConfigureDeviceAsync(device, openWizard: false);
    }

    private void IedSaveScl_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDeviceFromButton(sender, out var device) || device.IsBusy)
            return;

        var model = device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel;
        if (model == null)
        {
            SetStatus(device.HasDiscoveryCache
                ? $"{device.Name}: run Re-scan to capture a complete live model before saving SCL."
                : $"{device.Name}: open SCL or complete live discovery before saving SCL.");
            return;
        }

        SelectedDevice = device;
        var sourceDescription = device.SclWorkspace != null
            ? "Opened SCL design model"
            : "Last successful live MMS discovery";
        var schemaDialog = new SaveSclWindow(device.Name, sourceDescription)
        {
            Owner = this
        };
        if (schemaDialog.ShowDialog() != true)
            return;

        var schema = schemaDialog.ViewModel.SelectedSchemaProfile;
        var sourceSuffix = device.SclWorkspace != null ? "interoperable" : "discovered";
        var editionSuffix = schema.IsEdition2 ? "ed2" : "ed1";
        var dialog = new SaveFileDialog
        {
            Title = $"Save {device.Name} — {schema.DisplayName}",
            Filter = schema.IsEdition2
                ? "Edition 2 IID file (*.iid)|*.iid|All files (*.*)|*.*"
                : "Edition 1 ICD file (*.icd)|*.icd|All files (*.*)|*.*",
            DefaultExt = schema.DefaultExtension,
            AddExtension = true,
            FileName = $"{SafeSclFileStem(device.Name)}-{sourceSuffix}-{editionSuffix}{schema.DefaultExtension}"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            if (device.SclWorkspace != null &&
                schema.IsEdition2 &&
                !string.IsNullOrWhiteSpace(device.SclSourcePath) &&
                File.Exists(device.SclSourcePath))
            {
                SaveOpenedSclAsGenericEdition2(device, dialog.FileName);
                return;
            }

            SaveTypedModelAsScl(device, model, sourceDescription, schema, dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            AddLog("ERROR", "SCL Export", $"{device.Name}: {ex.Message}");
            MarkDiagnosticAlert();
            SetStatus($"{device.Name}: SCL export failed. Diagnostics is marked with !.");
            MessageBox.Show(this, ex.Message, "Save SCL", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            AddLog("ERROR", "SCL Export", $"{device.Name}: unexpected export failure: {ex}");
            MarkDiagnosticAlert();
            SetStatus($"{device.Name}: SCL export failed. Diagnostics is marked with !.");
            MessageBox.Show(this, ex.Message, "Save SCL", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveOpenedSclAsGenericEdition2(Iec61850MonitorDevice device, string outputPath)
    {
        var result = InteroperableSclConverter.WriteFiles(
            device.SclSourcePath,
            outputPath,
            new InteroperableSclConversionOptions
            {
                IedName = string.IsNullOrWhiteSpace(device.SclIedName) ? device.Name : device.SclIedName,
                PreserveAllIeds = false,
                ToolId = "ARIEC61850"
            });

        AddLog(
            "INFO",
            "SCL Export",
            $"{device.Name}: saved generic interoperable Edition 2 IID from opened SCL. LD={result.LogicalDeviceCount}, LN={result.LogicalNodeCount}, DataSet={result.DataSetCount}, RCB={result.ReportControlCount}, findings={result.Findings.Count}. SCL={result.OutputPath}");

        foreach (var finding in result.Findings.Take(12))
        {
            var level = finding.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)
                ? "ERROR"
                : finding.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? "WARN" : "INFO";
            var reference = string.IsNullOrWhiteSpace(finding.Reference)
                ? string.Empty
                : $" [{finding.Reference}]";
            AddLog(level, "SCL Export", $"{device.Name} • {finding.Code}{reference}: {finding.Message}");
        }
        if (result.Findings.Count > 12)
            AddLog("WARN", "SCL Export", $"{device.Name}: {result.Findings.Count - 12} additional interoperability finding(s) are available in {result.ReportPath}.");
        if (result.Findings.Any(finding => finding.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)))
            MarkDiagnosticAlert();

        SetStatus($"{device.Name}: generic interoperable Edition 2 IID saved to {result.OutputPath}");
        ShowSclSaveSuccess(
            "Edition 2 (generic interoperable IID)",
            result.OutputPath,
            result.ReportPath,
            result.SummaryPath);
    }

    private void SaveTypedModelAsScl(
        Iec61850MonitorDevice device,
        AR.Iec61850.Discovery.LiveIedModelDiscoveryDocument model,
        string sourceDescription,
        SclSchemaProfileDescriptor schema,
        string outputPath)
    {
        var result = LiveIedSclExporter.WriteFiles(
            model,
            outputPath,
            new LiveIedSclExportOptions
            {
                Profile = "safe-connection",
                SchemaProfile = schema.Profile,
                IpAddress = device.IpAddress
            });

        AddLog(
            "INFO",
            "SCL Export",
            $"{device.Name}: saved {result.SclSchema} from {sourceDescription.ToLowerInvariant()}. LD={result.LogicalDeviceCount}, LN={result.LogicalNodeCount}, DataSet={result.DataSetCount}, RCB={result.ReportControlCount}, warnings={result.Warnings.Count}. SCL={result.SclPath}");

        foreach (var warning in result.Warnings.Take(12))
        {
            var reference = string.IsNullOrWhiteSpace(warning.Reference)
                ? string.Empty
                : $" [{warning.Reference}]";
            AddLog("WARN", "SCL Export", $"{device.Name} • {warning.Code}{reference}: {warning.Message}");
        }
        if (result.Warnings.Count > 12)
            AddLog("WARN", "SCL Export", $"{device.Name}: {result.Warnings.Count - 12} additional export warning(s) are available in {result.ReportPath}.");

        SetStatus($"{device.Name}: {result.SclSchema} saved to {result.SclPath}");
        ShowSclSaveSuccess(result.SclSchema, result.SclPath, result.ReportPath, result.SummaryPath);
    }

    private void ShowSclSaveSuccess(string schema, string sclPath, string reportPath, string summaryPath)
    {
        MessageBox.Show(
            this,
            $"SCL saved successfully.\n\nSchema: {schema}\nSCL: {sclPath}\nEvidence: {reportPath}\nSummary: {summaryPath}",
            "Save SCL",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void IedRemove_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDeviceFromButton(sender, out var device) || device.IsBusy) return;
        SaveSignalSelectionMemory(device);
        await _runtime.StopDeviceAsync(device.DeviceId);
        device.HasReportStream = false;
        device.ReportPulseActive = false;
        _reportPulseUntil.Remove(device.DeviceId);
        RemoveDeviceHighlights(device.DeviceId);
        RemoveDevicePoints(device.DeviceId);
        DetachSignalHandlers(device.Signals);
        Devices.Remove(device);
        _pendingProjectSelections.Remove(device.DeviceId);
        RemoveControlFeedbackIndex(device.DeviceId);
        SelectedDevice = Devices.FirstOrDefault();
        RaiseWorkspaceCounts();
        SetStatus($"{device.Name}: removed from the workspace.");
    }

    private async Task<bool> StartDeviceMonitorAsync(Iec61850MonitorDevice device, bool navigateToExplorer = true)
    {
        try
        {
            device.HasReportStream = false;
            device.ReportPulseActive = false;
            _reportPulseUntil.Remove(device.DeviceId);
            SaveSignalSelectionMemory(device);
            // Clear the previous UI projection before starting the new runtime. Doing
            // this afterwards could discard the first live snapshots emitted by the
            // newly started session before WPF had a chance to render them.
            RemoveDeviceHighlights(device.DeviceId);
            RemoveDevicePoints(device.DeviceId);
            device.Points.Clear();

            SetStatus($"{device.Name}: arming static/dynamic IEC 61850 reporting…");
            var points = await _runtime.StartMonitoringAsync(
                device,
                device.Signals,
                PollingIntervalMs,
                _applicationCancellation.Token);

            device.Points.AddRange(points);
            GlobalPoints.AddRange(points);
            RebuildControlFeedbackIndex(device);
            foreach (var point in points)
                _pointIndex[point.PointKey] = point;
            device.RefreshComputed();
            RaiseWorkspaceCounts();
            if (navigateToExplorer)
                MainTabs.SelectedIndex = 0;
            SetStatus($"{device.Name}: monitoring {points.Count} point(s). {device.AcquisitionMode}");
            return true;
        }
        catch (OperationCanceledException)
        {
            SetStatus($"{device.Name}: monitor start cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            AddLog("ERROR", device.Name, ex.Message);
            SetStatus($"{device.Name}: monitor start failed. Diagnostics is marked with !.");
            MarkDiagnosticAlert();
            return false;
        }
    }

    private async Task StopDeviceMonitorAsync(Iec61850MonitorDevice device)
    {
        await _runtime.StopMonitoringAsync(device.DeviceId);
        device.HasReportStream = false;
        device.ReportPulseActive = false;
        _reportPulseUntil.Remove(device.DeviceId);
        RemoveDeviceHighlights(device.DeviceId);
        RemoveDevicePoints(device.DeviceId);
        device.Points.Clear();
        device.RefreshComputed();
        RaiseWorkspaceCounts();
        SetStatus($"{device.Name}: monitoring stopped; MMS connection remains available.");
    }

    private async Task StopDeviceConnectionAsync(Iec61850MonitorDevice device)
    {
        await _runtime.StopDeviceAsync(device.DeviceId);
        device.HasReportStream = false;
        device.ReportPulseActive = false;
        _reportPulseUntil.Remove(device.DeviceId);
        RemoveDeviceHighlights(device.DeviceId);
        RemoveDevicePoints(device.DeviceId);
        device.Points.Clear();
        if (device.HasDiscoveryCache)
        {
            device.Status = "Saved model ready";
            device.Detail = "Press Play for fast reconnect and live values. Use Re-scan only when the IED model changes.";
            device.AcquisitionMode = "Saved model • fast connect";
        }
        device.RefreshComputed();
        RaiseWorkspaceCounts();
        SetStatus($"{device.Name}: connection closed; saved model retained for fast reconnect.");
    }

    private void Runtime_PointUpdated(Iec61850PointSnapshot snapshot)
    {
        _pendingPointSnapshots.AddOrUpdate(
            snapshot.Point.PointKey,
            _ => new PendingPointUpdate(snapshot, snapshot.IsValueEdge),
            (_, current) => current.Merge(snapshot));
    }

    private void Runtime_EventRaised(Iec61850EventEntry entry)
        => _pendingEvents.Enqueue(entry);

    private void Runtime_Diagnostic(DiagnosticEntry entry)
    {
        if (!entry.Level.Equals("EVENT", StringComparison.OrdinalIgnoreCase))
            _pendingDiagnostics.Enqueue(entry);
    }

    private void ProgressAnimationTimer_Tick(object? sender, EventArgs e)
    {
        foreach (var device in Devices)
        {
            if (device.IsBusy)
                device.AdvanceDiscoveryProgressAnimation();
        }
    }

    private static async Task WaitForDiscoveryProgressAnimationAsync(
        Iec61850MonitorDevice device,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (device.DiscoveryProgressPercent < 99.5d && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        device.CompleteDiscoveryProgressAnimation();
    }

    private void UiFlushTimer_Tick(object? sender, EventArgs e)
    {
        FlushGooseSubscriberUi();

        var pointChanged = false;
        foreach (var pointKey in _pendingPointSnapshots.Keys.ToArray())
        {
            if (!_pendingPointSnapshots.TryRemove(pointKey, out var pending)) continue;
            var snapshot = pending.Snapshot;
            var point = snapshot.Point;
            if (snapshot.Sequence < point.Sequence)
                continue;
            var uiDetectedEdge = point.ApplyProcessValue(snapshot.Value);
            if (pending.HasValueEdge || snapshot.IsValueEdge || uiDetectedEdge)
                MarkPointRecentlyChanged(point);
            point.Quality = snapshot.Quality;
            point.DeviceTimestamp = snapshot.DeviceTimestamp;
            point.SourceMode = snapshot.SourceMode;
            point.Reason = snapshot.Reason;
            point.Status = snapshot.Status;
            point.Sequence = snapshot.Sequence;
            UpdateCommandFeedbackFromLivePoint(point);
            if (snapshot.IsReportTraffic)
            {
                var device = Devices.FirstOrDefault(item => item.DeviceId.Equals(point.DeviceId, StringComparison.OrdinalIgnoreCase));
                if (device != null)
                {
                    device.HasReportStream = true;
                    device.ReportPulseActive = true;
                    _reportPulseUntil[device.DeviceId] = DateTime.UtcNow.AddMilliseconds(650);
                }
            }
            pointChanged = true;
        }

        var eventBatch = new List<Iec61850EventEntry>(256);
        while (eventBatch.Count < 1000 && _pendingEvents.TryDequeue(out var entry))
            eventBatch.Add(entry);
        if (eventBatch.Count > 0)
        {
            // Runtime SOE is authoritative for discrete edges. Re-marking here is a
            // second guard against a later coalesced snapshot hiding the visual flash.
            foreach (var eventEntry in eventBatch)
                MarkPointRecentlyChanged(eventEntry.PointKey);

            if (MainTabs.SelectedIndex != 2)
            {
                foreach (var group in eventBatch.GroupBy(item => item.DeviceId, StringComparer.OrdinalIgnoreCase))
                {
                    var device = Devices.FirstOrDefault(item => item.DeviceId.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
                    device?.AddUnreadEvents(group.Count());
                }
            }

            Events.AddRange(eventBatch);
            Events.TrimStart(10000);
            var latest = eventBatch[^1];
            LastStatusText = $"EVENT {latest.DeviceName} / {latest.SignalName} = {latest.EventValue}";
        }

        var diagnosticBatch = new List<DiagnosticEntry>(128);
        while (diagnosticBatch.Count < 500 && _pendingDiagnostics.TryDequeue(out var diagnostic))
            diagnosticBatch.Add(diagnostic);
        if (diagnosticBatch.Count > 0)
        {
            Logs.AddRange(diagnosticBatch);
            Logs.TrimStart(4000);
            if (diagnosticBatch.Any(item => item.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase)))
                MarkDiagnosticAlert();

            var important = diagnosticBatch.LastOrDefault(item => item.Level is "ERROR" or "WARN");
            if (important != null)
                LastStatusText = $"{important.Level} • {important.Source} • {important.Message}";
        }

        if (_reportPulseUntil.Count > 0)
        {
            var nowUtc = DateTime.UtcNow;
            foreach (var deviceId in _reportPulseUntil
                         .Where(item => item.Value <= nowUtc)
                         .Select(item => item.Key)
                         .ToArray())
            {
                _reportPulseUntil.Remove(deviceId);
                var device = Devices.FirstOrDefault(item => item.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
                if (device != null)
                    device.ReportPulseActive = false;
            }
        }

        if (_pointHighlightUntil.Count > 0)
        {
            var nowUtc = DateTime.UtcNow;
            foreach (var pointKey in _pointHighlightUntil
                         .Where(item => item.Value <= nowUtc)
                         .Select(item => item.Key)
                         .ToArray())
            {
                _pointHighlightUntil.Remove(pointKey);
                if (_pointIndex.TryGetValue(pointKey, out var point))
                    point.IsRecentlyChanged = false;
            }
        }

        if (pointChanged || eventBatch.Count > 0 || diagnosticBatch.Count > 0)
            RaiseWorkspaceCounts();
    }

    private void Signal_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SignalDefinition.IsSelected)) return;
        if (sender is not SignalDefinition changedSignal) return;
        if (_signalOwners.TryGetValue(changedSignal, out var owner))
        {
            if (owner.IsBulkSignalSelectionUpdate)
                return;
            owner.ApplySignalSelectionChange(changedSignal, changedSignal.IsSelected);
        }
        RaiseWorkspaceCounts();
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        foreach (var device in Devices)
            SaveSignalSelectionMemory(device);

        var dialog = new SaveFileDialog
        {
            Title = "Save ArIED 61850 Project",
            Filter = "ArIED 61850 Project (*.aried.json)|*.aried.json|JSON files (*.json)|*.json",
            FileName = "ArIED-61850-Session.aried.json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;

        var project = new Iec61850TesterProject
        {
            ProjectName = Path.GetFileNameWithoutExtension(dialog.FileName),
            DefaultPollingIntervalMs = PollingIntervalMs,
            Devices = Devices.Select(device => new Iec61850TesterDeviceProfile
            {
                DeviceId = device.DeviceId,
                Name = device.Name,
                IdentitySource = device.IdentitySource,
                LogicalDeviceSummary = device.LogicalDeviceSummary,
                IpAddress = device.IpAddress,
                Port = device.Port,
                AllowDynamicDataSetWrites = device.AllowDynamicDataSetWrites,
                DiscoverySucceeded = device.HasDiscoveryCache && device.Signals.Count > 0,
                SclSourcePath = device.SclSourcePath,
                SclSourceSha256 = device.SclSourceSha256,
                SclIedName = device.SclIedName,
                SclAccessPointName = device.SclAccessPointName,
                SelectedReferences = device.Signals
                    .Where(signal => signal.IsSelected)
                    .Select(signal => NormalizeReference(signal.ObjectReference))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                CachedSignals = device.HasDiscoveryCache
                    ? device.Signals
                        .Where(signal => !signal.IsControlSignal || signal.IsValidControlObject)
                        .Select(Iec61850CachedSignalProfile.FromSignal)
                        .ToList()
                    : new List<Iec61850CachedSignalProfile>()
            }).ToList()
        };

        try
        {
            await TesterProjectStore.SaveAsync(dialog.FileName, project, _applicationCancellation.Token);
            SetStatus($"Project saved: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            AddLog("ERROR", "Project", ex.Message);
            SetStatus("Project save failed. Diagnostics is marked with !.");
        }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open ArIED 61850 Project",
            Filter = "ArIED 61850 Project (*.aried.json)|*.aried.json|JSON files (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var project = await TesterProjectStore.LoadAsync(dialog.FileName, _applicationCancellation.Token);
            if (project == null)
            {
                SetStatus("The selected project file is empty or unreadable.");
                return;
            }

            foreach (var device in Devices.ToList())
            {
                await _runtime.StopDeviceAsync(device.DeviceId);
                DetachSignalHandlers(device.Signals);
            }

            _signalOwners.Clear();
            Devices.Clear();
            GlobalPoints.Clear();
            _pointIndex.Clear();
            _controlFeedbackIndex.Clear();
            _pendingProjectSelections.Clear();
            _pointHighlightUntil.Clear();
            _reportPulseUntil.Clear();
            _pendingPointSnapshots.Clear();
            while (_pendingEvents.TryDequeue(out _)) { }
            PollingIntervalMs = project.DefaultPollingIntervalMs <= 0 ? 1000 : project.DefaultPollingIntervalMs;

            foreach (var profile in project.Devices ?? new List<Iec61850TesterDeviceProfile>())
            {
                var restoredSclWorkspace = await TryRestoreSclWorkspaceAsync(profile);
                var cachedSignals = (profile.CachedSignals ?? new List<Iec61850CachedSignalProfile>())
                    .Where(item => !string.IsNullOrWhiteSpace(item.ObjectReference))
                    .Select(item => item.ToSignal())
                    .Where(signal => !signal.IsControlSignal || signal.IsValidControlObject)
                    .GroupBy(item => NormalizeReference(item.ObjectReference), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                if (restoredSclWorkspace != null)
                    cachedSignals = SclWorkspaceSignalMapper.BuildSignals(restoredSclWorkspace).ToList();
                var selectedReferences = (profile.SelectedReferences ?? new List<string>())
                    .Select(NormalizeReference)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var hasSclProvenance = restoredSclWorkspace != null || !string.IsNullOrWhiteSpace(profile.SclSourceSha256);
                var hasSavedModel = (profile.DiscoverySucceeded || hasSclProvenance) && cachedSignals.Count > 0;
                var device = new Iec61850MonitorDevice
                {
                    DeviceId = string.IsNullOrWhiteSpace(profile.DeviceId) ? Guid.NewGuid().ToString("N") : profile.DeviceId,
                    Name = string.IsNullOrWhiteSpace(profile.Name) ? profile.IpAddress : profile.Name,
                    IdentitySource = profile.IdentitySource,
                    LogicalDeviceSummary = profile.LogicalDeviceSummary,
                    IpAddress = profile.IpAddress,
                    Port = profile.Port <= 0 ? 102 : profile.Port,
                    AllowDynamicDataSetWrites = hasSclProvenance
                        ? ShouldAllowDynamicReportingForScl(cachedSignals)
                        : profile.AllowDynamicDataSetWrites,
                    SclWorkspace = restoredSclWorkspace,
                    SclSourcePath = profile.SclSourcePath,
                    SclSourceSha256 = profile.SclSourceSha256,
                    SclIedName = profile.SclIedName,
                    SclAccessPointName = profile.SclAccessPointName,
                    HasDiscoveryCache = hasSavedModel,
                    Status = hasSclProvenance
                        ? string.IsNullOrWhiteSpace(profile.IpAddress) ? "SCL model ready — bind endpoint" : "SCL model ready"
                        : hasSavedModel ? "Saved model ready" : "Discovery required",
                    Detail = hasSclProvenance
                        ? "SCL design model restored from project provenance. Play fast-connects; Re-scan compares the full live model."
                        : hasSavedModel
                            ? "Press Play for fast connect and live values. Full signal discovery is skipped unless Re-scan is selected."
                            : "This IED has no successful saved discovery. Press Play to scan the live model.",
                    AcquisitionMode = hasSclProvenance
                        ? "SCL project model • offline"
                        : hasSavedModel ? "Saved model • fast connect" : "Not connected • scan required"
                };
                Devices.Add(device);

                if (hasSavedModel)
                {
                    foreach (var signal in cachedSignals)
                    {
                        signal.IsSelected = selectedReferences.Contains(NormalizeReference(signal.ObjectReference)) || signal.IsSelected;
                        signal.PropertyChanged += Signal_PropertyChanged;
                        _signalOwners[signal] = device;
                    }
                    device.Signals.AddRange(cachedSignals);
                    device.RecountSelectedSignals();
                }
                else
                {
                    _pendingProjectSelections[device.DeviceId] = selectedReferences;
                }
            }

            SelectedDevice = Devices.FirstOrDefault();
            RaiseWorkspaceCounts();
            var cachedCount = Devices.Count(device => device.HasDiscoveryCache);
            var sclCount = Devices.Count(device => device.HasSclDesignModel);
            SetStatus($"Project loaded: {Devices.Count} IED profile(s), {cachedCount} cached model(s), {sclCount} SCL design model(s) ready for offline browsing and fast Play connect.");
        }
        catch (Exception ex)
        {
            AddLog("ERROR", "Project", ex.Message);
            SetStatus("Project load failed. Diagnostics is marked with !.");
        }
    }

    private async Task<SclIedWorkspace?> TryRestoreSclWorkspaceAsync(Iec61850TesterDeviceProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.SclSourcePath) || string.IsNullOrWhiteSpace(profile.SclSourceSha256))
            return null;
        if (!File.Exists(profile.SclSourcePath))
        {
            AddLog("WARN", "SCL", $"Saved SCL source is unavailable: {profile.SclSourcePath}. The cached signal model remains usable.");
            return null;
        }

        try
        {
            var document = await _sclWorkspaceService.OpenAsync(
                profile.SclSourcePath,
                new SclWorkspaceOpenOptions
                {
                    IedName = profile.SclIedName,
                    AccessPointName = profile.SclAccessPointName
                },
                _applicationCancellation.Token);
            if (!document.SourceSha256.Equals(profile.SclSourceSha256, StringComparison.OrdinalIgnoreCase))
            {
                AddLog("ERROR", "SCL", $"Saved SCL source changed on disk: {profile.SclSourcePath}. Cached signals were retained and the changed file was not trusted automatically.");
                MarkDiagnosticAlert();
                return null;
            }

            return document.Ieds.FirstOrDefault(item =>
                (string.IsNullOrWhiteSpace(profile.SclIedName) || item.IedName.Equals(profile.SclIedName, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(profile.SclAccessPointName) || item.AccessPointName.Equals(profile.SclAccessPointName, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AddLog("WARN", "SCL", $"Could not restore {profile.SclSourcePath}: {ex.Message}. Cached signals were retained.");
            return null;
        }
    }

    private void ExportEvents_Click(object sender, RoutedEventArgs e)
    {
        if (Events.Count == 0)
        {
            SetStatus("Event log is empty.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export IEC 61850 Event Log",
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"ArIED-61850-Events-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var csv = new StringBuilder();
            csv.AppendLine("Sequence,DeviceTimestamp,IED,IP,Signal,Value,PreviousValue,Transition,Quality,Acquisition,IECTelegram");
            foreach (var entry in Events.OrderBy(item => item.Sequence))
            {
                csv.AppendLine(string.Join(',', new[]
                {
                    Csv(entry.Sequence.ToString(CultureInfo.InvariantCulture)),
                    Csv(entry.DeviceTimestamp),
                    Csv(entry.DeviceName),
                    Csv(entry.IpAddress),
                    Csv(entry.SignalName),
                    Csv(entry.EventValue),
                    Csv(entry.OldValue),
                    Csv(entry.EdgeType),
                    Csv(entry.Quality),
                    Csv(entry.SourceMode),
                    Csv(entry.IecTelegram)
                }));
            }
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            SetStatus($"Event log exported: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            AddLog("ERROR", "Export", ex.Message);
            SetStatus("Event export failed. Diagnostics is marked with !.");
        }
    }

    private void ClearEvents_Click(object sender, RoutedEventArgs e)
    {
        while (_pendingEvents.TryDequeue(out _)) { }
        Events.Clear();
        foreach (var device in Devices)
            device.ClearUnreadEvents();
        Raise(nameof(RuntimeSummaryText));
        SetStatus("Global event log cleared.");
    }

    private async void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var previousContent = button?.Content;
        try
        {
            if (button != null)
            {
                button.IsEnabled = false;
                button.Content = "Collecting…";
            }

            // Include any protocol entries that are still waiting for the normal
            // 100 ms UI batch before taking an immutable support snapshot.
            UiFlushTimer_Tick(null, EventArgs.Empty);
            var devices = Devices.ToArray();
            var logs = Logs.ToArray();
            var report = await DiagnosticReportBuilder.BuildAsync(
                devices,
                logs,
                SelectedDevice,
                _applicationCancellation.Token);

            Clipboard.SetText(report, TextDataFormat.UnicodeText);
            SetStatus($"Diagnostic report copied ({report.Length:N0} characters). Paste it into the support conversation.");
            AddLog("INFO", "Diagnostics", "Support diagnostic report copied to clipboard.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Diagnostic report collection cancelled.");
        }
        catch (Exception ex)
        {
            AddLog("ERROR", "Diagnostics", $"Copy Diagnostic failed: {ex.GetType().Name}: {ex.Message}");
            SetStatus("Copy Diagnostic failed. The error is recorded in Diagnostics.");
        }
        finally
        {
            if (button != null)
            {
                button.Content = previousContent ?? "Copy Diagnostic";
                button.IsEnabled = true;
            }
        }
    }

    private void ClearDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        while (_pendingDiagnostics.TryDequeue(out _)) { }
        Logs.Clear();
        ClearDiagnosticAlert();
        AddLog("INFO", "System", "Diagnostics cleared.");
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;

        // WPF still marks the Window as "closing" while this event is executing.
        // Calling Close() from an async continuation of the same event can therefore
        // throw VerifyNotClosing. Cancel this pass and schedule shutdown only after
        // the Closing event has completely returned to the dispatcher.
        e.Cancel = true;
        if (_shutdownStarted)
            return;

        _shutdownStarted = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => _ = ShutdownApplicationAsync()));
    }

    private async Task ShutdownApplicationAsync()
    {
        try
        {
            _uiFlushTimer.Stop();
            _progressAnimationTimer.Stop();

            foreach (var device in Devices)
            {
                try
                {
                    SaveSignalSelectionMemory(device);
                }
                catch
                {
                    // A preference-file failure must never trap the user in the app.
                }
            }

            _applicationCancellation.Cancel();
            try
            {
                var gooseDisposeTask = _gooseSubscriberRuntime.DisposeAsync().AsTask();
                var gooseCompleted = await Task.WhenAny(gooseDisposeTask, Task.Delay(TimeSpan.FromSeconds(5)));
                if (gooseCompleted == gooseDisposeTask)
                    await gooseDisposeTask;
            }
            catch
            {
                // Raw capture cleanup must not keep the application open.
            }

            try
            {
                var disposeTask = _runtime.DisposeAsync().AsTask();
                var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(5)));
                if (completed == disposeTask)
                    await disposeTask;
            }
            catch
            {
                // Shutdown cleanup must not keep the application open.
            }
        }
        finally
        {
            _applicationCancellation.Dispose();
            _allowClose = true;
            Application.Current.Shutdown();
        }
    }

    private bool TryReadEndpoint(out string ip, out int port)
    {
        ip = NewDeviceIp.Trim();
        port = 0;
        if (!System.Net.IPAddress.TryParse(ip, out _))
        {
            SetStatus($"'{ip}' is not a valid IPv4/IPv6 address.");
            return false;
        }
        if (!int.TryParse(NewDevicePort, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port is <= 0 or > 65535)
        {
            SetStatus("Invalid MMS port. Use TCP port 102 unless the IED is configured differently.");
            return false;
        }
        return true;
    }

    private static string SafeSclFileStem(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "IED" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
            builder.Append(invalid.Contains(character) ? '_' : character);

        var result = builder.ToString().Trim().Trim('.');
        return string.IsNullOrWhiteSpace(result) ? "IED" : result;
    }

    private static bool TryGetDeviceFromButton(object sender, out Iec61850MonitorDevice device)
    {
        device = null!;
        if (sender is not Button button || button.Tag is not Iec61850MonitorDevice target) return false;
        device = target;
        return true;
    }

    private int RestoreSignalSelection(Iec61850MonitorDevice device)
    {
        HashSet<string> selected;
        if (_pendingProjectSelections.TryGetValue(device.DeviceId, out var projectSelection))
        {
            selected = projectSelection;
            _pendingProjectSelections.Remove(device.DeviceId);
        }
        else
        {
            selected = UserPreferenceStore.LoadSignalSelectionProfile(device.Name, device.IpAddress, device.Port)
                .Select(NormalizeReference)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var preferredSelection = selected
            .Select(IecSignalReadResolver.GetPreferredSelectionReference)
            .Select(NormalizeReference)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var restored = 0;
        foreach (var signal in device.Signals)
        {
            var currentReference = NormalizeReference(signal.ObjectReference);
            var preferredReference = NormalizeReference(
                IecSignalReadResolver.GetPreferredSelectionReference(currentReference));
            signal.IsSelected = (signal.CanPublishToRuntime || signal.IsControlSignal) &&
                                currentReference.Equals(preferredReference, StringComparison.OrdinalIgnoreCase) &&
                                preferredSelection.Contains(preferredReference);
            if (signal.IsSelected) restored++;
        }
        device.RecountSelectedSignals();
        return restored;
    }

    private void RememberCurrentSelectionForReconnect(Iec61850MonitorDevice device)
    {
        if (device.Signals.Count == 0) return;
        _pendingProjectSelections[device.DeviceId] = device.Signals
            .Where(signal => signal.IsSelected)
            .Select(signal => NormalizeReference(signal.ObjectReference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        SaveSignalSelectionMemory(device);
    }

    private void SaveSignalSelectionMemory(Iec61850MonitorDevice device)
    {
        if (device.Signals.Count == 0) return;
        try
        {
            UserPreferenceStore.SaveSignalSelectionProfile(
                device.Name,
                device.IpAddress,
                device.Port,
                device.Signals
                    .Where(signal => signal.IsSelected)
                    .Select(signal => signal.ObjectReference));
        }
        catch (Exception ex)
        {
            AddLog("WARN", device.Name, $"Could not save signal-selection memory: {ex.Message}");
        }
    }

    private void MarkPointRecentlyChanged(Iec61850MonitorPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        point.IsRecentlyChanged = true;
        _pointHighlightUntil[point.PointKey] = DateTime.UtcNow.AddSeconds(3);
    }

    private void MarkPointRecentlyChanged(string? pointKey)
    {
        if (string.IsNullOrWhiteSpace(pointKey))
            return;

        if (_pointIndex.TryGetValue(pointKey, out var point))
            MarkPointRecentlyChanged(point);
    }

    private void RemoveDeviceHighlights(string deviceId)
    {
        var prefix = deviceId + "|";
        foreach (var pointKey in _pointHighlightUntil.Keys
                     .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            _pointHighlightUntil.Remove(pointKey);
        }

        foreach (var point in _pointIndex.Values.Where(item =>
                     item.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase)))
        {
            point.IsRecentlyChanged = false;
        }
    }

    private void RemoveDevicePoints(string deviceId)
    {
        for (var index = GlobalPoints.Count - 1; index >= 0; index--)
        {
            if (GlobalPoints[index].DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                GlobalPoints.RemoveAt(index);
        }

        var prefix = deviceId + "|";
        foreach (var pointKey in _pointIndex.Keys
                     .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            _pointIndex.Remove(pointKey);
        }

        foreach (var pointKey in _pendingPointSnapshots.Keys
                     .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            _pendingPointSnapshots.TryRemove(pointKey, out _);
        }
    }

    private void DetachSignalHandlers(BulkObservableCollection<SignalDefinition> signals)
    {
        foreach (var signal in signals)
        {
            signal.PropertyChanged -= Signal_PropertyChanged;
            _signalOwners.Remove(signal);
        }
    }

    internal void ReportUnexpectedUiError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        _pendingDiagnostics.Enqueue(new DiagnosticEntry
        {
            Time = DateTime.Now,
            Level = "ERROR",
            Source = "UI",
            Message = $"{exception.GetType().Name}: {exception.Message}"
        });

        MarkDiagnosticAlert();
        SetStatus("Unexpected UI error captured. Diagnostics is marked with !.");
    }

    private void AddLog(string level, string source, string message)
    {
        Logs.AddRange(new[]
        {
            new DiagnosticEntry
            {
                Time = DateTime.Now,
                Level = level,
                Source = source,
                Message = message
            }
        });
        Logs.TrimStart(4000);

        if (level.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
            MarkDiagnosticAlert();
    }

    private void MarkDiagnosticAlert()
    {
        if (MainTabs?.SelectedIndex == 4 || _hasUnreadDiagnosticError)
            return;

        _hasUnreadDiagnosticError = true;
        Raise(nameof(DiagnosticsAlertVisibility));
    }

    private void ClearDiagnosticAlert()
    {
        if (!_hasUnreadDiagnosticError)
            return;

        _hasUnreadDiagnosticError = false;
        Raise(nameof(DiagnosticsAlertVisibility));
    }

    private void SetStatus(string text)
    {
        LastStatusText = text;
        RaiseWorkspaceCounts();
    }

    private void RaiseWorkspaceCounts()
    {
        Raise(nameof(HeaderStatusText));
        Raise(nameof(DeviceCountText));
        Raise(nameof(RuntimeSummaryText));
        Raise(nameof(ConnectionInsightText));
        Raise(nameof(MonitoringInsightText));
        Raise(nameof(EventInsightText));
        Raise(nameof(SelectedDeviceNoLivePointsVisibility));
        Raise(nameof(ActiveIedTitle));
        Raise(nameof(ActiveIedSubtitle));
    }

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.').Replace("..", ".").ToLowerInvariant();

    private sealed record PendingPointUpdate(Iec61850PointSnapshot Snapshot, bool HasValueEdge)
    {
        public PendingPointUpdate Merge(Iec61850PointSnapshot next)
        {
            // A command-confirmed snapshot can overtake an older report/poll snapshot in
            // the 100 ms UI batching queue. Never let a lower process sequence roll the
            // command row and live grid back to the previous breaker state.
            var newest = next.Sequence < Snapshot.Sequence ? Snapshot : next;
            return new PendingPointUpdate(newest, HasValueEdge || next.IsValueEdge);
        }
    }

    private static string Csv(string value)
        => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(propertyName);
        return true;
    }

    private void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
