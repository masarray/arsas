using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed class DynamicReportStimulusWitnessTransition
{
    public int Index { get; init; }
    public string MemberReference { get; init; } = string.Empty;
    public string BeforeValue { get; init; } = string.Empty;
    public string AfterValue { get; init; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; init; }
}

internal sealed class DynamicReportStimulusWitnessResult
{
    public bool ArmedObserved { get; init; }
    public bool BaselineCaptured { get; init; }
    public bool ChangeObserved { get; init; }
    public bool AssociationHealthy { get; init; }
    public int SampleCycles { get; init; }
    public int ReadFailures { get; init; }
    public IReadOnlyList<string> BaselineValues { get; init; } = Array.Empty<string>();
    public IReadOnlyList<DynamicReportStimulusWitnessTransition> Transitions { get; init; } = Array.Empty<DynamicReportStimulusWitnessTransition>();
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
    public string Summary { get; init; } = string.Empty;
}

internal sealed class DynamicReportStimulusWitnessCommissioningResult
{
    public bool IsSuccess { get; init; }
    public bool StimulusWitnessProven { get; init; }
    public bool ReportCorrelationProven { get; init; }
    public IReadOnlyList<int> CorrelatedIndexes { get; init; } = Array.Empty<int>();
    public string Summary { get; init; } = string.Empty;
    public DynamicReportSpontaneousDataChangeCommissioningResult CoreResult { get; init; } = new();
    public DynamicReportStimulusWitnessResult Witness { get; init; } = new();
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// G2.5-A1 diagnostic wrapper around the physical G2.5-A dchg proof.
///
/// The existing reporting association remains unchanged and still performs the exact
/// one-URCB dchg-only / NO-GI proof. A second MMS association is read-only and samples
/// only the exact G2.4-proven members. It never reads or writes RCB attributes, never
/// defines/deletes a DataSet, and never sends GI. Its sole purpose is to prove whether
/// the operator stimulus actually changed one of the qualified members while G2.5-A
/// was armed.
/// </summary>
internal sealed class DynamicReportStimulusWitnessCommissioningService
{
    private static readonly TimeSpan AssociationTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan WitnessWindow = TimeSpan.FromSeconds(55);
    internal static readonly TimeSpan WitnessInterCycleDelay = TimeSpan.FromMilliseconds(50);
    internal const string ArmedMarker = "G2.5-A ARMED — NO GI";
    internal const string WitnessReadyMarker = "G2.5-A1 WITNESS READY";

    private readonly DynamicReportQualificationProfileStore _profileStore;

    public DynamicReportStimulusWitnessCommissioningService(
        DynamicReportQualificationProfileStore? profileStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
    }

    public async Task<DynamicReportStimulusWitnessCommissioningResult> RunAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        var evidence = new List<string>
        {
            "G2.5-A1 contract: reporting association is unchanged G2.5-A dchg-only/NO-GI; witness association is READ ONLY and samples only the exact proven members.",
            "G2.5-A1 operator contract: do not stimulate on the first ARMED message; wait until G2.5-A1 WITNESS READY is shown."
        };

        ArMms.MmsDynamicReportIedIdentity identity;
        try
        {
            identity = DynamicReportQualificationIdentity.Build(device, fullModelSignals);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return FailedBeforeCore("G2.5-A1 identity preflight failed: " + ex.Message, evidence);
        }

        var loaded = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        if (!loaded.IsValid || loaded.Profile is null ||
            loaded.Profile.State != ArMms.MmsDynamicReportQualificationState.InformationReportProven ||
            loaded.Profile.RcbActivationProof?.IsSuccess != true)
        {
            evidence.Add($"G2.5-A1 profile gate: exists={loaded.Exists}; valid={loaded.IsValid}; state={loaded.Profile?.State.ToString() ?? "-"}; reason={loaded.Reason}");
            return FailedBeforeCore("G2.5-A1 requires the identity-compatible InformationReportProven G2.4 profile.", evidence);
        }

        var memberReferences = loaded.Profile.RcbActivationProof.MemberReferences.ToArray();
        if (memberReferences.Length == 0)
            return FailedBeforeCore("G2.5-A1 profile has no exact proven member sequence.", evidence);

        evidence.Add($"G2.5-A1 target: members={memberReferences.Length}; stableKey={identity.StableIdentityKey}; profileState={loaded.Profile.State}");
        evidence.Add("G2.5-A1 exact members: " + string.Join(" | ", memberReferences));

