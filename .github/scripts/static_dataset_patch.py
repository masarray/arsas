from pathlib import Path
import re


def replace_once(path, old, new):
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected exactly one match, found {count}")
    p.write_text(text.replace(old, new), encoding="utf-8")


shared = Path("MainWindow.SharedSclWorkspace.cs")
text = shared.read_text(encoding="utf-8")
pattern = re.compile(r"    private void ApplyStaticDataSetSelection\(Iec61850MonitorDevice device\)\n    \{.*?\n    \}\n\n    private void ClearSharedSignalSelection", re.S)
replacement = '''    private void ApplyStaticDataSetSelection(Iec61850MonitorDevice device)
    {
        // Static DataSet is a protocol-authority mode, not a request to monitor the
        // whole IED and then opportunistically prefer reports. First make sure every
        // ARIEC-owned DataSet member is present in the workspace, then select only
        // runtime signals that carry an explicit static DataSet identity.
        var merge = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);
        RegisterRecoveredDataSetSignals(device, merge);
        Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device);

        device.BeginBulkSignalSelection();
        try
        {
            foreach (var signal in device.Signals)
                signal.IsSelected = Iec61850StaticDataSetSelectionPolicy.IsEligible(signal);
        }
        finally
        {
            device.EndBulkSignalSelection();
        }

        SynchronizeAllEngineeringSelectionsToFat(device);
        _sharedSclSelectionAuthorityDeviceIds.Add(device.DeviceId);
        SaveSignalSelectionMemory(device);
        device.RefreshComputed();

        AddLog(
            "INFO",
            device.Name,
            $"Static DataSet report-only authority selected: {device.SelectedLiveSignalCount} runtime DataSet signal(s); cyclic MMS process polling and dynamic DataSet writes are disabled for this monitoring mode.");
    }

    private void ClearSharedSignalSelection'''
text2, n = pattern.subn(replacement, text, count=1)
if n != 1:
    raise SystemExit(f"MainWindow.SharedSclWorkspace.cs: ApplyStaticDataSetSelection patch count={n}")
text = text2
old_mark = '''    private void MarkSharedSelectionAuthority(Iec61850MonitorDevice device)
    {
        _sharedSclSelectionAuthorityDeviceIds.Add(device.DeviceId);
        SaveSignalSelectionMemory(device);
    }'''
new_mark = '''    private void MarkSharedSelectionAuthority(Iec61850MonitorDevice device)
    {
        // Manual selection restores the normal Smart/Hybrid acquisition contract.
        Iec61850MonitoringModeRegistry.UseHybrid(device);
        _sharedSclSelectionAuthorityDeviceIds.Add(device.DeviceId);
        SaveSignalSelectionMemory(device);
    }'''
if text.count(old_mark) != 1:
    raise SystemExit("MainWindow.SharedSclWorkspace.cs: MarkSharedSelectionAuthority source contract changed")
shared.write_text(text.replace(old_mark, new_mark), encoding="utf-8")

Path("Services/Iec61850MonitoringModeRegistry.cs").write_text(r'''using System.Runtime.CompilerServices;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Per-device operator acquisition intent. The state follows the device object lifetime
/// and is deliberately not inferred from signal names, RCB availability, or polling rate.
/// </summary>
public static class Iec61850MonitoringModeRegistry
{
    private sealed class DeviceModeState
    {
        public bool StaticDataSetReportOnly { get; set; }
        public bool PreviousDynamicDataSetWrites { get; set; }
        public bool HasPreviousDynamicDataSetWrites { get; set; }
    }

    private static readonly ConditionalWeakTable<Iec61850MonitorDevice, DeviceModeState> States = new();

    public static bool IsStaticDataSetReportOnly(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return States.TryGetValue(device, out var state) && state.StaticDataSetReportOnly;
    }

    public static void UseStaticDataSetReportOnly(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var state = States.GetOrCreateValue(device);
        lock (state)
        {
            if (!state.StaticDataSetReportOnly)
            {
                state.PreviousDynamicDataSetWrites = device.AllowDynamicDataSetWrites;
                state.HasPreviousDynamicDataSetWrites = true;
            }

            state.StaticDataSetReportOnly = true;
            device.AllowDynamicDataSetWrites = false;
        }
    }

    public static void UseHybrid(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var state = States.GetOrCreateValue(device);
        lock (state)
        {
            state.StaticDataSetReportOnly = false;
            if (state.HasPreviousDynamicDataSetWrites)
            {
                device.AllowDynamicDataSetWrites = state.PreviousDynamicDataSetWrites;
                state.HasPreviousDynamicDataSetWrites = false;
            }
        }
    }
}
''', encoding="utf-8")

