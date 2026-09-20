using System.Diagnostics;
using AR.Iec61850.Discovery;
using ArMms = AR.Iec61850.Mms;
using ArScl = AR.Iec61850.Scl;

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

internal sealed class TrustedSclInitialValue
{
    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string SclBType { get; init; } = string.Empty;
    public object? Value { get; init; }
}

public sealed partial class NativeIec61850Client
{
    private readonly Dictionary<string, ArMms.MmsDataSetDirectoryResult> _trustedSclDataSetDirectories =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrustedSclInitialValue> _trustedSclInitialValues =
        // IEC 61850 object/member identity is case-sensitive. Edition 2 tracking
        // can legally expose distinct paths such as "...t" and "...T".
        new(StringComparer.Ordinal);
    private bool _trustedSclOnlineAuthorityActive;

    internal bool HasTrustedSclOnlineAuthority => _trustedSclOnlineAuthorityActive;
    internal int TrustedSclInitialValueCount => _trustedSclOnlineAuthorityActive ? _trustedSclInitialValues.Count : 0;

    internal IReadOnlyList<ArMms.MmsReportControlCandidate> TrustedSclReportControls
        => _trustedSclOnlineAuthorityActive && _lastDiscovery is not null
            ? _lastDiscovery.ReportInventory.ReportControls
            : Array.Empty<ArMms.MmsReportControlCandidate>();

    internal bool TryGetTrustedSclDataSetDirectory(
        string dataSetReference,
        out ArMms.MmsDataSetDirectoryResult directory)
    {
        directory = null!;
        if (!_trustedSclOnlineAuthorityActive)
            return false;

        return _trustedSclDataSetDirectories.TryGetValue(
            NormalizeTrustedSclReference(dataSetReference),
            out directory!);
    }

    internal bool TryGetTrustedSclInitialValue(
        string reference,
        out TrustedSclInitialValue value)
    {
        value = null!;
        if (!_trustedSclOnlineAuthorityActive)
            return false;

        return _trustedSclInitialValues.TryGetValue(
            NormalizeTrustedSclReference(reference),
            out value!);
    }

    private void ResetTrustedSclOnlineAuthority()
    {
        _trustedSclOnlineAuthorityActive = false;
        _trustedSclDataSetDirectories.Clear();
        _trustedSclInitialValues.Clear();
    }

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
    /// Opens the trusted-SCL online path: exact SCL association identity, one Domain/VMD
    /// reconciliation, then bounded sequential FC-root initial Reads. Static DataSet and
    /// ReportControl authority is retained from the SCL model in memory; no live directory
    /// discovery is introduced behind this path.
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

        ResetTrustedSclOnlineAuthority();
        await DisposeControlSessionsAsync().ConfigureAwait(false);
        LastErrorMessage = string.Empty;
        LastConnectionFailureKind = string.Empty;
        LastConnectionTechnicalSummary = string.Empty;
        LastDiscoverySummary = string.Empty;
        _lastDiscovery = null;
        _liveModel = null;
        ResetSmartDiscoveryAuthority();
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