        var armed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var witnessCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var relay = new RelayProgress(text =>
        {
            progress?.Report(text);
            if (text.Contains(ArmedMarker, StringComparison.OrdinalIgnoreCase))
                armed.TrySetResult(true);
        });

        var witnessTask = RunWitnessAsync(
            device,
            memberReferences,
            armed.Task,
            progress,
            witnessCancellation.Token);

        var coreService = new DynamicReportSpontaneousDataChangeCommissioningService(_profileStore);
        DynamicReportSpontaneousDataChangeCommissioningResult coreResult;
        try
        {
            coreResult = await coreService.RunAsync(
                device,
                fullModelSignals,
                relay,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!armed.Task.IsCompleted)
                witnessCancellation.Cancel();
        }

        DynamicReportStimulusWitnessResult witnessResult;
        try
        {
            witnessResult = await witnessTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            witnessResult = new DynamicReportStimulusWitnessResult
            {
                Summary = "G2.5-A1 witness was cancelled because the core G2.5-A attempt never reached ARMED.",
                EvidenceLines = ["G2.5-A1 witness: core attempt did not reach ARMED; no stimulus conclusion is possible."]
            };
        }

        evidence.AddRange(coreResult.EvidenceLines.Select(line => "CORE/" + line));
        evidence.AddRange(witnessResult.EvidenceLines);

        var changedIndexes = witnessResult.Transitions.Select(item => item.Index).Distinct().OrderBy(index => index).ToArray();
        var correlatedIndexes = coreResult.IncludedIndexes.Intersect(changedIndexes).Distinct().OrderBy(index => index).ToArray();
        var correlationProven = coreResult.SpontaneousDataChangeProven && correlatedIndexes.Length > 0;
        var witnessProven = witnessResult.BaselineCaptured && witnessResult.ChangeObserved && witnessResult.AssociationHealthy;
        var success = coreResult.IsSuccess && correlationProven;

        string diagnosis;
        if (success)
        {
            diagnosis = $"G2.5-A/A1 PASS: read-only witness observed a real qualified-member transition and the spontaneous data-change InformationReport included the same DataSet index(es) [{string.Join(",", correlatedIndexes)}].";
        }
        else if (!coreResult.ActivationProven)
        {
            diagnosis = "G2.5-A1 is inconclusive because the core dchg-only report activation did not reach a proven ARMED state.";
        }
        else if (!witnessResult.BaselineCaptured || !witnessResult.AssociationHealthy)
        {
            diagnosis = "G2.5-A1 witness is inconclusive because the independent read-only association could not maintain a reliable baseline/sample window.";
        }
        else if (!witnessResult.ChangeObserved && !coreResult.SpontaneousDataChangeProven)
        {
            diagnosis = "G2.5-A1 diagnosis: no qualified-member transition was observed during the armed window and no spontaneous report arrived. The physical stimulus is not yet proven to touch the 8-member envelope.";
        }
        else if (witnessResult.ChangeObserved && !coreResult.SpontaneousDataChangeProven)
        {
            diagnosis = $"G2.5-A1 diagnosis: stimulus WAS witnessed on qualified DataSet index(es) [{string.Join(",", changedIndexes)}], but no valid spontaneous dchg InformationReport arrived. This isolates the next investigation to IED dchg/report emission or receive-path evidence, not stimulus ambiguity.";
        }
        else if (coreResult.SpontaneousDataChangeProven && !correlationProven)
        {
            diagnosis = "G2.5-A core report proof passed, but the independent witness did not observe a transition on any index included by that report; G2.5-A1 correlation remains unproven.";
        }
        else
        {
            diagnosis = "G2.5-A1 did not close the stimulus/report correlation gate.";
        }

        evidence.Add($"G2.5-A1 combined: coreSuccess={coreResult.IsSuccess}; activation={coreResult.ActivationProven}; coreDchg={coreResult.SpontaneousDataChangeProven}; witnessBaseline={witnessResult.BaselineCaptured}; witnessChange={witnessResult.ChangeObserved}; witnessHealthy={witnessResult.AssociationHealthy}; witnessChanged=[{string.Join(",", changedIndexes)}]; reportIncluded=[{string.Join(",", coreResult.IncludedIndexes)}]; correlated=[{string.Join(",", correlatedIndexes)}]; success={success}");
        evidence.Add("G2.5-A1 diagnosis: " + diagnosis);
        evidence.Add("G2.5-A1 safety: witness performs no RCB/DataSet writes and does not alter the persisted InformationReportProven profile or production policy.");

