using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed class DynamicReportCleanupClosureCommissioningResult
{
    public bool IsSuccess { get; init; }
    public bool IsBlocked { get; init; }
    public string Summary { get; init; } = string.Empty;
    public ArMms.MmsDynamicReportIedIdentity? Identity { get; init; }
    public ArMms.MmsDynamicReportQualificationProfile? InputProfile { get; init; }
    public ArMms.MmsRcbAvailabilitySnapshot? FreshRcbSnapshot { get; init; }
    public string RcbReference { get; init; } = string.Empty;
    public string TemporaryDataSetReference { get; init; } = string.Empty;
    public bool TemporaryDataSetAbsentFromNameList { get; init; }
    public bool TemporaryDataSetDirectoryAbsent { get; init; }
    public bool AssociationHealthy { get; init; }
    public string ProfilePath { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// G2.4-C closes the only cleanup gap left after a successful one-URCB actual
/// InformationReport proof. It opens a NEW MMS association and performs READS ONLY.
/// No RCB attribute, DataSet, report monitor, GI, profile, or production policy is changed.
/// </summary>
internal sealed class DynamicReportCleanupClosureCommissioningService
{
    private static readonly TimeSpan AuxiliaryAssociationTimeout = TimeSpan.FromSeconds(10);

    private readonly DynamicReportQualificationProfileStore _profileStore;

    public DynamicReportCleanupClosureCommissioningService(
        DynamicReportQualificationProfileStore? profileStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
    }

    public async Task<DynamicReportCleanupClosureCommissioningResult> RunAsync(
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
            return Blocked("G2.4-C identity preflight failed: " + ex.Message, evidence);
        }

        evidence.Add($"G2.4-C identity stableKey={identity.StableIdentityKey}; fingerprint={identity.ModelFingerprint}; profileRevision={TextOrDash(identity.ProfileRevision)}");

        var loaded = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        evidence.Add($"G2.4-C persisted profile: exists={loaded.Exists}; valid={loaded.IsValid}; reason={loaded.Reason}");
        if (!loaded.IsValid || loaded.Profile is null)
        {
            return Blocked(
                "G2.4-C requires the identity-compatible profile produced by the successful G2.4 InformationReport proof.",
                evidence,
                identity,
                loaded.FilePath);
        }

        var profile = loaded.Profile;
        if (profile.State < ArMms.MmsDynamicReportQualificationState.InformationReportProven ||
            profile.RcbActivationProof?.IsSuccess != true ||
            profile.InformationReportProof?.IsSuccess != true)
        {
            return Blocked(
                $"G2.4-C requires successful InformationReportProven evidence; current profile state is {profile.State}.",
                evidence,
                identity,
                loaded.FilePath,
                profile);
        }

        var rcbReference = profile.RcbActivationProof.RcbReference;
        var temporaryDataSetReference = profile.RcbActivationProof.DataSetReference;
        if (string.IsNullOrWhiteSpace(rcbReference) || string.IsNullOrWhiteSpace(temporaryDataSetReference))
        {
            return Blocked(
                "G2.4-C profile is missing the exact RCB or temporary DataSet identity from G2.4.",
                evidence,
                identity,
                loaded.FilePath,
                profile);
        }

        evidence.Add($"G2.4-C target: profileState={profile.State}; rcb={rcbReference}; temporaryDataSet={temporaryDataSetReference}; members={profile.RcbActivationProof.MemberReferences.Count}");
        evidence.Add("G2.4-C contract: fresh association + read-only RCB/DataSet inspection only; zero MMS Write, DefineNamedVariableList, DeleteNamedVariableList, RptEna, Resv, DatSet, TrgOps, OptFlds or GI mutation.");

        await using var auxiliary = new ArMms.MmsClientSession();
        try
        {
            await auxiliary.ConnectAsync(
                device.IpAddress,
                device.Port,
                AuxiliaryAssociationTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            evidence.Add($"G2.4-C fresh association failed: {ex.GetType().Name}: {ex.Message}");
            return Failed(
                "G2.4-C could not establish the fresh read-only MMS association.",
                evidence,
                identity,
                profile,
                loaded.FilePath,
                rcbReference,
                temporaryDataSetReference);
        }

        evidence.Add($"G2.4-C fresh association ready: state={auxiliary.State}; localTcpAddress={TextOrDash(auxiliary.LocalTcpAddress)}; handshake={TextOrDash(auxiliary.LastHandshakeMessage)}");

        ArMms.MmsDiscoveryResult discovery;
        try
        {
            discovery = await auxiliary.DiscoverAsync(
                probeReportAttributes: true,
                maxReportAttributeProbes: 64,
                cancellationToken: cancellationToken,
                readDataSetDirectories: false,
                maxDataSetDirectoryReads: 0).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"G2.4-C fresh discovery failed: {ex.GetType().Name}: {ex.Message}");
            return Failed(
                "G2.4-C fresh discovery failed. No mutation was attempted.",
                evidence,
                identity,
                profile,
                loaded.FilePath,
                rcbReference,
                temporaryDataSetReference);
        }

        evidence.Add("G2.4-C discovery: " + discovery.Summary);

        var exactRcb = discovery.ReportInventory.ReportControls.FirstOrDefault(candidate =>
            SameReference(candidate.Reference, rcbReference));
        if (exactRcb is null)
        {
            evidence.Add("G2.4-C exact RCB lookup failed: the InformationReport-proven RCB is absent from fresh discovery.");
            return Failed(
                "G2.4-C could not re-identify the exact InformationReport-proven URCB on the fresh association.",
                evidence,
                identity,
                profile,
                loaded.FilePath,
                rcbReference,
                temporaryDataSetReference);
        }

        var oneRcbInventory = new ArMms.MmsReportInventory();
        oneRcbInventory.ReportControls.Add(exactRcb);

        ArMms.MmsRcbAvailabilityResult availability;
        try
        {
            availability = await auxiliary.CheckReportControlAvailabilityAsync(
                oneRcbInventory,
                discovery.IedDirectory,
                new ArMms.MmsRcbAvailabilityOptions
                {
                    MaxReportControls = 1,
                    ReadDataSetDirectories = false
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"G2.4-C exact RCB read failed: {ex.GetType().Name}: {ex.Message}");
            return Failed(
                "G2.4-C could not obtain fresh read-only RCB state.",
                evidence,
                identity,
                profile,
                loaded.FilePath,
                rcbReference,
                temporaryDataSetReference);
        }

        var snapshot = availability.ReportControls.SingleOrDefault();
        evidence.Add("G2.4-C fresh RCB availability: " + availability.Summary);
        if (snapshot is not null)
        {
            evidence.Add(
                $"G2.4-C fresh RCB: ref={snapshot.Reference}; availability={snapshot.Availability}; probe={snapshot.DataSetProbeState}; " +
                $"DatSet={TextOrDash(snapshot.DataSetReference)}; RptEna={TextOrDash(snapshot.EnabledState)}; Resv={TextOrDash(snapshot.ReservationState)}; " +
                $"Owner={TextOrDash(snapshot.Owner)}; ResvTms={TextOrDash(snapshot.ReservationTimeSeconds)}; " +
                $"TrgOps={TextOrDash(snapshot.TriggerOptions)}; OptFlds={TextOrDash(snapshot.OptionalFields)}; RptID={TextOrDash(snapshot.ReportId)}");
        }

        var temporaryNameAbsent = IsTemporaryDataSetAbsentFromNameList(
            discovery.Snapshot,
            temporaryDataSetReference,
            out var nameListReason);
        evidence.Add("G2.4-C temporary DataSet namespace: " + nameListReason);

        ArMms.MmsDataSetDirectoryResult dataSetDirectory;
        try
        {
            dataSetDirectory = await auxiliary.GetDataSetDirectoryAsync(
                temporaryDataSetReference,
                discovery.IedDirectory,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"G2.4-C temporary DataSet direct read exception: {ex.GetType().Name}: {ex.Message}");
            return Failed(
                "G2.4-C temporary DataSet absence could not be proven because the direct read ended in a transport/protocol exception.",
                evidence,
                identity,
                profile,
                loaded.FilePath,
                rcbReference,
                temporaryDataSetReference,
                snapshot,
                temporaryNameAbsent,
                false,
                auxiliary.IsMmsInitiated);
        }

        var directoryAbsent = !dataSetDirectory.IsSuccess;
        evidence.Add($"G2.4-C temporary DataSet direct directory: absent={directoryAbsent}; success={dataSetDirectory.IsSuccess}; members={dataSetDirectory.Members.Count}; result={dataSetDirectory.Message}");

        var associationHealthy = auxiliary.IsMmsInitiated;
        var closureSafe = IsFreshCleanupClosed(
            snapshot,
            temporaryNameAbsent,
            directoryAbsent,
            associationHealthy,
            out var closureReason);
        evidence.Add("G2.4-C closure evaluation: " + closureReason);
        evidence.Add($"G2.4-C result: success={closureSafe}; associationHealthy={associationHealthy}; temporaryNameAbsent={temporaryNameAbsent}; temporaryDirectoryAbsent={directoryAbsent}");
        evidence.Add("G2.4-C safety: READ ONLY. The persisted InformationReportProven profile is not modified and production automatic dynamic reporting remains OFF.");

        return new DynamicReportCleanupClosureCommissioningResult
        {
            IsSuccess = closureSafe,
            Summary = closureSafe
                ? "G2.4-C PASS: a fresh read-only MMS association proved the InformationReport-proven URCB released (RptEna=false, DatSet empty, Resv=false, Owner empty), the temporary G2.4 DataSet is absent, and the association remained healthy. G2.4 cleanup is closed; production dynamic reporting remains OFF."
                : "G2.4-C did not prove complete fresh-association cleanup closure. No mutation was attempted; production dynamic reporting remains OFF.",
            Identity = identity,
            InputProfile = profile,
            FreshRcbSnapshot = snapshot,
            RcbReference = rcbReference,
            TemporaryDataSetReference = temporaryDataSetReference,
            TemporaryDataSetAbsentFromNameList = temporaryNameAbsent,
            TemporaryDataSetDirectoryAbsent = directoryAbsent,
            AssociationHealthy = associationHealthy,
            ProfilePath = loaded.FilePath,
            EvidenceLines = evidence
        };
    }

    internal static bool IsFreshCleanupClosed(
        ArMms.MmsRcbAvailabilitySnapshot? snapshot,
        bool temporaryDataSetAbsentFromNameList,
        bool temporaryDataSetDirectoryAbsent,
        bool associationHealthy,
        out string reason)
    {
        if (snapshot is null)
        {
            reason = "Exact proven URCB snapshot is missing.";
            return false;
        }

        if (snapshot.Buffered)
        {
            reason = "G2.4-C expected the proven URCB, but the fresh snapshot is buffered.";
            return false;
        }

        if (snapshot.DataSetProbeState != ArMms.MmsRcbDataSetProbeState.ReadSucceeded ||
            !string.IsNullOrWhiteSpace(snapshot.DataSetReference))
        {
            reason = $"DatSet is not positively proven empty: probe={snapshot.DataSetProbeState}; DatSet={TextOrDash(snapshot.DataSetReference)}.";
            return false;
        }

        if (ParseBool(snapshot.EnabledState) != false)
        {
            reason = $"RptEna is not explicit false: {TextOrDash(snapshot.EnabledState)}.";
            return false;
        }

        if (!snapshot.Attributes.Contains("Resv", StringComparer.OrdinalIgnoreCase) ||
            ParseBool(snapshot.ReservationState) != false)
        {
            reason = $"Fresh-association URCB reservation release is not explicit: Resv={TextOrDash(snapshot.ReservationState)}.";
            return false;
        }

        if (HasOwner(snapshot.Owner))
        {
            reason = $"Fresh-association URCB Owner is still non-empty: {snapshot.Owner}.";
            return false;
        }

        if (ParseUnsigned(snapshot.ReservationTimeSeconds) is > 0)
        {
            reason = $"Fresh-association reservation time is still positive: {snapshot.ReservationTimeSeconds}.";
            return false;
        }

        if (!temporaryDataSetAbsentFromNameList)
        {
            reason = "Temporary G2.4 DataSet is still advertised by fresh NamedVariableList discovery.";
            return false;
        }

        if (!temporaryDataSetDirectoryAbsent)
        {
            reason = "Temporary G2.4 DataSet still has a readable directory on the fresh association.";
            return false;
        }

        if (!associationHealthy)
        {
            reason = "Fresh MMS association is not healthy after all read-only closure checks.";
            return false;
        }

        reason = $"Fresh URCB cleanup closed: DatSet empty, RptEna=false, Resv=false, Owner empty, temporary DataSet absent by namespace + direct directory, association healthy. Current read-only proof fields: TrgOps={TextOrDash(snapshot.TriggerOptions)}, OptFlds={TextOrDash(snapshot.OptionalFields)}.";
        return true;
    }

    internal static bool IsTemporaryDataSetAbsentFromNameList(
        ArMms.MmsDiscoverySnapshot snapshot,
        string temporaryDataSetReference,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!TryParseDataSetIdentity(temporaryDataSetReference, out var domain, out var itemName))
        {
            reason = $"Temporary DataSet reference could not be parsed: {TextOrDash(temporaryDataSetReference)}.";
            return false;
        }

        if (!snapshot.DomainVariableLists.TryGetValue(domain, out var names))
        {
            reason = $"Fresh NamedVariableList discovery has no domain entry for {domain}; absence cannot be proven.";
            return false;
        }

        var present = names.Any(name => NormalizeDataSetItem(name).Equals(itemName, StringComparison.OrdinalIgnoreCase));
        reason = present
            ? $"Temporary DataSet is still advertised: domain={domain}; item={itemName}."
            : $"Temporary DataSet is absent from fresh NamedVariableList discovery: domain={domain}; item={itemName}; advertisedLists={names.Count}.";
        return !present;
    }

    private static bool TryParseDataSetIdentity(string? reference, out string domain, out string itemName)
    {
        domain = string.Empty;
        itemName = string.Empty;
        var text = (reference ?? string.Empty).Trim();
        var slash = text.IndexOf('/');
        if (slash <= 0 || slash >= text.Length - 1)
            return false;

        domain = text[..slash];
        itemName = NormalizeDataSetItem(text[(slash + 1)..]);
        if (!itemName.Contains('$', StringComparison.Ordinal))
            itemName = "LLN0$" + itemName;
        return domain.Length > 0 && itemName.Length > 0;
    }

    private static string NormalizeDataSetItem(string? value)
        => (value ?? string.Empty).Trim().Replace('.', '$');

    private static bool SameReference(string? left, string? right)
        => NormalizeReference(left).Equals(NormalizeReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static bool? ParseBool(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text == "-")
            return null;
        if (bool.TryParse(text, out var parsed))
            return parsed;
        if (text is "1" or "01" || text.Equals("yes", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text is "0" or "00" || text.Equals("no", StringComparison.OrdinalIgnoreCase) || text.Equals("off", StringComparison.OrdinalIgnoreCase))
            return false;
        return null;
    }

    private static ulong? ParseUnsigned(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return ulong.TryParse(text, out var parsed) ? parsed : null;
    }

    private static bool HasOwner(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text == "-" || text == "[]" || text.Equals("null", StringComparison.OrdinalIgnoreCase))
            return false;
        var compact = text.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return compact.Length > 0 && compact.Any(character => character != '0');
    }

    private static DynamicReportCleanupClosureCommissioningResult Blocked(
        string summary,
        IReadOnlyList<string> evidence,
        ArMms.MmsDynamicReportIedIdentity? identity = null,
        string profilePath = "",
        ArMms.MmsDynamicReportQualificationProfile? profile = null)
        => new()
        {
            IsBlocked = true,
            Summary = summary,
            Identity = identity,
            InputProfile = profile,
            ProfilePath = profilePath,
            EvidenceLines = evidence.ToArray()
        };

    private static DynamicReportCleanupClosureCommissioningResult Failed(
        string summary,
        IReadOnlyList<string> evidence,
        ArMms.MmsDynamicReportIedIdentity identity,
        ArMms.MmsDynamicReportQualificationProfile profile,
        string profilePath,
        string rcbReference,
        string temporaryDataSetReference,
        ArMms.MmsRcbAvailabilitySnapshot? snapshot = null,
        bool temporaryNameAbsent = false,
        bool temporaryDirectoryAbsent = false,
        bool associationHealthy = false)
        => new()
        {
            IsSuccess = false,
            Summary = summary,
            Identity = identity,
            InputProfile = profile,
            FreshRcbSnapshot = snapshot,
            RcbReference = rcbReference,
            TemporaryDataSetReference = temporaryDataSetReference,
            TemporaryDataSetAbsentFromNameList = temporaryNameAbsent,
            TemporaryDataSetDirectoryAbsent = temporaryDirectoryAbsent,
            AssociationHealthy = associationHealthy,
            ProfilePath = profilePath,
            EvidenceLines = evidence.ToArray()
        };

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
