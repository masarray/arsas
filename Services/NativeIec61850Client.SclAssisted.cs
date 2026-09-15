using System.Diagnostics;
using ArMms = AR.Iec61850.Mms;
using ArScl = AR.Iec61850.Scl;
using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Services;

public sealed class SclAssistedClientConnectResult
{
    public bool IsSuccess { get; init; }
    public SclAssistedConnectionPreparation Preparation { get; init; } = new();
    public ArScl.SclAssistedMmsOnlineResult? Online { get; init; }
    public ArMms.InitialFcReadExecutionResult? InitialRead { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public TimeSpan AssociationValidationDuration { get; init; }
    public TimeSpan InitialReadDuration { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed partial class NativeIec61850Client
{
    public Task<SclAssistedClientConnectResult> ConnectUsingSclAsync(
        string sclXml,
        string iedName,
        string accessPointName,
        string host,
        int port,
        CancellationToken cancellationToken)
        => ConnectUsingSclAsync(
            sclXml,
            iedName,
            accessPointName,
            host,
            port,
            ArMms.MmsReadBatchCodec.MaximumVariableReferencesPerRead,
            cancellationToken);

    /// <summary>
    /// Opens the ARIEC61850 SCL-assisted online path: exact SCL association identity,
    /// Domain/VMD reconciliation, then bounded sequential FC-root initial Reads.
    /// The explicit batch-size argument supports controlled interoperability trials;
    /// it never enables discovery or a silent automatic fallback.
    /// </summary>
    public async Task<SclAssistedClientConnectResult> ConnectUsingSclAsync(
        string sclXml,
        string iedName,
        string accessPointName,
        string host,
        int port,
        int maximumVariableReferencesPerRead,
        CancellationToken cancellationToken)
    {
        var totalWatch = Stopwatch.StartNew();
        var associationDuration = TimeSpan.Zero;
        var initialReadDuration = TimeSpan.Zero;

        await DisposeControlSessionsAsync().ConfigureAwait(false);
        LastErrorMessage = string.Empty;
        LastConnectionFailureKind = string.Empty;
        LastConnectionTechnicalSummary = string.Empty;
        LastDiscoverySummary = string.Empty;
        _lastDiscovery = null;
        _liveModel = null;
        LastReportInventory = new NativeReportInventory();
        _reportMonitorSessions.Clear();
        _reportMonitorCoverage.Clear();
        ResetSemanticReportProjectionContext();
        Interlocked.Exchange(ref _engineCompatibilityWarningIssued, 0);
        DetectedIdentity = new Iec61850DeviceIdentity();
        _host = host?.Trim() ?? string.Empty;
        _port = port <= 0 ? 102 : port;

        var preparation = SclAssistedConnectionPreparationBuilder.Build(
            sclXml,
            iedName,
            accessPointName,
            _host,
            _port,
            maximumVariableReferencesPerRead);
        if (!preparation.IsSuccess ||
            preparation.AssociationPlan is null ||
            preparation.InitialReadDesign is null ||
            preparation.InitialReadPlan is null)
        {
            LastConnectionFailureKind = "SCL_PLAN_INVALID";
            LastErrorMessage = preparation.Errors.Count == 0
                ? "SCL-assisted connection preparation failed."
                : string.Join(" | ", preparation.Errors);
            LastConnectionTechnicalSummary = LastErrorMessage;
            totalWatch.Stop();
            return new SclAssistedClientConnectResult
            {
                Preparation = preparation,
                Warnings = preparation.Warnings,
                TotalDuration = totalWatch.Elapsed,
                Message = LastErrorMessage
            };
        }

        try
        {
            var associationWatch = Stopwatch.StartNew();
            var online = await _session.ConnectSclAssistedAsync(
                preparation.AssociationPlan,
                preparation.DomainInventory,
                TimeSpan.FromSeconds(8),
                cancellationToken).ConfigureAwait(false);
            associationWatch.Stop();
            associationDuration = associationWatch.Elapsed;

            if (!online.IsCompatible)
            {
                LastConnectionFailureKind = online.Status.ToString();
                LastErrorMessage = online.Message;
                LastConnectionTechnicalSummary = online.Domains?.Summary ?? online.Message;
                await _session.DisposeAsync().ConfigureAwait(false);
                totalWatch.Stop();
                return new SclAssistedClientConnectResult
                {
                    Preparation = preparation,
                    Online = online,
                    Warnings = preparation.Warnings,
                    AssociationValidationDuration = associationDuration,
                    TotalDuration = totalWatch.Elapsed,
                    Message = LastErrorMessage
                };
            }

            var readWatch = Stopwatch.StartNew();
            var initialRead = await _session.ExecuteInitialFcReadPlanAsync(
                preparation.InitialReadPlan,
                TimeSpan.FromSeconds(5),
                cancellationToken).ConfigureAwait(false);
            readWatch.Stop();
            initialReadDuration = readWatch.Elapsed;

            if (initialRead.Status is ArMms.InitialFcReadExecutionStatus.InvalidPlan
                or ArMms.InitialFcReadExecutionStatus.SessionNotReady
                or ArMms.InitialFcReadExecutionStatus.TimedOut
                or ArMms.InitialFcReadExecutionStatus.TransportFailure)
            {
                LastConnectionFailureKind = $"INITIAL_FC_READ_{initialRead.Status}";
                LastErrorMessage = initialRead.Message;
                LastConnectionTechnicalSummary = initialRead.Message;
                await _session.DisposeAsync().ConfigureAwait(false);
                totalWatch.Stop();
                return new SclAssistedClientConnectResult
                {
                    Preparation = preparation,
                    Online = online,
                    InitialRead = initialRead,
                    Warnings = preparation.Warnings,
                    AssociationValidationDuration = associationDuration,
                    InitialReadDuration = initialReadDuration,
                    TotalDuration = totalWatch.Elapsed,
                    Message = LastErrorMessage
                };
            }

            // Seed a deliberately minimal discovery context from trusted SCL authority.
            // Existing reporting code therefore sees that a model context exists and will
            // not invoke DiscoverAsync behind the SCL-assisted connection path. Empty RCB
            // inventory means reporting must prove only what it can, otherwise polling is
            // used by the existing runtime; Re-scan remains the explicit full-discovery path.
            var reconciledDomains = online.Domains?.MatchedDomains
                ?? preparation.DomainInventory.ExpectedDomains;
            var domainVariables = reconciledDomains
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(
                    domain => domain,
                    _ => (IReadOnlyList<string>)Array.Empty<string>(),
                    StringComparer.Ordinal);

            _lastDiscovery = new ArMms.MmsDiscoveryResult
            {
                Snapshot = new ArMms.MmsDiscoverySnapshot
                {
                    DomainVariables = domainVariables,
                    DomainVariableLists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                },
                ReportInventory = new ArMms.MmsReportInventory(),
                IedDirectory = new ArMms.MmsIedModelDirectory(Array.Empty<ArMms.MmsFcResolvedPoint>()),
                Summary = "Trusted SCL authority: domain validation and bounded initial FC-root snapshot completed; full live discovery intentionally skipped."
            };
            _liveModel = preparation.InitialReadDesign.Model;
            LastReportInventory = new NativeReportInventory();

            var projectionErrors = initialRead.Batches
                .Sum(batch => batch.Projections.Sum(projection => projection.Errors.Count));
            var extraDomains = online.Domains?.ExtraObservedDomains.Count ?? 0;
            var partial = initialRead.Status == ArMms.InitialFcReadExecutionStatus.Partial;
            LastDiscoverySummary =
                $"SCL-assisted MMS: domains={reconciledDomains.Count}, extraOnlineDomains={extraDomains}, " +
                $"FC-roots={initialRead.Plan.Targets.Count}, successfulReads={initialRead.SuccessfulTargetCount}, " +
                $"failedReads={initialRead.FailedTargetCount}, projectedLeaves={initialRead.ProjectedLeafCount}, " +
                $"projectionErrors={projectionErrors}, maxVariablesPerRead={initialRead.Plan.MaximumVariableReferencesPerRead}, fullDiscovery=skipped.";
            LastConnectionFailureKind = string.Empty;
            LastConnectionTechnicalSummary = online.Domains?.Summary ?? online.Message;
            LastErrorMessage = partial
                ? "SCL-assisted association is healthy, but one or more initial FC-root values could not be read or projected. The trusted SCL model was preserved."
                : string.Empty;

            var warnings = preparation.Warnings
                .Concat(extraDomains > 0
                    ? new[] { $"IED exposes {extraDomains} extra online MMS domain(s); they remain evidence only and do not mutate the SCL model." }
                    : Array.Empty<string>())
                .Concat(partial ? new[] { LastErrorMessage } : Array.Empty<string>())
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            totalWatch.Stop();
            return new SclAssistedClientConnectResult
            {
                IsSuccess = true,
                Preparation = preparation,
                Online = online,
                InitialRead = initialRead,
                Warnings = warnings,
                AssociationValidationDuration = associationDuration,
                InitialReadDuration = initialReadDuration,
                TotalDuration = totalWatch.Elapsed,
                Message = LastDiscoverySummary
            };
        }
        catch (OperationCanceledException)
        {
            totalWatch.Stop();
            await _session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            totalWatch.Stop();
            LastConnectionFailureKind = "SCL_ASSISTED_RUNTIME_FAILURE";
            LastErrorMessage = $"SCL-assisted MMS connection failed: {ex.GetType().Name}: {ex.Message}";
            LastConnectionTechnicalSummary = LastErrorMessage;
            await _session.DisposeAsync().ConfigureAwait(false);
            return new SclAssistedClientConnectResult
            {
                Preparation = preparation,
                Warnings = preparation.Warnings,
                AssociationValidationDuration = associationDuration,
                InitialReadDuration = initialReadDuration,
                TotalDuration = totalWatch.Elapsed,
                Message = LastErrorMessage
            };
        }
    }
}