Path("Services/Iec61850StaticDataSetSelectionPolicy.cs").write_text(r'''using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Dataset-centric selection boundary used by the SCL Static DataSet workflow.
/// DataSet membership is authoritative; ordinary browsed IED signals and controls do
/// not leak into Live Signal Values merely because MMS can read them.
/// </summary>
public static class Iec61850StaticDataSetSelectionPolicy
{
    public static bool IsEligible(SignalDefinition signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return !signal.IsControlSignal &&
               signal.CanPublishToRuntime &&
               !string.IsNullOrWhiteSpace(signal.DataSetReference);
    }
}
''', encoding="utf-8")

runtime = Path("Services/Iec61850MonitorRuntime.cs")
r = runtime.read_text(encoding="utf-8")
replacements = [
(
'''        public HybridReportPhysicalValidationTracker HybridValidation { get; } = new();
    }''',
'''        public HybridReportPhysicalValidationTracker HybridValidation { get; } = new();
        public bool StaticDataSetReportOnly { get; set; }
    }'''),
(
'''        var selected = selectedSignals
            .Where(signal => signal.IsSelected && signal.CanPublishToRuntime)
            .GroupBy(signal => NormalizeReference(signal.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();''',
'''        var staticDataSetReportOnly = Iec61850MonitoringModeRegistry.IsStaticDataSetReportOnly(device);
        var selected = selectedSignals
            .Where(signal => signal.IsSelected && signal.CanPublishToRuntime)
            .Where(signal => !staticDataSetReportOnly || !string.IsNullOrWhiteSpace(signal.DataSetReference))
            .GroupBy(signal => NormalizeReference(signal.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(signal => !string.IsNullOrWhiteSpace(signal.DataSetReference))
                .ThenByDescending(signal => !signal.Category.Equals("DataSet", StringComparison.OrdinalIgnoreCase))
                .First())
            .ToList();'''),
(
'''        session.HybridValidation.Reset(null);

        var safePollMs''',
'''        session.HybridValidation.Reset(null);
        session.StaticDataSetReportOnly = staticDataSetReportOnly;

        var safePollMs'''),
(
'''            session.States[point.PointKey] = new RuntimePointState
            {
                NextPollUtc = DateTime.UtcNow,
                SourceMode = signal.IsReportCapable ? "Report pending / polling fallback" : "MMS polling",
                Reason = signal.IsReportCapable ? "report plan pending" : "cyclic"
            };''',
'''            session.States[point.PointKey] = new RuntimePointState
            {
                NextPollUtc = staticDataSetReportOnly ? DateTime.MaxValue : DateTime.UtcNow,
                AcquisitionLabel = staticDataSetReportOnly ? "Static DataSet report pending" : "MMS polling",
                SourceMode = staticDataSetReportOnly
                    ? "Static DataSet report pending"
                    : signal.IsReportCapable ? "Report pending / polling fallback" : "MMS polling",
                Reason = staticDataSetReportOnly
                    ? "configured static DataSet / RCB required; cyclic MMS process polling disabled"
                    : signal.IsReportCapable ? "report plan pending" : "cyclic",
                Status = staticDataSetReportOnly ? "Waiting for static RCB" : "Queued"
            };'''),
(
'''        session.HealthProbePointKey = session.Points.Values
            .OrderByDescending(IsFastPoint)
            .ThenBy(point => point.SignalName, StringComparer.OrdinalIgnoreCase)
            .Select(point => point.PointKey)
            .FirstOrDefault() ?? string.Empty;''',
'''        session.HealthProbePointKey = staticDataSetReportOnly
            ? string.Empty
            : session.Points.Values
                .OrderByDescending(IsFastPoint)
                .ThenBy(point => point.SignalName, StringComparer.OrdinalIgnoreCase)
                .Select(point => point.PointKey)
                .FirstOrDefault() ?? string.Empty;'''),
(
'''        session.ReportSetupNotBeforeUtc = DateTime.UtcNow.AddMilliseconds(350);
        session.ReportSetupDeadlineUtc = DateTime.UtcNow.AddMilliseconds(1500);
        ResetPollQueue(session);

        device.IsMonitoring = true;
        device.IsConnected = true;
        device.Status = "Monitoring";
        device.AcquisitionMode = session.ReportSetupPending
            ? "MMS live start • arming ARIEC hybrid reporting"
            : $"MMS polling fallback • {session.Points.Count} point(s)";
        device.Detail = session.ReportSetupPending
            ? $"{session.Points.Count} point(s): MMS is reading the initial live image immediately while the ARIEC hybrid planner validates fresh static/dynamic BRCB/URCB capability in the same independent IED session."
            : $"{session.Points.Count} point(s): no report candidate is available; MMS polling is active.";
        device.RefreshComputed();

        Log("INFO", device.Name,
            $"Fast live start: points={session.Points.Count}, legacy compatibility plan(s)={plans.Count}, ARIEC hybrid authority={(hasHybridAuthority ? "available" : "unavailable")}, initial MMS scheduler={session.PollQueue.Count}, target={safePollMs} ms. Full signal discovery is not part of monitor start.");''',
'''        var setupUtc = DateTime.UtcNow;
        session.ReportSetupNotBeforeUtc = staticDataSetReportOnly ? setupUtc : setupUtc.AddMilliseconds(350);
        session.ReportSetupDeadlineUtc = staticDataSetReportOnly ? setupUtc : setupUtc.AddMilliseconds(1500);
        ResetPollQueue(session);

        device.IsMonitoring = true;
        device.IsConnected = true;
        device.Status = "Monitoring";
        if (staticDataSetReportOnly)
        {
            device.AcquisitionMode = session.ReportSetupPending
                ? "Static DataSet • arming configured RCB"
                : "Static DataSet • no static RCB candidate";
            device.Detail = session.ReportSetupPending
                ? $"{session.Points.Count} DataSet-derived point(s): configured BRCB/URCB reporting is being armed immediately. Cyclic MMS process polling and dynamic DataSet writes are disabled."
                : $"{session.Points.Count} DataSet-derived point(s): no static report candidate is available. Values remain unavailable rather than silently falling back to MMS polling.";
        }
        else
        {
            device.AcquisitionMode = session.ReportSetupPending
                ? "MMS live start • arming ARIEC hybrid reporting"
                : $"MMS polling fallback • {session.Points.Count} point(s)";
            device.Detail = session.ReportSetupPending
                ? $"{session.Points.Count} point(s): MMS is reading the initial live image immediately while the ARIEC hybrid planner validates fresh static/dynamic BRCB/URCB capability in the same independent IED session."
                : $"{session.Points.Count} point(s): no report candidate is available; MMS polling is active.";
        }
        device.RefreshComputed();

        Log("INFO", device.Name,
            staticDataSetReportOnly
                ? $"Static DataSet report-only start: points={session.Points.Count}, static report planning pending={session.ReportSetupPending}, cyclic MMS scheduler={session.PollQueue.Count}. Full signal discovery and dynamic DataSet creation are not part of this monitoring mode."
                : $"Fast live start: points={session.Points.Count}, legacy compatibility plan(s)={plans.Count}, ARIEC hybrid authority={(hasHybridAuthority ? "available" : "unavailable")}, initial MMS scheduler={session.PollQueue.Count}, target={safePollMs} ms. Full signal discovery is not part of monitor start.");'''),
(
'''        var initialImageReady = session.States.Values.All(state =>
            state.HasValue || state.ConsecutiveErrors > 0);''',
'''        var initialImageReady = session.StaticDataSetReportOnly || session.States.Values.All(state =>
            state.HasValue || state.ConsecutiveErrors > 0);'''),
(
'''        Log("INFO", session.Device.Name,
            initialImageReady
                ? "Initial live image is available. Validating static/dynamic report acquisition in the background monitor pipeline."
                : "Initial live-image deadline reached. Continuing report validation while MMS fallback remains active.");''',
'''        Log("INFO", session.Device.Name,
            session.StaticDataSetReportOnly
                ? "Static DataSet report-only mode: arming configured RCBs immediately; no cyclic MMS initial-image scheduler is active."
                : initialImageReady
                    ? "Initial live image is available. Validating static/dynamic report acquisition in the background monitor pipeline."
                    : "Initial live-image deadline reached. Continuing report validation while MMS fallback remains active.");'''),
(
'''                if (!result.IsSuccess)
                {
                    Log("WARN", session.Device.Name,
                        $"Report plan unavailable for {plan.DisplayReference}. MMS polling remains the final fallback. {result.Message}");
                    foreach (var warning in result.Warnings.Take(3))
                        Log("WARN", session.Device.Name, warning);
                    continue;
                }''',
'''                if (!result.IsSuccess)
                {
                    Log("WARN", session.Device.Name,
                        session.StaticDataSetReportOnly
                            ? $"Static DataSet report unavailable for {plan.DisplayReference}. MMS process fallback is disabled by operator mode; affected values remain unavailable. {result.Message}"
                            : $"Report plan unavailable for {plan.DisplayReference}. MMS polling remains the final fallback. {result.Message}");
                    if (session.StaticDataSetReportOnly)
                    {
                        foreach (var point in plan.Bindings)
                        {
                            if (!session.States.TryGetValue(point.PointKey, out var unavailableState))
                                continue;
                            unavailableState.SourceMode = "Static DataSet: RCB unavailable";
                            unavailableState.AcquisitionLabel = unavailableState.SourceMode;
                            unavailableState.Reason = result.Message;
                            EmitStatusSnapshot(point, unavailableState, "Static report unavailable / no MMS fallback", "Pending");
                        }
                    }
                    foreach (var warning in result.Warnings.Take(3))
                        Log("WARN", session.Device.Name, warning);
                    continue;
                }'''),
(
'''                foreach (var point in coveredPoints)
                {
                    if (RequiresExactMmsValueAuthority(point.IecReference))
                        continue;
                    session.PointPlanIds[point.PointKey] = plan.PlanId;''',
'''                foreach (var point in coveredPoints)
                {
                    if (RequiresExactMmsValueAuthority(point.IecReference))
                    {
                        if (session.StaticDataSetReportOnly && session.States.TryGetValue(point.PointKey, out var unresolvedState))
                        {
                            unresolvedState.SourceMode = "Static DataSet: report leaf unresolved";
                            unresolvedState.AcquisitionLabel = unresolvedState.SourceMode;
                            unresolvedState.Reason = "structured report projection is not yet schema-proven; unsafe MMS fallback is disabled in Static DataSet mode";
                            EmitStatusSnapshot(point, unresolvedState, "Static report leaf unresolved / no MMS fallback", "Pending");
                        }
                        continue;
                    }
                    session.PointPlanIds[point.PointKey] = plan.PlanId;'''),
(
'''            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log("WARN", session.Device.Name,
                    $"Report setup failed for {plan.DisplayReference}; MMS polling remains the final fallback. {ex.GetType().Name}: {ex.Message}");
            }''',
'''            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log("WARN", session.Device.Name,
                    session.StaticDataSetReportOnly
                        ? $"Static DataSet report setup failed for {plan.DisplayReference}; MMS process fallback is disabled. {ex.GetType().Name}: {ex.Message}"
                        : $"Report setup failed for {plan.DisplayReference}; MMS polling remains the final fallback. {ex.GetType().Name}: {ex.Message}");
                if (session.StaticDataSetReportOnly)
                {
                    foreach (var point in plan.Bindings)
                    {
                        if (!session.States.TryGetValue(point.PointKey, out var failedState))
                            continue;
                        failedState.SourceMode = "Static DataSet: report setup failed";
                        failedState.AcquisitionLabel = failedState.SourceMode;
                        failedState.Reason = $"{ex.GetType().Name}: {ex.Message}";
                        EmitStatusSnapshot(point, failedState, "Static report setup failed / no MMS fallback", "Pending");
                    }
                }
            }''')
]
for old, new in replacements:
    count = r.count(old)
    if count != 1:
        raise SystemExit(f"Iec61850MonitorRuntime.cs: expected one replacement, found {count}: {old[:90]!r}")
    r = r.replace(old, new)

