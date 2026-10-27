using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

internal static class DiagnosticReportBuilder
{
    private sealed record TcpProbeResult(string DeviceId, string Endpoint, string Result, string Detail);
    private sealed record AdapterIpv4(string InterfaceId, string Name, NetworkInterfaceType Type, IPAddress Address, int PrefixLength);
    private sealed record RouteAnalysis(string Source, string MatchingAdapters, string AdapterMatrix, string Note);

    public static string BuildEmergency(
        IReadOnlyCollection<Iec61850MonitorDevice> devices,
        IReadOnlyCollection<DiagnosticEntry> logs,
        Iec61850MonitorDevice? selectedDevice,
        Exception collectionFailure)
    {
        ArgumentNullException.ThrowIfNull(collectionFailure);

        var appAssembly = Assembly.GetEntryAssembly() ?? typeof(DiagnosticReportBuilder).Assembly;
        var engineAssembly = typeof(AR.Iec61850.Mms.MmsClientSession).Assembly;
        var builder = new StringBuilder(16 * 1024);
        builder.AppendLine("ARSAS Emergency Diagnostic Report");
        builder.AppendLine("Diagnostic collection degraded, but support evidence was preserved.");
        builder.AppendLine(new string('=', 72));
        builder.AppendLine($"Generated local : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
        builder.AppendLine($"App version     : {GetAssemblyVersion(appAssembly)}");
        builder.AppendLine($"Engine version  : {GetAssemblyVersion(engineAssembly)}");
        builder.AppendLine($"Collection error: {collectionFailure.GetType().Name}: {collectionFailure.Message}");
        builder.AppendLine($"IED count       : {devices.Count}");
        builder.AppendLine($"Selected IED    : {selectedDevice?.Name ?? "none"}");
        builder.AppendLine();

        builder.AppendLine("IED SNAPSHOT");
        builder.AppendLine(new string('-', 72));
        foreach (var device in devices)
        {
            builder.AppendLine(
                $"{device.DeviceId} | {device.Name} | endpoint={device.EndpointText} | connected={device.IsConnected} | monitoring={device.IsMonitoring} | status={device.Status} | staticParity={device.StaticAcquisitionParity.Summary} | routedTraffic={device.StaticAcquisitionParity.TrafficQualificationSummary}");
            if (device.SclAccessPointChoices.Count > 0)
            {
                builder.AppendLine($"  SCD selected AP={device.SclAccessPointName}; endpoint provenance={device.SclEndpointOrigin}");
                builder.AppendLine($"  SCD AP choices={string.Join(", ", device.SclAccessPointChoices.Select(choice => choice.HasDeclaredAddress ? $"{choice.AccessPointName}={choice.DeclaredEndpoint!.EndpointText} [SCD]" : $"{choice.AccessPointName}=[No SCD IP]"))}");
            }
            else if (device.SclEndpointCandidates.Count > 0)
            {
                // Backward-compatible diagnostics for older cached/project models
                // that supplied only direct ConnectedAP endpoints.
                builder.AppendLine($"  SCD selected AP={device.SclAccessPointName}; MMS APs={string.Join(", ", device.SclEndpointCandidates.Select(ap => $"{ap.AccessPointName}={ap.IpAddress}:{ap.Port}"))}");
            }
        }
        if (devices.Count == 0)
            builder.AppendLine("No IED card is present.");

        builder.AppendLine();
        builder.AppendLine("RECENT DIAGNOSTICS");
        builder.AppendLine(new string('-', 72));
        foreach (var entry in logs.OrderBy(item => item.Time).TakeLast(200))
        {
            builder.AppendLine(
                $"{entry.Time:yyyy-MM-dd HH:mm:ss.fff} | {entry.Level} | {entry.Source} | {entry.Message}");
        }
        if (logs.Count == 0)
            builder.AppendLine("No diagnostic entries.");

        builder.AppendLine();
        builder.AppendLine("NOTE");
        builder.AppendLine(new string('-', 72));
        builder.AppendLine("This fallback report intentionally skips network probes and complex aggregation so diagnostics remain copyable even when the normal collector encounters malformed workspace state.");
        return builder.ToString();
    }

    public static async Task<string> BuildAsync(
        IReadOnlyCollection<Iec61850MonitorDevice> devices,
        IReadOnlyCollection<DiagnosticEntry> logs,
        Iec61850MonitorDevice? selectedDevice,
        CancellationToken cancellationToken)
    {
        var appAssembly = Assembly.GetEntryAssembly() ?? typeof(DiagnosticReportBuilder).Assembly;
        var engineAssembly = typeof(AR.Iec61850.Mms.MmsClientSession).Assembly;
        var probes = await Task.WhenAll(devices.Select(device => ProbeDeviceAsync(device, cancellationToken))).ConfigureAwait(false);

        // Diagnostics must remain available even when the workspace itself is malformed,
        // duplicated, offline-only or contains multiple cards with the same endpoint text
        // (for example "No endpoint"). DeviceId is the stable per-card identity; endpoint
        // text is evidence, not a dictionary key.
        var probeByDeviceId = probes
            .GroupBy(result => result.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        var builder = new StringBuilder(64 * 1024);
        builder.AppendLine("ARSAS Diagnostic Report");
        builder.AppendLine("Smart IED Explorer & Monitor");
        builder.AppendLine(new string('=', 72));
        builder.AppendLine($"Generated local : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
        builder.AppendLine($"Generated UTC   : {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff 'UTC'}");
        builder.AppendLine($"App version     : {GetAssemblyVersion(appAssembly)}");
        builder.AppendLine($"App assembly    : {SafeAssemblyLocation(appAssembly)}");
        builder.AppendLine($"Engine version  : {GetAssemblyVersion(engineAssembly)}");
        builder.AppendLine($"Engine assembly : {SafeAssemblyLocation(engineAssembly)}");
        builder.AppendLine($"Runtime         : {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"OS              : {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Architecture    : Process={RuntimeInformation.ProcessArchitecture}, OS={RuntimeInformation.OSArchitecture}");
        builder.AppendLine($"Machine         : {Environment.MachineName}");
        builder.AppendLine($"Culture         : {System.Globalization.CultureInfo.CurrentCulture.Name}");
        builder.AppendLine($"Process path    : {Environment.ProcessPath ?? "unavailable"}");
        builder.AppendLine();

        builder.AppendLine("WORKSPACE SUMMARY");
        builder.AppendLine(new string('-', 72));
        builder.AppendLine($"IED count       : {devices.Count}");
        builder.AppendLine($"Connected       : {devices.Count(device => device.IsConnected)}");
        builder.AppendLine($"Monitoring      : {devices.Count(device => device.IsMonitoring)}");
        builder.AppendLine($"Selected IED    : {selectedDevice?.Name ?? "none"}");
        builder.AppendLine();

        builder.AppendLine("LOCAL NETWORK ADAPTERS");
        builder.AppendLine(new string('-', 72));
        AppendNetworkAdapters(builder);
        builder.AppendLine();

        builder.AppendLine("IED SESSION DETAILS");
        builder.AppendLine(new string('-', 72));
        if (devices.Count == 0)
        {
            builder.AppendLine("No IED is present in the workspace.");
        }
        else
        {
            foreach (var device in devices.OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var diagnostic = device.LastDiagnosticSnapshot ?? new Iec61850DeviceDiagnosticSnapshot();
                probeByDeviceId.TryGetValue(device.DeviceId, out var probe);

                builder.AppendLine($"IED              : {device.Name}");
                builder.AppendLine($"Endpoint         : {device.EndpointText}");
                if (device.SclAccessPointChoices.Count > 0)
                {
                    builder.AppendLine($"SCD selected AP  : {device.SclAccessPointName}");
                    builder.AppendLine($"Endpoint origin  : {device.SclEndpointOrigin}");
                    builder.AppendLine($"SCD AP choices   : {device.SclAccessPointChoices.Count}");
                    builder.AppendLine($"SCD direct MMS IP: {device.SclEndpointCandidates.Count}");
                    foreach (var choice in device.SclAccessPointChoices)
                    {
                        var current = choice.AccessPointName.Equals(device.SclAccessPointName, StringComparison.OrdinalIgnoreCase);
                        var declared = choice.HasDeclaredAddress
                            ? $"{choice.DeclaredEndpoint!.EndpointText} ({choice.DeclaredEndpoint.SubNetworkName})"
                            : "No SCD IP (local binding required)";
                        builder.AppendLine($"  {(current ? "SELECTED" : "AVAILABLE")} {choice.IedName}/{choice.AccessPointName} -> {declared}");
                    }
                }
                else if (device.SclEndpointCandidates.Count > 0)
                {
                    builder.AppendLine($"SCD selected AP  : {device.SclAccessPointName}");
                    builder.AppendLine($"Endpoint origin  : {device.SclEndpointOrigin}");
                    builder.AppendLine($"SCD MMS APs      : {device.SclEndpointCandidates.Count}");
                    foreach (var ap in device.SclEndpointCandidates)
                        builder.AppendLine($"  {ap.IedName}/{ap.AccessPointName} -> {ap.IpAddress}:{ap.Port} ({ap.SubNetworkName})");
                }
                builder.AppendLine($"Connected        : {device.IsConnected}");
                builder.AppendLine($"Monitoring       : {device.IsMonitoring}");
                // Diagnostic projection only: no MMS reads, new caches, or
                // synthetic values. Samples are scoped to the active IED.
                builder.AppendLine($"Browser ledger   : association={device.LiveValues.Association}, version={device.LiveValues.Version}, offline={device.LiveValues.IsOffline}");
                var hydrationFailures = device.InitialHydrationFailures?
                    .SnapshotCurrent(device, maxEntries: 12) ??
                    Array.Empty<IedBrowserReadFailureSample>();
                builder.AppendLine($"Initial MMS errors (sample max 12): {hydrationFailures.Count}");
                foreach (var evidence in hydrationFailures)
                {
                    var detail = evidence.Reason.Replace('\r', ' ').Replace('\n', ' ');
                    builder.AppendLine($"  {evidence.Reference} [{evidence.FunctionalConstraint}] : {detail}");
                }
                builder.AppendLine($"Busy             : {device.IsBusy}");
                builder.AppendLine($"Status           : {device.Status}");
                builder.AppendLine($"Detail           : {device.Detail}");
                builder.AppendLine($"Acquisition      : {device.AcquisitionMode}");
                AppendStaticIngressParity(builder, device.StaticAcquisitionParity);
                AppendReportContinuity(builder, device.ReportContinuityEvidence);
                if (device.IsMonitoring && Iec61850MonitoringModeRegistry.IsStaticDataSetReportOnly(device))
                {
                    AppendStaticInitialImage(builder, device.Points);
                    AppendStaticRcbImageTriage(builder, device.Points, device.ReportContinuityEvidence);
                }
                builder.AppendLine($"Saved model      : {device.HasDiscoveryCache} ({device.SignalCount:N0} signal(s))");
                builder.AppendLine($"Selected         : live={device.SelectedLiveSignalCount:N0}, control={device.SelectedControlSignalCount:N0}");
                var commandCatalog = Iec61850ControlIdentity.DistinctSelected(device.Signals);
                builder.AppendLine($"Control catalog  : selected={commandCatalog.Length}, verified-operable={commandCatalog.Count(s => s.ControlSupportsOperate)}, read-only-or-unverified={commandCatalog.Count(s => !s.ControlSupportsOperate)}");
                foreach (var command in commandCatalog.Take(40))
                {
                    // A bounded single line gives next real-test report the live
                    // ctlModel reference/value and reason without losing raw evidence.
                    var evidence = (command.ControlInspectionEvidence ?? string.Empty)
                        .Replace('\r', ' ').Replace('\n', ' ');
                    if (evidence.Length > 480) evidence = evidence[..480] + "…";
                    builder.AppendLine($"  {command.ObjectReference} | CDC={command.ControlCdc} | liveVerifiedCdc={command.VerifiedLiveControlCdc} | dataSetCdc={command.DataSetAdvisoryCdc} | model={command.ControlModelText} | canOperate={command.ControlSupportsOperate} | {evidence}");
                }
                // A static DataSet may report XCBR/XSWI Pos but omit the
                // separate command-owning CSWI.Pos in another LD. Report the
                // live discovery candidates independently of user selection;
                // diagnostic visibility NEVER grants an MMS control command.
                var independentCswi = device.LiveDiscoveryModel is not null
                    ? Iec61850ControlIdentity.IndependentCswiInspectionCandidates(device.Signals)
                    : [];
                var cswiTotal = device.Signals
                    .Count(s => !s.IsSelected &&
                        Iec61850ControlIdentity.IsIndependentCswiCandidate(s));
                builder.AppendLine($"CSWI outside DataSet: live candidates={cswiTotal}, bounded candidates={independentCswi.Length}, typed-operable={independentCswi.Count(s => s.ControlSupportsOperate)}, unresolved/status-only={independentCswi.Count(s => !s.ControlSupportsOperate)}; discovery does not imply command authority");
                foreach (var cswi in independentCswi.Take(12))
                {
                    var note = (cswi.ControlInspectionEvidence ?? string.Empty)
                        .Replace('\r', ' ').Replace('\n', ' ');
                    if (note.Length > 180) note = note[..180] + "…";
                    builder.AppendLine($"  CSWI {cswi.ObjectReference} | model={cswi.ControlModelText} | verifiedCDC={cswi.VerifiedLiveControlCdc} | operable={cswi.ControlSupportsOperate} | {note}");
                }
                // Side-effect-free SCD ↔ wire-model command parity for Siemens,
                // ZIV and other vendors; do not confuse offline ctlModel design
                // intent with a live MMS inspection or promote StatusOnly rows.
                foreach (var parity in Iec61850SclLiveCswiParityDiagnostic.Summarize(device))
                    builder.AppendLine(parity);
                builder.AppendLine($"Logical Devices  : {EmptyAsUnavailable(device.LogicalDeviceSummary)}");
                builder.AppendLine($"Identity source  : {EmptyAsUnavailable(device.IdentitySource)}");

                var dataSetModel = device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel;
                if (dataSetModel is not null)
                {
                    var completeness = Iec61850DataSetCompletenessDiagnostic.Evaluate(dataSetModel, device.Signals);
                    builder.AppendLine("DataSet inventory:");
                    foreach (var line in Iec61850DataSetCompletenessDiagnostic.FormatReportLines(completeness))
                        builder.AppendLine($"  {line}");
                    // Exact FCD/FCDA conflict diagnostics from the already
                    // attached model. This never opens an MMS association,
                    // reads a value, or changes Report/Read ownership.
                    var fcConflicts = IedBrowserDataSetFcConflictDiagnostic.Evaluate(
                        dataSetModel, maxSamples: 12);
                    builder.AppendLine($"DataSet FC model conflicts: {fcConflicts.TotalCount} across {fcConflicts.CheckedMembers} declared member(s)");
                    foreach (var conflict in fcConflicts.Samples)
                        builder.AppendLine($"  {conflict.DataSet} :: {conflict.Reference} [{conflict.FunctionalConstraint}] · inconsistent FCD/FCDA coverage");
                }
                else
                {
                    builder.AppendLine("DataSet inventory: unavailable (no SCL/live discovery model)");
                }

                var route = AnalyzeRoute(device.IpAddress);
                builder.AppendLine($"TCP probe        : {probe?.Result ?? "not run"} {probe?.Detail ?? string.Empty}".TrimEnd());
                builder.AppendLine($"Route source     : {route.Source}");
                builder.AppendLine($"Same-subnet NIC  : {route.MatchingAdapters}");
                builder.AppendLine($"Adapter matrix   : {route.AdapterMatrix}");
                builder.AppendLine($"Route note       : {route.Note}");
                if (probe != null &&
                    diagnostic.FailureKind.Equals("TCP_CONNECTION_REFUSED", StringComparison.OrdinalIgnoreCase) &&
                    !probe.Result.Equals("REFUSED", StringComparison.OrdinalIgnoreCase))
                {
                    builder.AppendLine($"Probe consistency: Previous connect was REFUSED but current probe is {probe.Result}; endpoint or network state changed between attempts.");
                }
                builder.AppendLine($"Diag captured    : {(diagnostic.CapturedAt == default ? "unavailable" : diagnostic.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))}");
                builder.AppendLine($"Diag phase       : {EmptyAsUnavailable(diagnostic.Phase)}");
                builder.AppendLine($"Failure kind     : {EmptyAsUnavailable(diagnostic.FailureKind)}");
                builder.AppendLine($"Friendly message : {EmptyAsUnavailable(diagnostic.FriendlyMessage)}");
                builder.AppendLine($"Exception        : {JoinNonEmpty(diagnostic.ExceptionType, diagnostic.ExceptionMessage)}");
                builder.AppendLine($"Connection mode  : {EmptyAsUnavailable(diagnostic.ConnectionMode)}");
                builder.AppendLine($"Native state     : {EmptyAsUnavailable(diagnostic.NativeState)}");
                builder.AppendLine($"Transport ready  : {diagnostic.TransportReady}");
                builder.AppendLine($"MMS ready        : {diagnostic.MmsReady}");
                builder.AppendLine($"Association      : {EmptyAsUnavailable(diagnostic.AssociationAttemptSummary)}");
                builder.AppendLine($"Association hex  : {Truncate(diagnostic.AssociationResponseHex, 4096)}");
                builder.AppendLine($"Discovery        : {EmptyAsUnavailable(diagnostic.DiscoverySummary)}");
                builder.AppendLine($"Discovery req    : {Truncate(diagnostic.DiscoveryRequestHex, 4096)}");
                builder.AppendLine($"Discovery resp   : {Truncate(diagnostic.DiscoveryResponseHex, 4096)}");
                builder.AppendLine(new string('-', 72));
            }
        }
        builder.AppendLine();

        builder.AppendLine("RECENT DIAGNOSTICS");
        builder.AppendLine(new string('-', 72));
        var recentLogs = logs
            .OrderBy(entry => entry.Time)
            .TakeLast(500)
            .ToArray();
        if (recentLogs.Length == 0)
        {
            builder.AppendLine("No diagnostic entries.");
        }
        else
        {
            foreach (var entry in recentLogs)
            {
                builder.Append(entry.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                builder.Append(" | ");
                builder.Append((entry.Level ?? string.Empty).PadRight(5));
                builder.Append(" | ");
                builder.Append(entry.Source ?? string.Empty);
                builder.Append(" | ");
                builder.AppendLine(entry.Message ?? string.Empty);
            }
        }

        builder.AppendLine();
        builder.AppendLine("INTERPRETATION NOTE");
        builder.AppendLine(new string('-', 72));
        builder.AppendLine("TCP_CONNECTION_REFUSED means the socket was rejected before COTP/ACSE/MMS negotiation.");
        builder.AppendLine("An association-profile name such as BalancedApTitle is an attempted handshake profile, not the root cause when TCP itself is refused.");
        builder.AppendLine("Paste this complete report into the support conversation for analysis.");
        return builder.ToString();
    }


    private static void AppendStaticInitialImage(
        StringBuilder builder,
        IEnumerable<Iec61850MonitorPoint> points)
    {
        var image = StaticDataSetInitialImageDiagnostic.Evaluate(points);
        builder.AppendLine($"Static initial image: {image.Summary}");
        builder.AppendLine("  Interpretation : display completeness only; Unknown q does not mean missing value, and RCB setup or first report does not prove every selected member is live.");
        foreach (var group in image.Groups.Take(12))
            builder.AppendLine($"  DataSet/RCB     : {group.DataSetReference} | {group.RcbReference} | visible={group.ValueVisible}/{group.Selected}, pending={group.ValuePending}, qNotSupplied={group.QualityNotSupplied}");
        foreach (var point in image.PendingPoints.Take(12))
            builder.AppendLine($"  Awaiting value  : {point.Reference} | RCB={point.RcbReference} | status={point.Status} | reason={point.Reason}");
        if (image.PendingPoints.Count > 12)
            builder.AppendLine($"  Additional pending: {image.PendingPoints.Count - 12} (showing first 12; never inferred unavailable)");
    }

    /// <summary>
    /// Per-RCB attribution for an already-captured static monitor image.
    /// A successful RptEna/GI request is not proof of a routed Report; zero-frame
    /// RCBs must be visible in Copy Diagnostic even when no continuity stream exists.
    /// This only formats immutable local evidence and performs no MMS operations.
    /// </summary>
    internal static void AppendStaticRcbImageTriage(
        StringBuilder builder,
        IEnumerable<Iec61850MonitorPoint> points,
        Iec61850ReportContinuitySnapshot? continuity)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var groups = StaticDataSetRcbImageTriage.Evaluate(points, continuity, maxGroups: 16);
        builder.AppendLine($"Static RCB traffic : shown={groups.Count}, " +
            $"routed={groups.Count(group => group.HasActualReportFrames)}, " +
            $"noRoutedFrames={groups.Count(group => !group.HasActualReportFrames)}");
        builder.AppendLine("  Attribution     : one exact DataSet/RCB per row; zero routed frames " +
            "is not proof of IED silence, GI rejection or MMS connection failure. " +
            "Missing activation proof stays unverified.");
        foreach (var group in groups)
            builder.AppendLine($"  RCB evidence    : {group.Summary}, " +
                $"ingress={(group.HasActualReportFrames ? "ROUTED" : "NO ROUTED FRAMES")}");
    }

    internal static void AppendReportContinuity(
        StringBuilder builder, Iec61850ReportContinuitySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            builder.AppendLine("Report continuity : NOT CAPTURED • no monitor evidence in current association");
            return;
        }

        var verdict = snapshot.Frames == 0 ? "NO FRAME METADATA" :
            snapshot.UntrackedStreamCount > 0 ? "INCOMPLETE • metadata stream limit reached" :
            snapshot.Findings > 0 ? "ANOMALY OBSERVED" :
            snapshot.Sequenced < snapshot.Frames ? "PARTIAL SqNum EVIDENCE" :
            "OBSERVED WITHOUT SEQUENCE ALERTS";
        builder.AppendLine($"Report continuity : {verdict} • frames={snapshot.Frames}, " +
            $"sequenced={snapshot.Sequenced}, processUpdates={snapshot.ProcessUpdatesSeen}, " +
            $"streams={snapshot.StreamCount}, findings={snapshot.Findings}, " +
            $"BufOvfl={snapshot.Overflows}, untracked={snapshot.UntrackedStreamCount}, " +
            $"OptFldsDecoded={snapshot.OptFldsDecoded}, OptFldsUnknown={snapshot.OptFldsUnknown}");
        builder.AppendLine($"  Frame triage    : framesWithFindings={snapshot.AnomalousFrames}, " +
            $"sequenceDiscontinuities={snapshot.SequenceDiscontinuities}; " +
            "these are observational counts, not confirmed lost SOE events.");
        builder.AppendLine("  Qualification   : ARIEC decoder-sourced wire OptFlds evidence; " +
            "OptFldsUnknown is not proof of absent wire fields. No alerts does not prove SOE/event continuity, " +
            "GI causality or reconnect/replay completeness.");

        foreach (var (stream, index) in snapshot.Streams.Select((stream, index) => (stream, index)))
        {
            static string Safe(string value)
            {
                var text = (value ?? string.Empty).Replace("\r", @"\r", StringComparison.Ordinal)
                    .Replace("\n", @"\n", StringComparison.Ordinal);
                return text.Length > 240 ? text[..240] + "…[TRUNCATED]" : text;
            }
            builder.AppendLine($"  stream[{index}]      : {(stream.Buffered ? "BRCB" : "URCB")}, " +
                $"rcb={Safe(stream.Rcb)}, dataset={Safe(stream.DataSet)}, " +
                $"rptId={Safe(stream.ReportId)}, frames={stream.Frames}, " +
                $"SqNum={stream.Sequenced}, missingSqNum={stream.SequenceMissing}, " +
                $"segmented={stream.Segmented}, findings={stream.Findings}, " +
                $"BufOvfl={stream.Overflow}, ConfRevChanges={stream.ConfRevChanges}, " +
                $"EntryIDPresent={stream.EntryIdPresent}, firstSqNum={stream.FirstSqNum?.ToString() ?? "-"}, " +
                $"lastSqNum={stream.LastSqNum?.ToString() ?? "-"}");
            if (stream.Findings > 0)
                builder.AppendLine($"    RCB triage    : anomalousFrames={stream.AnomalousFrames}, " +
                    $"sqDiscontinuities={stream.SequenceDiscontinuities}, " +
                    $"sqChangedEntryID={stream.SequenceWithChangedEntryId}, " +
                    $"sqRepeatedEntryID={stream.SequenceWithRepeatedEntryId}, " +
                    $"sqEntryIDUnknown={stream.SequenceWithoutComparableEntryId}, " +
                    $"sqWithGIreason={stream.SequenceWithGiReason}, " +
                    $"sqWithBufOvfl={stream.SequenceWithOverflow}; " +
                    "EntryID difference/identical is not an event-loss or harmless-replay conclusion.");
            if (stream.Buffered && stream.Findings > 0)
                builder.AppendLine($"    BRCB context  : {ExplainBrcbContinuity(stream)}");
            if (stream.AnomalousFrames > 0)
                builder.AppendLine($"    RCB follow-up : receivedAfterLastFinding={stream.FramesAfterLastFinding}, " +
                    $"firstFrameUtc={stream.FirstFrameReceivedAtUtc?.ToString("O") ?? "-"}, " +
                    $"lastFindingUtc={stream.LastFindingReceivedAtUtc?.ToString("O") ?? "-"}, " +
                    $"lastFrameUtc={stream.LastFrameReceivedAtUtc?.ToString("O") ?? "-"}; " +
                    "later clean frames are observed report traffic, not proof of complete buffered SOE.");
            // Chronology is a small immutable copy of decoder metadata only:
            // first 32 frames plus 8 most recent frames; no raw EntryID or values.
            if (stream.Buffered && stream.Chronology.Count > 0)
            {
                builder.AppendLine($"    BRCB chronology: first={stream.ChronologyInitialCount}, " +
                    $"recent={stream.ChronologyRecentCount}, omittedMiddle={stream.ChronologyOmittedFrames}; " +
                    "receive-order metadata only, not TCP/wire-order or SOE completeness proof.");
                foreach (var item in stream.Chronology)
                    builder.AppendLine($"      frame#{item.FrameOrdinal}: " +
                        $"receivedUtc={item.ReceivedAtUtc?.ToString("O") ?? "unknown"}, " +
                        $"SqNum={item.SequenceNumber?.ToString() ?? "unknown"}, " +
                        $"GIreason={Verdict(item.GiReason)}, " +
                        $"BufOvfl={Verdict(item.BufferOverflow)}, " +
                        $"EntryIDrelation={item.EntryIdRelation}, findings={item.FindingCount}");
            }
            if (stream.Buffered && stream.RecentAnomalies.Count > 0)
            {
                builder.AppendLine($"    BRCB anomaly history: last={stream.RecentAnomalies.Count} " +
                    $"of {stream.AnomalousFrames} anomalous frames; bounded decoder receive-order evidence. " +
                    "Some entries may also appear in the first/recent chronology.");
                foreach (var item in stream.RecentAnomalies)
                    builder.AppendLine($"      anomalyFrame#{item.FrameOrdinal}: " +
                        $"receivedUtc={item.ReceivedAtUtc?.ToString("O") ?? "unknown"}, " +
                        $"SqNum={item.SequenceNumber?.ToString() ?? "unknown"}, " +
                        $"GIreason={Verdict(item.GiReason)}, " +
                        $"BufOvfl={Verdict(item.BufferOverflow)}, " +
                        $"EntryIDrelation={item.EntryIdRelation}, findings={item.FindingCount}");
            }
            builder.AppendLine($"    OptFlds       : decoded={stream.OptFldsDecoded}/{stream.Frames}, " +
                $"unknown={stream.OptFldsUnknown}, lastMask={Safe(stream.LastOptFldsHex)}, " +
                $"SqNum requested={stream.SqNumAdvertised}, omitted={stream.SqNumOmitted}, " +
                $"requestedButUndecoded={stream.SqNumAdvertisedMissing}; " +
                $"EntryID requested={stream.EntryIdAdvertised}, omitted={stream.EntryIdOmitted}, " +
                $"emptyOrUnprojected={stream.EntryIdEmptyOrUnprojected}");
            // Do not inflate diagnostics for unrelated/no-evidence streams.
            // Keep the snapshot bounded even if many RptIDs are observed.
            // Start-return timestamp is not a precise MMS send timestamp.
            if (stream.RptEnaWriteAccepted.HasValue || stream.GiWriteAccepted.HasValue ||
                stream.ActivationReturnedAtUtc.HasValue || stream.GiReasonFrames > 0 ||
                stream.IntegrityReasonFrames > 0 || stream.ReasonUnavailableFrames > 0 ||
                stream.LastAnomalyCurrentSqNum.HasValue)
                builder.AppendLine($"    RCB lifecycle : RptEnaWrite={Verdict(stream.RptEnaWriteAccepted)}, " +
                $"GIWrite={Verdict(stream.GiWriteAccepted)}, " +
                $"startReturnUtc={stream.ActivationReturnedAtUtc?.ToString("O") ?? "-"}, " +
                $"GIreasonFrames={stream.GiReasonFrames}, integrityReasonFrames={stream.IntegrityReasonFrames}, " +
                $"reasonUnavailableFrames={stream.ReasonUnavailableFrames}, " +
                $"lastAnomalySqNum={(stream.LastAnomalyPriorSqNum.HasValue ? stream.LastAnomalyPriorSqNum.ToString() : "-")}→" +
                $"{(stream.LastAnomalyCurrentSqNum.HasValue ? stream.LastAnomalyCurrentSqNum.ToString() : "-")}, " +
                $"lastAnomalyGIreason={Verdict(stream.LastAnomalyGiReason)}, " +
                $"lastAnomalyUtc={stream.LastAnomalyReceivedAtUtc?.ToString("O") ?? "-"}");
        }
        if (snapshot.StreamCount > snapshot.Streams.Count)
            builder.AppendLine($"  stream list     : TRUNCATED • shown={snapshot.Streams.Count}, total={snapshot.StreamCount}");
    }

    /// <summary>
    /// IEC 61850-aware interpretation of already observed RCB metadata:
    /// temporal context and GI reason are correlations, never proof of lost
    /// buffered entries, a server reset, or that a sequence gap is harmless.
    /// No extra MMS traffic, raw event retention or timer.
    /// </summary>
    internal static string ExplainBrcbContinuity(Iec61850ReportContinuityStreamSnapshot stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var withoutProvenGi = Math.Max(0, stream.SequenceDiscontinuities - stream.SequenceWithGiReason);
        var context = $"sequenceDiscontinuities={stream.SequenceDiscontinuities}, " +
            $"GI-tagged={stream.SequenceWithGiReason}, withoutConfirmedGI={withoutProvenGi}, " +
            $"BufOvflAdvertised={stream.Overflow}";

        if (stream.ActivationReturnedAtUtc is { } activation &&
            stream.LastAnomalyReceivedAtUtc is { } anomaly &&
            anomaly >= activation)
        {
            var delta = anomaly - activation;
            var elapsed = delta.TotalMilliseconds;
            if (double.IsFinite(elapsed))
            {
                context += $", lastAnomalyAfterStartMs={elapsed:0}";
                if (delta >= TimeSpan.FromSeconds(5))
                    context += "; late recurrence observed beyond 5s of RCB activation (NOT startup-only)";
            }
        }
        else
        {
            context += "; timing=UNKNOWN (insufficient RCB lifecycle evidence)";
        }

        return context + "; received-frame order only, not TCP wire order, event-loss or GI causality proof";
    }

    private static string Verdict(bool? value) =>
        value is true ? "YES" : value is false ? "NO" : "UNKNOWN";

    internal static void AppendStaticIngressParity(
        StringBuilder builder,
        StaticAcquisitionParitySnapshot parity)
    {
        builder.AppendLine($"Static parity    : {parity.Summary}");
        builder.AppendLine($"Routed traffic   : {parity.TrafficQualificationSummary}");
        builder.AppendLine("Physical qualifier: semantic MATCH is not traffic proof; each planned static RCB needs a schema-safe routed process value on its own ingress.");

        if (parity.Discovery is { } discovery)
        {
            builder.AppendLine($"  Discovery      : {discovery.Summary}");
            builder.AppendLine($"    traffic       : {discovery.RoutedTrafficSummary}; first={discovery.FirstRoutedReportAtUtc?.ToString("O") ?? "pending"}");
            foreach (var target in discovery.RuntimeTargets.Take(8))
                builder.AppendLine($"    runtime       : {target} [{(discovery.RoutedReportTargets.Contains(target, StringComparer.Ordinal) ? "ROUTED" : "PENDING")}]");
            AppendStaticSemanticBasis(builder, discovery);
        }

        if (parity.OpenScl is { } openScl)
        {
            builder.AppendLine($"  Open SCL       : {openScl.Summary}");
            builder.AppendLine($"    traffic       : {openScl.RoutedTrafficSummary}; first={openScl.FirstRoutedReportAtUtc?.ToString("O") ?? "pending"}");
            foreach (var target in openScl.RuntimeTargets.Take(8))
                builder.AppendLine($"    runtime       : {target} [{(openScl.RoutedReportTargets.Contains(target, StringComparer.Ordinal) ? "ROUTED" : "PENDING")}]");
            AppendStaticSemanticBasis(builder, openScl);
        }

        foreach (var difference in parity.Differences.Take(12))
            builder.AppendLine($"  difference     : {difference}");
    }

    /// <summary>
    /// Read-only, bounded provenance for explaining why two independent field runs
    /// produced different hashes. These exact source-neutral rows feed the existing
    /// hash; indexed live RCB instances are intentionally excluded from the rows.
    /// Truncation is explicit: partial rows must not be used as parity proof.
    /// </summary>
    private static void AppendStaticSemanticBasis(
        StringBuilder builder,
        StaticAcquisitionIngressEvidence evidence)
    {
        const int maxRows = 12;
        const int maxCharsPerRow = 4096;

        builder.AppendLine($"    semantic gate  : {(evidence.IsComparable ? "COMPARABLE" : "INCOMPLETE")}" +
            (evidence.IsComparable ? string.Empty : $"; {evidence.IncomparableReason}"));
        builder.AppendLine($"    semantic basis : {evidence.SemanticLines.Count} fingerprint row(s); " +
            "indexed live RCB slots excluded; diagnostic copy only");
        for (var i = 0; i < Math.Min(evidence.SemanticLines.Count, maxRows); i++)
        {
            var row = evidence.SemanticLines[i] ?? string.Empty;
            var part = row.Length > maxCharsPerRow
                ? row[..maxCharsPerRow]
                : row;
            // Avoid synthetic diagnostic lines if an external source contains CR/LF.
            part = part.Replace("\r", @"\r", StringComparison.Ordinal)
                .Replace("\n", @"\n", StringComparison.Ordinal);
            builder.AppendLine($"    semantic[{i}]   : {part}" +
                (row.Length > maxCharsPerRow ? $" [TRUNCATED; full length={row.Length}]" : string.Empty));
        }

        if (evidence.SemanticLines.Count > maxRows)
            builder.AppendLine($"    semantic rows  : TRUNCATED; shown={maxRows}, total={evidence.SemanticLines.Count}");
    }

    private static RouteAnalysis AnalyzeRoute(string targetText)
    {
        if (!IPAddress.TryParse(targetText, out var target) || target.AddressFamily != AddressFamily.InterNetwork)
            return new RouteAnalysis("unavailable", "unavailable", "unavailable", "Route analysis currently supports IPv4 endpoints.");

        var adapters = GetActiveIpv4Adapters();
        var matchingAddresses = adapters
            .Where(item => IsInSameSubnet(item.Address, target, item.PrefixLength))
            .ToArray();
        var matching = matchingAddresses
            .Select(item => $"{item.Name}={item.Address}/{item.PrefixLength}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var matrix = adapters.Length == 0
            ? "no active IPv4 adapter"
            : string.Join("; ", adapters.Select(item =>
                $"{item.Name}={item.Address}/{item.PrefixLength} " +
                (IsInSameSubnet(item.Address, target, item.PrefixLength) ? "MATCH" : "NO_MATCH")));

        string source;
        string? sourceAdapter = null;
        try
        {
            using var routeSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            routeSocket.Connect(new IPEndPoint(target, 9));
            var local = (IPEndPoint?)routeSocket.LocalEndPoint;
            source = local?.Address.ToString() ?? "unavailable";
            sourceAdapter = adapters.FirstOrDefault(item => item.Address.Equals(local?.Address))?.Name;
            if (!string.IsNullOrWhiteSpace(sourceAdapter))
                source += $" via {sourceAdapter}";
        }
        catch (Exception ex)
        {
            source = $"unavailable ({ex.GetType().Name}: {ex.Message})";
        }

        // Do not confuse multiple IP aliases on ONE KM-TEST interface with
        // competing network adapters. Exact local IP means same-host endpoint;
        // neither condition warrants telling operators to disable interfaces.
        var note = ExplainIpv4Route(target,
            matchingAddresses.Select(item => (item.InterfaceId, item.Name)).ToArray(),
            adapters.Any(item => item.Address.Equals(target)));

        return new RouteAnalysis(
            source,
            matching.Length == 0 ? "none" : string.Join(", ", matching),
            matrix,
            note);
    }

    /// <summary>
    /// Pure, testable interface-aware route advice. The interface ID, not the
    /// number of matching IPv4 addresses, defines routing ambiguity. This is
    /// descriptive only: the actual OS routing table remains authoritative.
    /// </summary>
    internal static string ExplainIpv4Route(
        IPAddress target,
        IReadOnlyList<(string InterfaceId, string AdapterName)> matchedAddresses,
        bool targetAssignedLocally)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(matchedAddresses);
        var distinctInterfaces = matchedAddresses
            .GroupBy(item => item.InterfaceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().AdapterName)
            .ToArray();
        if (targetAssignedLocally)
            return $"Target {target} is assigned to this PC on " +
                $"{(distinctInterfaces.Length == 0 ? "a local interface" : string.Join(", ", distinctInterfaces))}; " +
                "this can be a same-host IED simulator. Do not infer a competing NIC from IPv4 aliases. " +
                "Check the local MMS listener and actual OS route if connection fails.";
        if (distinctInterfaces.Length == 0)
            return $"No directly matching IPv4 subnet for {target}; a gateway or static route can still provide connectivity. " +
                "Check the Windows routing table before drawing a connectivity conclusion.";
        if (distinctInterfaces.Length == 1)
            return $"One network interface ({distinctInterfaces[0]}) matches {target}, across " +
                $"{matchedAddresses.Count} IPv4 address(es). IP aliases are not competing adapters; " +
                "confirm actual OS routing and physical/virtual IED connection if needed.";
        return $"{distinctInterfaces.Length} distinct network interfaces match {target} " +
            $"({string.Join(", ", distinctInterfaces)}); Windows interface metrics/routing table may select " +
            "a different path. Check the OS route before changing adapter configuration.";
    }

    private static AdapterIpv4[] GetActiveIpv4Adapters()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                                  adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => new AdapterIpv4(
                        adapter.Id,
                        adapter.Name,
                        adapter.NetworkInterfaceType,
                        address.Address,
                        address.PrefixLength)))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Address.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<AdapterIpv4>();
        }
    }

