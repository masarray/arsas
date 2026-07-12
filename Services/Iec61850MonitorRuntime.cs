using System.Collections.Concurrent;
using System.Globalization;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Multi-IED monitoring runtime for ArIED 61850.
/// Each IED owns an isolated native MMS association. Smart Auto acquisition tries
/// existing static RCB/DataSet reporting first, then an association-scoped dynamic
/// DataSet/URCB, and uses cyclic MMS reads only for points that reporting cannot cover.
/// </summary>
public sealed class Iec61850MonitorRuntime : IAsyncDisposable
{
    private sealed class RuntimePointState
    {
        public string Value { get; set; } = "-";
        public string Quality { get; set; } = "Unknown";
        public string DeviceTimestamp { get; set; } = "-";
        public DateTime LastUpdateUtc { get; set; } = DateTime.MinValue;
        public DateTime NextPollUtc { get; set; } = DateTime.MinValue;
        public DateTime NextCompanionPollUtc { get; set; } = DateTime.MinValue;
        public long Sequence { get; set; }
        public string SourceMode { get; set; } = "Waiting";
        public string AcquisitionLabel { get; set; } = "MMS polling";
        public string Reason { get; set; } = "-";
        public string Status { get; set; } = "Queued";
        public bool HasValue { get; set; }
        public bool ReportTrafficSeen { get; set; }
        public bool ReportChangeVerified { get; set; }
        public DateTime LastReportUtc { get; set; } = DateTime.MinValue;
        public bool ReportMissLogged { get; set; }
        public int ConsecutiveErrors { get; set; }
    }

    private sealed class ReportStreamState
    {
        public ulong? LastSequenceNumber { get; set; }
        public ulong? SegmentedSequenceNumber { get; set; }
        public ulong? LastSubSequenceNumber { get; set; }
        public bool AwaitingMoreSegments { get; set; }
        public ulong? ConfigurationRevision { get; set; }
        public string LastEntryIdHex { get; set; } = string.Empty;
    }

    private sealed class DeviceSession
    {
        public required Iec61850MonitorDevice Device { get; init; }
        public NativeIec61850Client Client { get; set; } = new();
        public CancellationTokenSource MonitorCancellation { get; set; } = new();
        public Task? MonitorTask { get; set; }
        public Dictionary<string, Iec61850MonitorPoint> Points { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RuntimePointState> States { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ReportControlPlan> ActiveReportPlans { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ReportControlPlan> ActiveReportPlanOrder { get; } = new();
        public Dictionary<string, string> PointPlanIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Iec61850MonitorPoint> ReportReferenceIndex { get; } = new(StringComparer.OrdinalIgnoreCase);
        public PriorityQueue<string, long> PollQueue { get; } = new();
        public Dictionary<string, ReportStreamState> ReportStreams { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int LastUnroutedReportCount { get; set; }
        public int ReportPlanCursor { get; set; }
        public IReadOnlyList<ReportControlPlan> PendingReportPlans { get; set; } = Array.Empty<ReportControlPlan>();
        public bool ReportSetupPending { get; set; }
        public DateTime ReportSetupNotBeforeUtc { get; set; } = DateTime.MinValue;
        public DateTime ReportSetupDeadlineUtc { get; set; } = DateTime.MinValue;
        public DateTime NextReconnectUtc { get; set; } = DateTime.MinValue;
        public int ConsecutiveSessionErrors { get; set; }
    }

    private readonly ConcurrentDictionary<string, DeviceSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private long _eventSequence;

    public event Action<DiagnosticEntry>? Diagnostic;
    public event Action<Iec61850PointSnapshot>? PointUpdated;
    public event Action<Iec61850EventEntry>? EventRaised;

    public int ConnectedDeviceCount => _sessions.Values.Count(session => session.Client.IsConnected);
    public int MonitoringDeviceCount => _sessions.Values.Count(session => session.Device.IsMonitoring);

    public async Task<IReadOnlyList<SignalDefinition>> ConnectAndDiscoverAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken,
        IProgress<IedDiscoveryProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ValidateEndpoint(device);

        progress?.Report(new IedDiscoveryProgress(IedDiscoveryStage.PreparingSession, "Preparing an independent IEC 61850 session…", 2d, 1, 15));
        await StopDeviceAsync(device.DeviceId).ConfigureAwait(false);

        var session = new DeviceSession
        {
            Device = device,
            Client = new NativeIec61850Client()
        };
        _sessions[device.DeviceId] = session;

        device.IsConnected = false;
        device.LastDiagnosticSnapshot = session.Client.CaptureDiagnosticSnapshot("Preparing TCP/ACSE/MMS connection");
        device.Status = "Connecting";
        device.Detail = $"Opening TCP {device.IpAddress}:{device.Port} and negotiating ACSE/MMS.";
        Log("INFO", device.Name, $"Connecting to {device.IpAddress}:{device.Port} over TCP/TPKT/COTP/ACSE/MMS.");

        try
        {
            progress?.Report(new IedDiscoveryProgress(IedDiscoveryStage.OpeningTcp, $"Opening TCP {device.IpAddress}:{device.Port}…", 8d, 2, 15));
            await session.Client.ConnectAsync(device.IpAddress, device.Port, cancellationToken).ConfigureAwait(false);
            if (!session.Client.IsConnected)
            {
                device.Status = "Connection failed";
                device.Detail = string.IsNullOrWhiteSpace(session.Client.LastErrorMessage)
                    ? "The IED did not complete IEC 61850 ACSE/MMS association."
                    : session.Client.LastErrorMessage;
                throw new InvalidOperationException(device.Detail);
            }

            device.IsConnected = true;
            device.LastDiagnosticSnapshot = session.Client.CaptureDiagnosticSnapshot("ACSE/MMS associated");
            device.Status = "MMS associated";
            device.Detail = "Association ready. Scanning the live MMS schema, DataSets, and Report Control Blocks.";
            Log("INFO", device.Name, $"MMS association ready. Native state={session.Client.NativeState}.");

            progress?.Report(new IedDiscoveryProgress(IedDiscoveryStage.AssociatingMms, "ACSE/MMS associated. Starting online discovery…", 18d, 3, 15));
            var discovered = await session.Client.DiscoverSignalsAsync(cancellationToken, progress).ConfigureAwait(false);
            var signals = discovered
                .Where(signal => signal.CanPublishAsSignal || signal.IsControlSignal)
                .GroupBy(signal => NormalizeReference(signal.ObjectReference), StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(signal => signal.IsScadaCoreSignal)
                    .ThenByDescending(signal => signal.Confidence.Equals("High", StringComparison.OrdinalIgnoreCase))
                    .First())
                .OrderBy(signal => signal.SortPriority)
                .ThenBy(signal => signal.LogicalNode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(signal => signal.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            progress?.Report(new IedDiscoveryProgress(IedDiscoveryStage.PreparingWorkspace, "Filtering and ordering the discovered signal workspace…", 98d, 14, 15));
            var identity = session.Client.DetectedIdentity;
            if (!string.IsNullOrWhiteSpace(identity.IedName))
            {
                var previousName = device.Name;
                device.Name = identity.IedName;
                device.IdentitySource = identity.Source;
                if (!previousName.Equals(device.Name, StringComparison.OrdinalIgnoreCase))
                    Log("INFO", device.Name, $"Device identity resolved from {identity.Source}: {previousName} → {device.Name}.");
            }
            device.LogicalDeviceSummary = string.Join(", ", identity.LogicalDevices
                .Select(item => string.IsNullOrWhiteSpace(item.Instance) ? item.Domain : item.Instance)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8));

            var reportInventoryAvailable = session.Client.LastReportInventory.ReportControls.Count > 0;
            foreach (var signal in signals)
            {
                // No implicit recommendation selection. Only persisted user choices are
                // restored later by the UI after the live IED identity is known.
                signal.IsSelected = false;
                signal.ProbeStatus = signal.IsKnownReadFailure ? "Discovery read failed" : "Discovered / ready";

                if (!signal.CanPublishToRuntime)
                    continue;

                if (!string.IsNullOrWhiteSpace(signal.ReportControlReference) ||
                    !string.IsNullOrWhiteSpace(signal.DataSetReference))
                {
                    signal.IsReportCapable = true;
                    signal.ReportCoverage = device.AllowDynamicDataSetWrites
                        ? "Smart Auto: static → dynamic → polling"
                        : "Static report → polling fallback";
                }
                else if (device.AllowDynamicDataSetWrites)
                {
                    signal.IsReportCapable = true;
                    signal.ReportCoverage = "Smart Auto: dynamic report → polling";
                    signal.ReportCoverageReason = reportInventoryAvailable
                        ? "IED exposes Report Control Blocks; ArIED will create a temporary association-scoped DataSet/URCB for selected points when static coverage is unavailable."
                        : "No static DataSet mapping was confirmed during scan. At monitor start ArIED will perform final RCB discovery and attempt temporary dynamic reporting before polling.";
                }
                else
                {
                    signal.ReportCoverage = "Polling fallback";
                }
            }

            var liveSignalCount = signals.Count(signal => signal.CanPublishAsSignal);
            var controlSignalCount = signals.Count(signal => signal.IsControlSignal);
            device.Status = signals.Count > 0 ? "Ready" : "No monitor/control object";
            device.Detail = signals.Count > 0
                ? $"Found {liveSignalCount:N0} live signal(s) and {controlSignalCount:N0} control object(s). Choose the required objects in the signal selection wizard."
                : "MMS association succeeded, but no readable live signal or controllable object passed the smart workspace filter.";
            device.RefreshComputed();

            progress?.Report(new IedDiscoveryProgress(IedDiscoveryStage.Complete, $"Discovery complete — {liveSignalCount:N0} live and {controlSignalCount:N0} control object(s) ready.", 100d, 15, 15));
            device.LastDiagnosticSnapshot = session.Client.CaptureDiagnosticSnapshot("Discovery complete");
            Log(signals.Count > 0 ? "INFO" : "WARN", device.Name,
                $"Smart scan complete: {liveSignalCount} live signal(s), {controlSignalCount} control object(s). {session.Client.LastDiscoverySummary}");
            return signals;
        }
        catch (Exception ex)
        {
            device.LastDiagnosticSnapshot = session.Client.CaptureDiagnosticSnapshot(
                session.Client.IsConnected ? "Online discovery failed" : "TCP/ACSE/MMS connection failed",
                ex);

            if (!session.Client.IsConnected)
            {
                device.IsConnected = false;
                _sessions.TryRemove(device.DeviceId, out _);
                await session.Client.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
        finally
        {
            // The UI owns the card-local busy overlay so it can keep the progress bar
            // visible long enough to settle smoothly at 100%.
            device.RefreshComputed();
        }
    }

    /// <summary>
    /// Opens only the TCP/ACSE/MMS association and reuses the discovery model already
    /// persisted in an ArIED project. No signal-directory scan, vendor probing, unit
    /// enrichment, or selection wizard is executed here.
    /// </summary>
    public async Task ConnectUsingCachedModelAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken,
        IProgress<IedDiscoveryProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ValidateEndpoint(device);
        if (!device.HasDiscoveryCache || device.Signals.Count == 0)
            throw new InvalidOperationException($"{device.Name} has no successful saved discovery model. Run a full discovery first.");

        progress?.Report(new IedDiscoveryProgress(
            IedDiscoveryStage.PreparingSession,
            "Preparing saved IEC 61850 model…",
            4d,
            1,
            4));

        await StopDeviceAsync(device.DeviceId).ConfigureAwait(false);
        var session = new DeviceSession
        {
            Device = device,
            Client = new NativeIec61850Client()
        };
        _sessions[device.DeviceId] = session;

        device.IsConnected = false;
        device.LastDiagnosticSnapshot = session.Client.CaptureDiagnosticSnapshot("Preparing fast connection from saved model");
        device.Status = "Fast connecting";
        device.Detail = $"Opening {device.IpAddress}:{device.Port} with the saved discovery model.";
        Log("INFO", device.Name,
            $"Fast reconnect using saved model ({device.SignalCount:N0} signals); full live discovery is skipped.");

        try
        {
            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.OpeningTcp,
                $"Opening TCP {device.IpAddress}:{device.Port}…",
                24d,
                2,
                4));

            await session.Client.ConnectAsync(device.IpAddress, device.Port, cancellationToken).ConfigureAwait(false);
            if (!session.Client.IsConnected)
            {
                device.Status = "Connection failed";
                device.Detail = string.IsNullOrWhiteSpace(session.Client.LastErrorMessage)
                    ? "The IED did not complete IEC 61850 ACSE/MMS association."
                    : session.Client.LastErrorMessage;
                throw new InvalidOperationException(device.Detail);
            }

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.AssociatingMms,
                "ACSE/MMS associated. Restoring saved signal workspace…",
                74d,
                3,
                4));

            device.IsConnected = true;
            device.LastDiagnosticSnapshot = session.Client.CaptureDiagnosticSnapshot("Fast connection complete");
            device.Status = "Ready";
            device.Detail = $"Connected with saved model: {device.SignalCount:N0} signal(s), {device.SelectedSignalCount:N0} selected. Full discovery was skipped.";
            device.AcquisitionMode = "Saved model • ready to monitor";
            device.RefreshComputed();

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.Complete,
                "Saved model restored — ready for live values.",
                100d,
                4,
                4));