summary_pattern = re.compile(r"    private void UpdateDeviceAcquisitionSummary\(DeviceSession session\)\n    \{.*?\n    \}\n\n    private static int CalculateLoopDelayMs", re.S)
summary_replacement = '''    private void UpdateDeviceAcquisitionSummary(DeviceSession session)
    {
        var dynamicReportCount = session.ActiveReportPlans.Values.Count(plan =>
            plan.Status.Contains("Dynamic", StringComparison.OrdinalIgnoreCase));
        var staticReportCount = session.ActiveReportPlans.Count - dynamicReportCount;
        var unassignedCount = Math.Max(0, session.Points.Count - session.PointPlanIds.Count);

        if (session.StaticDataSetReportOnly)
        {
            session.Device.AcquisitionMode = staticReportCount > 0
                ? $"Static DataSet reporting • RCB {staticReportCount} • unresolved {unassignedCount}"
                : $"Static DataSet reporting unavailable • unresolved {unassignedCount}";
            session.Device.Detail = staticReportCount > 0
                ? $"{session.Points.Count} DataSet-derived point(s): configured RCB reporting is the process-value authority; {unassignedCount} point(s) are unresolved/unavailable. Cyclic MMS process polling is disabled."
                : $"{session.Points.Count} DataSet-derived point(s): no configured RCB could be armed. Values remain unavailable; MMS process fallback is disabled by Static DataSet mode.";
            session.Device.RefreshComputed();
            Log("INFO", session.Device.Name,
                $"Static DataSet acquisition ready: static report plan(s)={staticReportCount}, report-covered={session.PointPlanIds.Count}, unresolved={unassignedCount}, cyclic MMS process polling=0.");
            return;
        }

        var pollingFallbackCount = unassignedCount;
        session.Device.AcquisitionMode = dynamicReportCount > 0
            ? $"Smart reporting • dynamic {dynamicReportCount} • static {staticReportCount} • fallback {pollingFallbackCount}"
            : staticReportCount > 0
                ? $"Smart reporting • static {staticReportCount} • fallback {pollingFallbackCount}"
                : $"MMS polling fallback • {pollingFallbackCount} point(s)";
        session.Device.Detail = session.ActiveReportPlans.Count > 0
            ? $"{session.Points.Count} point(s): event-driven reporting is primary; one lightweight MMS heartbeat and low-rate verification keep connection health reliable."
            : $"{session.Points.Count} point(s): reporting could not be armed; bounded MMS polling fallback remains active.";
        session.Device.RefreshComputed();

        Log("INFO", session.Device.Name,
            $"Acquisition ready: report plan(s)={session.ActiveReportPlans.Count}, report-covered={session.PointPlanIds.Count}, MMS fallback={pollingFallbackCount}.");
    }

    private static int CalculateLoopDelayMs'''
