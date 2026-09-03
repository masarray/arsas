using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record IoFatSclImportFinding(
    string SourceFileName,
    string Severity,
    string Code,
    string Message,
    string ObjectReference = "");

public sealed record IoFatSclProjectImportResult(
    IoTestProject Project,
    FatVerificationProject VerificationProject,
    IReadOnlyList<IoFatSourceDescriptor> Sources,
    IReadOnlyList<IoFatSourceInput> SourceInputs,
    IReadOnlyList<IoFatSclImportFinding> Findings);

/// <summary>
/// Production SCL -> FAT orchestration.
///
/// ARIEC61850 owns XML/SCL parsing, workspace construction, static DataSet semantics and
/// mandatory signal projection. This service only composes those engine-owned results into
/// the persisted ARSAS FAT project model introduced by P3.
/// </summary>
public sealed class IoFatSclProjectImportService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".scd", ".cid", ".icd", ".iid", ".ssd", ".xml"
    };

    private readonly SclWorkspaceService _workspaceService;
    private readonly object _runtimeWorkspaceGate = new();
    private IReadOnlyList<SclIedWorkspace> _runtimeWorkspaces = Array.Empty<SclIedWorkspace>();

    public IoFatSclProjectImportService()
        : this(new SclWorkspaceService())
    {
    }

    internal IoFatSclProjectImportService(SclWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    internal bool TryGetRuntimeWorkspace(
        string? iedName,
        string? ipAddress,
        out SclIedWorkspace? workspace)
    {
        var name = (iedName ?? string.Empty).Trim();
        var ip = (ipAddress ?? string.Empty).Trim();
        lock (_runtimeWorkspaceGate)
        {
            var named = _runtimeWorkspaces
                .Where(candidate => candidate.IedName.Equals(name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            workspace = named.FirstOrDefault(candidate =>
                !string.IsNullOrWhiteSpace(ip) &&
                candidate.PreferredEndpoint?.HasUsableAddress == true &&
                candidate.PreferredEndpoint.IpAddress.Equals(ip, StringComparison.OrdinalIgnoreCase));
            if (workspace is null && named.Length == 1)
                workspace = named[0];
            return workspace is not null;
        }
    }

    public Task<IoFatSclProjectImportResult> ImportAsync(
        IReadOnlyCollection<string> sclPaths,
        string? projectName = null,
        CancellationToken cancellationToken = default)
        => ImportCoreAsync(sclPaths, projectName, replaceRuntimeWorkspaces: true, cancellationToken);

    public Task<IoFatSclProjectImportResult> ImportAdditionalAsync(
        IReadOnlyCollection<string> sclPaths,
        CancellationToken cancellationToken = default)
        => ImportCoreAsync(sclPaths, projectName: null, replaceRuntimeWorkspaces: false, cancellationToken);

    private async Task<IoFatSclProjectImportResult> ImportCoreAsync(
        IReadOnlyCollection<string> sclPaths,
        string? projectName,
        bool replaceRuntimeWorkspaces,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sclPaths);
        if (replaceRuntimeWorkspaces)
            SetRuntimeWorkspaces(Array.Empty<SclIedWorkspace>());
        var requestedPaths = sclPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedPaths.Length == 0)
            throw new InvalidDataException("Select at least one SCL file for FAT import.");

        var findings = new List<IoFatSclImportFinding>();
        var loadedSources = new List<LoadedSource>();
        var seenHashes = new Dictionary<string, LoadedSource>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in requestedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePath(path);

            var input = new IoFatSourceInput(path, IoFatSourceKinds.Scl);
            var descriptor = await IoFatSourceIdentity.DescribeAsync(input, cancellationToken).ConfigureAwait(false);
            if (seenHashes.TryGetValue(descriptor.Sha256, out var duplicate))
            {
                findings.Add(new IoFatSclImportFinding(
                    descriptor.FileName,
                    "Info",
                    "SCL_DUPLICATE_CONTENT",
                    $"Identical SCL bytes are already represented by '{duplicate.Descriptor.FileName}'; the duplicate source was not added twice."));
                continue;
            }

            var document = await _workspaceService.OpenAsync(
                path,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!descriptor.Sha256.Equals(document.SourceSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"ARIEC source identity for '{descriptor.FileName}' does not match the FAT source SHA-256.");
            }

            var loaded = new LoadedSource(descriptor, input, document);
            loadedSources.Add(loaded);
            seenHashes.Add(descriptor.Sha256, loaded);

            foreach (var finding in document.Findings)
            {
                findings.Add(new IoFatSclImportFinding(
                    descriptor.FileName,
                    finding.Severity,
                    finding.Code,
                    finding.Message,
                    finding.ObjectReference));
            }

            if (document.Ieds.Count == 0)
            {
                findings.Add(new IoFatSclImportFinding(
                    descriptor.FileName,
                    "Warning",
                    "SCL_NO_IED_WORKSPACE",
                    "ARIEC opened the SCL source but returned no IED/AccessPoint workspace."));
            }
        }

        if (loadedSources.Count == 0)
            throw new InvalidDataException("No unique SCL source remained after source validation.");

        var workspaceSources = loadedSources
            .SelectMany(source => source.Document.Ieds.Select(workspace => new FatSclWorkspaceSource(
                source.Descriptor.FileName,
                source.Descriptor.Sha256,
                workspace)))
            .ToArray();

        FatVerificationProject verificationProject;
        if (workspaceSources.Length == 0)
        {
            verificationProject = new FatVerificationProject();
        }
        else
        {
            verificationProject = FatSclWorkspaceImportService.Import(workspaceSources).Project;
        }

        var sourceByWorkspace = BuildSourceAuthorityMap(loadedSources);
        foreach (var loaded in loadedSources)
        {
            foreach (var workspace in loaded.Document.Ieds)
            {
                var staticMemberCount = workspace.DesignModel.DataSets.Sum(dataSet => dataSet.Members.Count);
                if (staticMemberCount == 0)
                {
                    findings.Add(new IoFatSclImportFinding(
                        loaded.Descriptor.FileName,
                        "Warning",
                        "SCL_NO_STATIC_DATASET_MEMBERS",
                        $"{workspace.WorkspaceKey} contains no static DataSet members; FAT did not fabricate any rows."));
                }

                if (workspace.RequiresEndpointBinding)
                {
                    findings.Add(new IoFatSclImportFinding(
                        loaded.Descriptor.FileName,
                        "Info",
                        "SCL_ENDPOINT_REQUIRED",
                        $"{workspace.WorkspaceKey} has no usable MMS endpoint. Static FAT scope remains available offline; endpoint binding is a separate live-acquisition step."));
                }
            }
        }

        var sourceDescriptors = loadedSources
            .Select(source => source.Descriptor)
            .OrderBy(source => source.SourceId, StringComparer.Ordinal)
            .ToArray();
        var sourceFingerprint = IoFatSourceIdentity.ComputeSetFingerprint(sourceDescriptors);
        var project = new IoTestProject
        {
            ProjectId = "FAT-SCL-" + sourceFingerprint[..16],
            SchemaVersion = "ARSAS-FAT-SCL-1.0",
            ProjectName = BuildProjectName(projectName, sourceDescriptors),
            DocumentControl = new IoFatDocumentControl
            {
                DocumentTitle = "IEC 61850 FAT",
                SourceDocumentName = string.Join("; ", sourceDescriptors.Select(source => source.FileName))
            },
            Ieds = BuildIedPlans(loadedSources, verificationProject, sourceByWorkspace)
        };
        project.SetSources(sourceDescriptors, sourceFingerprint);
        project.InitializeRuntimeNotifications();

        if (project.SignalCount != verificationProject.Signals.Count)
        {
            throw new InvalidDataException(
                $"SCL FAT project contains {project.SignalCount} persisted point(s), but authoritative projection produced {verificationProject.Signals.Count}. Import refuses partial persistence.");
        }
        if (project.SignalCount != loadedSources.Sum(source =>
                source.Document.Ieds.Sum(workspace => workspace.DesignModel.DataSets.Sum(dataSet => dataSet.Members.Count))))
        {
            throw new InvalidDataException(
                "SCL FAT project row count does not equal the complete ARIEC static DataSet membership count.");
        }

        // Keep the exact engine-owned workspaces alive for the current FAT run. AutoConnect
        // can attach these already-parsed models to the IED and perform a fast MMS association
        // instead of repeating a network-wide Smart Scan that SCL-backed FAT does not need.
        var importedWorkspaces = loadedSources
            .SelectMany(source => source.Document.Ieds)
            .ToArray();
        if (replaceRuntimeWorkspaces)
            SetRuntimeWorkspaces(importedWorkspaces);
        else
            AddRuntimeWorkspaces(importedWorkspaces);

        return new IoFatSclProjectImportResult(
            project,
            verificationProject,
            sourceDescriptors,
            loadedSources.Select(source => source.Input).ToArray(),
            findings);
    }

    private void SetRuntimeWorkspaces(IReadOnlyList<SclIedWorkspace> workspaces)
    {
        lock (_runtimeWorkspaceGate)
            _runtimeWorkspaces = workspaces;
    }

    private void AddRuntimeWorkspaces(IReadOnlyCollection<SclIedWorkspace> workspaces)
    {
        lock (_runtimeWorkspaceGate)
        {
            _runtimeWorkspaces = _runtimeWorkspaces
                .Concat(workspaces)
                .GroupBy(workspace => workspace.WorkspaceKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToArray();
        }
    }

    private static List<IoTestIedPlan> BuildIedPlans(
        IReadOnlyCollection<LoadedSource> loadedSources,
        FatVerificationProject verificationProject,
        IReadOnlyDictionary<string, LoadedSource> sourceByWorkspace)
    {
        var plans = new List<IoTestIedPlan>();
        foreach (var source in loadedSources.OrderBy(item => item.Descriptor.SourceId, StringComparer.Ordinal))
        {
            foreach (var workspace in source.Document.Ieds
                         .OrderBy(item => item.IedName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.AccessPointName, StringComparer.OrdinalIgnoreCase))
            {
                var authorityKey = WorkspaceIdentity(workspace.IedName, workspace.AccessPointName);
                if (!sourceByWorkspace.TryGetValue(authorityKey, out var authority) ||
                    !authority.Descriptor.SourceId.Equals(source.Descriptor.SourceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rows = verificationProject.Signals
                    .Where(signal => WorkspaceIdentity(signal.IedName, signal.AccessPointName)
                        .Equals(authorityKey, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(signal => signal.DataSetReference, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(signal => signal.DataSetMemberIndex)
                    .ToArray();
                var endpoint = workspace.PreferredEndpoint?.HasUsableAddress == true
                    ? workspace.PreferredEndpoint
                    : null;

                plans.Add(new IoTestIedPlan
                {
                    IedName = workspace.IedName,
                    IpAddress = endpoint?.IpAddress ?? string.Empty,
                    IedRole = FirstNonEmpty(workspace.IedType, workspace.Manufacturer),
                    TestPoints = rows.Select(signal => ToPointPlan(signal, source, workspace)).ToList()
                });
            }
        }
        return plans;
    }

    private static IoTestPointPlan ToPointPlan(
        FatVerificationSignal signal,
        LoadedSource source,
        SclIedWorkspace workspace)
    {
        var discrete = signal.SignalKind == FatSignalKind.Discrete;
        return new IoTestPointPlan
        {
            TestPointId = $"scl-{source.Descriptor.SourceId}-{signal.SignalId}",
            IedName = signal.IedName,
            IpAddress = workspace.PreferredEndpoint?.HasUsableAddress == true
                ? workspace.PreferredEndpoint.IpAddress
                : string.Empty,
            SignalName = signal.SignalName,
            ObjectReference = FirstNonEmpty(signal.RuntimeReference, signal.StaticMemberReference),
            FunctionalConstraint = signal.FunctionalConstraint,
            ExpectedOnText = discrete ? "TRUE" : "Value 1",
            ExpectedOffText = discrete ? "FALSE" : "Value 2",
            ExpectedOnRaw = 1,
            ExpectedOffRaw = 0,
            DataType = signal.DataType,
            SignalAddress = source.Descriptor.SourceId,
            DataSetName = signal.DataSetReference,
            SourceIecReference = signal.StaticMemberReference,
            ReportDisplayReference = signal.StaticMemberReference,
            EventLogSearchReference = signal.StaticMemberReference,
            EvidenceExpected = signal.CaptureMode == FatCaptureMode.AutomaticTransition
                ? "Automatic Value 1 / Value 2 transition capture"
                : "Operator Value 1 / Value 2 snapshot capture",
            SourceSheet = source.Descriptor.FileName,
            SourceRow = signal.DataSetMemberIndex + 1,
            SignalKind = signal.SignalKind,
            CaptureMode = signal.CaptureMode,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = "SCL_DATASET_AUTHORITY",
            BindingEvidence = string.Join(" • ", new[]
            {
                "ARIEC static DataSet authority",
                $"sourceId={source.Descriptor.SourceId}",
                $"sourceSha256={source.Descriptor.Sha256}",
                $"workspace={workspace.WorkspaceKey}",
                $"dataset={signal.DataSetReference}",
                $"memberIndex={signal.DataSetMemberIndex}",
                $"static={signal.StaticMemberReference}",
                $"kind={signal.SignalKind}",
                $"capture={signal.CaptureMode}"
            })
        };
    }

    private static Dictionary<string, LoadedSource> BuildSourceAuthorityMap(IEnumerable<LoadedSource> sources)
    {
        var map = new Dictionary<string, LoadedSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in sources
                     .SelectMany(source => source.Document.Ieds.Select(workspace => (source, workspace)))
                     .GroupBy(item => WorkspaceIdentity(item.workspace.IedName, item.workspace.AccessPointName), StringComparer.OrdinalIgnoreCase))
        {
            var hashes = group.Select(item => item.source.Descriptor.Sha256)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (hashes.Length > 1)
            {
                throw new InvalidDataException(
                    $"Conflicting SCL sources define the same IED/AccessPoint '{group.Key}'. FAT import will not silently merge competing engineering authorities.");
            }
            map[group.Key] = group
                .OrderBy(item => item.source.Descriptor.FileName, StringComparer.OrdinalIgnoreCase)
                .First().source;
        }
        return map;
    }

    private static string BuildProjectName(string? requested, IReadOnlyList<IoFatSourceDescriptor> sources)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return requested.Trim();
        if (sources.Count == 1)
            return Path.GetFileNameWithoutExtension(sources[0].FileName) + " FAT";
        return $"IEC 61850 SCL FAT ({sources.Count} sources)";
    }

    private static string WorkspaceIdentity(string? iedName, string? accessPointName)
        => $"{(iedName ?? string.Empty).Trim()}|{(accessPointName ?? string.Empty).Trim()}";

    private static void ValidatePath(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The selected SCL source was not found.", path);
        var extension = Path.GetExtension(path);
        if (!SupportedExtensions.Contains(extension))
            throw new InvalidDataException($"'{Path.GetFileName(path)}' is not a supported IEC 61850 SCL source.");
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record LoadedSource(
        IoFatSourceDescriptor Descriptor,
        IoFatSourceInput Input,
        SclWorkspaceDocument Document);
}