        return new DynamicReportStimulusWitnessCommissioningResult
        {
            IsSuccess = success,
            StimulusWitnessProven = witnessProven,
            ReportCorrelationProven = correlationProven,
            CorrelatedIndexes = correlatedIndexes,
            Summary = diagnosis + " Production automatic dynamic reporting remains OFF.",
            CoreResult = coreResult,
            Witness = witnessResult,
            EvidenceLines = evidence.ToArray()
        };
    }

    private async Task<DynamicReportStimulusWitnessResult> RunWitnessAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<string> memberReferences,
        Task armedSignal,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var evidence = new List<string>();
        await using var witness = new ArMms.MmsClientSession();
        try
        {
            await witness.ConnectAsync(device.IpAddress, device.Port, AssociationTimeout, cancellationToken).ConfigureAwait(false);
            evidence.Add($"G2.5-A1 witness association ready: state={witness.State}; localTcpAddress={TextOrDash(witness.LocalTcpAddress)}; READ-ONLY=true");

            var discovery = await witness.DiscoverAsync(
                probeReportAttributes: false,
                maxReportAttributeProbes: 0,
                cancellationToken: cancellationToken,
                readDataSetDirectories: false,
                maxDataSetDirectoryReads: 0).ConfigureAwait(false);
            if (!DynamicReportActivationCommissioningService.TryResolveExactQualifiedMembers(
                    discovery.IedDirectory,
                    memberReferences,
                    out var exactPoints,
                    out var exactReason))
            {
                evidence.Add("G2.5-A1 witness member resolution failed: " + exactReason);
                return WitnessFailure("Witness could not resolve the exact qualified member sequence.", evidence, witness.IsMmsInitiated);
            }

            evidence.Add("G2.5-A1 witness prepared exact read-only member set and is waiting for core ARMED state.");
            await armedSignal.WaitAsync(cancellationToken).ConfigureAwait(false);

            var baseline = await ReadWitnessValuesAsync(witness, exactPoints, cancellationToken).ConfigureAwait(false);
            if (!baseline.IsSuccess)
            {
                evidence.Add("G2.5-A1 witness baseline failed: " + baseline.Message);
                return WitnessFailure("Witness baseline could not be captured completely.", evidence, witness.IsMmsInitiated, baseline.ReadFailures);
            }

            evidence.Add("G2.5-A1 witness baseline: " + string.Join(" | ", memberReferences.Select((reference, index) => $"[{index}] {reference}={baseline.Values[index]}")));
            progress?.Report($"{WitnessReadyMarker} — NOW perform ONE safe physical/process stimulus that changes one of the 8 proven points. Witness is read-only; NO GI.");
            evidence.Add($"{WitnessReadyMarker}: baseline complete; sampling starts now for up to {WitnessWindow.TotalSeconds:0}s.");

            var deadline = DateTimeOffset.UtcNow + WitnessWindow;
            var cycles = 0;
            var readFailures = 0;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = await ReadWitnessValuesAsync(witness, exactPoints, cancellationToken).ConfigureAwait(false);
                cycles++;
                readFailures += current.ReadFailures;

                if (!witness.IsMmsInitiated)
                    return WitnessFailure("Witness association left MmsInitiated during the stimulus window.", evidence, false, readFailures, baseline.Values, cycles);

                if (current.IsSuccess)
                {
                    var transitions = CompareStimulusWitnessSamples(memberReferences, baseline.Values, current.Values, DateTimeOffset.UtcNow);
                    if (transitions.Count > 0)
                    {
                        foreach (var transition in transitions)
                            evidence.Add($"G2.5-A1 WITNESSED TRANSITION: index={transition.Index}; member={transition.MemberReference}; before={transition.BeforeValue}; after={transition.AfterValue}; at={transition.ObservedAtUtc:O}");

                        return new DynamicReportStimulusWitnessResult
                        {
                            ArmedObserved = true,
                            BaselineCaptured = true,
                            ChangeObserved = true,
                            AssociationHealthy = witness.IsMmsInitiated,
                            SampleCycles = cycles,
                            ReadFailures = readFailures,
                            BaselineValues = baseline.Values,
                            Transitions = transitions,
                            EvidenceLines = evidence.ToArray(),
                            Summary = $"Witness observed {transitions.Count} qualified-member transition(s) after ARMED."
                        };
                    }
                }

                if (WitnessInterCycleDelay > TimeSpan.Zero)
                    await Task.Delay(WitnessInterCycleDelay, cancellationToken).ConfigureAwait(false);
            }

