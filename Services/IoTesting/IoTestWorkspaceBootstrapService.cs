using System.Text.Json;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record IoTestWorkspaceLaunchResult(
    IoTestProject Project,
    IoTestSessionController Session,
    IoTestWorkspacePersistence Workspace,
    bool RestoredProgress,
    IReadOnlyList<string> Warnings);

public static class IoTestWorkspaceBootstrapService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Task<IoTestWorkspaceLaunchResult> OpenWorkbookAsync(
        IoTestProject importedProject,
        string workbookPath,
        string localProjectsRoot,
        string evidenceRoot,
        Func<IoTestProject, string, IoTestSessionController> sessionFactory,
        CancellationToken cancellationToken = default)
        => OpenSourcesAsync(
            importedProject,
            new[] { new IoFatSourceInput(workbookPath, IoFatSourceKinds.Workbook) },
            localProjectsRoot,
            evidenceRoot,
            sessionFactory,
            cancellationToken);

    public static async Task<IoTestWorkspaceLaunchResult> OpenSourcesAsync(
        IoTestProject importedProject,
        IReadOnlyCollection<IoFatSourceInput> sourceInputs,
        string localProjectsRoot,
        string evidenceRoot,
        Func<IoTestProject, string, IoTestSessionController> sessionFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importedProject);
        ArgumentNullException.ThrowIfNull(sourceInputs);
        ArgumentNullException.ThrowIfNull(sessionFactory);

        var described = await IoFatSourceWorkspaceService.DescribeAsync(sourceInputs, cancellationToken).ConfigureAwait(false);
        return await OpenDescribedSourcesAsync(
            importedProject,
            described,
            localProjectsRoot,
            evidenceRoot,
            sessionFactory,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Continues FAT bootstrap with source identities already SHA-256-described upstream.
    /// The persistence layer accepts the same immutable descriptions and still verifies the
    /// bytes again while staging them, so this removes redundant full-file hashing without
    /// weakening the source/evidence boundary.
    /// </summary>
    public static async Task<IoTestWorkspaceLaunchResult> OpenDescribedSourcesAsync(
        IoTestProject importedProject,
        IReadOnlyCollection<IoFatDescribedSource> describedSources,
        string localProjectsRoot,
        string evidenceRoot,
        Func<IoTestProject, string, IoTestSessionController> sessionFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importedProject);
        ArgumentNullException.ThrowIfNull(describedSources);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        if (describedSources.Count == 0)
            throw new InvalidDataException("A FAT workspace must contain at least one source file.");

        var described = describedSources.ToArray();
        IoFatSourceIdentity.AttachOrValidate(importedProject, described.Select(source => source.Source).ToArray());

        var localDirectory = ProjectDirectory(localProjectsRoot, importedProject);
        var snapshotPath = Path.Combine(localDirectory, "project.snapshot.json");
        var backupPath = snapshotPath + ".bootstrap-" + Guid.NewGuid().ToString("N");
        var warnings = new List<string>();
        var restored = false;
        var movedSnapshot = false;
        var restoreSnapshotPath = File.Exists(snapshotPath)
            ? snapshotPath
            : FindCompatibleSclSnapshot(localProjectsRoot, importedProject);

        try
        {
            if (!string.IsNullOrWhiteSpace(restoreSnapshotPath) && File.Exists(restoreSnapshotPath))
            {
                // Isolate the canonical persisted snapshot BEFORE attempting selective restore.
                // Otherwise a rejected/invalid partial restore could fall through to
                // IoTestWorkspacePersistence.OpenDescribedSourcesAsync(), which is allowed to restore a
                // matching snapshot wholesale. Engineering is the fresh plan authority here:
                // the old snapshot is read-only evidence input, never a replacement project.
                if (restoreSnapshotPath.Equals(snapshotPath, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(localDirectory);
                    File.Move(snapshotPath, backupPath, true);
                    movedSnapshot = true;
                    restoreSnapshotPath = backupPath;
                }

                try
                {
                    ApplySnapshotProgress(importedProject, restoreSnapshotPath);
                    restored = true;
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
                {
                    warnings.Add($"Local progress could not be restored; a fresh FAT workspace was opened: {ex.Message}");
                }
            }

            var session = sessionFactory(importedProject, evidenceRoot);
            try
            {
                var opened = await IoTestWorkspacePersistence.OpenDescribedSourcesAsync(
                    importedProject,
                    session,
                    described,
                    localProjectsRoot,
                    evidenceRoot,
                    cancellationToken).ConfigureAwait(false);
                warnings.AddRange(opened.Warnings);
                if (movedSnapshot && File.Exists(backupPath))
                    File.Delete(backupPath);
                return new IoTestWorkspaceLaunchResult(
                    opened.Project,
                    session,
                    opened.Workspace,
                    restored || opened.RestoredProgress,
                    warnings);
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }
        catch
        {
            if (movedSnapshot && File.Exists(backupPath) && !File.Exists(snapshotPath))
                File.Move(backupPath, snapshotPath, true);
            throw;
        }
    }

    public static async Task<IoTestWorkspaceLaunchResult> OpenPackageAsync(
        string packagePath,
        string localProjectsRoot,
        string evidenceRoot,
        Func<IoTestProject, string, IoTestSessionController> sessionFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        await IoFatProjectPackageService.ValidateAsync(packagePath, cancellationToken).ConfigureAwait(false);

        IoTestSessionController? createdSession = null;
        var opened = await IoTestWorkspacePersistence.ImportPackageAsync(
            packagePath,
            (project, root) => createdSession = sessionFactory(project, root),
            localProjectsRoot,
            evidenceRoot,
            cancellationToken).ConfigureAwait(false);
        opened.Workspace.SaveNow();
        return new IoTestWorkspaceLaunchResult(
            opened.Project,
            createdSession ?? throw new InvalidOperationException("The IO FAT package did not create a session controller."),
            opened.Workspace,
            true,
            opened.Warnings);
    }

    private static string? FindCompatibleSclSnapshot(
        string localProjectsRoot,
        IoTestProject project)
    {
        if (!IsSclContinuationProject(project) || !Directory.Exists(localProjectsRoot))
            return null;

        string? match = null;
        foreach (var directory in Directory.EnumerateDirectories(localProjectsRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var candidate = Path.Combine(directory, "project.snapshot.json");
            if (!File.Exists(candidate))
                continue;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(candidate));
                var root = document.RootElement;
                if (!RequiredString(root, "snapshotVersion")
                        .Equals(IoTestWorkspacePersistence.SnapshotVersion, StringComparison.Ordinal))
                {
                    continue;
                }

                var savedProject = RequiredObject(root, "project");
                if (!IoFatSourceIdentity.CompatibleContinuationSchema(
                        RequiredString(savedProject, "schemaVersion"),
                        project.SchemaVersion) ||
                    !SnapshotSourceMatches(savedProject, project))
                {
                    continue;
                }

                // Ambiguous compatible snapshots fail closed instead of guessing which
                // operator history owns the reopened source.
                if (match != null)
                    return null;
                match = candidate;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                // A corrupt/unrelated local snapshot must not block normal FAT launch.
            }
        }

        return match;
    }

    private static bool IsSclContinuationProject(IoTestProject project)
        => project.SchemaVersion.StartsWith("ARSAS-FAT-SCL-", StringComparison.OrdinalIgnoreCase) &&
           project.Sources.Count > 0 &&
           project.Sources.All(source =>
               source.Kind.Equals(IoFatSourceKinds.Scl, StringComparison.OrdinalIgnoreCase));

    private static void ApplySnapshotProgress(IoTestProject project, string snapshotPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(snapshotPath));
        var root = document.RootElement;
        var version = RequiredString(root, "snapshotVersion");
        if (!version.Equals(IoTestWorkspacePersistence.SnapshotVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported local snapshot version '{version}'.");

        var savedProject = RequiredObject(root, "project");
        var schemaMatches = IoFatSourceIdentity.CompatibleContinuationSchema(
            RequiredString(savedProject, "schemaVersion"),
            project.SchemaVersion);
        var sourceMatches = SnapshotSourceMatches(savedProject, project);
        var projectIdMatches = RequiredString(savedProject, "projectId")
            .Equals(project.ProjectId, StringComparison.OrdinalIgnoreCase);

        // Historical SCL ProjectId values include filename-derived source identity. For
        // Engineering -> FAT continuation the exact SCL bytes + compatible schema are the
        // authority; per-point IEC fingerprints below still fail closed before evidence
        // can be projected onto a fresh row.
        if (!schemaMatches ||
            !sourceMatches ||
            (!projectIdMatches && !IsSclContinuationProject(project)))
        {
            throw new InvalidDataException("The local snapshot belongs to a different FAT source set or schema.");
        }

        var savedIeds = RequiredArray(savedProject, "ieds").EnumerateArray().ToArray();
        RestoreIedLevelEvidence(project, savedIeds);
        RestoreMissingManualWorkspaceRows(project, savedIeds);

        // Phase B: progress is owned by one physical IED first, then one point inside it.
        // TestPointId is not treated as a project-global key because different IEDs may use
        // the same local point identifier. Duplicate endpoint identities fail closed.
        var savedIedsByIdentity = savedIeds
            .GroupBy(
                ied => IoTestPerIedProgressIdentity.IedKey(
                    OptionalString(ied, "iedName", string.Empty),
                    OptionalString(ied, "ipAddress", string.Empty)),
                StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var currentIedIdentityCounts = project.Ieds
            .GroupBy(IoTestPerIedProgressIdentity.IedKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var ied in project.Ieds)
        {
            var iedKey = IoTestPerIedProgressIdentity.IedKey(ied);
            if (!currentIedIdentityCounts.TryGetValue(iedKey, out var currentIdentityCount) ||
                currentIdentityCount != 1 ||
                !savedIedsByIdentity.TryGetValue(iedKey, out var savedIed) ||
                !IoTestPerIedProgressIdentity.PersistedIedOwnershipMatches(ied, savedIed))
            {
                continue;
            }

            var savedPointArray = RequiredArray(savedIed, "testPoints")
                .EnumerateArray()
                .ToArray();
            var savedPoints = savedPointArray
                .GroupBy(point => RequiredString(point, "testPointId"), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
            var sclContinuation = project.SchemaVersion.StartsWith(
                "ARSAS-FAT-SCL-",
                StringComparison.OrdinalIgnoreCase);

            foreach (var point in ied.TestPoints)
            {
                var hasExactId = savedPoints.TryGetValue(point.TestPointId, out var saved);
                if (!hasExactId && sclContinuation)
                {
                    // SourceId is filename-derived on historical static SCL rows, so the
                    // same IEC member may receive a different TestPointId after reopening
                    // identical SCL bytes through another Engineering staging identity.
                    // Accept only one unique IEC/configuration match; duplicates fail closed.
                    var candidates = savedPointArray
                        .Where(candidate =>
                            IoTestPerIedProgressIdentity.PointConfigurationMatchesForSclContinuation(
                                point,
                                candidate))
                        .Take(2)
                        .ToArray();
                    if (candidates.Length == 1)
                        saved = candidates[0];
                    else
                        continue;
                }

                // Never project old FAT evidence onto a newly imported point just because
                // its TestPointId was reused. Evidence-critical IEC/configuration semantics
                // must still match the deterministic Phase-B fingerprint. SCL continuation
                // may ignore source staging address only; all IEC/DataSet semantics remain
                // part of the fingerprint.
                if (!IoTestPerIedProgressIdentity.PointConfigurationMatches(point, saved) &&
                    !(sclContinuation &&
                      IoTestPerIedProgressIdentity.PointConfigurationMatchesForSclContinuation(point, saved)))
                {
                    continue;
                }

                RestorePointProgress(point, saved);
            }
        }
        project.InitializeRuntimeNotifications();
    }

    private static void RestorePointProgress(IoTestPointPlan point, JsonElement saved)
    {
        // Shared Engineering/FAT membership, FAT TEST scope, and FAT disposition are
        // three independent operator authorities. Same-source continuation restores all
        // three without allowing Remove/Restore to rewrite Engineering selection.
        if (saved.TryGetProperty("workspaceSelected", out var workspaceSelected) &&
            workspaceSelected.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            point.WorkspaceSelected = workspaceSelected.GetBoolean();
        }
        if (saved.TryGetProperty("testEnabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
            point.TestEnabled = enabled.GetBoolean();
        point.RestoreFatDisposition(OptionalEnum(saved, "fatDisposition", FatSignalDisposition.Included));

        if (!saved.TryGetProperty("runtime", out var runtime) || runtime.ValueKind != JsonValueKind.Object)
            return;

        point.Runtime.Attempt = OptionalInt(runtime, "attempt", 0);
        point.Runtime.OnEvidence = OptionalEvidence(runtime, "onEvidence");
        point.Runtime.OffEvidence = OptionalEvidence(runtime, "offEvidence");
        point.Runtime.Value1Evidence = OptionalFatEvidence(runtime, "value1Evidence");
        point.Runtime.Value2Evidence = OptionalFatEvidence(runtime, "value2Evidence");
        point.Runtime.LastObservedState = null;
        point.Runtime.LastSequence = -1;
        point.Runtime.ConnectionGeneration = -1;
        point.Runtime.CurrentValue = "-";
        point.Runtime.CurrentQuality = "Unknown";
        point.Runtime.CurrentSource = "Restored · live baseline required";

        if (point.CaptureMode == FatCaptureMode.OperatorSnapshot)
        {
            point.Runtime.State = IoTestPointState.NotStarted;
            point.Runtime.StatusReason = point.IsFatEvidenceComplete
                ? "Value 1 and Value 2 evidence restored; reconnect live acquisition to recapture either value."
                : point.Runtime.Value1Evidence is not null || point.Runtime.Value2Evidence is not null
                    ? "Partial Value 1 / Value 2 evidence restored; reconnect live acquisition to capture the remaining value."
                    : "Progress restored; reconnect live acquisition before operator snapshot capture.";
            return;
        }

        var savedState = OptionalState(runtime, "state", IoTestPointState.NotStarted);
        if (savedState is IoTestPointState.Passed or IoTestPointState.Review or IoTestPointState.Failed)
        {
            point.Runtime.State = savedState;
            point.Runtime.StatusReason = OptionalString(runtime, "statusReason", "Restored completed result");
        }
        else if (point.Runtime.OnEvidence != null && point.Runtime.OffEvidence == null)
        {
            point.Runtime.State = IoTestPointState.Review;
            point.Runtime.StatusReason = "Progress was restored after the live session ended; OFF continuity after saved ON evidence cannot be proven.";
        }
        else
        {
            point.Runtime.State = IoTestPointState.NotStarted;
            point.Runtime.StatusReason = "Progress restored; a new good-quality live baseline is required before continuing.";
        }
    }

    private static bool SnapshotSourceMatches(JsonElement savedProject, IoTestProject project)
    {
        var expectedSet = IoFatSourceIdentity.ProjectSourceFingerprint(project);
        var savedSet = OptionalString(savedProject, "sourceSetSha256", string.Empty);
        if (!string.IsNullOrWhiteSpace(savedSet) &&
            savedSet.Equals(expectedSet, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Engineering and FAT are two views over one SCL authority. Across compatible
        // ARSAS revisions the same SCL bytes may carry different filename/SourceId staging
        // metadata, so continuation falls back to content SHA-256 only for SCL-only sets.
        // Point evidence is still gated later by PointConfigurationMatches().
        if (TrySnapshotSources(savedProject, out var savedSources) &&
            IoFatSourceIdentity.SameSclContentSet(savedSources, project.Sources))
        {
            return true;
        }

        var legacyWorkbookSha = OptionalString(savedProject, "sourceWorkbookSha256", string.Empty);
        return !string.IsNullOrWhiteSpace(legacyWorkbookSha) &&
               legacyWorkbookSha.Equals(project.SourceWorkbookSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySnapshotSources(
        JsonElement savedProject,
        out IReadOnlyList<IoFatSourceDescriptor> sources)
    {
        sources = Array.Empty<IoFatSourceDescriptor>();
        if (!savedProject.TryGetProperty("sources", out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<IoFatSourceDescriptor>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                return false;

            var kind = OptionalString(item, "kind", string.Empty);
            var sha256 = OptionalString(item, "sha256", string.Empty);
            if (!kind.Equals(IoFatSourceKinds.Scl, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(sha256))
            {
                return false;
            }

            parsed.Add(new IoFatSourceDescriptor(
                SourceId: string.Empty,
                Kind: IoFatSourceKinds.Scl,
                FileName: "snapshot.scl",
                Sha256: sha256,
                Length: 0));
        }

        sources = parsed;
        return parsed.Count > 0;
    }

    private static void RestoreMissingManualWorkspaceRows(
        IoTestProject project,
        IReadOnlyList<JsonElement> savedIeds)
    {
        if (!project.SchemaVersion.StartsWith("ARSAS-FAT-SCL-", StringComparison.OrdinalIgnoreCase))
            return;

        var trustedSclHashes = project.Sources
            .Where(source => source.Kind.Equals(IoFatSourceKinds.Scl, StringComparison.OrdinalIgnoreCase))
            .Select(source => source.Sha256)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (trustedSclHashes.Count == 0)
            return;

        foreach (var ied in project.Ieds)
        {
            var savedIed = savedIeds.FirstOrDefault(candidate =>
                OptionalString(candidate, "iedName", string.Empty).Equals(ied.IedName, StringComparison.OrdinalIgnoreCase) &&
                OptionalString(candidate, "ipAddress", string.Empty).Equals(ied.IpAddress, StringComparison.OrdinalIgnoreCase));
            if (savedIed.ValueKind != JsonValueKind.Object ||
                !IoTestPerIedProgressIdentity.PersistedIedOwnershipMatches(ied, savedIed))
            {
                continue;
            }

            var existingIds = ied.TestPoints
                .Select(point => point.TestPointId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var savedPoint in RequiredArray(savedIed, "testPoints").EnumerateArray())
            {
                var testPointId = RequiredString(savedPoint, "testPointId");
                if (existingIds.Contains(testPointId))
                    continue;

                var bindingStatus = OptionalString(savedPoint, "bindingStatus", string.Empty);
                if (!bindingStatus.Equals(
                        IoTestSignalSelectionService.SclWorkspaceAuthorityBindingStatus,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!testPointId.StartsWith("scl-manual-", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Saved manual SCL row '{testPointId}' has an invalid workspace identity.");

                var savedIedName = RequiredString(savedPoint, "iedName");
                var savedIpAddress = RequiredString(savedPoint, "ipAddress");
                if (!savedIedName.Equals(ied.IedName, StringComparison.OrdinalIgnoreCase) ||
                    !savedIpAddress.Equals(ied.IpAddress, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Saved manual SCL row '{testPointId}' does not belong to its persisted IED endpoint.");
                }

                var sourceSha = OptionalString(savedPoint, "signalAddress", string.Empty).Trim();
                if (sourceSha.Length == 0 || !trustedSclHashes.Contains(sourceSha))
                {
                    throw new InvalidDataException(
                        $"Saved manual SCL row '{testPointId}' does not belong to the exact reopened SCL source set.");
                }

                var objectReference = RequiredString(savedPoint, "objectReference");
                var sourceReference = OptionalString(savedPoint, "sourceIecReference", string.Empty);
                if (string.IsNullOrWhiteSpace(objectReference) || string.IsNullOrWhiteSpace(sourceReference))
                    throw new InvalidDataException($"Saved manual SCL row '{testPointId}' has incomplete IEC 61850 identity.");

                var point = new IoTestPointPlan
                {
                    TestPointId = testPointId,
                    IedName = savedIedName,
                    IpAddress = savedIpAddress,
                    SignalName = RequiredString(savedPoint, "signalName"),
                    ObjectReference = objectReference,
                    FunctionalConstraint = OptionalString(savedPoint, "functionalConstraint", string.Empty),
                    ExpectedOnText = RequiredString(savedPoint, "expectedOnText"),
                    ExpectedOffText = RequiredString(savedPoint, "expectedOffText"),
                    ExpectedOnRaw = OptionalInt(savedPoint, "expectedOnRaw", 1),
                    ExpectedOffRaw = OptionalInt(savedPoint, "expectedOffRaw", 0),
                    DataType = OptionalString(savedPoint, "dataType", "SDI"),
                    SignalAddress = sourceSha,
                    DataSetName = OptionalString(savedPoint, "dataSetName", string.Empty),
                    LogicalDevice = OptionalString(savedPoint, "logicalDevice", string.Empty),
                    LogicalNode = OptionalString(savedPoint, "logicalNode", string.Empty),
                    DataObject = OptionalString(savedPoint, "dataObject", string.Empty),
                    DataAttribute = OptionalString(savedPoint, "dataAttribute", string.Empty),
                    Cdc = OptionalString(savedPoint, "cdc", string.Empty),
                    SourceIecReference = sourceReference,
                    ReportDisplayReference = OptionalString(savedPoint, "reportDisplayReference", sourceReference),
                    EventLogSearchReference = OptionalString(savedPoint, "eventLogSearchReference", objectReference),
                    EvidenceExpected = OptionalString(savedPoint, "evidenceExpected", string.Empty),
                    MappingQuality = OptionalString(savedPoint, "mappingQuality", string.Empty),
                    ReviewStatus = OptionalString(savedPoint, "reviewStatus", string.Empty),
                    ReviewReason = OptionalString(savedPoint, "reviewReason", string.Empty),
                    EventLogMatch = OptionalString(savedPoint, "eventLogMatch", string.Empty),
                    EvidenceReference = OptionalString(savedPoint, "evidenceReference", string.Empty),
                    ReviewerComment = OptionalString(savedPoint, "reviewerComment", string.Empty),
                    SourceSheet = OptionalString(savedPoint, "sourceSheet", string.Empty),
                    SourceRow = OptionalInt(savedPoint, "sourceRow", 0),
                    SignalKind = OptionalEnum(savedPoint, "signalKind", FatSignalKind.Other),
                    CaptureMode = OptionalEnum(savedPoint, "captureMode", FatCaptureMode.OperatorSnapshot),
                    WorkspaceSelected = OptionalBool(savedPoint, "workspaceSelected", true),
                    TestEnabled = OptionalBool(savedPoint, "testEnabled", true),
                    ImportReady = OptionalBool(savedPoint, "importReady", true),
                    BindingStatus = bindingStatus,
                    BindingEvidence = OptionalString(savedPoint, "bindingEvidence", string.Empty)
                };

                if (!ied.AddTestPoint(point))
                    throw new InvalidDataException($"Saved manual SCL row '{testPointId}' collides with the reopened workspace.");
                existingIds.Add(testPointId);
            }
        }
    }

    private static void RestoreIedLevelEvidence(IoTestProject project, IReadOnlyList<JsonElement> savedIeds)
    {
        foreach (var ied in project.Ieds)
        {
            var saved = savedIeds.FirstOrDefault(candidate =>
                OptionalString(candidate, "iedName", string.Empty).Equals(ied.IedName, StringComparison.OrdinalIgnoreCase) &&
                OptionalString(candidate, "ipAddress", string.Empty).Equals(ied.IpAddress, StringComparison.OrdinalIgnoreCase));
            if (saved.ValueKind != JsonValueKind.Object ||
                !IoTestPerIedProgressIdentity.PersistedIedOwnershipMatches(ied, saved))
            {
                continue;
            }

            ied.LatestComtradeFiles = OptionalString(saved, "latestComtradeFiles", string.Empty);
            ied.LatestComtradeRemotePath = OptionalString(saved, "latestComtradeRemotePath", string.Empty);
            ied.LatestComtradeCompleteness = OptionalString(saved, "latestComtradeCompleteness", string.Empty);
            ied.LatestComtradeAcquisitionSource = OptionalString(saved, "latestComtradeAcquisitionSource", string.Empty);
            ied.LatestComtradeModifiedAtUtc = OptionalDateTimeOffset(saved, "latestComtradeModifiedAtUtc");
            ied.LatestComtradeCapturedAtUtc = OptionalDateTimeOffset(saved, "latestComtradeCapturedAtUtc");
            ied.LatestComtradeFileCount = OptionalInt(saved, "latestComtradeFileCount", 0);
            ied.LatestComtradeKnownSizeBytes = OptionalLong(saved, "latestComtradeKnownSizeBytes", 0L);
        }
    }

    private static IoTestTransitionEvidence? OptionalEvidence(JsonElement runtime, string property)
    {
        if (!runtime.TryGetProperty(property, out var element) || element.ValueKind == JsonValueKind.Null)
            return null;
        return element.Deserialize<IoTestTransitionEvidence>(JsonOptions);
    }

    private static FatValueEvidence? OptionalFatEvidence(JsonElement runtime, string property)
    {
        if (!runtime.TryGetProperty(property, out var element) || element.ValueKind == JsonValueKind.Null)
            return null;
        return element.Deserialize<FatValueEvidence>(JsonOptions);
    }

    private static TEnum OptionalEnum<TEnum>(JsonElement parent, string property, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (!parent.TryGetProperty(property, out var value))
            return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && Enum.IsDefined(typeof(TEnum), number))
            return (TEnum)Enum.ToObject(typeof(TEnum), number);
        if (value.ValueKind == JsonValueKind.String && Enum.TryParse<TEnum>(value.GetString(), ignoreCase: true, out var parsed))
            return parsed;
        return fallback;
    }

    private static IoTestPointState OptionalState(
        JsonElement parent,
        string property,
        IoTestPointState fallback)
        => OptionalEnum(parent, property, fallback);

    private static JsonElement RequiredObject(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Snapshot property '{property}' is missing or invalid.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Snapshot property '{property}' is missing or invalid.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Snapshot property '{property}' is missing or invalid.");
        return value.GetString() ?? string.Empty;
    }

    private static string OptionalString(JsonElement parent, string property, string fallback)
        => parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool OptionalBool(JsonElement parent, string property, bool fallback)
        => parent.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static int OptionalInt(JsonElement parent, string property, int fallback)
        => parent.TryGetProperty(property, out var value) && value.TryGetInt32(out var number)
            ? number
            : fallback;

    private static long OptionalLong(JsonElement parent, string property, long fallback)
        => parent.TryGetProperty(property, out var value) && value.TryGetInt64(out var number)
            ? number
            : fallback;

    private static DateTimeOffset? OptionalDateTimeOffset(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String &&
           value.TryGetDateTimeOffset(out var timestamp)
            ? timestamp
            : null;

    private static string ProjectDirectory(string root, IoTestProject project)
    {
        var fingerprint = IoFatSourceIdentity.ProjectStorageFingerprint(project);
        var hash = string.IsNullOrWhiteSpace(fingerprint)
            ? "nohash"
            : fingerprint[..Math.Min(12, fingerprint.Length)];
        return Path.Combine(root, $"{Sanitize(project.ProjectId)}_{hash}");
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string((value ?? "IO-TEST").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return result.Length == 0 ? "IO-TEST" : result;
    }
}