    private static bool IsInSameSubnet(IPAddress left, IPAddress right, int prefixLength)
    {
        if (left.AddressFamily != AddressFamily.InterNetwork ||
            right.AddressFamily != AddressFamily.InterNetwork ||
            prefixLength is < 0 or > 32)
        {
            return false;
        }

        var leftBytes = left.GetAddressBytes();
        var rightBytes = right.GetAddressBytes();
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var index = 0; index < fullBytes; index++)
        {
            if (leftBytes[index] != rightBytes[index])
                return false;
        }

        if (remainingBits == 0)
            return true;

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (leftBytes[fullBytes] & mask) == (rightBytes[fullBytes] & mask);
    }

    private static async Task<TcpProbeResult> ProbeDeviceAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken)
    {
        if (device.IsConnected)
            return new TcpProbeResult(device.DeviceId, device.EndpointText, "SKIPPED", "active MMS session already connected");

        if (string.IsNullOrWhiteSpace(device.IpAddress) || device.Port is <= 0 or > 65535)
            return new TcpProbeResult(device.DeviceId, device.EndpointText, "INVALID", "invalid endpoint");

        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(1800));
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(device.IpAddress, device.Port, timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new TcpProbeResult(device.DeviceId, device.EndpointText, "OPEN", $"TCP accepted in {stopwatch.Elapsed.TotalMilliseconds:0} ms");
        }
        catch (SocketException ex)
        {
            stopwatch.Stop();
            return new TcpProbeResult(
                device.DeviceId,
                device.EndpointText,
                ex.SocketErrorCode == SocketError.ConnectionRefused ? "REFUSED" : "SOCKET_ERROR",
                $"SocketError={ex.SocketErrorCode}; {ex.Message}; {stopwatch.Elapsed.TotalMilliseconds:0} ms");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new TcpProbeResult(device.DeviceId, device.EndpointText, "TIMEOUT", $"> {stopwatch.Elapsed.TotalMilliseconds:0} ms");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new TcpProbeResult(device.DeviceId, device.EndpointText, "FAILED", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void AppendNetworkAdapters(StringBuilder builder)
    {
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                .OrderBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (adapters.Length == 0)
            {
                builder.AppendLine("No active network adapter was reported by Windows.");
                return;
            }

            foreach (var adapter in adapters)
            {
                var properties = adapter.GetIPProperties();
                var addresses = properties.UnicastAddresses
                    .Where(address => address.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    .Select(address => $"{address.Address}/{address.PrefixLength}")
                    .ToArray();
                var gateways = properties.GatewayAddresses
                    .Select(gateway => gateway.Address.ToString())
                    .Where(value => !string.IsNullOrWhiteSpace(value) && value != "0.0.0.0")
                    .ToArray();

                builder.AppendLine($"{adapter.Name} | {adapter.NetworkInterfaceType} | IP={string.Join(", ", addresses)} | GW={string.Join(", ", gateways)}");
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine($"Network adapter enumeration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string GetAssemblyVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational;
        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static string SafeAssemblyLocation(Assembly assembly)
    {
        try
        {
            return string.IsNullOrWhiteSpace(assembly.Location) ? "single-file/in-memory" : assembly.Location;
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string EmptyAsUnavailable(string? value)
        => string.IsNullOrWhiteSpace(value) ? "unavailable" : value.Trim();

    private static string JoinNonEmpty(params string?[] values)
    {
        var result = string.Join(": ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
        return string.IsNullOrWhiteSpace(result) ? "unavailable" : result;
    }

    private static string Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unavailable";
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength] + $"… [truncated {normalized.Length - maximumLength:N0} chars]";
    }
}