            evidence.Add($"G2.5-A1 witness window ended: transitions=0; cycles={cycles}; readFailures={readFailures}; associationHealthy={witness.IsMmsInitiated}");
            return new DynamicReportStimulusWitnessResult
            {
                ArmedObserved = true,
                BaselineCaptured = true,
                ChangeObserved = false,
                AssociationHealthy = witness.IsMmsInitiated,
                SampleCycles = cycles,
                ReadFailures = readFailures,
                BaselineValues = baseline.Values,
                EvidenceLines = evidence.ToArray(),
                Summary = "No qualified-member transition was observed by the independent read-only witness during the armed window."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            evidence.Add($"G2.5-A1 witness exception: {ex.GetType().Name}: {ex.Message}");
            return WitnessFailure("Witness failed before a conclusive stimulus observation.", evidence, witness.IsMmsInitiated);
        }
    }

    private static async Task<WitnessReadBatch> ReadWitnessValuesAsync(
        ArMms.MmsClientSession session,
        IReadOnlyList<ArMms.MmsFcResolvedPoint> points,
        CancellationToken cancellationToken)
    {
        var values = new string[points.Count];
        var failures = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var read = await session.ReadSingleVariableAsync(points[index].ToObjectReference(), cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess)
            {
                failures++;
                values[index] = "<read-failed>";
                continue;
            }

            values[index] = ExtractWitnessValue(read.Message);
        }

        return new WitnessReadBatch
        {
            IsSuccess = failures == 0,
            ReadFailures = failures,
            Values = values,
            Message = failures == 0 ? "all reads succeeded" : $"{failures} of {points.Count} reads failed"
        };
    }

    internal static IReadOnlyList<DynamicReportStimulusWitnessTransition> CompareStimulusWitnessSamples(
        IReadOnlyList<string> memberReferences,
        IReadOnlyList<string> baselineValues,
        IReadOnlyList<string> currentValues,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(memberReferences);
        ArgumentNullException.ThrowIfNull(baselineValues);
        ArgumentNullException.ThrowIfNull(currentValues);
        if (memberReferences.Count != baselineValues.Count || memberReferences.Count != currentValues.Count)
            throw new ArgumentException("Stimulus witness arrays must have identical lengths.");

        var transitions = new List<DynamicReportStimulusWitnessTransition>();
        for (var index = 0; index < memberReferences.Count; index++)
        {
            if (string.Equals(baselineValues[index], currentValues[index], StringComparison.OrdinalIgnoreCase))
                continue;
            if (baselineValues[index] == "<read-failed>" || currentValues[index] == "<read-failed>")
                continue;

            transitions.Add(new DynamicReportStimulusWitnessTransition
            {
                Index = index,
                MemberReference = memberReferences[index],
                BeforeValue = baselineValues[index],
                AfterValue = currentValues[index],
                ObservedAtUtc = observedAtUtc
            });
        }
        return transitions;
    }

    internal static string ExtractWitnessValue(string? readMessage)
    {
        var text = (readMessage ?? string.Empty).Trim();
        const string marker = "decoded value:";
        var markerIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
            text = text[(markerIndex + marker.Length)..].Trim();
        return text.TrimEnd('.').Trim();
    }

    private static DynamicReportStimulusWitnessResult WitnessFailure(
        string summary,
        IReadOnlyList<string> evidence,
        bool associationHealthy,
        int readFailures = 0,
        IReadOnlyList<string>? baseline = null,
        int cycles = 0)
        => new()
        {
            BaselineCaptured = baseline is { Count: > 0 },
            AssociationHealthy = associationHealthy,
            SampleCycles = cycles,
            ReadFailures = readFailures,
            BaselineValues = baseline?.ToArray() ?? Array.Empty<string>(),
            Summary = summary,
            EvidenceLines = evidence.ToArray()
        };

    private static DynamicReportStimulusWitnessCommissioningResult FailedBeforeCore(string summary, IReadOnlyList<string> evidence)
        => new()
        {
            Summary = summary + " Production automatic dynamic reporting remains OFF.",
            EvidenceLines = evidence.ToArray()
        };

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private sealed class RelayProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    private sealed class WitnessReadBatch
    {
        public bool IsSuccess { get; init; }
        public int ReadFailures { get; init; }
        public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();
        public string Message { get; init; } = string.Empty;
    }
}
