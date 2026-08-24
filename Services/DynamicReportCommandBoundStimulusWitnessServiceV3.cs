using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// G2.5-A2.1 V3 command capture wrapper.
///
/// V1/V2 physical runs proved that UI-layer observers are not reliable enough for the
/// operator's actual control path. V3 therefore observes the already-existing runtime
/// Diagnostic event. Iec61850MonitorRuntime emits "Control execution requested:" before
/// invoking the native control client for every ExecuteControlAsync caller. V3 only
/// parses that diagnostic and republishes immutable intent into the existing A2.1
/// observer bus. No control payload, sequencing, timeout, SBOw, Operate, termination,
/// feedback, engine or report behavior is changed.
/// </summary>
internal sealed class DynamicReportCommandBoundStimulusWitnessServiceV3
{
    private const string RuntimePrefix = "Control execution requested: ";
    private const string ValueMarker = " value=";
    private const string ValueEndMarker = "; test=";
    internal const string RuntimeDiagnosticSource = "Iec61850MonitorRuntime.Diagnostic.ControlExecutionRequested";

    private readonly DynamicReportCommandBoundStimulusWitnessServiceV2 _inner;

    internal DynamicReportCommandBoundStimulusWitnessServiceV3(
        DynamicReportQualificationProfileStore? profileStore = null)
    {
        _inner = new DynamicReportCommandBoundStimulusWitnessServiceV2(profileStore);
    }

    internal async Task<DynamicReportCommandBoundStimulusWitnessResult> RunAsync(
        Iec61850MonitorRuntime runtime,
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        void RuntimeDiagnosticHandler(DiagnosticEntry entry)
        {
            // Fail open: a commissioning observer must never throw into the existing
            // runtime Diagnostic multicast path or influence the control transaction.
            try
            {
                if (TryBuildRuntimeIntent(entry, device, fullModelSignals, out var intent))
                    DynamicReportCommandIntentObservation.Publish(intent!);
            }
            catch
            {
                // Intentionally contained. Existing control continues unchanged.
            }
        }

        runtime.Diagnostic += RuntimeDiagnosticHandler;
        try
        {
            progress?.Report("G2.5-A2.1 V3: runtime-diagnostic command observer armed; preparing isolated read-only MMS baseline…");
            var result = await _inner.RunAsync(device, fullModelSignals, progress, cancellationToken).ConfigureAwait(false);
            return RewriteAsV3(result);
        }
        finally
        {
            runtime.Diagnostic -= RuntimeDiagnosticHandler;
        }
    }

    internal static bool TryBuildRuntimeIntent(
        DiagnosticEntry entry,
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        out DynamicReportObservedCommandIntent? intent)
    {
        intent = null;
        if (entry is null || device is null || fullModelSignals is null)
            return false;

        if (!string.Equals(entry.Source?.Trim(), device.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var message = entry.Message ?? string.Empty;
        if (!message.StartsWith(RuntimePrefix, StringComparison.Ordinal))
            return false;

        var referenceStart = RuntimePrefix.Length;
        var valueMarker = message.IndexOf(ValueMarker, referenceStart, StringComparison.Ordinal);
        if (valueMarker <= referenceStart)
            return false;

        var objectReference = message[referenceStart..valueMarker].Trim();
        if (string.IsNullOrWhiteSpace(objectReference))
            return false;

        var valueStart = valueMarker + ValueMarker.Length;
        var valueEnd = message.IndexOf(ValueEndMarker, valueStart, StringComparison.Ordinal);
        if (valueEnd < valueStart)
            return false;
        var requestedValue = message[valueStart..valueEnd].Trim();

        var signal = fullModelSignals.FirstOrDefault(candidate =>
            candidate.IsControlSignal &&
            !string.IsNullOrWhiteSpace(candidate.ControlStatusReference) &&
            SameReference(candidate.ObjectReference, objectReference));
        if (signal is null)
            return false;

        intent = new DynamicReportObservedCommandIntent(
            device,
            signal,
            requestedValue,
            RuntimeDiagnosticSource,
            DateTimeOffset.UtcNow);
        return true;
    }

    private static DynamicReportCommandBoundStimulusWitnessResult RewriteAsV3(
        DynamicReportCommandBoundStimulusWitnessResult result)
    {
        var evidence = result.EvidenceLines
            .Select(line => (line ?? string.Empty)
                .Replace("G2.5-A2.1 V2", "G2.5-A2.1 V3", StringComparison.Ordinal)
                .Replace("A2.1 V2", "A2.1 V3", StringComparison.Ordinal))
            .ToList();

        evidence.Insert(0,
            "G2.5-A2.1 V3 command capture authority: existing Iec61850MonitorRuntime.Diagnostic 'Control execution requested:' event is the primary observer; fast-panel busy and ControlCommandWindow routed-click remain non-authoritative fallbacks. Runtime/control source is not modified.");

        if (!result.CommandCaptured)
        {
            evidence.Add(
                "G2.5-A2.1 V3 timeout interpretation: no matching existing runtime control-request diagnostic reached the observer while armed; do not infer any MMS status or spontaneous-report conclusion from this run.");
        }

        return new DynamicReportCommandBoundStimulusWitnessResult
        {
            IsSuccess = result.IsSuccess,
            IsBlocked = result.IsBlocked,
            BaselineCaptured = result.BaselineCaptured,
            CommandCaptured = result.CommandCaptured,
            AssociationHealthy = result.AssociationHealthy,
            StimulusWitnessProven = result.StimulusWitnessProven,
            CommandSignalReference = result.CommandSignalReference,
            ControlStatusReference = result.ControlStatusReference,
            ControlModelText = result.ControlModelText,
            PreCommandBaselineCount = result.PreCommandBaselineCount,
            FocusCandidateCount = result.FocusCandidateCount,
            SampleCycles = result.SampleCycles,
            ReadFailures = result.ReadFailures,
            Summary = (result.Summary ?? string.Empty)
                .Replace("A2.1 V2", "A2.1 V3", StringComparison.Ordinal)
                .Replace("G2.5-A2.1 V2", "G2.5-A2.1 V3", StringComparison.Ordinal),
            Identity = result.Identity,
            InputProfile = result.InputProfile,
            Observations = result.Observations,
            EligibleCandidates = result.EligibleCandidates,
            EvidenceLines = evidence
        };
    }

    private static bool SameReference(string? left, string? right)
        => NormalizeReference(left).Equals(NormalizeReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty)
            .Trim()
            .Replace('$', '.')
            .TrimEnd('.');
}