r2, n = summary_pattern.subn(summary_replacement, r, count=1)
if n != 1:
    raise SystemExit(f"Iec61850MonitorRuntime.cs: acquisition summary patch count={n}")
r = r2

replace_pairs = [
(
'''    private async Task ProbeSessionHealthAsync(DeviceSession session, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref session.ControlCommandActive) > 0)
            return;''',
'''    private async Task ProbeSessionHealthAsync(DeviceSession session, CancellationToken cancellationToken)
    {
        // Static DataSet report-only uses report receive/transport evidence for health;
        // process leaves are never repurposed as cyclic MMS heartbeat reads.
        if (session.StaticDataSetReportOnly)
            return;
        if (Volatile.Read(ref session.ControlCommandActive) > 0)
            return;'''),
(
'''    private static void ResetPollQueue(
        DeviceSession session,
        bool staggerForRecovery = false)
    {
        session.PollQueue.Clear();
        var nowUtc = DateTime.UtcNow;''',
'''    private static void ResetPollQueue(
        DeviceSession session,
        bool staggerForRecovery = false)
    {
        session.PollQueue.Clear();
        if (session.StaticDataSetReportOnly)
        {
            foreach (var state in session.States.Values)
                state.NextPollUtc = DateTime.MaxValue;
            return;
        }

        var nowUtc = DateTime.UtcNow;'''),
(
'''            PollingIntervalMs = pollingIntervalMs,
            SourceMode = signal.IsReportCapable ? "Report pending / polling fallback" : "MMS polling"
        };''',
'''            PollingIntervalMs = pollingIntervalMs,
            SourceMode = Iec61850MonitoringModeRegistry.IsStaticDataSetReportOnly(device)
                ? "Static DataSet report pending"
                : signal.IsReportCapable ? "Report pending / polling fallback" : "MMS polling"
        };'''),
(
'''            state.AcquisitionLabel = "MMS polling";
            state.SourceMode = "Report rearming / MMS polling fallback";
            state.Reason = "new MMS association / report evidence reset";
            state.Status = "Reconnected / report rearming";''',
'''            state.AcquisitionLabel = session.StaticDataSetReportOnly ? "Static DataSet report rearming" : "MMS polling";
            state.SourceMode = session.StaticDataSetReportOnly
                ? "Static DataSet report rearming"
                : "Report rearming / MMS polling fallback";
            state.Reason = session.StaticDataSetReportOnly
                ? "new MMS association / configured RCB evidence reset; MMS process fallback disabled"
                : "new MMS association / report evidence reset";
            state.Status = "Reconnected / report rearming";''')
]
for old, new in replace_pairs:
    if r.count(old) != 1:
        raise SystemExit(f"Iec61850MonitorRuntime.cs: source contract changed: {old[:80]!r}")
    r = r.replace(old, new)

