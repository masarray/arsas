using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private const string DemoShortcutText = "Ctrl+Shift+D";
    private readonly DispatcherTimer _demoTimer = new(DispatcherPriority.Background);
    private readonly Random _demoRandom = new(61850);
    private readonly List<DemoPointState> _demoPointStates = new();
    private readonly List<DemoGooseStreamState> _demoGooseStates = new();
    private bool _isDemoMode;
    private bool _rcbExportMockOpen;
    private int _demoTick;
    private long _demoEventSequence = 5000;

    public bool IsDemoMode => _isDemoMode;
    public Visibility DemoModeVisibility => Visibility.Collapsed;
    public string DemoModeText => string.Empty;

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift) || e.Key != Key.D)
            return;

        e.Handled = true;
        ToggleDemoMode();
    }

    private void ToggleDemoMode()
    {
        if (_isDemoMode)
        {
            DeactivateDemoMode();
            return;
        }

        if (Devices.Any(device => device.IsConnected || device.IsMonitoring || device.IsBusy) || IsGooseCapturing)
        {
            MessageBox.Show(
                this,
                "Disconnect every active IED and stop GOOSE capture before loading the communication workspace.",
                "ARSAS Workspace",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Devices.Count > 0 || GlobalPoints.Count > 0 || Events.Count > 0)
        {
            var choice = MessageBox.Show(
                this,
                "The communication workspace replaces the current offline workspace. Save the current project first if it must be retained.\n\nContinue?",
                "Load Communication Workspace",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (choice != MessageBoxResult.Yes)
                return;
        }

        ActivateDemoMode();
    }

    private void ActivateDemoMode()
    {
        ClearWorkspaceForDemo();
        _isDemoMode = true;
        _demoTick = 0;
        _demoEventSequence = 5000;
        Title = "ARSAS - Smart IEC 61850 Communication Tester";

        InstallGoosePresentationWorkspace();
        BuildDemoDevicesAndLivePoints();
        BuildDemoEventHistory();
        BuildDemoGooseWorkspace();
        BuildDemoDiagnostics();

        SelectedDevice = Devices.FirstOrDefault();
        MainTabs.SelectedIndex = 0;
        UpdateNavigationVisuals(0, animate: false);
        QueueExplorerSignalGridLayout();

        _demoTimer.Stop();
        _demoTimer.Interval = TimeSpan.FromMilliseconds(1100);
        _demoTimer.Tick -= DemoTimer_Tick;
        _demoTimer.Tick += DemoTimer_Tick;
        _demoTimer.Start();

        Raise(nameof(IsDemoMode));
        Raise(nameof(DemoModeVisibility));
        Raise(nameof(DemoModeText));
        RaiseWorkspaceCounts();
        SetStatus($"Online • {Devices.Count(device => device.IsConnected)} MMS associations • {Devices.Count(device => device.IsMonitoring)} BRCB monitoring • {GlobalPoints.Count:N0} values • {GooseStreams.Count:N0} GOOSE publishers");

        // The existing Ctrl+Shift+D presentation path now opens the RCB export mock
        // after the communication workspace has rendered. No live IED or SCL engine
        // operation is performed by this screenshot-only UX flow.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(OpenDemoRcbExportFilter));
    }

    private void OpenDemoRcbExportFilter()
    {
        if (!_isDemoMode || _rcbExportMockOpen || SelectedDevice is not { IsDemo: true } device)
            return;

        _rcbExportMockOpen = true;
        try
        {
            var dialog = new RcbExportFilterWindow(device.Name, device.EndpointText)
            {
                Owner = this,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            dialog.ShowDialog();
        }
        finally
        {
            _rcbExportMockOpen = false;
        }
    }

    private void DeactivateDemoMode()
    {
        _demoTimer.Stop();
        IsGooseCapturing = false;
        _isDemoMode = false;
        Title = "ARSAS - Smart IEC 61850 Communication Tester";
        ClearWorkspaceForDemo();
        AddLog("INFO", "System", "Communication workspace cleared.");
        Raise(nameof(IsDemoMode));
        Raise(nameof(DemoModeVisibility));
        Raise(nameof(DemoModeText));
        RaiseWorkspaceCounts();
        SetStatus("Ready. Add an IEC 61850 IED or open a saved ARSAS project.");
    }

    private void ClearWorkspaceForDemo()
    {
        _demoTimer.Stop();
        SelectedDevice = null;

        foreach (var device in Devices)
            DetachSignalHandlers(device.Signals);

        Devices.Clear();
        GlobalPoints.Clear();
        Events.Clear();
        Logs.Clear();
        _signalOwners.Clear();
        _pointIndex.Clear();
        _controlFeedbackIndex.Clear();
        _pendingPointSnapshots.Clear();
        _reportPulseUntil.Clear();
        _pointHighlightUntil.Clear();
        while (_pendingEvents.TryDequeue(out _)) { }
        while (_pendingDiagnostics.TryDequeue(out _)) { }

        _demoPointStates.Clear();
        _demoGooseStates.Clear();
        ResetGooseView(resetCounters: true);
        ResetGooseTimelineUi();
        GooseAdapters.Clear();
        SelectedGooseAdapter = null;
    }

    private void BuildDemoDevicesAndLivePoints()
    {
        var deviceSpecs = new[]
        {
            new DemoDeviceSpec("E02BCU1", "BCU • 150 kV line incomer", "192.168.10.11", "4 LD • 29 LN • 186 DO • 1,042 DA", DemoDeviceRole.Bcu),
            new DemoDeviceSpec("E03BCU2", "BCU • transformer HV bay", "192.168.10.12", "4 LD • 31 LN • 194 DO • 1,108 DA", DemoDeviceRole.Bcu),
            new DemoDeviceSpec("E05OCR1", "OCR/GFR • 20 kV incomer", "192.168.10.21", "4 LD • 26 LN • 171 DO • 936 DA", DemoDeviceRole.Ocr),
            new DemoDeviceSpec("E06OCR2", "OCR/GFR • 20 kV feeder", "192.168.10.22", "4 LD • 24 LN • 158 DO • 874 DA", DemoDeviceRole.Ocr),
            new DemoDeviceSpec("E02LDIF1", "87L line differential", "192.168.10.31", "4 LD • 34 LN • 221 DO • 1,294 DA", DemoDeviceRole.LineDiff),
            new DemoDeviceSpec("E03DIST1", "21 distance protection", "192.168.10.32", "4 LD • 33 LN • 216 DO • 1,247 DA", DemoDeviceRole.Distance),
            new DemoDeviceSpec("E03TDIF1", "87T transformer differential", "192.168.10.41", "4 LD • 38 LN • 248 DO • 1,462 DA", DemoDeviceRole.TrafoDiff),
            new DemoDeviceSpec("E04BDIF1", "87B busbar differential", "192.168.10.51", "4 LD • 46 LN • 312 DO • 1,856 DA", DemoDeviceRole.BusbarDiff),
            new DemoDeviceSpec("E05CAP1", "capacitor-bank protection", "192.168.10.61", "4 LD • 25 LN • 164 DO • 902 DA", DemoDeviceRole.CapBank),
            new DemoDeviceSpec("E06BCU3", "BCU • 150 kV bus coupler", "192.168.10.71", "4 LD • 30 LN • 191 DO • 1,081 DA", DemoDeviceRole.Coupler)
        };

        var reportInstances = new[]
        {
            "A_BRCB01", "A_BRCB101", "A_BRCB201", "A_BRCB301", "A_BRCB401",
            "A_BRCB501", "A_BRCB601", "A_BRCB701", "A_BRCB801", "A_BRCB901"
        };

        for (var deviceIndex = 0; deviceIndex < deviceSpecs.Length; deviceIndex++)
        {
            var spec = deviceSpecs[deviceIndex];
            var device = new Iec61850MonitorDevice
            {
                DeviceId = $"demo-{deviceIndex + 1:00}-{spec.Name.ToLowerInvariant()}",
                Name = spec.Name,
                IdentitySource = $"Live MMS discovery • SIPROTEC-class {spec.Description}",
                LogicalDeviceSummary = spec.ModelSummary,
                IpAddress = spec.IpAddress,
                Port = 102,
                Status = "Monitoring • BRCB active",
                Detail = $"Active {spec.Description} communication session with report acquisition, model verification and GOOSE binding.",
                AcquisitionMode = $"Dynamic: {reportInstances[deviceIndex]}",
                HasDiscoveryCache = true,
                IsConnected = true,
                IsMonitoring = true,
                HasReportStream = true,
                IsDemo = true
            };

            var seeds = BuildDemoSignalSeeds(spec.Role, deviceIndex);
            foreach (var seed in seeds)
                AddDemoPoint(device, seed, deviceIndex, reportInstances[deviceIndex]);

            AddDemoCircuitBreakerControl(device);
            device.RecountSelectedSignals();
            device.AddUnreadEvents(1 + deviceIndex % 4);
            Devices.Add(device);
        }
    }
}
