using System.ComponentModel;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record IoTestWorkspaceOpenResult(
    IoTestProject Project,
    IoTestWorkspacePersistence Workspace,
    bool RestoredProgress,
    IReadOnlyList<string> Warnings);

public sealed class IoTestWorkspacePersistence : ObservableObject, IDisposable
{
    public const string PackageExtension = ".arsas-iofat";
    public const string SnapshotVersion = "ARSAS-IOFAT-SNAPSHOT-1.0";
    public const string PackageVersion = "ARSAS-IOFAT-PACKAGE-1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _saveSync = new();
    private readonly Timer _saveTimer;
    private readonly IoTestSessionController _session;
    private bool _disposed;
    private string _statusText = "Progress storage is ready";
    private DateTimeOffset? _lastSavedAtUtc;
    private string _lastExportPath = string.Empty;

    private IoTestWorkspacePersistence(
        IoTestProject project,
        IoTestSessionController session,
        string localDirectory,
        IReadOnlyList<IoFatWorkspaceSource> sourceFiles,
        string evidenceProjectDirectory)
    {
        Project = project;
        _session = session;
        LocalDirectory = localDirectory;
        SnapshotPath = Path.Combine(localDirectory, "project.snapshot.json");
        SourceFiles = sourceFiles;
        EvidenceProjectDirectory = evidenceProjectDirectory;
        _saveTimer = new Timer(_ => SaveFromTimer(), null, Timeout.Infinite, Timeout.Infinite);
        Subscribe();
    }

