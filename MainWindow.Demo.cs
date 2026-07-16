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
    private int _demoTick;
    private long _demoEventSequence = 5000;

    public bool IsDemoMode => _isDemoMode;
    public Visibility DemoModeVisibility => _isDemoMode ? Visibility.Visible : Visibility.Collapsed;
    public string DemoModeText => $"DEMO MODE • {DemoShortcutText}";

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
                "Disconnect every live IED and stop GOOSE capture before entering Demo Mode.",
                "Demo Mode",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Devices.Count > 0 || GlobalPoints.Count > 0 || Events.Count > 0)
        {
            var choice = MessageBox.Show(
                this,
                "Demo Mode replaces the current offline workspace with synthetic substation data. Save the current project first if it must be retained.\n\nContinue?",
                "Load Demo Workspace",
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
        Title = "ArIED 61850 — Synthetic Substation Demo";

        InstallGoosePresentationWorkspace();
        BuildDemoDevicesAndLivePoints();
        BuildDemoEventHistory();
        BuildDemoGooseWorkspace();
        BuildDemoDiagnostics();

        SelectedDevice = Devices.FirstOrDefault();
        MainTabs.SelectedIndex = 0;
        UpdateNavigationVisuals(0, animate: false);

        _demoTimer.Stop();
        _demoTimer.Interval = TimeSpan.FromMilliseconds(1100);
        _demoTimer.Tick -= DemoTimer_Tick;
        _demoTimer.Tick += DemoTimer_Tick;
        _demoTimer.Start();

        Raise(nameof(IsDemoMode));
        Raise(nameof(DemoModeVisibility));
        Raise(nameof(DemoModeText));
        RaiseWorkspaceCounts();
        SetStatus($"DEMO MODE active • 10 synthetic IEDs • {GlobalPoints.Count:N0} live values • {GooseStreams.Count:N0} GOOSE publishers • press {DemoShortcutText} to exit.");
    }

    private void DeactivateDemoMode()
    {
        _demoTimer.Stop();
        IsGooseCapturing = false;
        _isDemoMode = false;
        Title = "ArIED 61850 — Smart IED Explorer & Monitor";
        ClearWorkspaceForDemo();
        AddLog("INFO", "Demo", "Synthetic substation demo closed. No network sessions were created.");
        Raise(nameof(IsDemoMode));
        Raise(nameof(DemoModeVisibility));
        Raise(nameof(DemoModeText));
        RaiseWorkspaceCounts();
        SetStatus($"Demo workspace cleared. Press {DemoShortcutText} to load it again.");
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
            new DemoDeviceSpec("BCU_INCOMER_150KV", "BCU • 150 kV line incomer", "192.168.10.11", "3 LD • 29 LN • 186 DO • 1,042 DA", DemoDeviceRole.Bcu),
            new DemoDeviceSpec("BCU_TRAFO_BAY_TR1", "BCU • transformer HV bay", "192.168.10.12", "3 LD • 31 LN • 194 DO • 1,108 DA", DemoDeviceRole.Bcu),
            new DemoDeviceSpec("PROT_OCR_INCOMER_20KV", "OCR/GFR • 20 kV incomer", "192.168.10.21", "2 LD • 26 LN • 171 DO • 936 DA", DemoDeviceRole.Ocr),
            new DemoDeviceSpec("PROT_OCR_FEEDER_F01", "OCR/GFR • 20 kV feeder", "192.168.10.22", "2 LD • 24 LN • 158 DO • 874 DA", DemoDeviceRole.Ocr),
            new DemoDeviceSpec("PROT_LINE_DIFF_L01", "87L line differential", "192.168.10.31", "3 LD • 34 LN • 221 DO • 1,294 DA", DemoDeviceRole.LineDiff),
            new DemoDeviceSpec("PROT_DISTANCE_L02", "21 distance protection", "192.168.10.32", "3 LD • 33 LN • 216 DO • 1,247 DA", DemoDeviceRole.Distance),
            new DemoDeviceSpec("PROT_TRAFO_DIFF_TR1", "87T transformer differential", "192.168.10.41", "3 LD • 38 LN • 248 DO • 1,462 DA", DemoDeviceRole.TrafoDiff),
            new DemoDeviceSpec("PROT_BUSBAR_DIFF_BB1", "87B busbar differential", "192.168.10.51", "4 LD • 46 LN • 312 DO • 1,856 DA", DemoDeviceRole.BusbarDiff),
            new DemoDeviceSpec("PROT_CAPBANK_CB1", "capacitor-bank protection", "192.168.10.61", "2 LD • 25 LN • 164 DO • 902 DA", DemoDeviceRole.CapBank),
            new DemoDeviceSpec("BCU_BUS_COUPLER_150KV", "BCU • 150 kV bus coupler", "192.168.10.71", "3 LD • 30 LN • 191 DO • 1,081 DA", DemoDeviceRole.Coupler)
        };

        for (var deviceIndex = 0; deviceIndex < deviceSpecs.Length; deviceIndex++)
        {
            var spec = deviceSpecs[deviceIndex];
            var device = new Iec61850MonitorDevice
            {
                DeviceId = $"demo-{deviceIndex + 1:00}-{spec.Name.ToLowerInvariant()}",
                Name = spec.Name,
                IdentitySource = $"DEMO • live MMS discovery • {spec.Description}",
                LogicalDeviceSummary = spec.ModelSummary,
                IpAddress = spec.IpAddress,
                Port = 102,
                Status = "Monitoring • BRCB active",
                Detail = $"Synthetic {spec.Description} session. Buffered reports, MMS validation reads and GOOSE bindings are generated locally; no packets are transmitted.",
                AcquisitionMode = deviceIndex % 3 == 0
                    ? "Buffered Report • dchg/qchg/dupd"
                    : deviceIndex % 3 == 1
                        ? "Unbuffered Report • dchg + integrity"
                        : "Static DataSet report • MMS verification",
                HasDiscoveryCache = true,
                IsConnected = true,
                IsMonitoring = true,
                HasReportStream = true,
                IsDemo = true
            };

            var seeds = BuildDemoSignalSeeds(spec.Role, deviceIndex);
            foreach (var seed in seeds)
                AddDemoPoint(device, seed, deviceIndex);

            device.RecountSelectedSignals();
            device.AddUnreadEvents(1 + deviceIndex % 4);
            Devices.Add(device);
        }
    }
}