            Log("INFO", device.Name,
                "Fast reconnect complete. Reporting setup will validate only the acquisition objects required by the selected points; the full signal scan remains cached.");
        }
        catch (Exception ex)
        {
            device.LastDiagnosticSnapshot = session.Client.CaptureDiagnosticSnapshot(
                "Fast TCP/ACSE/MMS connection failed",
                ex);
            if (!session.Client.IsConnected)
            {
                device.IsConnected = false;
                _sessions.TryRemove(device.DeviceId, out _);
                await session.Client.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
        finally
        {
            device.RefreshComputed();
        }
    }

    public async Task<IReadOnlyList<Iec61850MonitorPoint>> StartMonitoringAsync(
        Iec61850MonitorDevice device,
        IEnumerable<SignalDefinition> selectedSignals,
        int pollingIntervalMs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(selectedSignals);

        if (!_sessions.TryGetValue(device.DeviceId, out var session) || !session.Client.IsConnected)
            throw new InvalidOperationException($"{device.Name} is not connected. Connect and discover this IED first.");

        var selected = selectedSignals
            .Where(signal => signal.IsSelected && signal.CanPublishToRuntime)
            .GroupBy(signal => NormalizeReference(signal.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (selected.Count == 0)
            throw new InvalidOperationException("No readable IEC 61850 signal is selected.");

        await StopMonitoringSessionAsync(session, preservePointDefinitions: false).ConfigureAwait(false);
        session.MonitorCancellation.Dispose();
        session.MonitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        session.Points.Clear();
        session.States.Clear();
        session.ActiveReportPlans.Clear();
        session.ActiveReportPlanOrder.Clear();
        session.PointPlanIds.Clear();
        session.ReportReferenceIndex.Clear();
        session.PollQueue.Clear();
        session.ReportStreams.Clear();
        session.LastUnroutedReportCount = 0;
        session.ReportPlanCursor = 0;
        session.PendingReportPlans = Array.Empty<ReportControlPlan>();
        session.ReportSetupPending = false;
        session.ReportSetupNotBeforeUtc = DateTime.MinValue;
        session.ReportSetupDeadlineUtc = DateTime.MinValue;
        session.ConsecutiveSessionErrors = 0;

        var safePollMs = Math.Clamp(pollingIntervalMs <= 0 ? 1000 : pollingIntervalMs, 50, 600000);
        foreach (var signal in selected)
        {
            var point = CreatePoint(device, signal, safePollMs);
            session.Points[point.PointKey] = point;
            IndexPointReference(session, point);
            session.States[point.PointKey] = new RuntimePointState
            {
                NextPollUtc = DateTime.UtcNow,
                SourceMode = signal.IsReportCapable ? "Report pending / polling fallback" : "MMS polling",
                Reason = signal.IsReportCapable ? "report plan pending" : "cyclic"
            };
        }

        var plans = Iec61850ReportPlanner.BuildPlans(device, session.Points.Values);
        session.PendingReportPlans = plans;
        session.ReportSetupPending = plans.Count > 0;
        session.ReportSetupNotBeforeUtc = DateTime.UtcNow.AddMilliseconds(350);
        session.ReportSetupDeadlineUtc = DateTime.UtcNow.AddMilliseconds(1500);
        ResetPollQueue(session);

        device.IsMonitoring = true;
        device.IsConnected = true;
        device.Status = "Monitoring";
        device.AcquisitionMode = plans.Count > 0
            ? "MMS live start • arming smart reporting"
            : $"MMS polling fallback • {session.Points.Count} point(s)";
        device.Detail = plans.Count > 0
            ? $"{session.Points.Count} point(s): MMS is reading the initial live image immediately while static/dynamic reporting is validated in the same independent IED session."
            : $"{session.Points.Count} point(s): no report candidate is available; MMS polling is active.";
        device.RefreshComputed();

        Log("INFO", device.Name,
            $"Fast live start: points={session.Points.Count}, pending report plan(s)={plans.Count}, initial MMS scheduler={session.PollQueue.Count}, target={safePollMs} ms. Full signal discovery is not part of monitor start.");

        session.MonitorTask = Task.Run(
            () => MonitorLoopAsync(session, session.MonitorCancellation.Token),
            CancellationToken.None);

        return session.Points.Values
            .OrderBy(point => point.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.SignalName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<Iec61850ControlCapabilities> InspectControlAsync(
        string deviceId,
        SignalDefinition signal,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentNullException.ThrowIfNull(signal);

        if (!_sessions.TryGetValue(deviceId, out var session) || !session.Client.IsConnected)
            throw new InvalidOperationException("The IED must be connected before a control object can be inspected.");

        try
        {
            var capabilities = await session.Client.InspectControlAsync(signal, cancellationToken).ConfigureAwait(false);
            Log("INFO", session.Device.Name,
                $"Control inspected: {signal.ObjectReference}; model={capabilities.ControlModelText}; CDC={capabilities.ControlCdc}; current={capabilities.CurrentValue}.");
            return capabilities;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log("ERROR", session.Device.Name,
                $"Control inspection failed for {signal.ObjectReference}: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    public async Task<Iec61850ControlCommandResult> ExecuteControlAsync(
        string deviceId,
        Iec61850ControlCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryGetValue(deviceId, out var session) || !session.Client.IsConnected)
            throw new InvalidOperationException("The IED must be connected before a command can be sent.");

        Log("WARN", session.Device.Name,
            $"Control requested: {request.Signal.ObjectReference} value={request.ValueText}; test={request.TestMode}; interlock={request.InterlockCheck}; synchro={request.SynchroCheck}.");

        var result = await session.Client.ExecuteControlAsync(request, cancellationToken).ConfigureAwait(false);
        var protocolEvidence = string.Join("; ", new[]
        {
            string.IsNullOrWhiteSpace(result.CompletionState) ? null : $"completion={result.CompletionState}",
            result.CommandTerminationReceived ? $"termination={(result.PositiveTermination ? "positive" : "negative")}" : null,
            string.IsNullOrWhiteSpace(result.ControlError) ? null : $"controlError={result.ControlError}",
            string.IsNullOrWhiteSpace(result.AddCause) ? null : $"addCause={result.AddCause}",
            result.ControlNumber == "-" ? null : $"ctlNum={result.ControlNumber}",
            result.ElapsedText == "-" ? null : $"control={result.ElapsedText}",
            result.FeedbackElapsedText == "-" ? null : $"feedback={result.FeedbackElapsedText}",
            result.TotalElapsedText == "-" ? null : $"total={result.TotalElapsedText}"
        }.Where(text => !string.IsNullOrWhiteSpace(text)));

        Log(result.IsSuccess ? "INFO" : "ERROR", session.Device.Name,
            $"Control {result.Stage}: {request.Signal.ObjectReference}; sequence={result.SequenceText}; requested={result.RequestedValue}; feedback={result.FeedbackValue}; {protocolEvidence}; {result.Message}");
        return result;
    }

    public async Task StopMonitoringAsync(string deviceId)
    {
        if (!_sessions.TryGetValue(deviceId, out var session))
            return;

        await StopMonitoringSessionAsync(session, preservePointDefinitions: false).ConfigureAwait(false);
        session.Device.IsMonitoring = false;
        session.Device.IsConnected = session.Client.IsConnected;
        session.Device.Status = session.Client.IsConnected ? "Ready" : "Disconnected";
        session.Device.AcquisitionMode = "Not started";
        session.Device.Detail = session.Client.IsConnected
            ? "Monitoring stopped. The MMS association remains available for a new signal selection."
            : "Session stopped.";
        session.Device.RefreshComputed();
        Log("INFO", session.Device.Name, "Live monitoring stopped.");
    }

    public async Task StopDeviceAsync(string deviceId)
    {
        if (!_sessions.TryRemove(deviceId, out var session))
            return;

        await StopMonitoringSessionAsync(session, preservePointDefinitions: false).ConfigureAwait(false);
        await session.Client.DisposeAsync().ConfigureAwait(false);
        session.MonitorCancellation.Dispose();
        session.Device.IsMonitoring = false;
        session.Device.IsConnected = false;
        session.Device.AcquisitionMode = "Not started";
        session.Device.Status = "Disconnected";
        session.Device.Detail = "IEC 61850 session closed.";
        session.Device.RefreshComputed();
        Log("INFO", session.Device.Name, "IEC 61850 session disconnected.");
    }

    private async Task StartReportPlansAsync(
        DeviceSession session,
        IReadOnlyList<ReportControlPlan> plans,
        CancellationToken cancellationToken)
    {
        var pending = new Queue<ReportControlPlan>(plans);
        var queuedPlanIds = plans.Select(plan => plan.PlanId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            var plan = pending.Dequeue();
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await session.Client.StartReportMonitorAsync(plan, cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    Log("WARN", session.Device.Name,
                        $"Report plan unavailable for {plan.DisplayReference}. MMS polling remains the final fallback. {result.Message}");
                    foreach (var warning in result.Warnings.Take(3))
                        Log("WARN", session.Device.Name, warning);
                    continue;
                }

                plan.Status = result.UsedDynamicDataSet ? "Dynamic active" : "Static active";
                session.ActiveReportPlans[plan.PlanId] = plan;
                session.ActiveReportPlanOrder.Add(plan);

                var coveredPoints = ResolveCoveredPoints(plan, result.CoveredReferences);
                var acquisitionLabel = string.IsNullOrWhiteSpace(result.AcquisitionLabel)
                    ? BuildPlanAcquisitionLabel(plan, result.UsedDynamicDataSet)
                    : result.AcquisitionLabel;
                foreach (var point in coveredPoints)
                {
                    session.PointPlanIds[point.PointKey] = plan.PlanId;
                    if (!string.IsNullOrWhiteSpace(result.ReportControlReference))
                        point.ReportControlReference = result.ReportControlReference;
                    if (!string.IsNullOrWhiteSpace(result.DataSetReference))
                        point.DataSetReference = result.DataSetReference;
                    if (session.States.TryGetValue(point.PointKey, out var pointState))
                    {
                        pointState.AcquisitionLabel = acquisitionLabel;
                        pointState.SourceMode = acquisitionLabel;
                        pointState.Reason = "awaiting report / MMS initial read";
                        pointState.Status = "Report armed / verification active";
                    }
                }

                if (result.CoveredReferences.Count == 0)
                {
                    Log("WARN", session.Device.Name,
                        $"Report monitor started for {plan.DisplayReference}, but exact DataSet member references were not returned. Points remain on safe polling fallback until live reports prove their mapping.");
                }

                Log("INFO", session.Device.Name,
                    $"{(result.UsedDynamicDataSet ? "Dynamic" : "Static")} report monitor active: {plan.DisplayReference}; DataSet members={result.MemberCount}; exact selected coverage={coveredPoints.Count}; setup writes={result.WriteStepCount}. {result.SubscriptionSummary}");
                foreach (var warning in result.Warnings.Take(3))
                    Log("WARN", session.Device.Name, warning);

                // A discovered static DataSet can cover only part of a heuristic group.
                // Recover the exact uncovered remainder through temporary dynamic reporting
                // before leaving those points on cyclic MMS polling.
                if (!result.UsedDynamicDataSet &&
                    plan.AllowDynamicDataSetWrites &&
                    result.CoveredReferences.Count > 0 &&
                    coveredPoints.Count < plan.Bindings.Count)
                {
                    var coveredKeys = coveredPoints
                        .Select(point => point.PointKey)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var uncovered = plan.Bindings
                        .Where(point => !coveredKeys.Contains(point.PointKey))
                        .ToList();

                    foreach (var recoveryPlan in Iec61850ReportPlanner.BuildDynamicFallbackPlans(session.Device, uncovered))
                    {
                        if (queuedPlanIds.Add(recoveryPlan.PlanId))
                            pending.Enqueue(recoveryPlan);
                    }

                    if (uncovered.Count > 0)
                    {
                        Log("INFO", session.Device.Name,
                            $"Static DataSet covered {coveredPoints.Count}/{plan.Bindings.Count} selected points; Smart Auto queued dynamic reporting for {uncovered.Count} uncovered point(s).");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log("WARN", session.Device.Name,
                    $"Report setup failed for {plan.DisplayReference}; MMS polling remains the final fallback. {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private async Task MonitorLoopAsync(DeviceSession session, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!session.Client.IsConnected)
                {
                    await TryReconnectAsync(session, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await ReceiveReportSlicesAsync(session, cancellationToken).ConfigureAwait(false);
                await PollDuePointsAsync(session, cancellationToken).ConfigureAwait(false);
                await TryStartPendingReportSetupAsync(session, cancellationToken).ConfigureAwait(false);
                session.ConsecutiveSessionErrors = 0;

                await Task.Delay(CalculateLoopDelayMs(session), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                session.ConsecutiveSessionErrors++;
                Log("WARN", session.Device.Name,
                    $"Monitor loop recovered from {ex.GetType().Name}: {ex.Message}");

                if (session.ConsecutiveSessionErrors >= 5)
                    await ForceReconnectAsync(session).ConfigureAwait(false);

                try
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task TryStartPendingReportSetupAsync(
        DeviceSession session,
        CancellationToken cancellationToken)
    {
        if (!session.ReportSetupPending)
            return;

        var now = DateTime.UtcNow;
        if (now < session.ReportSetupNotBeforeUtc)
            return;

        var initialImageReady = session.States.Values.All(state =>
            state.HasValue || state.ConsecutiveErrors > 0);
        if (!initialImageReady && now < session.ReportSetupDeadlineUtc)
            return;

        session.ReportSetupPending = false;
        var plans = session.PendingReportPlans;
        session.PendingReportPlans = Array.Empty<ReportControlPlan>();

        Log("INFO", session.Device.Name,
            initialImageReady
                ? "Initial live image is available. Validating static/dynamic report acquisition in the background monitor pipeline."
                : "Initial live-image deadline reached. Continuing report validation while MMS fallback remains active.");

        await StartReportPlansAsync(session, plans, cancellationToken).ConfigureAwait(false);
        ResetPollQueue(session);
        UpdateDeviceAcquisitionSummary(session);
    }

    private void UpdateDeviceAcquisitionSummary(DeviceSession session)
    {
        var dynamicReportCount = session.ActiveReportPlans.Values.Count(plan =>
            plan.Status.Contains("Dynamic", StringComparison.OrdinalIgnoreCase));
        var staticReportCount = session.ActiveReportPlans.Count - dynamicReportCount;
        var pollingFallbackCount = Math.Max(0, session.Points.Count - session.PointPlanIds.Count);

        session.Device.AcquisitionMode = dynamicReportCount > 0
            ? $"Smart reporting • dynamic {dynamicReportCount} • static {staticReportCount} • fallback {pollingFallbackCount}"
            : staticReportCount > 0
                ? $"Smart reporting • static {staticReportCount} • fallback {pollingFallbackCount}"
                : $"MMS polling fallback • {pollingFallbackCount} point(s)";
        session.Device.Detail = session.ActiveReportPlans.Count > 0
            ? $"{session.Points.Count} point(s): live values started by MMS, then report-first acquisition was armed; MMS remains for q/t and low-rate verification."
            : $"{session.Points.Count} point(s): reporting could not be armed; MMS polling fallback remains active.";
        session.Device.RefreshComputed();

        Log("INFO", session.Device.Name,
            $"Acquisition ready: report plan(s)={session.ActiveReportPlans.Count}, report-covered={session.PointPlanIds.Count}, MMS fallback={pollingFallbackCount}.");
    }

    private static int CalculateLoopDelayMs(DeviceSession session)
    {
        var defaultDelay = session.ActiveReportPlans.Count > 0 ? 5 : 10;
        if (!session.PollQueue.TryPeek(out _, out var dueTicks))
            return defaultDelay;

        var remainingMs = TimeSpan.FromTicks(Math.Max(0, dueTicks - DateTime.UtcNow.Ticks)).TotalMilliseconds;
        if (remainingMs <= 0)
            return 1;
        return Math.Clamp((int)Math.Ceiling(remainingMs), 1, defaultDelay);
    }

    private async Task ReceiveReportSlicesAsync(DeviceSession session, CancellationToken cancellationToken)
    {
        var plans = session.ActiveReportPlanOrder;
        if (plans.Count == 0)
            return;

        // Avoid N × blocking slices when an IED exposes many RCBs. A bounded
        // round-robin drain keeps reporting responsive without starving polling.
        var batchCount = Math.Min(plans.Count, 8);
        for (var offset = 0; offset < batchCount; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = (session.ReportPlanCursor + offset) % plans.Count;
            var plan = plans[index];
            var slice = await session.Client.ReceiveReportMonitorSliceAsync(
                plan.PlanId,
                TimeSpan.FromMilliseconds(4),
                cancellationToken).ConfigureAwait(false);

            ProcessReportHealth(session, plan, slice);

            foreach (var update in slice.Updates)
            {
                var point = FindPointForReportReference(session, update.Reference);
                if (point == null)
                    continue;

                // A real report update proves reference coverage, but an initial GI or
                // integrity image does not yet prove that dchg/qchg is working. Keep MMS
                // verification active until a change report is actually observed.
                session.PointPlanIds[point.PointKey] = plan.PlanId;

                var state = session.States[point.PointKey];
                var display = update.HasValue
                    ? Iec61850ValueFormatter.Format(update.Value, point.IecDataType, point.Unit)
                    : state.Value;
                if (update.HasValue && LooksLikeReferenceEcho(display, update.Reference, point.IecReference))
                    continue;

                var hadValueBeforeReport = state.HasValue;
                var valueChangedByReport = update.HasValue && hadValueBeforeReport && HasExactSemanticEdge(point, state.Value, display);
                state.ReportTrafficSeen = true;
                state.LastReportUtc = DateTime.UtcNow;
                if (valueChangedByReport || IsChangeReportReason(update.Reason))
                {
                    state.ReportChangeVerified = true;
                    state.ReportMissLogged = false;
                }

                var reportSource = string.IsNullOrWhiteSpace(state.AcquisitionLabel)
                    ? BuildPlanAcquisitionLabel(plan, plan.Status.Contains("Dynamic", StringComparison.OrdinalIgnoreCase))
                    : state.AcquisitionLabel;

                var reportQuality = update.HasQuality && IsUsefulProcessField(update.Quality)
                    ? NormalizeQuality(update.Quality)
                    : state.HasValue ? state.Quality : "Pending / q not supplied";
                var reportTimestamp = update.HasTimestamp && IsUsefulProcessField(update.Timestamp)
                    ? update.Timestamp
                    : state.HasValue ? state.DeviceTimestamp : "-";

                ApplyValueUpdate(
                    session,
                    point,
                    display,
                    reportQuality,
                    reportTimestamp,
                    reportSource,
                    string.IsNullOrWhiteSpace(update.Reason) ? "report / reason not supplied" : update.Reason,
                    update.UpdatedAt == default ? DateTime.UtcNow : update.UpdatedAt.UtcDateTime,
                    state.ReportChangeVerified
                        ? string.IsNullOrWhiteSpace(update.ProjectionStatus) ? "Live / report verified" : $"Live / report verified ({update.ProjectionStatus})"
                        : string.IsNullOrWhiteSpace(update.ProjectionStatus) ? "Live / report traffic + MMS verification" : $"Live / report traffic + MMS verification ({update.ProjectionStatus})",
                    trustReportEdge: true,
                    hasProcessValue: update.HasValue);
            }

            foreach (var warning in slice.Warnings.Take(2))
                Log("WARN", session.Device.Name, warning);
        }

        session.ReportPlanCursor = (session.ReportPlanCursor + batchCount) % plans.Count;
    }

    private void ProcessReportHealth(
        DeviceSession session,
        ReportControlPlan plan,
        NativeReportMonitorSliceResult slice)
    {
        if (slice.UnroutedReportCount > session.LastUnroutedReportCount)
        {
            var delta = slice.UnroutedReportCount - session.LastUnroutedReportCount;
            session.LastUnroutedReportCount = slice.UnroutedReportCount;
            Log("WARN", session.Device.Name,
                $"{delta} IEC 61850 InformationReport frame(s) were not routed because RptID/DataSet identity was ambiguous. The engine intentionally refused unsafe DataSet projection.");
        }

        foreach (var frame in slice.ReportFrames)
        {
            var streamKey = BuildReportStreamKey(plan, frame);
            if (!session.ReportStreams.TryGetValue(streamKey, out var state))
            {
                state = new ReportStreamState();
                session.ReportStreams[streamKey] = state;
            }

            if (frame.BufferOverflow == true)
            {
                Log("WARN", session.Device.Name,
                    $"BRCB buffer overflow reported by {ReportName(frame.ReportControlReference, plan.ReportControlReference)}. Buffered event continuity may be incomplete.");
            }

            if (frame.ConfRev.HasValue)
            {
                if (state.ConfigurationRevision.HasValue && state.ConfigurationRevision.Value != frame.ConfRev.Value)
                {
                    Log("WARN", session.Device.Name,
                        $"Report ConfRev changed on {ReportName(frame.ReportControlReference, plan.ReportControlReference)}: {state.ConfigurationRevision.Value} → {frame.ConfRev.Value}. DataSet coverage is being treated as changed and should be revalidated.");
                }
                state.ConfigurationRevision = frame.ConfRev;
            }

            ValidateReportSequence(session, plan, frame, state);

            if (!string.IsNullOrWhiteSpace(frame.EntryIdHex))
                state.LastEntryIdHex = frame.EntryIdHex;
        }
    }

    private void ValidateReportSequence(
        DeviceSession session,
        ReportControlPlan plan,
        NativeReportFrameMetadata frame,
        ReportStreamState state)
    {
        if (!frame.SequenceNumber.HasValue)
            return;

        var current = frame.SequenceNumber.Value;
        var reportName = ReportName(frame.ReportControlReference, plan.ReportControlReference);
        if (frame.SubSequenceNumber.HasValue)
        {
            var currentSub = frame.SubSequenceNumber.Value;
            if (state.SegmentedSequenceNumber.HasValue)
            {
                var expectedSub = state.LastSubSequenceNumber.GetValueOrDefault() + 1;
                if (state.SegmentedSequenceNumber.Value != current || currentSub != expectedSub)
                {
                    Log("WARN", session.Device.Name,
                        $"Segmented report discontinuity on {reportName}: expected sqNum={state.SegmentedSequenceNumber.Value}, subSqNum={expectedSub}; received sqNum={current}, subSqNum={currentSub}.");
                }
            }
            else if (state.LastSequenceNumber.HasValue &&
                     !IsExpectedReportSequence(state.LastSequenceNumber.Value, current))
            {
                Log("WARN", session.Device.Name,
                    $"Report sequence discontinuity on {reportName}: previous={state.LastSequenceNumber.Value}, current={current}.");
            }

            state.SegmentedSequenceNumber = current;
            state.LastSubSequenceNumber = currentSub;
            state.AwaitingMoreSegments = frame.MoreSegmentsFollow == true;
            if (!state.AwaitingMoreSegments)
            {
                state.LastSequenceNumber = current;
                state.SegmentedSequenceNumber = null;
                state.LastSubSequenceNumber = null;
            }

            return;
        }

        if (state.AwaitingMoreSegments)
        {
            Log("WARN", session.Device.Name,
                $"Segmented report on {reportName} ended without the expected continuation before sqNum={current}.");
            state.SegmentedSequenceNumber = null;
            state.LastSubSequenceNumber = null;
            state.AwaitingMoreSegments = false;
        }

        if (state.LastSequenceNumber.HasValue &&
            !IsExpectedReportSequence(state.LastSequenceNumber.Value, current))
        {
            Log("WARN", session.Device.Name,
                $"Report sequence discontinuity on {reportName}: previous={state.LastSequenceNumber.Value}, current={current}.");
        }

        state.LastSequenceNumber = current;
    }

    private static bool IsExpectedReportSequence(ulong previous, ulong current)
    {
        if (current == previous + 1)
            return true;

        // Report sequence counters are vendor/RCB dependent and commonly wrap.
        // A reset to zero is accepted; duplicate or skipped non-zero values are not.
        return current == 0;
    }

    private static string BuildReportStreamKey(ReportControlPlan plan, NativeReportFrameMetadata frame)
    {
        var rcb = string.IsNullOrWhiteSpace(frame.ReportControlReference) ? plan.ReportControlReference : frame.ReportControlReference;
        var reportId = string.IsNullOrWhiteSpace(frame.ReportId) ? "-" : frame.ReportId;
        var dataSet = string.IsNullOrWhiteSpace(frame.DataSetReference) ? plan.DataSetReference : frame.DataSetReference;
        return $"{NormalizeReference(rcb)}|{NormalizeReference(reportId)}|{NormalizeReference(dataSet)}";
    }

    private static string ReportName(string actualReference, string plannedReference)
    {
        var reference = string.IsNullOrWhiteSpace(actualReference) ? plannedReference : actualReference;
        var normalized = (reference ?? string.Empty).Replace('$', '.').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "RCB";
        var segments = normalized.Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? normalized : segments[^1];
    }

    private async Task PollDuePointsAsync(DeviceSession session, CancellationToken cancellationToken)
    {
        var processed = 0;
        while (processed < 12 && session.PollQueue.TryPeek(out var pointKey, out var dueTicks))
        {
            var nowUtc = DateTime.UtcNow;
            if (dueTicks > nowUtc.Ticks)
                break;

            session.PollQueue.Dequeue();
            if (!session.Points.TryGetValue(pointKey, out var point) ||
                !session.States.TryGetValue(pointKey, out var state))
                continue;

            var reportAssigned = session.PointPlanIds.ContainsKey(point.PointKey);
            cancellationToken.ThrowIfCancellationRequested();
            processed++;
            var nextIntervalMs = GetVerificationPollIntervalMs(point, state, reportAssigned);
            state.NextPollUtc = nowUtc.AddMilliseconds(nextIntervalMs);
            session.PollQueue.Enqueue(point.PointKey, state.NextPollUtc.Ticks);

            try
            {
                var signal = new SignalDefinition
                {
                    Name = point.SignalName,
                    ObjectReference = point.IecReference,
                    FunctionalConstraint = point.FunctionalConstraint,
                    DataType = point.IecDataType,
                    Category = point.Category,
                    Unit = point.Unit
                };

                var resolved = await IecSignalReadResolver.ReadAsync(session.Client, signal, cancellationToken).ConfigureAwait(false);
                if (resolved == null)
                {
                    state.ConsecutiveErrors++;
                    EmitStatusSnapshot(
                        point,
                        state,
                        "MMS read returned no value",
                        state.HasValue ? state.Quality : "Pending");
                    continue;
                }

                state.ConsecutiveErrors = 0;
                if (resolved.UsedAlternateReference(point.IecReference))
                {
                    Log("INFO", session.Device.Name,
                        $"Smart schema resolver read {point.SignalName} through leaf {resolved.EffectiveReference}.");
                }

                var rich = resolved.Value as Iec61850ReadValue;
                var raw = Iec61850ReadValue.Unwrap(resolved.Value);
                var display = Iec61850ValueFormatter.Format(raw, point.IecDataType, point.Unit);
                var quality = rich?.HasQuality == true ? rich.Quality : state.Quality;
                var deviceTimestamp = rich?.HasDeviceTimestamp == true ? rich.DeviceTimestamp : state.DeviceTimestamp;

                if ((rich?.HasQuality != true || rich?.HasDeviceTimestamp != true) &&
                    nowUtc >= state.NextCompanionPollUtc)
                {
                    state.NextCompanionPollUtc = nowUtc.AddMilliseconds(GetCompanionPollIntervalMs(point));
                    var companions = await ReadCompanionAttributesAsync(
                        session.Client,
                        point,
                        resolved.EffectiveReference,
                        quality,
                        deviceTimestamp,
                        cancellationToken).ConfigureAwait(false);
                    quality = companions.Quality;
                    deviceTimestamp = companions.DeviceTimestamp;
                }

                var normalizedQuality = NormalizeQuality(quality);
                var normalizedTimestamp = string.IsNullOrWhiteSpace(deviceTimestamp) ? "-" : deviceTimestamp;
                var pollDetectedChange = state.HasValue && !string.Equals(state.Value, display, StringComparison.Ordinal);
                var sourceMode = "MMS polling";
                var reason = "cyclic";
                var status = "Live / polling";

                if (reportAssigned)
                {
                    if (state.ReportChangeVerified && pollDetectedChange)
                    {
                        sourceMode = string.IsNullOrWhiteSpace(state.AcquisitionLabel)
                            ? "MMS fallback"
                            : state.AcquisitionLabel + " + MMS fallback";
                        reason = "value changed without matching report";
                        status = "Live / report degraded, MMS fallback";
                        state.ReportChangeVerified = false;
                        if (!state.ReportMissLogged)
                        {
                            state.ReportMissLogged = true;
                            Log("WARN", session.Device.Name,
                                $"{point.SignalName}: MMS validation detected a value change not delivered by the armed report. Fast polling fallback remains active until dchg is verified again.");
                        }
                    }
                    else if (state.ReportTrafficSeen)
                    {
                        // Preserve the report source in the live grid while MMS fills q/t
                        // and verifies that the subscription is not silently frozen.
                        sourceMode = string.IsNullOrWhiteSpace(state.AcquisitionLabel)
                            ? state.SourceMode
                            : state.AcquisitionLabel;
                        reason = state.Reason;
                        status = state.ReportChangeVerified
                            ? "Live / report verified + MMS validation"
                            : "Live / report traffic + MMS verification";
                    }
                    else
                    {
                        sourceMode = string.IsNullOrWhiteSpace(state.AcquisitionLabel)
                            ? (state.HasValue ? "MMS verification" : "MMS initial read")
                            : state.AcquisitionLabel;
                        reason = "report armed / awaiting traffic";
                        status = "Live / report armed + MMS verification";
                    }
                }

                ApplyValueUpdate(
                    session,
                    point,
                    display,
                    normalizedQuality,
                    normalizedTimestamp,
                    sourceMode,
                    reason,
                    DateTime.UtcNow,
                    status,
                    trustReportEdge: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                state.ConsecutiveErrors++;
                EmitStatusSnapshot(point, state, $"Read error: {ex.Message}", "Bad / communication error");
                if (state.ConsecutiveErrors >= 5)
                    session.ConsecutiveSessionErrors++;
            }
        }
    }

    private void ApplyValueUpdate(
        DeviceSession session,
        Iec61850MonitorPoint point,
        string display,
        string quality,
        string deviceTimestamp,
        string sourceMode,
        string reason,
        DateTime timestampUtc,
        string status,
        bool trustReportEdge,
        bool hasProcessValue = true)
    {
        var state = session.States[point.PointKey];
        var hadValue = state.HasValue;
        var oldValue = state.Value;
        var oldQuality = state.Quality;
        var displayChanged = hasProcessValue && !string.Equals(oldValue, display, StringComparison.Ordinal);
        var qualityChangedForUi = !string.Equals(oldQuality, quality, StringComparison.OrdinalIgnoreCase);
        var metadataChanged = !string.Equals(state.DeviceTimestamp, deviceTimestamp, StringComparison.Ordinal) ||
                              !string.Equals(state.SourceMode, sourceMode, StringComparison.Ordinal) ||
                              !string.Equals(state.Reason, reason, StringComparison.Ordinal) ||
                              !string.Equals(state.Status, status, StringComparison.Ordinal);
        var changed = hasProcessValue && hadValue && (trustReportEdge
            ? HasExactSemanticEdge(point, oldValue, display)
            : HasMeaningfulEdge(point, oldValue, display));
        if (hasProcessValue)
        {
            state.HasValue = true;
            state.Value = display;
        }
        state.Quality = quality;
        state.DeviceTimestamp = deviceTimestamp;
        state.LastUpdateUtc = timestampUtc;
        state.SourceMode = sourceMode;
        state.Reason = reason;
        state.Status = status;
        if (changed)
            state.Sequence++;

        if (!hadValue || displayChanged || qualityChangedForUi || metadataChanged)
        {
            PointUpdated?.Invoke(new Iec61850PointSnapshot
            {
                Point = point,
                PreviousValue = oldValue,
                Value = state.Value,
                Quality = quality,
                DeviceTimestamp = deviceTimestamp,
                SourceMode = sourceMode,
                Reason = reason,
                Status = status,
                IsValueEdge = changed,
                IsReportTraffic = trustReportEdge,
                Sequence = state.Sequence
            });
        }

        // SAS/SCADA Sequence of Events records actual discrete process transitions.
        // Initial images, GI/integrity refreshes with unchanged state, formatting-only
        // differences, quality-only updates, and continuously changing analog values
        // stay out of SOE.
        if (!changed || !ShouldRecordSequenceOfEvents(point))
            return;

        var entry = new Iec61850EventEntry
        {
            Sequence = Interlocked.Increment(ref _eventSequence),
            DeviceId = point.DeviceId,
            PointKey = point.PointKey,
            DeviceTimestamp = deviceTimestamp,
            DeviceName = point.DeviceName,
            IpAddress = point.IpAddress,
            SignalName = point.SignalName,
            IecReference = point.IecReference,
            OldValue = oldValue,
            NewValue = display,
            Quality = quality,
            SourceMode = sourceMode,
            Reason = reason
        };
        EventRaised?.Invoke(entry);
    }

    private void EmitStatusSnapshot(
        Iec61850MonitorPoint point,
        RuntimePointState state,
        string status,
        string quality)
    {
        state.Status = status;
        PointUpdated?.Invoke(new Iec61850PointSnapshot
        {
            Point = point,
            PreviousValue = state.Value,
            Value = state.Value,
            Quality = quality,
            DeviceTimestamp = state.DeviceTimestamp,
            SourceMode = state.SourceMode,
            Reason = state.Reason,
            Status = status,
            IsValueEdge = false,
            Sequence = state.Sequence
        });
    }

    private async Task TryReconnectAsync(DeviceSession session, CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow < session.NextReconnectUtc)
            return;

        session.NextReconnectUtc = DateTime.UtcNow.AddSeconds(3);
        session.Device.Status = "Reconnecting";
        session.Device.Detail = $"Retrying MMS association to {session.Device.EndpointText}.";
        Log("WARN", session.Device.Name, "IEC 61850 session is offline. Smart reconnect started.");

        try
        {
            await session.Client.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Old session cleanup must not block reconnect.
        }

        session.Client = new NativeIec61850Client();
        await session.Client.ConnectAsync(session.Device.IpAddress, session.Device.Port, cancellationToken).ConfigureAwait(false);
        if (!session.Client.IsConnected)
        {
            session.Device.IsConnected = false;
            session.Device.Status = "Reconnect pending";
            session.Device.Detail = session.Client.LastErrorMessage;
            return;
        }

        session.ActiveReportPlans.Clear();
        session.ActiveReportPlanOrder.Clear();
        session.PointPlanIds.Clear();
        session.ReportStreams.Clear();
        session.LastUnroutedReportCount = 0;
        var plans = Iec61850ReportPlanner.BuildPlans(session.Device, session.Points.Values);
        await StartReportPlansAsync(session, plans, cancellationToken).ConfigureAwait(false);
        ResetPollQueue(session);
        UpdateDeviceAcquisitionSummary(session);

        session.ConsecutiveSessionErrors = 0;
        session.Device.IsConnected = true;
        session.Device.Status = "Monitoring";
        session.Device.Detail = $"MMS reconnected. {session.Points.Count} point(s) resumed.";
        Log("INFO", session.Device.Name, "MMS reconnect successful. Monitoring resumed automatically.");
    }

    private async Task ForceReconnectAsync(DeviceSession session)
    {
        session.ConsecutiveSessionErrors = 0;
        session.NextReconnectUtc = DateTime.MinValue;
        try
        {
            await session.Client.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Reconnect loop will create a fresh client.
        }
    }

    private static async Task<(string Quality, string DeviceTimestamp)> ReadCompanionAttributesAsync(
        IIec61850Client client,
        Iec61850MonitorPoint point,
        string effectiveValueReference,
        string currentQuality,
        string currentTimestamp,
        CancellationToken cancellationToken)
    {
        var quality = currentQuality;
        var timestamp = currentTimestamp;
        var effectiveQRef = BuildCompanionReference(effectiveValueReference, "q");
        var effectiveTRef = BuildCompanionReference(effectiveValueReference, "t");
        var originalQRef = BuildCompanionReference(point.IecReference, "q");
        var originalTRef = BuildCompanionReference(point.IecReference, "t");
        var qCandidates = BuildCompanionCandidates(point.QualityReference, effectiveQRef, originalQRef);
        var tCandidates = BuildCompanionCandidates(point.TimestampReference, effectiveTRef, originalTRef);

        foreach (var qRef in qCandidates)
        {
            try
            {
                var value = await client.ReadValueAsync(qRef, point.FunctionalConstraint, "Quality", cancellationToken).ConfigureAwait(false);
                if (value != null)
                {
                    quality = value.ToString() ?? currentQuality;
                    break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Some vendors expose q through a discovered explicit reference while
                // others require the canonical sibling path. Try the next candidate.
            }
        }

        foreach (var tRef in tCandidates)
        {
            try
            {
                var value = await client.ReadValueAsync(tRef, point.FunctionalConstraint, "Timestamp", cancellationToken).ConfigureAwait(false);
                if (value != null)
                {
                    timestamp = value.ToString() ?? currentTimestamp;
                    break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // t is optional; try the canonical sibling before leaving it unknown.
            }
        }

        return (quality, timestamp);
    }

    private async Task StopMonitoringSessionAsync(DeviceSession session, bool preservePointDefinitions)
    {
        try
        {
            session.MonitorCancellation.Cancel();
        }
        catch
        {
            // Repeated cancellation is harmless.
        }

        if (session.MonitorTask != null)
        {
            try
            {
                await session.MonitorTask.ConfigureAwait(false);
            }
            catch
            {
                // Loop errors have already been surfaced as diagnostics.
            }
        }

        try
        {
            var results = await session.Client.StopReportMonitorsAsync().ConfigureAwait(false);
            foreach (var result in results)
                Log(result.IsSuccess ? "INFO" : "WARN", session.Device.Name, result.Message);
        }
        catch (Exception ex)
        {
            Log("WARN", session.Device.Name, $"Report monitor cleanup: {ex.Message}");
        }

        session.MonitorTask = null;
        session.ActiveReportPlans.Clear();
        session.ActiveReportPlanOrder.Clear();
        session.PointPlanIds.Clear();
        session.PollQueue.Clear();
        session.ReportStreams.Clear();
        session.LastUnroutedReportCount = 0;
        session.ReportPlanCursor = 0;
        session.PendingReportPlans = Array.Empty<ReportControlPlan>();
        session.ReportSetupPending = false;
        session.ReportSetupNotBeforeUtc = DateTime.MinValue;
        session.ReportSetupDeadlineUtc = DateTime.MinValue;
        if (!preservePointDefinitions)
        {
            session.Points.Clear();
            session.States.Clear();
            session.ReportReferenceIndex.Clear();
        }
    }

    private static Iec61850MonitorPoint CreatePoint(
        Iec61850MonitorDevice device,
        SignalDefinition signal,
        int pollingIntervalMs)
    {
        return new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            IpAddress = device.IpAddress,
            SignalName = string.IsNullOrWhiteSpace(signal.Name) ? signal.ObjectReference : signal.Name,
            IecReference = signal.ObjectReference,
            QualityReference = signal.QualityReference,
            TimestampReference = signal.TimestampReference,
            FunctionalConstraint = signal.FunctionalConstraint,
            IecDataType = signal.DataType,
            Category = signal.Category,
            Unit = signal.Unit,
            DataSetReference = signal.DataSetReference,
            ReportControlReference = signal.ReportControlReference,
            PollingIntervalMs = pollingIntervalMs,
            SourceMode = signal.IsReportCapable ? "Report pending / polling fallback" : "MMS polling"
        };
    }

    private static IReadOnlyList<string> BuildCompanionCandidates(params string[] references)
    {
        var candidates = new List<string>(references.Length);
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference) ||
                candidates.Contains(reference, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            candidates.Add(reference.Trim());
        }
        return candidates;
    }

    private static bool IsFastPoint(Iec61850MonitorPoint point)
    {
        var reference = NormalizeReference(point.IecReference);
        return point.Category.Equals("Position", StringComparison.OrdinalIgnoreCase) ||
               point.Category.Equals("Protection", StringComparison.OrdinalIgnoreCase) ||
               point.Category.Equals("Status", StringComparison.OrdinalIgnoreCase) ||
               point.IecDataType.Equals("Boolean", StringComparison.OrdinalIgnoreCase) ||
               point.IecDataType.Equals("Dbpos", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".pos.stval") ||
               reference.EndsWith(".general");
    }

    private static bool HasExactSemanticEdge(Iec61850MonitorPoint point, string oldValue, string newValue)
    {
        var oldText = (oldValue ?? string.Empty).Trim();
        var newText = (newValue ?? string.Empty).Trim();
        if (string.Equals(oldText, newText, StringComparison.OrdinalIgnoreCase))
            return false;

        // Report values can arrive through different decoders (false/False, 0/0.0,
        // Open [01]/OPEN [01]). SOE must represent a process edge, not formatting noise.
        if (TryNormalizeDiscreteState(point, oldText, out var oldState) &&
            TryNormalizeDiscreteState(point, newText, out var newState))
        {
            return !oldState.Equals(newState, StringComparison.Ordinal);
        }

        if (TryExtractNumber(oldText, out var oldNumber) && TryExtractNumber(newText, out var newNumber))
            return Math.Abs(oldNumber - newNumber) > 0.000000001;

        var oldStateCode = ExtractStateCode(oldText);
        var newStateCode = ExtractStateCode(newText);
        if (!string.IsNullOrWhiteSpace(oldStateCode) && !string.IsNullOrWhiteSpace(newStateCode))
            return !oldStateCode.Equals(newStateCode, StringComparison.OrdinalIgnoreCase);

        return !oldText.Equals(newText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeDiscreteState(
        Iec61850MonitorPoint point,
        string value,
        out string normalized)
    {
        normalized = string.Empty;
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
            return false;

        if (bool.TryParse(text, out var boolean))
        {
            normalized = boolean ? "bool:1" : "bool:0";
            return true;
        }

        var isBooleanPoint = point.IecDataType.Contains("Boolean", StringComparison.OrdinalIgnoreCase);
        if (isBooleanPoint)
        {
            if (text.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("1.0", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("asserted", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "bool:1";
                return true;
            }

            if (text.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("0.0", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("inactive", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("deasserted", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "bool:0";
                return true;
            }
        }

        var stateCode = ExtractStateCode(text);
        if (!string.IsNullOrWhiteSpace(stateCode))
        {
            normalized = "state:" + stateCode.ToLowerInvariant();
            return true;
        }

        if (!IsAnalogPoint(point) && TryExtractNumber(text, out var numeric))
        {
            normalized = "number:" + numeric.ToString("R", CultureInfo.InvariantCulture);
            return true;
        }

        if (IsAnalogPoint(point))
            return false;

        normalized = "text:" + text.ToLowerInvariant();
        return true;
    }

    private static string ExtractStateCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var open = value.LastIndexOf('[');
        var close = value.LastIndexOf(']');
        return open >= 0 && close > open ? value[(open + 1)..close].Trim() : string.Empty;
    }

    private static bool HasMeaningfulEdge(Iec61850MonitorPoint point, string oldValue, string newValue)
    {
        if (!IsAnalogPoint(point))
            return HasExactSemanticEdge(point, oldValue, newValue);

        if (string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!TryExtractNumber(oldValue, out var oldNumber) || !TryExtractNumber(newValue, out var newNumber))
            return !oldValue.Trim().Equals(newValue.Trim(), StringComparison.OrdinalIgnoreCase);

        var absolute = Math.Abs(newNumber - oldNumber);
        var reference = Math.Max(Math.Abs(oldNumber), Math.Abs(newNumber));
        var deadband = Math.Max(reference * 0.001, 0.000001); // 0.1% analog deadband
        return absolute >= deadband;
    }

    private static bool ShouldRecordSequenceOfEvents(Iec61850MonitorPoint point)
    {
        if (IsAnalogPoint(point))
            return false;

        var type = point.IecDataType ?? string.Empty;
        var category = point.Category ?? string.Empty;
        var reference = NormalizeReference(point.IecReference);

        return type.Contains("Boolean", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("Dbpos", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("Enum", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("Bit", StringComparison.OrdinalIgnoreCase) ||
               category.Equals("Position", StringComparison.OrdinalIgnoreCase) ||
               category.Equals("Protection", StringComparison.OrdinalIgnoreCase) ||
               category.Equals("Status", StringComparison.OrdinalIgnoreCase) ||
               reference.EndsWith(".stval", StringComparison.OrdinalIgnoreCase) ||
               reference.EndsWith(".general", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".pos.stval", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetCompanionPollIntervalMs(Iec61850MonitorPoint point)
        => Math.Clamp(point.PollingIntervalMs * 5, 3000, 30000);

    private static int GetVerificationPollIntervalMs(
        Iec61850MonitorPoint point,
        RuntimePointState state,
        bool reportAssigned)
    {
        if (!reportAssigned || !state.ReportChangeVerified)
            return point.PollingIntervalMs;

        // Once an actual dchg/qchg/dupd edge is observed, report is primary. Keep a
        // lightweight safety read so a misconfigured or later-stalled RCB cannot freeze
        // the tester silently. Fast status points are validated more often than analogs.
        var minimum = IsFastPoint(point) ? 5000 : 15000;
        return Math.Clamp(Math.Max(point.PollingIntervalMs * 10, minimum), minimum, 60000);
    }

    private static string BuildPlanAcquisitionLabel(ReportControlPlan plan, bool dynamicDataSet)
    {
        var reference = (plan.ReportControlReference ?? string.Empty).Trim().Replace('$', '.');
        var fallback = plan.Buffered ? "BRCB" : "URCB";
        var name = fallback;
        if (!string.IsNullOrWhiteSpace(reference))
        {
            var slash = reference.LastIndexOf('/');
            var item = slash >= 0 && slash < reference.Length - 1 ? reference[(slash + 1)..] : reference;
            var segments = item.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length > 0)
                name = segments[^1];
        }
        return $"{(dynamicDataSet ? "Dynamic" : "Static")}: {name}";
    }

    private static bool IsChangeReportReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return false;
        var text = reason.Trim().ToLowerInvariant();
        return text.Contains("dchg") || text.Contains("data-change") ||
               text.Contains("qchg") || text.Contains("quality-change") ||
               text.Contains("dupd") || text.Contains("data-update");
    }

    private static bool IsUsefulProcessField(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           !value.Equals("-", StringComparison.OrdinalIgnoreCase) &&
           !value.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
           !value.Equals("unavailable", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnalogPoint(Iec61850MonitorPoint point)
        => point.Category.Equals("Measurement", StringComparison.OrdinalIgnoreCase) ||
           point.IecDataType.Contains("Float", StringComparison.OrdinalIgnoreCase) ||
           point.IecDataType.Contains("Double", StringComparison.OrdinalIgnoreCase);

    private static bool TryExtractNumber(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var token = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeQuality(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality) || quality.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return "Unavailable / q not supplied";
        return quality;
    }

    private static void IndexPointReference(DeviceSession session, Iec61850MonitorPoint point)
    {
        foreach (var key in GetReferenceKeys(point.IecReference))
            session.ReportReferenceIndex.TryAdd(key, point);
    }

    private static Iec61850MonitorPoint? FindPointForReportReference(DeviceSession session, string reference)
    {
        foreach (var key in GetReferenceKeys(reference))
        {
            if (session.ReportReferenceIndex.TryGetValue(key, out var indexed))
                return indexed;
        }

        // Rare vendor projection fallback. Cache the resolved alias so subsequent report
        // events do not scan every monitored point again.
        var canonical = CanonicalDataReference(reference);
        var point = session.Points.Values.FirstOrDefault(candidate =>
        {
            var candidateCanonical = CanonicalDataReference(candidate.IecReference);
            return candidateCanonical.Equals(canonical, StringComparison.OrdinalIgnoreCase) ||
                   candidateCanonical.StartsWith(canonical + ".", StringComparison.OrdinalIgnoreCase) ||
                   canonical.StartsWith(candidateCanonical + ".", StringComparison.OrdinalIgnoreCase);
        });

        if (point != null)
        {
            foreach (var key in GetReferenceKeys(reference))
                session.ReportReferenceIndex[key] = point;
        }

        return point;
    }

    private static IReadOnlyList<Iec61850MonitorPoint> ResolveCoveredPoints(
        ReportControlPlan plan,
        IReadOnlyList<string> coveredReferences)
    {
        if (coveredReferences.Count == 0)
            return Array.Empty<Iec61850MonitorPoint>();

        var members = coveredReferences
            .Select(CanonicalDataReference)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return plan.Bindings
            .Where(point =>
            {
                var pointReference = CanonicalDataReference(point.IecReference);
                return members.Any(member =>
                    pointReference.Equals(member, StringComparison.OrdinalIgnoreCase) ||
                    pointReference.StartsWith(member + ".", StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    private static void ResetPollQueue(DeviceSession session)
    {
        session.PollQueue.Clear();
        var nowUtc = DateTime.UtcNow;
        foreach (var point in session.Points.Values)
        {
            // Every selected point gets an immediate initial read. For report-assigned
            // points this supplies value/q/t and verifies that the RCB is not silently
            // frozen; after dchg is proven, validation automatically slows down.
            var state = session.States[point.PointKey];
            state.NextPollUtc = nowUtc;
            session.PollQueue.Enqueue(point.PointKey, nowUtc.Ticks);
        }
    }

    private static IEnumerable<string> GetReferenceKeys(string? reference)
    {
        var normalized = NormalizeReference(reference);
        if (!string.IsNullOrWhiteSpace(normalized))
            yield return normalized;

        var canonical = CanonicalDataReference(reference);
        if (!string.IsNullOrWhiteSpace(canonical) &&
            !canonical.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            yield return canonical;
    }

    private static string CanonicalDataReference(string? reference)
    {
        var normalized = NormalizeReference(reference);
        var slash = normalized.IndexOf('/');
        if (slash < 0 || slash >= normalized.Length - 1)
            return normalized;

        var domain = normalized[..(slash + 1)];
        var segments = normalized[(slash + 1)..]
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (segments.Count > 1 && IsFunctionalConstraint(segments[1]))
            segments.RemoveAt(1);
        return domain + string.Join('.', segments);
    }

    private static bool IsFunctionalConstraint(string value)
        => value is "st" or "mx" or "sp" or "sv" or "cf" or "dc" or "sg" or "se" or
                    "sr" or "or" or "bl" or "ex" or "co" or "rp" or "br" or "lg" or "go" or "gs";

    private static bool LooksLikeReferenceEcho(string value, string updateReference, string pointReference)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (bool.TryParse(value, out _) || TryExtractNumber(value, out _))
            return false;

        var normalizedValue = NormalizeReference(value);
        return normalizedValue.Equals(NormalizeReference(updateReference), StringComparison.OrdinalIgnoreCase) ||
               normalizedValue.Equals(NormalizeReference(pointReference), StringComparison.OrdinalIgnoreCase) ||
               (value.Contains('/') && value.Contains('$'));
    }

    private static string BuildCompanionReference(string reference, string companion)
    {
        var normalized = reference.Replace('$', '.').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        foreach (var suffix in new[]
                 {
                     ".valWTr.posVal", ".instCVal.mag.f", ".cVal.mag.f", ".stVal", ".general", ".mag.f"
                 })
        {
            if (!normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;
            return normalized[..^suffix.Length] + "." + companion;
        }

        var dot = normalized.LastIndexOf('.');
        return dot > normalized.IndexOf('/') ? normalized[..dot] + "." + companion : string.Empty;
    }

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.').Replace("..", ".").ToLowerInvariant();

    private static void ValidateEndpoint(Iec61850MonitorDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            throw new ArgumentException("IED IP address is required.", nameof(device));
        if (!System.Net.IPAddress.TryParse(device.IpAddress, out _))
            throw new ArgumentException($"'{device.IpAddress}' is not a valid IPv4/IPv6 address.", nameof(device));
        if (device.Port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(device), "MMS port must be between 1 and 65535.");
    }

    private void Log(string level, string source, string message)
        => Diagnostic?.Invoke(new DiagnosticEntry
        {
            Time = DateTime.Now,
            Level = level,
            Source = source,
            Message = message
        });

    public async ValueTask DisposeAsync()
    {
        foreach (var deviceId in _sessions.Keys.ToList())
            await StopDeviceAsync(deviceId).ConfigureAwait(false);
    }
}
