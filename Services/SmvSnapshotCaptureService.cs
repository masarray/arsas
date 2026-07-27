using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using AR.Iec61850.SampledValues;
using AR.Iec61850.SampledValues.Analysis;
using AR.Iec61850.SampledValues.Measurements;
using AR.Iec61850.Transports;
using AR.Iec61850.Transports.Npcap;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed record SmvSnapshotRequest
{
    public required string AdapterSelector { get; init; }
    public ushort? AppId { get; init; }
    public string StreamId { get; init; } = string.Empty;
    public string DestinationMac { get; init; } = string.Empty;
    public ushort? DeclaredSampleRateHint { get; init; }
    public ushort? DeclaredSampleModeHint { get; init; }
    public double TrustedNominalFrequencyHz { get; init; } = 50.0;
    public int CycleCount { get; init; } = 2;
    public int MaximumChannels { get; init; } = 8;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);
}

public sealed record SmvSnapshotProgress
{
    public int CapturedSamples { get; init; }
    public int TargetSamples { get; init; }
    public long CapturedFrames { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed record SmvSnapshotChannel
{
    public int ChannelIndex { get; init; }
    public int PayloadWordIndex { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Interpretation { get; init; } = string.Empty;
    public IReadOnlyList<double> Samples { get; init; } = Array.Empty<double>();
    public double Minimum { get; init; }
    public double Maximum { get; init; }
    public double PeakToPeak => Maximum - Minimum;
}

public sealed record SmvSnapshotResult
{
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public ushort AppId { get; init; }
    public string SourceMac { get; init; } = string.Empty;
    public string DestinationMac { get; init; } = string.Empty;
    public string VlanText { get; init; } = "-";
    public string StreamId { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public uint ConfigurationRevision { get; init; }
    public byte SampleSynchronization { get; init; }
    public ushort? DeclaredSampleRate { get; init; }
    public ushort? DeclaredSampleMode { get; init; }
    public double NominalFrequencyHz { get; init; }
    public int SamplesPerCycle { get; init; }
    public int CycleCount { get; init; }
    public int TargetSamples { get; init; }
    public int CapturedSamples { get; init; }
    public long CapturedFrames { get; init; }
    public long ParsedAsdus { get; init; }
    public ushort FirstSampleCount { get; init; }
    public ushort LastSampleCount { get; init; }
    public int ContinuousTransitions { get; init; }
    public int NormalWraps { get; init; }
    public int GapTransitions { get; init; }
    public int MissingSamples { get; init; }
    public int DuplicateTransitions { get; init; }
    public int OutOfOrderTransitions { get; init; }
    public int RestartTransitions { get; init; }
    public string TimebaseReason { get; init; } = string.Empty;
    public string PayloadShape { get; init; } = string.Empty;
    public IReadOnlyList<SmvSnapshotChannel> Channels { get; init; } = Array.Empty<SmvSnapshotChannel>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    public bool HasCounterAnomaly => GapTransitions > 0 || DuplicateTransitions > 0 || OutOfOrderTransitions > 0;
    public bool IsComplete => CapturedSamples >= TargetSamples;
    public bool IsCleanProof => IsComplete && !HasCounterAnomaly;
    public TimeSpan CaptureDuration => CompletedAt - StartedAt;
}

/// <summary>
/// Read-only bounded IEC 61850-9-2 capture used to prove that one selected SV stream can be
/// received, parsed and followed for a fixed two-cycle observation window. Protocol parsing,
/// generic payload inspection and sample-counter analysis remain engine-owned.
/// </summary>
public sealed class SmvSnapshotCaptureService
{
    public const string DefaultCaptureFilter = "ether proto 0x88ba or (vlan and ether proto 0x88ba)";

    public IReadOnlyList<GooseAdapterOption> ListAdapters()
    {
        var windowsAdapters = NetworkInterface.GetAllNetworkInterfaces();
        return NpcapAdapterCatalog.ListAdapters()
            .Select(adapter =>
            {
                var macAddress = adapter.MacAddress?.ToString() ?? string.Empty;
                return new GooseAdapterOption
                {
                    Index = adapter.Index,
                    Name = adapter.Name,
                    Description = adapter.Description,
                    MacAddress = macAddress,
                    FriendlyName = ResolveAdapterFriendlyName(adapter.Name, adapter.Description, macAddress, windowsAdapters)
                };
            })
            .ToArray();
    }

    public async Task<SmvSnapshotResult> CaptureAsync(
        SmvSnapshotRequest request,
        IProgress<SmvSnapshotProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AdapterSelector);
        if (request.CycleCount is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(request.CycleCount));
        if (request.MaximumChannels is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(request.MaximumChannels));
        if (request.TrustedNominalFrequencyHz is not (50.0 or 60.0))
            throw new ArgumentOutOfRangeException(nameof(request.TrustedNominalFrequencyHz), "Snapshot frequency must be explicitly set to 50 or 60 Hz.");

        using var timeoutCancellation = new CancellationTokenSource(request.Timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
        var token = linkedCancellation.Token;
        var startedAt = DateTimeOffset.UtcNow;

        var samplesByChannel = new List<List<double>>();
        var channelWordIndexes = new List<int>();
        var diagnostics = new List<string>();
        var counterTracker = new SvSampleCounterTracker();
        SvTimebaseResolution? timebase = null;
        SvGenericPayloadInspection? firstPayload = null;
        SampledValuesFrame? firstFrame = null;
        SampledValueAsdu? firstAsdu = null;
        SampledValueAsdu? lastAsdu = null;
        int targetSamples = 0;
        int acceptedSamples = 0;
        long capturedFrames = 0;
        long parsedAsdus = 0;
        int continuous = 0;
        int normalWraps = 0;
        int gaps = 0;
        int missing = 0;
        int duplicates = 0;
        int outOfOrder = 0;
        int restarts = 0;

        try
        {
            using var source = new NpcapProcessBusFrameSource(request.AdapterSelector);
            var options = new ProcessBusCaptureOptions
            {
                Filter = DefaultCaptureFilter,
                ReadTimeoutMilliseconds = 250,
                BufferCapacity = 8192
            };

            progress?.Report(new SmvSnapshotProgress
            {
                Message = "Listening for the selected IEC 61850-9-2 stream…"
            });

            await foreach (var captured in source.CaptureAsync(options, token).ConfigureAwait(false))
            {
                if (!SampledValuesFrameParser.TryParseEthernetFrame(captured.Frame, out var frame) ||
                    !MatchesFrame(request, frame))
                {
                    continue;
                }

                var matchedFrame = false;
                foreach (var asdu in frame.Pdu.Asdus)
                {
                    if (!MatchesAsdu(request, asdu))
                        continue;

                    matchedFrame = true;
                    parsedAsdus++;
                    var inspection = SvGenericAsduInspector.Inspect(asdu).Payload;
                    if (inspection.CompleteWordCount == 0)
                    {
                        diagnostics.Add($"ASDU smpCnt={asdu.SampleCount} contained no complete 32-bit seqOfData word.");
                        continue;
                    }

                    if (timebase is null)
                    {
                        timebase = ResolveTimebase(request, asdu, inspection);
                        if (!timebase.IsResolved || timebase.SamplesPerCycle is not > 0)
                        {
                            throw new InvalidDataException(
                                $"The two-cycle target cannot be resolved from smpRate/smpMod and the explicit {request.TrustedNominalFrequencyHz:0} Hz setting. {timebase.Reason}");
                        }

                        targetSamples = checked(timebase.SamplesPerCycle.Value * request.CycleCount);
                        firstFrame = frame;
                        firstAsdu = asdu;
                        firstPayload = inspection;
                        InitializeChannels(inspection, request.MaximumChannels, samplesByChannel, channelWordIndexes);
                        diagnostics.AddRange(inspection.Diagnostics);
                    }

                    ValidateStablePayload(firstPayload!, inspection, channelWordIndexes);
                    AppendSample(inspection, channelWordIndexes, samplesByChannel);
                    acceptedSamples++;
                    lastAsdu = asdu;

                    var transition = counterTracker.Observe(asdu.SampleCount, timebase.SampleCounterWrap);
                    switch (transition.Kind)
                    {
                        case SvSampleCounterTransitionKind.Continuous:
                            continuous++;
                            break;
                        case SvSampleCounterTransitionKind.NormalWrap:
                            normalWraps++;
                            break;
                        case SvSampleCounterTransitionKind.Gap:
                            gaps++;
                            missing += transition.MissingSamples;
                            diagnostics.Add(transition.Detail);
                            break;
                        case SvSampleCounterTransitionKind.Duplicate:
                            duplicates++;
                            diagnostics.Add(transition.Detail);
                            break;
                        case SvSampleCounterTransitionKind.OutOfOrder:
                            outOfOrder++;
                            diagnostics.Add(transition.Detail);
                            break;
                        case SvSampleCounterTransitionKind.Restart:
                            restarts++;
                            diagnostics.Add(transition.Detail);
                            break;
                    }

                    progress?.Report(new SmvSnapshotProgress
                    {
                        CapturedSamples = acceptedSamples,
                        TargetSamples = targetSamples,
                        CapturedFrames = capturedFrames + 1,
                        Message = $"Decoded {acceptedSamples:N0} / {targetSamples:N0} samples"
                    });

                    if (acceptedSamples >= targetSamples)
                    {
                        capturedFrames++;
                        return BuildResult(
                            request,
                            startedAt,
                            DateTimeOffset.UtcNow,
                            firstFrame!,
                            firstAsdu!,
                            lastAsdu!,
                            firstPayload!,
                            timebase,
                            samplesByChannel,
                            channelWordIndexes,
                            acceptedSamples,
                            capturedFrames,
                            parsedAsdus,
                            continuous,
                            normalWraps,
                            gaps,
                            missing,
                            duplicates,
                            outOfOrder,
                            restarts,
                            diagnostics);
                    }
                }

                if (matchedFrame)
                    capturedFrames++;
            }
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(BuildTimeoutMessage(request, capturedFrames, acceptedSamples, targetSamples));
        }

        throw new InvalidDataException(BuildTimeoutMessage(request, capturedFrames, acceptedSamples, targetSamples));
    }

    private static SvTimebaseResolution ResolveTimebase(
        SmvSnapshotRequest request,
        SampledValueAsdu asdu,
        SvGenericPayloadInspection payload)
    {
        var declaredRate = asdu.SampleRate ?? request.DeclaredSampleRateHint;
        var declaredMode = asdu.SampleMode ?? request.DeclaredSampleModeHint;
        return SvTimebaseResolver.Resolve(new SvTimebaseEvidence
        {
            DeclaredSampleRate = declaredRate,
            DeclaredSampleMode = declaredMode,
            IsFixedLegacyProtectionLayout = payload.HasEightByteGroupShape && payload.PayloadLength == 64,
            TrustedNominalFrequencyHz = request.TrustedNominalFrequencyHz
        });
    }

    private static void InitializeChannels(
        SvGenericPayloadInspection inspection,
        int maximumChannels,
        ICollection<List<double>> samplesByChannel,
        ICollection<int> channelWordIndexes)
    {
        var candidateWords = inspection.HasEightByteGroupShape
            ? inspection.Words.Where(word => word.StructuralRole == SvGenericPayloadWordRole.FirstWordInEightByteGroup)
            : inspection.Words;

        foreach (var word in candidateWords.Take(maximumChannels))
        {
            channelWordIndexes.Add(word.Index);
            samplesByChannel.Add(new List<double>());
        }

        if (channelWordIndexes.Count == 0)
            throw new InvalidDataException("No plottable 32-bit seqOfData word was found in the selected SV ASDU.");
    }

    private static void ValidateStablePayload(
        SvGenericPayloadInspection first,
        SvGenericPayloadInspection current,
        IReadOnlyCollection<int> channelWordIndexes)
    {
        if (current.PayloadLength != first.PayloadLength ||
            current.CompleteWordCount != first.CompleteWordCount ||
            channelWordIndexes.Any(index => index >= current.Words.Count))
        {
            throw new InvalidDataException(
                $"The selected stream changed seqOfData shape inside the snapshot window ({first.PayloadLength} → {current.PayloadLength} bytes).");
        }
    }

    private static void AppendSample(
        SvGenericPayloadInspection inspection,
        IReadOnlyList<int> channelWordIndexes,
        IReadOnlyList<List<double>> samplesByChannel)
    {
        for (var index = 0; index < channelWordIndexes.Count; index++)
            samplesByChannel[index].Add(inspection.Words[channelWordIndexes[index]].SignedInt32);
    }

    private static SmvSnapshotResult BuildResult(
        SmvSnapshotRequest request,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        SampledValuesFrame firstFrame,
        SampledValueAsdu firstAsdu,
        SampledValueAsdu lastAsdu,
        SvGenericPayloadInspection firstPayload,
        SvTimebaseResolution timebase,
        IReadOnlyList<List<double>> samplesByChannel,
        IReadOnlyList<int> channelWordIndexes,
        int acceptedSamples,
        long capturedFrames,
        long parsedAsdus,
        int continuous,
        int normalWraps,
        int gaps,
        int missing,
        int duplicates,
        int outOfOrder,
        int restarts,
        IReadOnlyList<string> diagnostics)
    {
        var channels = samplesByChannel.Select((samples, index) =>
        {
            var wordIndex = channelWordIndexes[index];
            return new SmvSnapshotChannel
            {
                ChannelIndex = index + 1,
                PayloadWordIndex = wordIndex,
                Label = firstPayload.HasEightByteGroupShape
                    ? $"Raw group {index + 1} · word {wordIndex + 1}"
                    : $"Raw word {wordIndex + 1}",
                Interpretation = firstPayload.HasEightByteGroupShape
                    ? "Signed INT32, first word in structural 8-byte group; channel/unit semantics unresolved"
                    : "Signed INT32 representation; channel/unit semantics unresolved",
                Samples = samples.ToArray(),
                Minimum = samples.Min(),
                Maximum = samples.Max()
            };
        }).ToArray();

        return new SmvSnapshotResult
        {
            StartedAt = startedAt,
            CompletedAt = completedAt,
            AppId = firstFrame.AppId,
            SourceMac = firstFrame.Source.ToString(),
            DestinationMac = firstFrame.Destination.ToString(),
            VlanText = firstFrame.Vlan?.VlanId.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            StreamId = firstAsdu.SvId,
            DataSetReference = firstAsdu.DataSetReference,
            ConfigurationRevision = firstAsdu.ConfigurationRevision,
            SampleSynchronization = firstAsdu.SampleSynchronization,
            DeclaredSampleRate = firstAsdu.SampleRate ?? request.DeclaredSampleRateHint,
            DeclaredSampleMode = firstAsdu.SampleMode ?? request.DeclaredSampleModeHint,
            NominalFrequencyHz = timebase.NominalFrequencyHz ?? request.TrustedNominalFrequencyHz,
            SamplesPerCycle = timebase.SamplesPerCycle!.Value,
            CycleCount = request.CycleCount,
            TargetSamples = timebase.SamplesPerCycle.Value * request.CycleCount,
            CapturedSamples = acceptedSamples,
            CapturedFrames = capturedFrames,
            ParsedAsdus = parsedAsdus,
            FirstSampleCount = firstAsdu.SampleCount,
            LastSampleCount = lastAsdu.SampleCount,
            ContinuousTransitions = continuous,
            NormalWraps = normalWraps,
            GapTransitions = gaps,
            MissingSamples = missing,
            DuplicateTransitions = duplicates,
            OutOfOrderTransitions = outOfOrder,
            RestartTransitions = restarts,
            TimebaseReason = timebase.Reason,
            PayloadShape = firstPayload.HasEightByteGroupShape
                ? $"{firstPayload.PayloadLength} bytes · {firstPayload.PayloadLength / 8} structural 8-byte group(s)"
                : $"{firstPayload.PayloadLength} bytes · {firstPayload.CompleteWordCount} raw 32-bit word(s)",
            Channels = channels,
            Diagnostics = diagnostics.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static bool MatchesFrame(SmvSnapshotRequest request, SampledValuesFrame frame)
    {
        if (request.AppId.HasValue && frame.AppId != request.AppId.Value)
            return false;

        var expectedMac = NormalizeMac(request.DestinationMac);
        return string.IsNullOrWhiteSpace(expectedMac) ||
               expectedMac == NormalizeMac(frame.Destination.ToString());
    }

    private static bool MatchesAsdu(SmvSnapshotRequest request, SampledValueAsdu asdu)
    {
        var expected = NormalizeIdentity(request.StreamId);
        if (string.IsNullOrWhiteSpace(expected))
            return true;
        return NormalizeIdentity(asdu.SvId).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildTimeoutMessage(
        SmvSnapshotRequest request,
        long capturedFrames,
        int acceptedSamples,
        int targetSamples)
    {
        var identity = string.Join(", ", new[]
        {
            request.AppId.HasValue ? $"APPID 0x{request.AppId.Value:X4}" : string.Empty,
            string.IsNullOrWhiteSpace(NormalizeIdentity(request.StreamId)) ? string.Empty : $"svID {request.StreamId}",
            string.IsNullOrWhiteSpace(NormalizeMac(request.DestinationMac)) ? string.Empty : $"MAC {request.DestinationMac}"
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(identity))
            identity = "the first decodable SV stream";

        var target = targetSamples > 0 ? $"{acceptedSamples:N0}/{targetSamples:N0} samples" : $"{acceptedSamples:N0} samples";
        return $"Snapshot did not complete within {request.Timeout.TotalSeconds:0.#} s for {identity}. Received {capturedFrames:N0} matching frame(s) and {target}. Check Npcap, adapter selection, port mirroring, APPID/MAC/svID, and publisher state.";
    }

    private static string NormalizeIdentity(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized == "-" ? string.Empty : normalized;
    }

    private static string NormalizeMac(string? value)
        => Regex.Replace(value ?? string.Empty, "[^0-9A-Fa-f]", string.Empty).ToUpperInvariant();

    private static string ResolveAdapterFriendlyName(
        string captureName,
        string captureDescription,
        string macAddress,
        IReadOnlyList<NetworkInterface> windowsAdapters)
    {
        var normalizedMac = NormalizeMac(macAddress);
        var captureId = ExtractAdapterId(captureName);
        var match = windowsAdapters.FirstOrDefault(adapter =>
            (!string.IsNullOrWhiteSpace(normalizedMac) && NormalizeMac(adapter.GetPhysicalAddress().ToString()) == normalizedMac) ||
            (!string.IsNullOrWhiteSpace(captureId) && adapter.Id.Equals(captureId, StringComparison.OrdinalIgnoreCase)));

        if (match is not null)
        {
            var windowsName = CleanAdapterLabel(match.Name);
            if (!string.IsNullOrWhiteSpace(windowsName))
                return windowsName;
            var windowsDescription = CleanAdapterLabel(match.Description);
            if (!string.IsNullOrWhiteSpace(windowsDescription))
                return windowsDescription;
        }

        return FirstAdapterLabel(CleanAdapterLabel(captureDescription), CleanAdapterLabel(captureName), "Network adapter");
    }

    private static string ExtractAdapterId(string? value)
    {
        var match = Regex.Match(value ?? string.Empty, @"\{(?<id>[0-9A-Fa-f-]{36})\}");
        return match.Success ? match.Groups["id"].Value : string.Empty;
    }

    private static string CleanAdapterLabel(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Equals("ArIED61850", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("ArIED 61850", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return text;
    }

    private static string FirstAdapterLabel(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Network adapter";
}