            var safeInitialTargets = preparation.InitialReadPlan.Targets
                .Where(target => IsSafeTrustedSclInitialReadFc(target.FunctionalConstraint))
                .ToArray();
            var safeInitialPlan = ArMms.InitialFcReadPlanner.Build(
                safeInitialTargets,
                preparation.InitialReadPlan.MaximumVariableReferencesPerRead);
            if (!safeInitialPlan.IsValid)
            {
                LastConnectionFailureKind = "SCL_INITIAL_FC_PLAN_INVALID";
                LastErrorMessage = string.Join(" | ", safeInitialPlan.Errors);
                LastConnectionTechnicalSummary = LastErrorMessage;
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
            var initialRead = await _session.ExecuteInitialFcReadPlanSmartAsync(
                safeInitialPlan,
                new ArMms.MmsSmartInitialFcReadOptions
                {
                    MaxOutstandingBatches = 8,
                    UnknownPeerMaxOutstandingBatches = 4,
                    PerBatchTimeout = TimeSpan.FromSeconds(5)
                },
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

            var reconciledDomains = online.Domains?.MatchedDomains
                ?? preparation.DomainInventory.ExpectedDomains;
            var domainVariables = reconciledDomains
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(
                    domain => domain,
                    _ => (IReadOnlyList<string>)Array.Empty<string>(),
                    StringComparer.Ordinal);

            _liveModel = preparation.InitialReadDesign.Model;
            var reportInventory = BuildTrustedSclReportInventory(_liveModel);
            var dataSetDirectories = BuildModelDataSetDirectories(_liveModel, "TrustedScl");
            foreach (var directory in dataSetDirectories)
            {
                _trustedSclDataSetDirectories[
                    NormalizeTrustedSclReference(directory.DataSetReference)] = directory;
            }

            var projectedInitialValueKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var leaf in initialRead.Batches
                         .SelectMany(batch => batch.Projections)
                         .SelectMany(projection => projection.Leaves))
            {
                if (string.IsNullOrWhiteSpace(leaf.Reference))
                    continue;

                var normalizedReference = NormalizeTrustedSclReference(leaf.Reference);
                if (string.IsNullOrWhiteSpace(normalizedReference))
                    continue;

                projectedInitialValueKeys.Add(normalizedReference);
                _trustedSclInitialValues[normalizedReference] = new TrustedSclInitialValue
                {
                    Reference = leaf.Reference,
                    FunctionalConstraint = leaf.FunctionalConstraint,
                    SclBType = leaf.SclBType,
                    Value = ConvertTrustedSclInitialValue(leaf.Value)
                };
            }

            var projectedUniqueValues = projectedInitialValueKeys.Count;
            var initialValueCacheLoss = Math.Max(
                0,
                projectedUniqueValues - _trustedSclInitialValues.Count);

            _lastDiscovery = new ArMms.MmsDiscoveryResult
            {
                Snapshot = new ArMms.MmsDiscoverySnapshot
                {
                    DomainVariables = domainVariables,
                    DomainVariableLists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                },
                ReportInventory = reportInventory,
                IedDirectory = new ArMms.MmsIedModelDirectory(Array.Empty<ArMms.MmsFcResolvedPoint>()),
                DataSetDirectories = dataSetDirectories,
                Summary =
                    "Trusted SCL authority: Domain/VMD validation and bounded SCL-guided structured snapshot completed; " +
                    "static DataSet/RCB authority retained locally; full live discovery intentionally skipped."
            };
            LastReportInventory = ToNativeInventory(reportInventory);
            _trustedSclOnlineAuthorityActive = true;

            var projectionErrorDetails = initialRead.Batches
                .SelectMany(batch => batch.Projections)
                .SelectMany(projection => projection.Errors.Select(error =>
                    $"{projection.Target.MmsReference}: {error}"))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var projectionErrors = projectionErrorDetails.Length;
            var projectionErrorSamples = projectionErrorDetails
                .Take(8)
                .ToArray();
            var extraDomains = online.Domains?.ExtraObservedDomains.Count ?? 0;
            var dataObjectScopedTargets = initialRead.Plan.Targets.Count(target => target.IsDataObjectScoped);
            var fcRootTargets = initialRead.Plan.Targets.Count - dataObjectScopedTargets;
            var partial = initialRead.Status == ArMms.InitialFcReadExecutionStatus.Partial ||
                          initialValueCacheLoss > 0;
            LastDiscoverySummary =
                $"SCL-assisted MMS: domains={reconciledDomains.Count}, extraOnlineDomains={extraDomains}, " +
                $"initialTargets={initialRead.Plan.Targets.Count}, fcRootTargets={fcRootTargets}, doScopedTargets={dataObjectScopedTargets}, " +
                $"successfulReads={initialRead.SuccessfulTargetCount}, failedReads={initialRead.FailedTargetCount}, projectedLeaves={initialRead.ProjectedLeafCount}, " +
                $"projectedUniqueValues={projectedUniqueValues}, initialValueCache={_trustedSclInitialValues.Count}, cacheLoss={initialValueCacheLoss}, " +
                $"projectionErrors={projectionErrors}, maxVariablesPerRead={initialRead.Plan.MaximumVariableReferencesPerRead}, " +
                $"staticDataSets={dataSetDirectories.Count}, staticRCB={reportInventory.ReportControls.Count}, fullDiscovery=skipped." +
                (projectionErrorSamples.Length == 0
                    ? string.Empty
                    : $" projectionErrorSamples=[{string.Join(" || ", projectionErrorSamples)}]");
            LastConnectionFailureKind = string.Empty;
            LastConnectionTechnicalSummary = online.Domains?.Summary ?? online.Message;
            LastErrorMessage = initialValueCacheLoss > 0
                ? $"SCL-assisted association is healthy, but exact case-sensitive value caching lost {initialValueCacheLoss} projected value(s). The trusted SCL model was preserved and semantic convergence remains incomplete."
                : partial
                    ? "SCL-assisted association is healthy, but one or more initial SCL-guided values could not be read or projected. The trusted SCL model was preserved."
                    : string.Empty;

            var warnings = preparation.Warnings
                .Concat(extraDomains > 0
                    ? new[] { $"IED exposes {extraDomains} extra online MMS domain(s); they remain evidence only and do not mutate the SCL model." }
                    : Array.Empty<string>())
                .Concat(partial ? new[] { LastErrorMessage } : Array.Empty<string>())
                .Concat(projectionErrorSamples.Select(error => $"SCL initial projection: {error}"))
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
            ResetTrustedSclOnlineAuthority();
            totalWatch.Stop();
            await _session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            ResetTrustedSclOnlineAuthority();
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

    private static ArMms.MmsReportInventory BuildTrustedSclReportInventory(
        LiveIedModelDiscoveryDocument model)
    {
        var inventory = new ArMms.MmsReportInventory();
        foreach (var dataSet in model.DataSets)
        {
            var (domain, itemName) = ParseTrustedSclDataSetReference(
                dataSet.Reference,
                dataSet.Domain,
                dataSet.LogicalNode,
                dataSet.Name);
            inventory.DataSets.Add(new ArMms.MmsDataSetCandidate
            {
                Domain = domain,
                LogicalNode = dataSet.LogicalNode,
                Name = dataSet.Name,
                Reference = dataSet.Reference,
                RawMmsName = itemName
            });
        }

        foreach (var report in model.ReportControls)
        {
            var reference = ConcreteFirstStaticRcbReference(report.Reference, report.Indexed);
            var candidate = new ArMms.MmsReportControlCandidate
            {
                Domain = report.Domain,
                LogicalNode = report.LogicalNode,
                FunctionalConstraint = report.Buffered ? "BR" : "RP",
                Name = StaticRcbLeaf(reference),
                Reference = reference,
                Buffered = report.Buffered,
                DataSetReference = report.DataSetReference,
                DataSetProbeState = ArMms.MmsRcbDataSetProbeState.NotAttempted,
                DataSetProbeMessage = "Trusted SCL authority; no live DataSet probe performed.",
                ReportId = report.ReportId,
                ConfRev = report.ConfRev,
                IntegrityPeriodMs = report.IntegrityPeriodMs,
                EnabledState = report.EnabledState,
                ReservationState = report.ReservationState,
                ReservationTimeSeconds = report.ReservationTimeSeconds,
                BufferTimeMs = report.BufferTimeMs,
                TriggerOptions = report.TriggerOptions,
                OptionalFields = report.OptionalFields,
                Status = "TrustedSclAuthority"
            };

            candidate.Attributes.AddRange(report.Buffered
                ? new[]
                {
                    "RptID", "RptEna", "DatSet", "ConfRev", "OptFlds", "BufTm", "SqNum",
                    "TrgOps", "IntgPd", "GI", "PurgeBuf", "EntryID", "TimeOfEntry", "ResvTms", "Owner"
                }
                : new[]
                {
                    "RptID", "RptEna", "Resv", "DatSet", "ConfRev", "OptFlds", "BufTm",
                    "SqNum", "TrgOps", "IntgPd", "GI", "Owner"
                });
            inventory.ReportControls.Add(candidate);
        }

        return inventory;
    }

    private static IReadOnlyList<ArMms.MmsDataSetDirectoryResult> BuildModelDataSetDirectories(
        LiveIedModelDiscoveryDocument model,
        string source)
        => model.DataSets
            .Select(dataSet =>
            {
                var (domain, itemName) = ParseTrustedSclDataSetReference(
                    dataSet.Reference,
                    dataSet.Domain,
                    dataSet.LogicalNode,
                    dataSet.Name);
                var members = dataSet.Members
                    .OrderBy(member => member.Index)
                    .Select(member => BuildModelDataSetMember(member, domain, source))
                    .ToArray();
                return new ArMms.MmsDataSetDirectoryResult
                {
                    IsSuccess = members.Length > 0,
                    DataSetReference = dataSet.Reference,
                    Domain = domain,
                    DataSetMmsName = itemName,
                    IsDeletable = dataSet.IsDeletable,
                    Members = members,
                    Message =
                        $"{source} DataSet authority: {dataSet.Reference} has {members.Length} ordered member(s); model order is preserved."
                };
            })
            .ToArray();

    private static ArMms.MmsDataSetDirectoryMember BuildModelDataSetMember(
        LiveIedDataSetMemberModel member,
        string fallbackDomain,
        string source)
    {
        var mmsReference = member.MmsReference?.Trim() ?? string.Empty;
        var slash = mmsReference.IndexOf('/');
        var domain = slash > 0 ? mmsReference[..slash] : fallbackDomain;
        var itemName = slash > 0 && slash + 1 < mmsReference.Length
            ? mmsReference[(slash + 1)..]
            : BuildMmsItemNameFromUserReference(member.Reference, member.FunctionalConstraint);
        var tokens = itemName.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var logicalNode = tokens.FirstOrDefault() ?? string.Empty;
        var dataObjectPath = tokens.Length > 2
            ? string.Join('.', tokens.Skip(2))
            : string.Empty;

        return new ArMms.MmsDataSetDirectoryMember
        {
            Domain = domain,
            MmsItemName = itemName,
            UserReference = member.Reference,
            FunctionalConstraint = member.FunctionalConstraint,
            LogicalNode = logicalNode,
            DataObjectPath = dataObjectPath,
            Source = source,
            Confidence = 100
        };
    }

    private static (string Domain, string ItemName) ParseTrustedSclDataSetReference(
        string reference,
        string fallbackDomain,
        string logicalNode,
        string name)
    {
        var normalized = reference?.Trim() ?? string.Empty;
        var slash = normalized.IndexOf('/');
        var domain = slash > 0 ? normalized[..slash] : fallbackDomain;
        var tail = slash > 0 && slash + 1 < normalized.Length
            ? normalized[(slash + 1)..]
            : string.Empty;
        var itemName = string.IsNullOrWhiteSpace(tail)
            ? $"{(string.IsNullOrWhiteSpace(logicalNode) ? "LLN0" : logicalNode)}${name}"
            : tail.Replace('.', '$');
        if (!itemName.Contains('$', StringComparison.Ordinal))
            itemName = $"LLN0${itemName}";
        return (domain, itemName);
    }

    private static string BuildMmsItemNameFromUserReference(string reference, string functionalConstraint)
    {
        var normalized = reference?.Trim() ?? string.Empty;
        var slash = normalized.IndexOf('/');
        var tail = slash >= 0 && slash + 1 < normalized.Length
            ? normalized[(slash + 1)..]
            : normalized;
        var parts = tail.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return string.Empty;
        var logicalNode = parts[0];
        var dataPath = parts.Length > 1 ? string.Join('$', parts.Skip(1)) : string.Empty;
        return string.IsNullOrWhiteSpace(dataPath)
            ? $"{logicalNode}${functionalConstraint}"
            : $"{logicalNode}${functionalConstraint}${dataPath}";
    }

    private static string ConcreteFirstStaticRcbReference(string reference, bool indexed)
    {
        var normalized = reference?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized) || !indexed)
            return normalized;

