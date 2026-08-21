using System.Threading;
using System.Windows;
using System.Windows.Input;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

internal static class DynamicReportQualificationUiBehavior
{
    private static int _installed;
    private static int _qualificationBusy;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnPreviewKeyDown),
            handledEventsToo: true);
    }

    private static async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not MainWindow window ||
            Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift) ||
            (e.Key != Key.Q && e.Key != Key.R && e.Key != Key.T && e.Key != Key.O && e.Key != Key.C && e.Key != Key.D && e.Key != Key.E))
            return;

        e.Handled = true;

        var device = window.SelectedDevice;
        if (device is null)
        {
            MessageBox.Show(
                window,
                "Select one IEC 61850 IED first. G2 commissioning is intentionally scoped to one explicit IED at a time.",
                "G2 Dynamic Reporting Commissioning",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Interlocked.Exchange(ref _qualificationBusy, 1) != 0)
        {
            MessageBox.Show(
                window,
                "Another G2 commissioning operation is already running.",
                "G2 Dynamic Reporting Commissioning",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            if (e.Key == Key.Q)
                await RunG23Async(window, device);
            else if (e.Key == Key.T)
                await RunP0TriggerProbeAsync(window, device);
            else if (e.Key == Key.O)
                await RunP1OptionalFieldsProbeAsync(window, device);
            else if (e.Key == Key.C)
                await RunG24CleanupClosureAsync(window, device);
            else if (e.Key == Key.D)
                await RunG25ASpontaneousDataChangeAsync(window, device);
            else if (e.Key == Key.E)
                await RunG25A2StimulusEligibilityAsync(window, device);
            else
                await RunG24Async(window, device);
        }
        catch (Exception ex)
        {
            window.LastStatusText = "G2 commissioning stopped locally; production monitoring policy was not changed.";
            MessageBox.Show(
                window,
                "G2 commissioning stopped. Production automatic dynamic reporting remains OFF.\n\n" + ex,
                "G2 Dynamic Reporting Commissioning",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _qualificationBusy, 0);
        }
    }

    private static async Task RunG23Async(MainWindow window, Models.Iec61850MonitorDevice device)
    {
        var selectedCount = device.Signals.Count(signal => signal.IsSelected);
        var answer = MessageBox.Show(
            window,
            $"Run G2.3 dynamic reporting qualification for {device.Name} ({device.EndpointText})?\n\n" +
            $"Selected signals: {selectedCount}\n\n" +
            "This is an explicit COMMISSIONING operation. ARSAS will open a separate auxiliary MMS association, validate exact Class-A scalar points by direct MMS read, then temporarily Define/GetAttributes/Delete bounded DataSets using the 1 → 4 → 8 → 16 → 32 ladder.\n\n" +
            "It will NOT enable an RCB, will NOT write RptEna/GI, will NOT change the production monitoring planner, and will stop if association continuity or cleanup is not proven.\n\n" +
            "Continue?",
            "G2.3 Dynamic Reporting Qualification",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        window.LastStatusText = $"G2.3 qualification: opening isolated auxiliary MMS association to {device.Name}…";
        var service = new DynamicReportQualificationCommissioningService();
        var result = await service.RunAsync(device, device.Signals.ToArray(), CancellationToken.None);
        window.LastStatusText = result.Summary;
        var evidenceWindow = new DynamicReportQualificationResultWindow(result) { Owner = window };
        evidenceWindow.ShowDialog();
    }

    private static async Task RunP0TriggerProbeAsync(MainWindow window, Models.Iec61850MonitorDevice device)
    {
        var answer = MessageBox.Show(
            window,
            $"Run P0 isolated TrgOps micro-probe for {device.Name} ({device.EndpointText})?\n\n" +
            "ACTIVE COMMISSIONING WRITE WARNING — TrgOps ONLY\n\n" +
            "ARSAS will open a separate auxiliary MMS association, force-read live URCB state, choose exactly ONE proven-empty/free URCB, capture its original TrgOps, then write corrected IEC dchg+GI TrgOps using the reserved-bit mapping (canonical raw target 0244).\n\n" +
            "It will immediately read back the significant TrgOps bits, then restore the exact captured original TrgOps value in a finally path and verify the significant bits again. Raw BER differences are retained as evidence, including padding-only differences.\n\n" +
            "This P0 action does NOT write OptFlds, DatSet, Resv, RptEna or GI, does NOT create/delete a DataSet, does NOT start a report monitor, and does NOT advance the G2 profile. Production dynamic reporting remains OFF.\n\n" +
            "Continue?",
            "P0 Isolated TrgOps Micro-Probe",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        window.LastStatusText = $"P0: opening isolated auxiliary MMS association to {device.Name} for one-URCB TrgOps micro-probe…";
        var service = new DynamicReportTriggerOptionsProbeCommissioningService();
        var result = await service.RunAsync(device, device.Signals.ToArray(), CancellationToken.None);
        window.LastStatusText = result.Summary;
        var evidenceWindow = new DynamicReportQualificationResultWindow(result) { Owner = window };
        evidenceWindow.ShowDialog();
    }

    private static async Task RunP1OptionalFieldsProbeAsync(MainWindow window, Models.Iec61850MonitorDevice device)
    {
        var answer = MessageBox.Show(
            window,
            $"Run P1 isolated OptFlds micro-probe for {device.Name} ({device.EndpointText})?\n\n" +
            "ACTIVE COMMISSIONING WRITE WARNING — OptFlds ONLY\n\n" +
            "ARSAS will open a separate auxiliary MMS association, force-read live URCB state, choose exactly ONE proven-empty/free URCB, capture its original OptFlds, then temporarily request only reason-for-inclusion + data-set-name (canonical raw target 061800).\n\n" +
            "It will immediately read back the significant OptFlds bits, then restore the exact captured original OptFlds value in a finally path and verify the significant bits again. Raw BER differences are retained separately, including padding-only differences.\n\n" +
            "This P1 action does NOT write TrgOps, DatSet, Resv, RptEna or GI, does NOT create/delete a DataSet, does NOT start a report monitor, and does NOT advance the G2 profile. Production dynamic reporting remains OFF.\n\n" +
            "Continue?",
            "P1 Isolated OptFlds Micro-Probe",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        window.LastStatusText = $"P1: opening isolated auxiliary MMS association to {device.Name} for one-URCB OptFlds micro-probe…";
        var service = new DynamicReportOptionalFieldsProbeCommissioningService();
        var result = await service.RunAsync(device, device.Signals.ToArray(), CancellationToken.None);
        window.LastStatusText = result.Summary;
        var evidenceWindow = new DynamicReportQualificationResultWindow(result) { Owner = window };
        evidenceWindow.ShowDialog();
    }

    private static async Task RunG24CleanupClosureAsync(MainWindow window, Models.Iec61850MonitorDevice device)
    {
        var answer = MessageBox.Show(
            window,
            $"Run G2.4-C fresh-association cleanup closure for {device.Name} ({device.EndpointText})?\n\n" +
            "READ-ONLY PHYSICAL MERGE GATE\n\n" +
            "ARSAS will open a NEW auxiliary MMS association and re-read the exact InformationReport-proven URCB plus the exact temporary G2.4 DataSet identity stored in the profile.\n\n" +
            "PASS requires fresh evidence that RptEna=false, DatSet is empty, Resv=false, Owner is empty, the temporary DataSet is absent from NamedVariableList discovery and has no readable directory, and the fresh association remains healthy. Current TrgOps/OptFlds are captured as corroborating read-only evidence.\n\n" +
            "This G2.4-C action performs ZERO MMS writes: no RptEna, Resv, DatSet, TrgOps, OptFlds, GI, DefineNamedVariableList, DeleteNamedVariableList, report monitor, profile save, or production-policy change.\n\n" +
            "Continue?",
            "G2.4-C Fresh Association Cleanup Closure",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        window.LastStatusText = $"G2.4-C: opening fresh read-only auxiliary MMS association to {device.Name}…";
        var service = new DynamicReportCleanupClosureCommissioningService();
        var result = await service.RunAsync(device, device.Signals.ToArray(), CancellationToken.None);
        window.LastStatusText = result.Summary;
        var evidenceWindow = new DynamicReportQualificationResultWindow(result) { Owner = window };
        evidenceWindow.ShowDialog();
    }

    private static async Task RunG25A2StimulusEligibilityAsync(MainWindow window, Models.Iec61850MonitorDevice device)
    {
        var answer = MessageBox.Show(
            window,
            $"Run G2.5-A2 read-only stimulus eligibility discovery for {device.Name} ({device.EndpointText})?\n\n" +
            "READ-ONLY PHYSICAL DIAGNOSTIC — NO RCB / NO DATASET / NO GI\n\n" +
            "ARSAS will open ONE isolated read-only MMS association, discover and rank a bounded set of ST/stVal status candidates, and capture their baseline values. Priority is given to the live ControlStatusReference, XCBR/CSWI/XSWI Pos.stVal, breaker command-received status, and related GGIO status points.\n\n" +
            "IMPORTANT: do NOT operate anything while discovery/baseline is running. WAIT until the status explicitly shows 'G2.5-A2 READY — READ ONLY'. Only then perform ONE normal safe OPEN/CLOSE or equivalent physical stimulus. Do not repeat the stimulus.\n\n" +
            "A2 samples a fast lane of at most 8 candidates plus a bounded secondary lane. After the first observed transition it samples for a short settle period to classify the candidate as persistent/latched or momentary/pulse-like.\n\n" +
            "This A2 action performs ZERO RCB/DataSet mutation: no RptEna, Resv, DatSet, TrgOps, OptFlds, GI, Define/DeleteNamedVariableList, report monitor, profile save, or ProductionEligible change. A2 PASS only identifies which MMS point physically responds; it does NOT prove dchg reporting.\n\n" +
            "Continue?",
            "G2.5-A2 Stimulus Eligibility Discovery",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        window.LastStatusText = $"G2.5-A2: discovering bounded read-only stimulus candidates on {device.Name}; DO NOT stimulate yet…";
        var progress = new Progress<string>(text => window.LastStatusText = text);
        var service = new DynamicReportStimulusEligibilityDiscoveryService();
        var result = await service.RunAsync(device, device.Signals.ToArray(), progress, CancellationToken.None);
        window.LastStatusText = result.Summary;
        var evidenceWindow = new DynamicReportQualificationResultWindow(result) { Owner = window };
        evidenceWindow.ShowDialog();
    }

    private static async Task RunG25ASpontaneousDataChangeAsync(MainWindow window, Models.Iec61850MonitorDevice device)
    {
        var answer = MessageBox.Show(
            window,
            $"Run G2.5-A1 spontaneous dchg + independent stimulus witness for {device.Name} ({device.EndpointText})?\n\n" +
            "ACTIVE COMMISSIONING — ONE URCB / NO GI + READ-ONLY WITNESS\n\n" +
            "The core path is unchanged G2.5-A: exact G2.4-proven URCB + 8-member set, temporary TrgOps=dchg ONLY (0240), reason-for-inclusion + data-set-name (061800), one temporary DataSet, RptEna=true, NO GI, and strict spontaneous data-change report validation.\n\n" +
            "G2.5-A1 adds a SECOND MMS association that is READ ONLY. It resolves and samples only the same 8 proven process/status members. It does NOT read/write RCB attributes, does NOT Define/Delete a DataSet, and does NOT send GI.\n\n" +
            "IMPORTANT: the status may briefly show 'G2.5-A ARMED — NO GI'. DO NOT stimulate on that message. WAIT until the status changes to 'G2.5-A1 WITNESS READY'. Only then cause ONE normal safe physical/process status change affecting one of the 8 proven points. Do NOT manually edit any RCB or DataSet.\n\n" +
            "The witness records baseline -> changed values and DataSet indexes. If no report arrives, evidence distinguishes 'stimulus did not touch the envelope' from 'qualified member changed but no dchg report arrived'. If a report arrives, G2.5-A1 correlates the witnessed changed index with the report included index.\n\n" +
            "The existing G2.5-A cleanup/restore/fresh-association closure remains mandatory. The persisted InformationReportProven profile is NOT modified and production dynamic reporting remains OFF.\n\n" +
            "Continue?",
            "G2.5-A1 Stimulus Witness",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        window.LastStatusText = $"G2.5-A1: preparing dchg-only report path plus independent read-only witness for {device.Name}…";
        var progress = new Progress<string>(text => window.LastStatusText = text);
        var service = new DynamicReportStimulusWitnessCommissioningService();
        var result = await service.RunAsync(device, device.Signals.ToArray(), progress, CancellationToken.None);
        window.LastStatusText = result.Summary;
        var evidenceWindow = new DynamicReportQualificationResultWindow(result) { Owner = window };
        evidenceWindow.ShowDialog();
    }

    private static async Task RunG24Async(MainWindow window, Models.Iec61850MonitorDevice device)
    {
        var answer = MessageBox.Show(
            window,
            $"Run G2.4 one-URCB InformationReport proof for {device.Name} ({device.EndpointText})?\n\n" +
            "ACTIVE COMMISSIONING WRITE WARNING\n\n" +
            "ARSAS will use the identity-bound G2.3 EnvelopeQualified profile, open a separate auxiliary MMS association, force-read live URCB DatSet/RptEna/Resv/Owner state, and choose exactly ONE proven-empty/free URCB.\n\n" +
            "For that one URCB only, ARSAS will capture the original TrgOps and OptFlds values, temporarily enable dchg+GI and reason-for-inclusion+data-set-name with IEC significant-bit readback, then create ONE temporary dynamic DataSet with no more than 8 already-qualified members, bind it, reserve the URCB when supported, write RptEna=true, register report routing, request GI, and wait for an ACTUAL strictly mapped InformationReport.\n\n" +
            "RptEna/GI acceptance alone is NOT success. After the proof attempt ARSAS will disable the URCB, restore its prior DatSet binding, delete the temporary DataSet, release reservation, then restore the captured original OptFlds and TrgOps values and verify IEC significant-bit readback. Raw BER evidence is retained separately. The profile advances only when actual report proof AND all cleanup/restore evidence pass.\n\n" +
            "Production monitoring and automatic production dynamic reporting remain OFF/untouched.\n\n" +
            "Continue?",
            "G2.4 One-URCB InformationReport Proof",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        window.LastStatusText = $"G2.4: opening isolated auxiliary MMS association to {device.Name} for transactional one-URCB proof…";
        var service = new DynamicReportActivationCommissioningServiceV2();
        var result = await service.RunAsync(device, device.Signals.ToArray(), CancellationToken.None);
        window.LastStatusText = result.Summary;
        var evidenceWindow = new DynamicReportQualificationResultWindow(result) { Owner = window };
        evidenceWindow.ShowDialog();
    }
}