runtime.write_text(r, encoding="utf-8")

Path("tests/ARSAS.Tests/StaticDataSetReportOnlyModeRegressionTests.cs").write_text(r'''using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class StaticDataSetReportOnlyModeRegressionTests
{
    [Fact]
    public void StaticMode_DisablesDynamicWrites_AndManualModeRestoresPriorValue()
    {
        var device = new Iec61850MonitorDevice { AllowDynamicDataSetWrites = true };
        Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device);
        Assert.True(Iec61850MonitoringModeRegistry.IsStaticDataSetReportOnly(device));
        Assert.False(device.AllowDynamicDataSetWrites);
        Iec61850MonitoringModeRegistry.UseHybrid(device);
        Assert.False(Iec61850MonitoringModeRegistry.IsStaticDataSetReportOnly(device));
        Assert.True(device.AllowDynamicDataSetWrites);
    }

    [Fact]
    public void StaticSelection_AllowsOnlyRuntimeSignalsWithExplicitDataSetAuthority()
    {
        var dataSetLeaf = Signal("IEDLD/MMXU1.TotW.mag.f", "IEDLD/LLN0.Analog");
        var browsedLeaf = Signal("IEDLD/MMXU1.Hz.mag.f", string.Empty);
        var control = Signal("IEDLD/CSWI1.Pos", "IEDLD/LLN0.Digital");
        control.IsControlSignal = true;
        Assert.True(Iec61850StaticDataSetSelectionPolicy.IsEligible(dataSetLeaf));
        Assert.False(Iec61850StaticDataSetSelectionPolicy.IsEligible(browsedLeaf));
        Assert.False(Iec61850StaticDataSetSelectionPolicy.IsEligible(control));
    }

    [Fact]
    public void RuntimeContract_StaticDataSetMode_DoesNotScheduleCyclicMmsProcessPolling()
    {
        var source = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));
        Assert.Contains("Static DataSet acquisition ready", source, StringComparison.Ordinal);
        Assert.Contains("cyclic MMS process polling=0", source, StringComparison.Ordinal);
        Assert.Contains("state.NextPollUtc = DateTime.MaxValue", source, StringComparison.Ordinal);
        Assert.Contains("process leaves are never repurposed as cyclic MMS heartbeat reads", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedSclStaticSelection_UsesDatasetAuthorityPolicy_NotSelectEverything()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.SharedSclWorkspace.cs"));
        Assert.Contains("Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device)", source, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device)", source, StringComparison.Ordinal);
        Assert.Contains("Iec61850StaticDataSetSelectionPolicy.IsEligible(signal)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("signal.IsSelected = !string.IsNullOrWhiteSpace(signal.DataSetReference)", source, StringComparison.Ordinal);
    }

    private static SignalDefinition Signal(string reference, string dataSetReference)
        => new()
        {
            Name = "signal",
            ObjectReference = reference,
            DisplayReference = reference,
            FunctionalConstraint = "MX",
            DataType = "Float32",
            Category = "Measurement",
            DataSetReference = dataSetReference,
            Confidence = "High"
        };

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
        throw new FileNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
''', encoding="utf-8")