        var separator = Math.Max(normalized.LastIndexOf('$'), normalized.LastIndexOf('.'));
        var leaf = separator >= 0 ? normalized[(separator + 1)..] : normalized;
        return leaf.Length > 0 && !char.IsDigit(leaf[^1])
            ? normalized + "01"
            : normalized;
    }

    private static object? ConvertTrustedSclInitialValue(ArMms.MmsDataValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Kind switch
        {
            ArMms.MmsDataKind.Boolean or
            ArMms.MmsDataKind.Integer or
            ArMms.MmsDataKind.Unsigned or
            ArMms.MmsDataKind.FloatingPoint or
            ArMms.MmsDataKind.VisibleString or
            ArMms.MmsDataKind.MmsString or
            ArMms.MmsDataKind.UtcTime => value.Value,
            ArMms.MmsDataKind.BitString => FormatTrustedSclBitString(value.RawValue),
            ArMms.MmsDataKind.OctetString or ArMms.MmsDataKind.BinaryTime =>
                Convert.ToHexString(value.RawValue.ToArray()),
            _ => value.Value
        };
    }

    private static string FormatTrustedSclBitString(IReadOnlyList<byte> raw)
    {
        if (raw.Count == 0)
            return "bits(00,unused=0)";

        var unusedBits = raw[0];
        var data = raw.Skip(1).ToArray();
        var hex = data.Length == 0 ? "00" : Convert.ToHexString(data);
        return $"bits({hex},unused={unusedBits})";
    }

    private static string StaticRcbLeaf(string reference)
    {
        var separator = Math.Max(reference.LastIndexOf('$'), reference.LastIndexOf('.'));
        return separator >= 0 && separator + 1 < reference.Length
            ? reference[(separator + 1)..]
            : reference;
    }

    private static bool IsSafeTrustedSclInitialReadFc(string? functionalConstraint)
        => (functionalConstraint ?? string.Empty).Trim().ToUpperInvariant() is
            "ST" or "MX" or "SP" or "SV" or "CF" or "DC" or "EX" or "BL" or "OR" or "SR";

    private static string NormalizeTrustedSclReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace((char)36, '.');
}