    public IoTestProject Project { get; }
    public string LocalDirectory { get; }
    public string SnapshotPath { get; }
    public IReadOnlyList<IoFatWorkspaceSource> SourceFiles { get; }
    public string SourceWorkbookPath => SourceFiles
        .FirstOrDefault(source => source.Source.Kind.Equals(IoFatSourceKinds.Workbook, StringComparison.OrdinalIgnoreCase))
        ?.LocalPath ?? string.Empty;
    public string PrimarySourcePath => SourceFiles.FirstOrDefault()?.LocalPath ?? string.Empty;
    public string EvidenceProjectDirectory { get; }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value ?? string.Empty); }
    public DateTimeOffset? LastSavedAtUtc { get => _lastSavedAtUtc; private set { if (Set(ref _lastSavedAtUtc, value)) Raise(nameof(LastSavedText)); } }
    public string LastSavedText => LastSavedAtUtc == null
        ? "Not saved yet"
        : $"Saved locally {LastSavedAtUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
    public string LastExportPath { get => _lastExportPath; private set => Set(ref _lastExportPath, value ?? string.Empty); }
    public bool CanExport => !_session.IsSessionActive;

    public static Task<IoTestWorkspaceOpenResult> OpenWorkbookAsync(
        IoTestProject importedProject,
        IoTestSessionController session,
        string workbookPath,
        string localProjectsRoot,
        string evidenceRoot,
        CancellationToken cancellationToken = default)
        => OpenSourcesAsync(
            importedProject,
            session,
            new[] { new IoFatSourceInput(workbookPath, IoFatSourceKinds.Workbook) },
            localProjectsRoot,
            evidenceRoot,
            cancellationToken);

    public static async Task<IoTestWorkspaceOpenResult> OpenSourcesAsync(
        IoTestProject importedProject,
        IoTestSessionController session,
        IReadOnlyCollection<IoFatSourceInput> sourceInputs,
        string localProjectsRoot,
        string evidenceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importedProject);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sourceInputs);
        cancellationToken.ThrowIfCancellationRequested();

        var described = await IoFatSourceWorkspaceService.DescribeAsync(sourceInputs, cancellationToken).ConfigureAwait(false);
        IoFatSourceIdentity.AttachOrValidate(importedProject, described.Select(source => source.Source).ToArray());

        var localDirectory = ProjectDirectory(localProjectsRoot, importedProject);
        Directory.CreateDirectory(localDirectory);
        var localSourceDirectory = Path.Combine(localDirectory, "source");
        var sourceFiles = await IoFatSourceWorkspaceService.StageDescribedAsync(
            importedProject,
            described,
            localSourceDirectory,
            cancellationToken).ConfigureAwait(false);

        var snapshotPath = Path.Combine(localDirectory, "project.snapshot.json");
        var project = importedProject;
        var restored = false;
        var warnings = new List<string>();
        if (File.Exists(snapshotPath))
        {
            try
            {
                var candidate = await LoadSnapshotAsync(snapshotPath, cancellationToken).ConfigureAwait(false);
                if (candidate.ProjectId.Equals(importedProject.ProjectId, StringComparison.OrdinalIgnoreCase) &&
                    candidate.SchemaVersion.Equals(importedProject.SchemaVersion, StringComparison.OrdinalIgnoreCase) &&
                    SameSourceSet(candidate, importedProject))
                {
                    project = candidate;
                    restored = true;
                }
                else
                {
                    warnings.Add("A local snapshot existed but did not match the current FAT source-set identity, so it was not restored.");
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                warnings.Add($"The local snapshot could not be restored and a fresh workspace was opened: {ex.Message}");
            }
        }

        IoFatSourceIdentity.AttachOrValidate(project, sourceFiles.Select(source => source.Source).ToArray());
        project.InitializeRuntimeNotifications();
        var evidenceDirectory = Path.Combine(evidenceRoot, SanitizePathPart(project.ProjectId));
        var workspace = new IoTestWorkspacePersistence(project, session, localDirectory, sourceFiles, evidenceDirectory);
        workspace.SaveNow();
        return new IoTestWorkspaceOpenResult(project, workspace, restored, warnings);
    }

    public static async Task<IoTestWorkspaceOpenResult> ImportPackageAsync(
        string packagePath,
        IoTestSessionControllerFactory sessionFactory,
        string localProjectsRoot,
        string evidenceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("The IO FAT handover package was not found.", packagePath);
        if (new FileInfo(packagePath).Length > 500L * 1024 * 1024)
            throw new InvalidDataException("The IO FAT handover package exceeds the 500 MB safety limit.");

        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > 10_000)
            throw new InvalidDataException("The IO FAT handover package contains too many entries.");

        var manifestEntry = RequiredEntry(archive, "manifest.json");
        var manifestBytes = await ReadEntryAsync(manifestEntry, 5 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<IoTestPackageManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("The IO FAT package manifest is invalid.");
        if (!manifest.PackageVersion.Equals(PackageVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported IO FAT package version '{manifest.PackageVersion}'.");

        var snapshotEntry = RequiredEntry(archive, manifest.SnapshotEntry);
        var snapshotBytes = await ReadEntryAsync(snapshotEntry, 100 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        VerifyHash(snapshotBytes, manifest.SnapshotSha256, "project snapshot");
        var snapshot = JsonSerializer.Deserialize<IoTestProjectSnapshot>(snapshotBytes, JsonOptions)
            ?? throw new InvalidDataException("The IO FAT project snapshot is invalid.");
        var project = RestoreProject(snapshot);
        if (!project.ProjectId.Equals(manifest.ProjectId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The package project identity does not match its snapshot.");

        var manifestSources = manifest.SourceFiles ?? new List<IoFatPackageSource>();
        if (manifestSources.Count > 0 && !string.IsNullOrWhiteSpace(manifest.SourceSetSha256))
        {
            var manifestFingerprint = IoFatSourceIdentity.ComputeSetFingerprint(manifestSources.Select(source => source.Descriptor));
            if (!manifestFingerprint.Equals(manifest.SourceSetSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The package FAT source-set fingerprint is invalid.");
        }

        if (manifestSources.Count == 0 && project.Sources.Count > 1)
            throw new InvalidDataException("The package snapshot declares multiple FAT sources but its manifest contains only the legacy workbook contract.");

        var localDirectory = ProjectDirectory(localProjectsRoot, project);
        Directory.CreateDirectory(localDirectory);
        var sourceDirectory = Path.Combine(localDirectory, "source");
        var sourceFiles = await IoFatSourceWorkspaceService.ImportAsync(
            project,
            archive,
            manifestSources,
            manifest.SourceWorkbookEntry,
            string.IsNullOrWhiteSpace(manifest.SourceWorkbookSha256)
                ? project.SourceWorkbookSha256
                : manifest.SourceWorkbookSha256,
            sourceDirectory,
            cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(manifest.SourceSetSha256) &&
            !IoFatSourceIdentity.ComputeSetFingerprint(sourceFiles.Select(source => source.Source))
                .Equals(manifest.SourceSetSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Imported FAT sources do not match the package source-set fingerprint.");
        }

        var evidenceDirectory = Path.Combine(evidenceRoot, SanitizePathPart(project.ProjectId));
        Directory.CreateDirectory(evidenceDirectory);
        var warnings = new List<string>();
        foreach (var evidence in manifest.EvidenceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = RequiredEntry(archive, evidence.Entry);
            var bytes = await ReadEntryAsync(entry, 100 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
            VerifyHash(bytes, evidence.Sha256, $"evidence '{evidence.Entry}'");
            var destination = Path.Combine(evidenceDirectory, SafeFileName(Path.GetFileName(evidence.Entry), "evidence.jsonl"));
            if (File.Exists(destination))
            {
                var existingHash = HashFile(destination);
                if (existingHash.Equals(evidence.Sha256, StringComparison.OrdinalIgnoreCase))
                    continue;
                destination = Path.Combine(
                    evidenceDirectory,
                    $"imported_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{SafeFileName(Path.GetFileName(evidence.Entry), "evidence.jsonl")}");
                warnings.Add($"Evidence filename collision was preserved as '{Path.GetFileName(destination)}'.");
            }
            await WriteFileAtomicAsync(destination, bytes, cancellationToken).ConfigureAwait(false);
            var verification = IoTestEvidenceJournal.Verify(destination);
            if (!verification.IsValid)
                throw new InvalidDataException($"Imported evidence failed hash-chain verification: {verification.Error}");
        }

        await WriteFileAtomicAsync(Path.Combine(localDirectory, "project.snapshot.json"), snapshotBytes, cancellationToken).ConfigureAwait(false);
        project.InitializeRuntimeNotifications();
        var session = sessionFactory(project, evidenceRoot);
        var workspace = new IoTestWorkspacePersistence(project, session, localDirectory, sourceFiles, evidenceDirectory);
        workspace.StatusText = $"Handover package imported from {Path.GetFileName(packagePath)}";
        workspace.LastSavedAtUtc = snapshot.SavedAtUtc;
        workspace.SaveNow();
        return new IoTestWorkspaceOpenResult(project, workspace, true, warnings);
    }

    public void ScheduleSave()
    {
        if (_disposed)
            return;
        StatusText = "Progress changed · saving shortly…";
        _saveTimer.Change(650, Timeout.Infinite);
    }

    public void SaveNow()
    {
        ThrowIfDisposed();
        lock (_saveSync)
        {
            Directory.CreateDirectory(LocalDirectory);
            var snapshot = CaptureSnapshot(Project);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
            WriteFileAtomic(SnapshotPath, bytes);
            LastSavedAtUtc = snapshot.SavedAtUtc;
            StatusText = $"Progress saved locally · {Path.GetFileName(SnapshotPath)}";
        }
    }

    public async Task ExportPackageAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (_session.IsSessionActive)
            throw new InvalidOperationException("Stop the active FAT session before exporting a handover package so every evidence journal is sealed.");

        SaveNow();
        var snapshotBytes = await File.ReadAllBytesAsync(SnapshotPath, cancellationToken).ConfigureAwait(false);
        var sourcePayloads = new List<(IoFatWorkspaceSource Source, byte[] Bytes)>();
        foreach (var source in SourceFiles)
        {
            sourcePayloads.Add((
                source,
                await IoFatSourceWorkspaceService.ReadVerifiedAsync(source, cancellationToken).ConfigureAwait(false)));
        }
        var packageSources = IoFatSourceWorkspaceService.ToPackageSources(SourceFiles).ToList();
        var sourceSetSha256 = IoFatSourceIdentity.ComputeSetFingerprint(SourceFiles.Select(source => source.Source));
        var workbook = SourceFiles.FirstOrDefault(source =>
            source.Source.Kind.Equals(IoFatSourceKinds.Workbook, StringComparison.OrdinalIgnoreCase));

        var evidenceFiles = new List<IoTestPackageEvidence>();
        if (Directory.Exists(EvidenceProjectDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(EvidenceProjectDirectory, "*.evidence.jsonl", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var verification = IoTestEvidenceJournal.Verify(path);
                if (!verification.IsValid)
                    throw new InvalidDataException($"Evidence '{Path.GetFileName(path)}' failed verification: {verification.Error}");
                evidenceFiles.Add(new IoTestPackageEvidence(
                    $"evidence/{Path.GetFileName(path)}",
                    HashFile(path),
                    verification.RecordCount,
                    verification.LastHash));
            }
        }

        var snapshotHash = HashBytes(snapshotBytes);
        var reportBytes = Encoding.UTF8.GetBytes(BuildPrintableReport(Project));
        var manifest = new IoTestPackageManifest(
            PackageVersion,
            DateTimeOffset.UtcNow,
            Project.ProjectId,
            Project.ProjectName,
            Project.SchemaVersion,
            "project.snapshot.json",
            snapshotHash,
            workbook?.PackageEntry ?? string.Empty,
            workbook?.Source.Sha256 ?? string.Empty,
            "report/IO-FAT-Report.html",
            evidenceFiles)
        {
            SourceSetSha256 = sourceSetSha256,
            SourceFiles = packageSources
        };

        var fullDestination = destinationPath.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase)
            ? destinationPath
            : destinationPath + PackageExtension;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(fullDestination))!);
        var temporary = fullDestination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                await WriteEntryAsync(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions), cancellationToken).ConfigureAwait(false);
                await WriteEntryAsync(archive, manifest.SnapshotEntry, snapshotBytes, cancellationToken).ConfigureAwait(false);
                foreach (var source in sourcePayloads)
                    await WriteEntryAsync(archive, source.Source.PackageEntry, source.Bytes, cancellationToken).ConfigureAwait(false);
                await WriteEntryAsync(archive, manifest.ReportEntry, reportBytes, cancellationToken).ConfigureAwait(false);
                await WriteEntryAsync(archive, "README.txt", Encoding.UTF8.GetBytes(BuildPackageReadme()), cancellationToken).ConfigureAwait(false);
                foreach (var evidence in evidenceFiles)
                {
                    var path = Path.Combine(EvidenceProjectDirectory, Path.GetFileName(evidence.Entry));
                    await WriteEntryAsync(archive, evidence.Entry, await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                }
            }
            File.Move(temporary, fullDestination, true);
            LastExportPath = fullDestination;
            StatusText = $"Portable handover exported · {Path.GetFileName(fullDestination)}";
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        try
        {
            SaveNow();
        }
        catch
        {
            // Window shutdown must not be blocked by a secondary autosave failure.
        }
        _disposed = true;
        _saveTimer.Dispose();
        Unsubscribe();
    }

    private void Subscribe()
    {
        _session.PropertyChanged += Changed;
        foreach (var ied in Project.Ieds)
        {
            ied.PropertyChanged += Changed;
            foreach (var point in ied.TestPoints)
            {
                point.PropertyChanged += Changed;
                point.Runtime.PropertyChanged += Changed;
            }
        }
    }

    private void Unsubscribe()
    {
        _session.PropertyChanged -= Changed;
        foreach (var ied in Project.Ieds)
        {
            ied.PropertyChanged -= Changed;
            foreach (var point in ied.TestPoints)
            {
                point.PropertyChanged -= Changed;
                point.Runtime.PropertyChanged -= Changed;
            }
        }
    }

    private void Changed(object? sender, PropertyChangedEventArgs e) => ScheduleSave();

    private void SaveFromTimer()
    {
        try
        {
            SaveNow();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText = $"Autosave failed: {ex.Message}";
        }
    }

    private static IoTestProjectSnapshot CaptureSnapshot(IoTestProject project) => new(
        SnapshotVersion,
        DateTimeOffset.UtcNow,
        new IoTestProjectData(
            project.ProjectId,
            project.SchemaVersion,
            project.ProjectName,
            project.SourceWorkbookName,
            project.SourceWorkbookSha256,
            project.ImportedAt,
            project.Ieds.Select(ied => new IoTestIedData(
                ied.IedName,
                ied.IpAddress,
                ied.IedRole,
                ied.Location,
                ied.VoltageLevel,
                ied.Switchgear,
                ied.TestPoints.Select(point => new IoTestPointData(
                    point.TestPointId,
                    point.IedName,
                    point.IpAddress,
                    point.SignalName,
                    point.ObjectReference,
                    point.FunctionalConstraint,
                    point.ExpectedOnText,
                    point.ExpectedOffText,
                    point.ExpectedOnRaw,
                    point.ExpectedOffRaw,
                    point.DataType,
                    point.SignalAddress,
                    point.DataSetName,
                    point.LogicalDevice,
                    point.LogicalNode,
                    point.DataObject,
                    point.DataAttribute,
                    point.SourceSheet,
                    point.SourceRow,
                    point.TestEnabled,
                    point.ImportReady,
                    point.BindingStatus,
                    point.BindingEvidence,
                    new IoTestRuntimeData(
                        point.Runtime.State,
                        point.Runtime.LastObservedState,
                        point.Runtime.LastSequence,
                        point.Runtime.ConnectionGeneration,
                        point.Runtime.OnEvidence,
                        point.Runtime.OffEvidence,
                        point.Runtime.StatusReason,
                        point.Runtime.Attempt,
                        point.Runtime.CurrentValue,
                        point.Runtime.CurrentQuality,
                        point.Runtime.CurrentSource)
                    {
                        Value1Evidence = point.Runtime.Value1Evidence,
                        Value2Evidence = point.Runtime.Value2Evidence
                    })
                {
                    SignalKind = point.SignalKind,
                    CaptureMode = point.CaptureMode,
                    FatDisposition = point.FatDisposition
                }).ToList())
            {
                LatestComtradeFiles = ied.LatestComtradeFiles,
                LatestComtradeRemotePath = ied.LatestComtradeRemotePath,
                LatestComtradeCompleteness = ied.LatestComtradeCompleteness,
                LatestComtradeAcquisitionSource = ied.LatestComtradeAcquisitionSource,
                LatestComtradeModifiedAtUtc = ied.LatestComtradeModifiedAtUtc,
                LatestComtradeCapturedAtUtc = ied.LatestComtradeCapturedAtUtc,
                LatestComtradeFileCount = ied.LatestComtradeFileCount,
                LatestComtradeKnownSizeBytes = ied.LatestComtradeKnownSizeBytes
            }).ToList())
        {
            SourceSetSha256 = IoFatSourceIdentity.ProjectSourceFingerprint(project),
            Sources = project.Sources.Select(IoFatSourceIdentity.Normalize).ToList()
        });

    private static async Task<IoTestProject> LoadSnapshotAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var snapshot = JsonSerializer.Deserialize<IoTestProjectSnapshot>(bytes, JsonOptions)
            ?? throw new InvalidDataException("Local IO FAT snapshot is invalid.");
        return RestoreProject(snapshot);
    }

    private static IoTestProject RestoreProject(IoTestProjectSnapshot snapshot)
    {
        if (!snapshot.SnapshotVersion.Equals(SnapshotVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported IO FAT snapshot version '{snapshot.SnapshotVersion}'.");

        var project = new IoTestProject
        {
            ProjectId = snapshot.Project.ProjectId,
            SchemaVersion = snapshot.Project.SchemaVersion,
            ProjectName = snapshot.Project.ProjectName,
            SourceWorkbookName = snapshot.Project.SourceWorkbookName,
            SourceWorkbookSha256 = snapshot.Project.SourceWorkbookSha256,
            ImportedAt = snapshot.Project.ImportedAt,
            Ieds = snapshot.Project.Ieds.Select(ied => new IoTestIedPlan
            {
                IedName = ied.IedName,
                IpAddress = ied.IpAddress,
                IedRole = ied.IedRole,
                Location = ied.Location,
                VoltageLevel = ied.VoltageLevel,
                Switchgear = ied.Switchgear,
                LatestComtradeFiles = ied.LatestComtradeFiles ?? string.Empty,
                LatestComtradeRemotePath = ied.LatestComtradeRemotePath ?? string.Empty,
                LatestComtradeCompleteness = ied.LatestComtradeCompleteness ?? string.Empty,
                LatestComtradeAcquisitionSource = ied.LatestComtradeAcquisitionSource ?? string.Empty,
                LatestComtradeModifiedAtUtc = ied.LatestComtradeModifiedAtUtc,
                LatestComtradeCapturedAtUtc = ied.LatestComtradeCapturedAtUtc,
                LatestComtradeFileCount = ied.LatestComtradeFileCount,
                LatestComtradeKnownSizeBytes = ied.LatestComtradeKnownSizeBytes,
                TestPoints = ied.TestPoints.Select(RestorePoint).ToList()
            }).ToList()
        };

        var sources = snapshot.Project.Sources ?? new List<IoFatSourceDescriptor>();
        if (sources.Count > 0)
        {
            var normalized = sources.Select(IoFatSourceIdentity.Normalize).ToArray();
            var fingerprint = IoFatSourceIdentity.ComputeSetFingerprint(normalized);
            if (!string.IsNullOrWhiteSpace(snapshot.Project.SourceSetSha256) &&
                !fingerprint.Equals(snapshot.Project.SourceSetSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The local snapshot FAT source-set fingerprint is invalid.");
            }
            project.SetSources(normalized, fingerprint);
        }
        else if (!string.IsNullOrWhiteSpace(project.SourceWorkbookSha256))
        {
            var legacy = IoFatSourceIdentity.LegacyWorkbook(project.SourceWorkbookName, project.SourceWorkbookSha256);
            project.SetSources(new[] { legacy }, IoFatSourceIdentity.ComputeSetFingerprint(new[] { legacy }));
        }

        project.InitializeRuntimeNotifications();
        return project;
    }

    private static IoTestPointPlan RestorePoint(IoTestPointData data)
    {
        var point = new IoTestPointPlan
        {
            TestPointId = data.TestPointId,
            IedName = data.IedName,
            IpAddress = data.IpAddress,
            SignalName = data.SignalName,
            ObjectReference = data.ObjectReference,
            FunctionalConstraint = data.FunctionalConstraint,
            ExpectedOnText = data.ExpectedOnText,
            ExpectedOffText = data.ExpectedOffText,
            ExpectedOnRaw = data.ExpectedOnRaw,
            ExpectedOffRaw = data.ExpectedOffRaw,
            DataType = data.DataType,
            SignalAddress = data.SignalAddress,
            DataSetName = data.DataSetName,
            LogicalDevice = data.LogicalDevice,
            LogicalNode = data.LogicalNode,
            DataObject = data.DataObject,
            DataAttribute = data.DataAttribute,
            SourceSheet = data.SourceSheet,
            SourceRow = data.SourceRow,
            SignalKind = data.SignalKind,
            CaptureMode = data.CaptureMode,
            TestEnabled = data.TestEnabled,
            ImportReady = data.ImportReady,
            BindingStatus = data.BindingStatus,
            BindingEvidence = data.BindingEvidence
        };
        point.RestoreFatDisposition(data.FatDisposition);

        var runtime = point.Runtime;
        runtime.Attempt = data.Runtime.Attempt;
        runtime.OnEvidence = data.Runtime.OnEvidence;
        runtime.OffEvidence = data.Runtime.OffEvidence;
        runtime.Value1Evidence = data.Runtime.Value1Evidence;
        runtime.Value2Evidence = data.Runtime.Value2Evidence;
        runtime.CurrentValue = "-";
        runtime.CurrentQuality = "Unknown";
        runtime.CurrentSource = "Restored · live baseline required";
        runtime.LastObservedState = null;
        runtime.LastSequence = -1;
        runtime.ConnectionGeneration = -1;

        if (point.CaptureMode == FatCaptureMode.OperatorSnapshot)
        {
            runtime.State = IoTestPointState.NotStarted;
            runtime.StatusReason = point.IsFatEvidenceComplete
                ? "Value 1 and Value 2 evidence restored; reconnect live acquisition to recapture either value."
                : runtime.Value1Evidence is not null || runtime.Value2Evidence is not null
                    ? "Partial Value 1 / Value 2 evidence restored; reconnect live acquisition to capture the remaining value."
                    : "Progress restored; reconnect live acquisition before operator snapshot capture.";
        }
        else if (data.Runtime.State is IoTestPointState.Passed or IoTestPointState.Review or IoTestPointState.Failed)
        {
            runtime.State = data.Runtime.State;
            runtime.StatusReason = data.Runtime.StatusReason;
        }
        else if (data.Runtime.OnEvidence != null && data.Runtime.OffEvidence == null)
        {
            runtime.State = IoTestPointState.Review;
            runtime.StatusReason = "Progress was restored after the live session ended; OFF continuity after saved ON evidence cannot be proven.";
        }
        else
        {
            runtime.State = IoTestPointState.NotStarted;
            runtime.StatusReason = "Progress restored; a new good-quality live baseline is required before continuing.";
        }
        return point;
    }

    private static string BuildPrintableReport(IoTestProject project)
    {
        var totalPassed = project.Ieds.Sum(ied => ied.PassedCount);
        var totalReview = project.Ieds.Sum(ied => ied.ReviewCount);
        var totalComplete = project.Ieds.Sum(ied => ied.CompleteCount);
        var builder = new StringBuilder();
        builder.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>")
            .Append(Html(project.ProjectName)).Append(" - FAT Report</title><style>")
            .Append("body{font-family:Segoe UI,Arial,sans-serif;color:#172033;margin:28px}h1{margin:0;color:#2458b8}h2{margin-top:30px;border-bottom:2px solid #dbe6f6;padding-bottom:6px}.meta{background:#f5f8fd;border:1px solid #dbe6f6;border-radius:10px;padding:14px;margin:16px 0}.summary{display:flex;gap:12px;flex-wrap:wrap}.pill{border:1px solid #dbe6f6;border-radius:16px;padding:7px 12px;background:#fff}.file-evidence{background:#f0fdf4;border:1px solid #bbf7d0;border-radius:8px;padding:10px 12px;margin:8px 0 14px}table{width:100%;border-collapse:collapse;font-size:11px}th,td{border:1px solid #cdd8e8;padding:6px;vertical-align:top}th{background:#eaf1fb;text-align:left}.pass{color:#08783f;font-weight:700}.review,.failed{color:#a75800;font-weight:700}.pending{color:#667085}@media print{body{margin:8mm}.ied{page-break-before:always}.ied:first-of-type{page-break-before:auto}.no-print{display:none}}@page{size:A4 landscape;margin:8mm}</style></head><body>");
        builder.Append("<h1>ARSAS IEC 61850 FAT Evidence Report</h1><div class=\"meta\"><b>Project:</b> ").Append(Html(project.ProjectName))
            .Append("<br><b>Project ID:</b> ").Append(Html(project.ProjectId))
            .Append("<br><b>FAT sources:</b> ").Append(Html(SourceSummary(project)))
            .Append("<br><b>Source-set SHA-256:</b> ").Append(Html(IoFatSourceIdentity.ProjectSourceFingerprint(project)))
            .Append("<br><b>Generated:</b> ").Append(Html(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))).Append("</div>");
        builder.Append("<div class=\"summary\"><span class=\"pill\">IED: ").Append(project.Ieds.Count)
            .Append("</span><span class=\"pill\">Included signals: ").Append(project.IncludedSignalCount)
            .Append("</span><span class=\"pill\">Complete: ").Append(totalComplete)
            .Append("</span><span class=\"pill pass\">Digital PASS: ").Append(totalPassed)
            .Append("</span><span class=\"pill review\">Review: ").Append(totalReview).Append("</span></div>");

        foreach (var ied in project.Ieds)
        {
            builder.Append("<section class=\"ied\"><h2>").Append(Html(ied.IedName)).Append(" · ").Append(Html(ied.IpAddress)).Append("</h2>")
                .Append("<p>").Append(Html(ied.IedRole)).Append(" · ").Append(Html(ied.Location)).Append(" · ").Append(Html(ied.VoltageLevel)).Append("</p>");

            if (ied.HasRemoteComtradeEvidence)
            {
                builder.Append("<div class=\"file-evidence\"><b>IEC 61850 File Service:</b> <span class=\"pass\">PASS</span>")
                    .Append("<br><b>Latest remote COMTRADE:</b> ").Append(Html(ied.LatestComtradeFiles))
                    .Append("<br><b>Remote path:</b> ").Append(Html(ied.LatestComtradeRemotePath))
                    .Append("<br><b>Relay modified:</b> ").Append(Html(ied.LatestComtradeModifiedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "not supplied"))
                    .Append("<br><b>Evidence source:</b> IEC 61850 FileDirectory")
                    .Append("<br><b>Download:</b> Optional additional verification; not a FAT gate.")
                    .Append("</div>");
            }

            builder.Append("<table><thead><tr><th>#</th><th>Signal</th><th>IEC 61850 reference</th><th>Type / capture</th><th>Value 1</th><th>Value 2</th><th>Result</th><th>Reason</th></tr></thead><tbody>");
            var index = 0;
            foreach (var point in ied.TestPoints.Where(point => point.IsIncludedInFat))
            {
                index++;
                builder.Append("<tr><td>").Append(index).Append("</td><td>").Append(Html(point.SignalName)).Append("</td><td>").Append(Html(point.ReportIecReference)).Append("</td><td>")
                    .Append(Html($"{point.SignalKind} / {point.CaptureMode}"))
                    .Append("</td><td>").Append(GenericEvidenceHtml(point, FatValueSlot.Value1)).Append("</td><td>").Append(GenericEvidenceHtml(point, FatValueSlot.Value2)).Append("</td><td class=\"")
                    .Append(point.CaptureMode == FatCaptureMode.OperatorSnapshot && point.IsFatEvidenceComplete ? "pass" : ResultClass(point.Runtime.State)).Append("\">")
                    .Append(Html(point.FatResultText)).Append("</td><td>").Append(Html(point.Runtime.StatusReason)).Append("</td></tr>");
            }
            builder.Append("</tbody></table></section>");
        }
        builder.Append("<p class=\"no-print\">Open this file in a browser and choose Print → Save as PDF.</p></body></html>");
        return builder.ToString();
    }

    private static string GenericEvidenceHtml(IoTestPointPlan point, FatValueSlot slot)
    {
        if (point.CaptureMode == FatCaptureMode.AutomaticTransition)
        {
            var evidence = slot == FatValueSlot.Value1 ? point.Runtime.OnEvidence : point.Runtime.OffEvidence;
            return EvidenceHtml(evidence);
        }

        var snapshot = slot == FatValueSlot.Value1 ? point.Runtime.Value1Evidence : point.Runtime.Value2Evidence;
        return EvidenceHtml(snapshot);
    }

    private static string EvidenceHtml(IoTestTransitionEvidence? evidence)
    {
        if (evidence == null)
            return "<span class=\"pending\">—</span>";
        var iedTime = global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(evidence.IedTimestamp, "yyyy-MM-dd HH:mm:ss.fff zzz", "not supplied");
        var arsasTime = global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(evidence.CapturedAt, "yyyy-MM-dd HH:mm:ss.fff zzz");
        return Html($"IED {iedTime}\nARSAS {arsasTime}\n{evidence.RawValue} · {evidence.Quality} · {evidence.AcquisitionSource}\n{evidence.Verdict}")
            .Replace("\n", "<br>", StringComparison.Ordinal);
    }

    private static string EvidenceHtml(FatValueEvidence? evidence)
    {
        if (evidence == null)
            return "<span class=\"pending\">—</span>";
        var iedTime = global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(evidence.IedTimestamp, "yyyy-MM-dd HH:mm:ss.fff zzz", "not supplied");
        var arsasTime = global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(evidence.CapturedAt, "yyyy-MM-dd HH:mm:ss.fff zzz");
        return Html($"IED {iedTime}\nARSAS {arsasTime}\n{evidence.RawValue} · {evidence.Quality} · {evidence.AcquisitionSource}\n{evidence.CaptureKind}")
            .Replace("\n", "<br>", StringComparison.Ordinal);
    }

    private static string ResultClass(IoTestPointState state) => state switch
    {
        IoTestPointState.Passed => "pass",
        IoTestPointState.Review => "review",
        IoTestPointState.Failed => "failed",
        _ => "pending"
    };

    private static string SourceSummary(IoTestProject project)
    {
        if (project.Sources.Count > 0)
            return string.Join(", ", project.Sources.OrderBy(source => source.FileName, StringComparer.OrdinalIgnoreCase).Select(source => $"{source.FileName} [{source.Kind}]"));
        return string.IsNullOrWhiteSpace(project.SourceWorkbookName) ? "not supplied" : project.SourceWorkbookName;
    }

    private static string BuildPackageReadme() =>
        "ARSAS IO FAT portable handover package\r\n\r\n" +
        "To continue testing: open this .arsas-iofat file from the FAT / IO List Testing card in ARSAS.\r\n" +
        "To print or create PDF without ARSAS: extract the package and open report/IO-FAT-Report.html in a browser, then Print -> Save as PDF.\r\n" +
        "The package contains the project snapshot, complete FAT source bundle, verified evidence journals, and a printable report.\r\n";

    private static ZipArchiveEntry RequiredEntry(ZipArchive archive, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(name))
            throw new InvalidDataException("The package contains an unsafe entry path.");
        return archive.GetEntry(name.Replace('\\', '/'))
            ?? throw new InvalidDataException($"The package entry '{name}' is missing.");
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, long maximumBytes, CancellationToken cancellationToken)
    {
        if (entry.Length > maximumBytes)
            throw new InvalidDataException($"Package entry '{entry.FullName}' exceeds its safety limit.");
        await using var source = entry.Open();
        using var memory = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        await source.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        if (memory.Length > maximumBytes)
            throw new InvalidDataException($"Package entry '{entry.FullName}' exceeds its safety limit.");
        return memory.ToArray();
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string entryName, byte[] bytes, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName.Replace('\\', '/'), CompressionLevel.Optimal);
        await using var destination = entry.Open();
        await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static string ProjectDirectory(string root, IoTestProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fingerprint = IoFatSourceIdentity.ProjectStorageFingerprint(project);
        var hash = string.IsNullOrWhiteSpace(fingerprint)
            ? "nohash"
            : fingerprint[..Math.Min(12, fingerprint.Length)];
        return Path.Combine(root, $"{SanitizePathPart(project.ProjectId)}_{hash}");
    }

    private static bool SameSourceSet(IoTestProject left, IoTestProject right)
    {
        var leftHash = IoFatSourceIdentity.ProjectSourceFingerprint(left);
        var rightHash = IoFatSourceIdentity.ProjectSourceFingerprint(right);
        if (!string.IsNullOrWhiteSpace(leftHash) || !string.IsNullOrWhiteSpace(rightHash))
            return leftHash.Equals(rightHash, StringComparison.OrdinalIgnoreCase);
        return left.SourceWorkbookSha256.Equals(right.SourceWorkbookSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeFileName(string? value, string fallback)
    {
        var name = Path.GetFileName(string.IsNullOrWhiteSpace(value) ? fallback : value.Trim());
        return SanitizePathPart(name);
    }

    private static string SanitizePathPart(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "IO-TEST" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(text.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return sanitized.Length == 0 ? "IO-TEST" : sanitized;
    }

    private static async Task WriteFileAtomicAsync(string destination, byte[] bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void WriteFileAtomic(string destination, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullDestination: destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void VerifyHash(byte[] bytes, string expected, string label)
    {
        var actual = HashBytes(bytes);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The {label} SHA-256 does not match the package manifest.");
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string HashFile(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private void RaiseExportState()
    {
        Raise(nameof(CanExport));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public delegate IoTestSessionController IoTestSessionControllerFactory(IoTestProject project, string evidenceRoot);

    private sealed record IoTestProjectSnapshot(
        string SnapshotVersion,
        DateTimeOffset SavedAtUtc,
        IoTestProjectData Project);

    private sealed record IoTestProjectData(
        string ProjectId,
        string SchemaVersion,
        string ProjectName,
        string SourceWorkbookName,
        string SourceWorkbookSha256,
        DateTimeOffset ImportedAt,
        List<IoTestIedData> Ieds)
    {
        public string SourceSetSha256 { get; init; } = string.Empty;
        public List<IoFatSourceDescriptor> Sources { get; init; } = new();
    }

    private sealed record IoTestIedData(
        string IedName,
        string IpAddress,
        string IedRole,
        string Location,
        string VoltageLevel,
        string Switchgear,
        List<IoTestPointData> TestPoints)
    {
        public string LatestComtradeFiles { get; init; } = string.Empty;
        public string LatestComtradeRemotePath { get; init; } = string.Empty;
        public string LatestComtradeCompleteness { get; init; } = string.Empty;
        public string LatestComtradeAcquisitionSource { get; init; } = string.Empty;
        public DateTimeOffset? LatestComtradeModifiedAtUtc { get; init; }
        public DateTimeOffset? LatestComtradeCapturedAtUtc { get; init; }
        public int LatestComtradeFileCount { get; init; }
        public long LatestComtradeKnownSizeBytes { get; init; }
    }

    private sealed record IoTestPointData(
        string TestPointId,
        string IedName,
        string IpAddress,
        string SignalName,
        string ObjectReference,
        string FunctionalConstraint,
        string ExpectedOnText,
        string ExpectedOffText,
        int ExpectedOnRaw,
        int ExpectedOffRaw,
        string DataType,
        string SignalAddress,
        string DataSetName,
        string LogicalDevice,
        string LogicalNode,
        string DataObject,
        string DataAttribute,
        string SourceSheet,
        int SourceRow,
        bool TestEnabled,
        bool ImportReady,
        string BindingStatus,
        string BindingEvidence,
        IoTestRuntimeData Runtime)
    {
        public FatSignalKind SignalKind { get; init; } = FatSignalKind.Discrete;
        public FatCaptureMode CaptureMode { get; init; } = FatCaptureMode.AutomaticTransition;
        public FatSignalDisposition FatDisposition { get; init; } = FatSignalDisposition.Included;
    }

    private sealed record IoTestRuntimeData(
        IoTestPointState State,
        bool? LastObservedState,
        long LastSequence,
        long ConnectionGeneration,
        IoTestTransitionEvidence? OnEvidence,
        IoTestTransitionEvidence? OffEvidence,
        string StatusReason,
        int Attempt,
        string CurrentValue,
        string CurrentQuality,
        string CurrentSource)
    {
        public FatValueEvidence? Value1Evidence { get; init; }
        public FatValueEvidence? Value2Evidence { get; init; }
    }

    private sealed record IoTestPackageManifest(
        string PackageVersion,
        DateTimeOffset CreatedAtUtc,
        string ProjectId,
        string ProjectName,
        string SchemaVersion,
        string SnapshotEntry,
        string SnapshotSha256,
        string SourceWorkbookEntry,
        string SourceWorkbookSha256,
        string ReportEntry,
        List<IoTestPackageEvidence> EvidenceFiles)
    {
        public string SourceSetSha256 { get; init; } = string.Empty;
        public List<IoFatPackageSource> SourceFiles { get; init; } = new();
    }

    private sealed record IoTestPackageEvidence(
        string Entry,
        string Sha256,
        long RecordCount,
        string LastHash);
}