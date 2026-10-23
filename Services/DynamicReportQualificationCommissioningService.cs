using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed class DynamicReportQualificationCandidateSummary
{
    public int SelectedSignalCount { get; init; }
    public int ScalarClassACount { get; init; }
    public int ExactResolvedCount { get; init; }
    public int DirectReadValidatedCount { get; init; }
    public IReadOnlyList<string> Rejections { get; init; } = Array.Empty<string>();
}

internal sealed class DynamicReportQualificationCommissioningResult
{
    public bool IsSuccess { get; init; }
    public bool IsBlocked { get; init; }
    public string Summary { get; init; } = string.Empty;
    public ArMms.MmsDynamicReportIedIdentity? Identity { get; init; }
    public DynamicReportQualificationCandidateSummary Candidates { get; init; } = new();
    public ArMms.MmsDynamicDataSetQualificationCoordinatorResult? Coordinator { get; init; }
    public ArMms.MmsDynamicReportQualificationProfile? SavedProfile { get; init; }
    public string ProfilePath { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

internal sealed class DynamicReportQualificationCommissioningService
{
    private const int MaximumClassACandidates = 64;
    private static readonly TimeSpan AuxiliaryAssociationTimeout = TimeSpan.FromSeconds(10);
    private readonly DynamicReportQualificationProfileStore _profileStore;

    public DynamicReportQualificationCommissioningService(
        DynamicReportQualificationProfileStore? profileStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
    }

    public async Task<DynamicReportQualificationCommissioningResult> RunAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        var evidence = new List<string>();
        ArMms.MmsDynamicReportIedIdentity identity;
        try
        {
            identity = DynamicReportQualificationIdentity.Build(device, fullModelSignals);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Blocked($"G2 qualification identity preflight failed: {ex.Message}", evidence);
        }

        evidence.Add($"G2.3 identity stableKey={identity.StableIdentityKey}; fingerprint={identity.ModelFingerprint}; profileRevision={TextOrDash(identity.ProfileRevision)}");

        var existing = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        evidence.Add($"G2.3 persisted profile: exists={existing.Exists}; valid={existing.IsValid}; reason={existing.Reason}");
        if (existing.IsValid &&
            existing.Profile is not null &&
            existing.Profile.State > ArMms.MmsDynamicReportQualificationState.EnvelopeQualified)
        {
            return new DynamicReportQualificationCommissioningResult
            {
                IsBlocked = true,
                Summary = $"Existing identity-compatible dynamic qualification profile is already {existing.Profile.State}; G2.3 commissioning will not downgrade advanced evidence.",
                Identity = identity,
                SavedProfile = existing.Profile,
                ProfilePath = existing.FilePath,
                EvidenceLines = evidence
            };
        }

        var selectedCount = fullModelSignals.Count(signal => signal.IsSelected);
        var scalarSignals = SelectScalarClassASignals(fullModelSignals)
            .Take(MaximumClassACandidates)
            .ToArray();
        if (scalarSignals.Length == 0)
        {
            return new DynamicReportQualificationCommissioningResult
            {
                IsBlocked = true,
                Summary = "No selected Class-A scalar ST/MX process points are available for explicit dynamic DataSet qualification.",
                Identity = identity,
                Candidates = new DynamicReportQualificationCandidateSummary
                {
                    SelectedSignalCount = selectedCount,
                    ScalarClassACount = 0
                },
                EvidenceLines = evidence
            };
        }

        evidence.Add($"G2.3 Class-A prefilter selected={selectedCount}; scalarCandidates={scalarSignals.Length}; hardCandidateCeiling={MaximumClassACandidates}");

        await using var auxiliary = new ArMms.MmsClientSession();
        try
        {
            await auxiliary.ConnectAsync(
                device.IpAddress,
                device.Port,
                AuxiliaryAssociationTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"G2.3 auxiliary association failed: {ex.GetType().Name}: {ex.Message}");
            return new DynamicReportQualificationCommissioningResult
            {
                IsBlocked = true,
                Summary = "The IED did not accept the isolated auxiliary MMS association. No dynamic qualification mutation was attempted. Keep production monitoring unchanged; qualification requires an explicit commissioning window if the device supports only one association.",
                Identity = identity,
                Candidates = new DynamicReportQualificationCandidateSummary
                {
                    SelectedSignalCount = selectedCount,
                    ScalarClassACount = scalarSignals.Length
                },
                EvidenceLines = evidence
            };
        }

        evidence.Add($"G2.3 auxiliary association ready: state={auxiliary.State}; handshake={TextOrDash(auxiliary.LastHandshakeMessage)}");

        ArMms.MmsDiscoveryResult discovery;
        try
        {
            discovery = await auxiliary.DiscoverAsync(
                probeReportAttributes: false,
                maxReportAttributeProbes: 0,
                cancellationToken: cancellationToken,
                readDataSetDirectories: false,
                maxDataSetDirectoryReads: 0).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"G2.3 auxiliary discovery failed: {ex.GetType().Name}: {ex.Message}");
            return new DynamicReportQualificationCommissioningResult
            {
                IsBlocked = true,
                Summary = "Auxiliary MMS discovery failed before qualification. No NamedVariableList qualification was started.",
                Identity = identity,
                Candidates = new DynamicReportQualificationCandidateSummary
                {
                    SelectedSignalCount = selectedCount,
                    ScalarClassACount = scalarSignals.Length
                },
                EvidenceLines = evidence
            };
        }

        evidence.Add($"G2.3 auxiliary discovery: {discovery.Summary}");

        var rejected = new List<string>();
        var exactResolved = new List<(SignalDefinition Signal, ArMms.MmsFcResolvedPoint Point)>();
        foreach (var signal in scalarSignals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = discovery.IedDirectory
                .FindByUserReference(signal.ObjectReference)
                .Where(point => string.IsNullOrWhiteSpace(signal.FunctionalConstraint) ||
                                point.FunctionalConstraint.Equals(signal.FunctionalConstraint.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(point => !point.IsControlAttribute && !point.IsReportAttribute)
                .ToArray();

            if (matches.Length != 1)
            {
                rejected.Add($"{signal.ObjectReference}: exact live MMS mapping count={matches.Length}, expected=1.");
                continue;
            }

            exactResolved.Add((signal, matches[0]));
        }

        evidence.Add($"G2.3 exact Class-A mapping: resolved={exactResolved.Count}/{scalarSignals.Length}; rejected={rejected.Count}");
        if (exactResolved.Count == 0)
        {
            return new DynamicReportQualificationCommissioningResult
            {
                IsBlocked = true,
                Summary = "No Class-A candidate has exactly one literal live MMS mapping. Qualification remains blocked; fuzzy IEC reference matching is not allowed.",
                Identity = identity,
                Candidates = CandidateSummary(selectedCount, scalarSignals.Length, 0, 0, rejected),
                EvidenceLines = evidence.Concat(rejected.Take(20)).ToArray()
            };
        }

        var directReadValidated = new List<ArMms.MmsObjectReference>();
        foreach (var candidate in exactResolved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var objectReference = candidate.Point.ToObjectReference();
            var read = await auxiliary.ReadSingleVariableAsync(objectReference, cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess)
            {
                rejected.Add($"{candidate.Signal.ObjectReference}: direct MMS read rejected for qualification: {read.Message}");
                if (!auxiliary.IsMmsInitiated)
                {
                    evidence.Add("G2.3 direct-read validation lost the auxiliary association; qualification stopped before NamedVariableList mutation.");
                    break;
                }
                continue;
            }

            directReadValidated.Add(objectReference);
        }

        evidence.Add($"G2.3 direct MMS validation: passed={directReadValidated.Count}/{exactResolved.Count}; association={auxiliary.State}");
        if (!auxiliary.IsMmsInitiated)
        {
            return new DynamicReportQualificationCommissioningResult
            {
                IsBlocked = true,
                Summary = "Auxiliary association was lost during direct MMS-read candidate validation. No dynamic DataSet qualification mutation was started.",
                Identity = identity,
                Candidates = CandidateSummary(selectedCount, scalarSignals.Length, exactResolved.Count, directReadValidated.Count, rejected),
                EvidenceLines = evidence.Concat(rejected.Take(20)).ToArray()
            };
        }

        if (directReadValidated.Count == 0)
        {
            return new DynamicReportQualificationCommissioningResult
            {
                IsBlocked = true,
                Summary = "No exact Class-A candidate passed direct MMS read validation; NamedVariableList qualification was not attempted.",
                Identity = identity,
                Candidates = CandidateSummary(selectedCount, scalarSignals.Length, exactResolved.Count, 0, rejected),
                EvidenceLines = evidence.Concat(rejected.Take(20)).ToArray()
            };
        }

        var dataSetReference = BuildTemporaryDataSetReference(directReadValidated[0].Domain);
        evidence.Add($"G2.3 qualification dataset={dataSetReference}; candidates={directReadValidated.Count}; mode=ExplicitCommissioning; productionPlannerUntouched=true");

        ArMms.MmsDynamicDataSetQualificationCoordinatorResult coordinator;
        try
        {
            coordinator = await auxiliary.RunDynamicDataSetQualificationCommissioningAsync(
                dataSetReference,
                directReadValidated,
                new ArMms.MmsDynamicDataSetQualificationCoordinatorOptions
                {
                    ExecutionMode = ArMms.MmsDynamicDataSetQualificationExecutionMode.ExplicitCommissioning,
                    MaxAttempts = 16,
                    LocalizeFailedBatch = true,
                    Ladder = new ArMms.MmsDynamicDataSetQualificationLadderOptions
                    {
                        Milestones = [1, 4, 8, 16, 32],
                        ApplicationSafetyMemberLimit = MaximumClassACandidates,
                        IncludeTerminalCandidateCount = false
                    },
                    Probe = new ArMms.MmsDynamicDataSetQualificationProbeOptions
                    {
                        ApplicationSafetyMemberLimit = MaximumClassACandidates,
                        RejectKnownNegotiatedPduOverflow = true
                    }
                },
                discovery.IedDirectory,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or ArgumentException)
        {
            evidence.Add($"G2.3 coordinator exception: {ex.GetType().Name}: {ex.Message}");
            return new DynamicReportQualificationCommissioningResult
            {
                IsSuccess = false,
                Summary = "Explicit qualification stopped on an exception. Production dynamic reporting remains quarantined.",
                Identity = identity,
                Candidates = CandidateSummary(selectedCount, scalarSignals.Length, exactResolved.Count, directReadValidated.Count, rejected),
                EvidenceLines = evidence.Concat(rejected.Take(20)).ToArray()
            };
        }

        evidence.Add($"G2.3 coordinator: {coordinator.Summary}");
        foreach (var attempt in coordinator.Attempts)
        {
            evidence.Add(
                $"G2.3 attempt {attempt.AttemptId}: members={attempt.MemberCount}; success={attempt.IsQualificationSuccess}; " +
                $"requestBytes={attempt.DefineRequestByteCount}; maxPdu={attempt.NegotiatedMaxMmsPduSize?.ToString() ?? "?"}; " +
                $"associationSurvived={attempt.AssociationSurvived}; cleanup={attempt.CleanupSucceeded}; stage={attempt.FailureStage}");
        }
        evidence.AddRange(coordinator.Warnings.Select(warning => "G2.3 warning: " + warning));

        if (coordinator.RequiresFreshAssociation ||
            !coordinator.Assessment.HasMultiMemberEnvelopeCandidate ||
            string.IsNullOrWhiteSpace(coordinator.EnvelopeCandidateAttemptId))
        {
            return new DynamicReportQualificationCommissioningResult
            {
                IsSuccess = false,
                Summary = coordinator.RequiresFreshAssociation
                    ? "Qualification stopped because association continuity or cleanup was not proven. No profile was advanced; use a fresh explicit commissioning association for the next attempt."
                    : "Qualification completed without a cleanup-safe multi-member envelope. Production dynamic reporting remains quarantined and no EnvelopeQualified profile was saved.",
                Identity = identity,
                Candidates = CandidateSummary(selectedCount, scalarSignals.Length, exactResolved.Count, directReadValidated.Count, rejected),
                Coordinator = coordinator,
                EvidenceLines = evidence.Concat(rejected.Take(20)).ToArray()
            };
        }

        var acceptedEnvelope = ArMms.MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope(
            coordinator.Assessment,
            coordinator.EnvelopeCandidateAttemptId);
        var fieldEvidenceId = $"arsas-g2.3-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        var profile = ArMms.MmsDynamicReportQualificationProfilePolicy.CreateEnvelopeQualifiedProfile(
            identity,
            acceptedEnvelope,
            coordinator.Assessment,
            capacityEvidence: null,
            sourceEvidenceId: fieldEvidenceId,
            nowUtc: DateTimeOffset.UtcNow);

        await _profileStore.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
        var profilePath = _profileStore.GetProfilePath(identity);
        evidence.Add($"G2.3 profile saved: state={profile.State}; members={profile.ProvenSafeMemberCount}; requestBytes={profile.ProvenSafeDefineRequestByteCount}; path={profilePath}");
        evidence.Add("G2.3 safety: EnvelopeQualified is NOT RcbActivationProven, NOT InformationReportProven, and NOT ProductionEligible.");

        return new DynamicReportQualificationCommissioningResult
        {
            IsSuccess = true,
            Summary = $"G2.3 qualification produced an identity-bound EnvelopeQualified profile: safe exact envelope={profile.ProvenSafeMemberCount} member(s), Define request={profile.ProvenSafeDefineRequestByteCount} byte(s). Automatic production dynamic reporting remains OFF until G2.4/G2.5/G2.6 evidence is proven.",
            Identity = identity,
            Candidates = CandidateSummary(selectedCount, scalarSignals.Length, exactResolved.Count, directReadValidated.Count, rejected),
            Coordinator = coordinator,
            SavedProfile = profile,
            ProfilePath = profilePath,
            EvidenceLines = evidence.Concat(rejected.Take(20)).ToArray()
        };
    }

    internal static IReadOnlyList<SignalDefinition> SelectScalarClassASignals(
        IReadOnlyList<SignalDefinition> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        return signals
            .Where(signal => signal.IsSelected)
            .Where(signal => !signal.IsControlSignal)
            .Where(signal => IsSafeProcessFunctionalConstraint(signal.FunctionalConstraint))
            .Where(signal => IsKnownScalarType(signal.DataType))
            .Where(signal => IsPrimaryProcessReference(signal.ObjectReference))
            .Where(signal => !string.IsNullOrWhiteSpace(signal.ObjectReference))
            .GroupBy(signal => signal.ObjectReference.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(signal => signal.ObjectReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool IsKnownScalarType(string? dataType)
    {
        var normalized = (dataType ?? string.Empty)
            .Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (normalized.Contains("STRUCT", StringComparison.Ordinal) ||
            normalized.Contains("ARRAY", StringComparison.Ordinal) ||
            normalized.Contains("OCTET", StringComparison.Ordinal) ||
            normalized.Contains("VISIBLE", StringComparison.Ordinal) ||
            normalized.Contains("UNICODE", StringComparison.Ordinal) ||
            normalized.Contains("TIMESTAMP", StringComparison.Ordinal) ||
            normalized.Contains("QUALITY", StringComparison.Ordinal))
            return false;

        return normalized.Contains("BOOL", StringComparison.Ordinal) ||
               normalized.Contains("BOOLEAN", StringComparison.Ordinal) ||
               normalized.StartsWith("INT", StringComparison.Ordinal) ||
               normalized.StartsWith("UINT", StringComparison.Ordinal) ||
               normalized.Contains("INTEGER", StringComparison.Ordinal) ||
               normalized.Contains("FLOAT", StringComparison.Ordinal) ||
               normalized.Contains("DOUBLE", StringComparison.Ordinal) ||
               normalized.Contains("ENUM", StringComparison.Ordinal);
    }

    internal static bool IsSafeProcessFunctionalConstraint(string? functionalConstraint)
        => string.Equals(functionalConstraint?.Trim(), "ST", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(functionalConstraint?.Trim(), "MX", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPrimaryProcessReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;
        var leaf = reference.Trim()
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? string.Empty;
        return !leaf.Equals("q", StringComparison.OrdinalIgnoreCase) &&
               !leaf.Equals("t", StringComparison.OrdinalIgnoreCase) &&
               !leaf.Equals("quality", StringComparison.OrdinalIgnoreCase) &&
               !leaf.Equals("timestamp", StringComparison.OrdinalIgnoreCase);
    }

    private static DynamicReportQualificationCandidateSummary CandidateSummary(
        int selected,
        int scalar,
        int exact,
        int reads,
        IReadOnlyList<string> rejections)
        => new()
        {
            SelectedSignalCount = selected,
            ScalarClassACount = scalar,
            ExactResolvedCount = exact,
            DirectReadValidatedCount = reads,
            Rejections = rejections.ToArray()
        };

    private static string BuildTemporaryDataSetReference(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new InvalidOperationException("The first qualified MMS member has no logical-device domain for a temporary DataSet.");
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"{domain.Trim()}/LLN0.ARQ{suffix}";
    }

    private static DynamicReportQualificationCommissioningResult Blocked(
        string summary,
        IReadOnlyList<string> evidence)
        => new()
        {
            IsBlocked = true,
            Summary = summary,
            EvidenceLines = evidence.ToArray()
        };

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
