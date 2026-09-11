using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record IoFatEngineeringWorkspaceProjection(
    IoTestProject Project,
    IReadOnlyList<IoFatSourceInput> SourceInputs,
    IReadOnlyList<SclIedWorkspace> RuntimeWorkspaces);

/// <summary>
/// Builds the production FAT project directly from the already-parsed Engineering SCL
/// workspaces. This is deliberately not an SCL importer: it never opens/parses XML and it
/// never creates a second IEC 61850 model. Engineering remains the static DataSet/live-value
/// authority; FAT adds only its production evidence/session lifecycle on top.
/// </summary>
public static class IoFatEngineeringWorkspaceProjectionService
{
    public static async Task<IoFatEngineeringWorkspaceProjection> BuildAsync(
        IReadOnlyCollection<Iec61850MonitorDevice> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var usable = devices
            .Where(device => device.SclWorkspace != null)
            .Where(device => device.SclWorkspace!.DesignModel.DataSets.Sum(dataSet => dataSet.Members.Count) > 0)
            .Where(device => !string.IsNullOrWhiteSpace(device.SclSourcePath))
            .GroupBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
        if (usable.Length == 0)
        {
            throw new InvalidDataException(
                "No Engineering IED has an already-parsed SCL workspace with static DataSet members and source provenance.");
        }

        // Describe every candidate source once so conflicting Engineering authorities are
        // detected before canonicalization. The resulting canonical source set is then used
        // for the FAT projection/staging path so duplicate Explorer entries do not multiply
        // static DataSet rows or repeat downstream workspace work.
        var candidateSourceInputs = usable
            .Select(device => Path.GetFullPath(device.SclSourcePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new IoFatSourceInput(path, IoFatSourceKinds.Scl))
            .ToArray();
        var described = await IoFatSourceWorkspaceService.DescribeAsync(candidateSourceInputs, cancellationToken)
            .ConfigureAwait(false);
        var descriptorByPath = described.ToDictionary(
            item => Path.GetFullPath(item.OriginalPath),
            item => item.Source,
            StringComparer.OrdinalIgnoreCase);

        foreach (var device in usable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(device.SclSourcePath);
            if (!descriptorByPath.TryGetValue(path, out var descriptor))
                throw new InvalidDataException($"Engineering SCL provenance is unavailable for '{device.Name}'.");
            if (!string.IsNullOrWhiteSpace(device.SclSourceSha256) &&
                !descriptor.Sha256.Equals(device.SclSourceSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The Engineering SCL source for '{device.Name}' changed on disk after it was parsed. FAT will not bind a stale in-memory model to different source bytes.");
            }
        }

        var canonicalDevices = CanonicalizeEngineeringDevices(usable, descriptorByPath);
        var sourceInputs = canonicalDevices
            .Select(device => Path.GetFullPath(device.SclSourcePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new IoFatSourceInput(path, IoFatSourceKinds.Scl))
            .ToArray();

        var workspaceSources = canonicalDevices
            .Select(device =>
            {
                var descriptor = descriptorByPath[Path.GetFullPath(device.SclSourcePath)];
                return new FatSclWorkspaceSource(
                    descriptor.FileName,
                    descriptor.Sha256,
                    device.SclWorkspace!);
            })
            .ToArray();
        var verification = FatSclWorkspaceImportService.Import(workspaceSources).Project;

        var deviceByWorkspace = canonicalDevices
            .ToDictionary(
                device => WorkspaceIdentity(device.SclWorkspace!),
                device => device,
                StringComparer.OrdinalIgnoreCase);
        var descriptorByWorkspace = canonicalDevices
            .ToDictionary(
                device => WorkspaceIdentity(device.SclWorkspace!),
                device => descriptorByPath[Path.GetFullPath(device.SclSourcePath)],
                StringComparer.OrdinalIgnoreCase);

        var plans = new List<IoTestIedPlan>();
        foreach (var workspaceGroup in workspaceSources
                     .GroupBy(source => WorkspaceIdentity(source.Workspace), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var workspace = workspaceGroup.First().Workspace;
            var key = WorkspaceIdentity(workspace);
            var device = deviceByWorkspace[key];
            var descriptor = descriptorByWorkspace[key];
            var signals = verification.Signals
                .Where(signal => WorkspaceIdentity(signal.IedName, signal.AccessPointName)
                    .Equals(key, StringComparison.OrdinalIgnoreCase))
                .OrderBy(signal => signal.DataSetReference, StringComparer.OrdinalIgnoreCase)
                .ThenBy(signal => signal.DataSetMemberIndex)
                .ToArray();

            var endpoint = !string.IsNullOrWhiteSpace(device.IpAddress)
                ? device.IpAddress
                : workspace.PreferredEndpoint?.HasUsableAddress == true
                    ? workspace.PreferredEndpoint.IpAddress
                    : string.Empty;
            var plan = new IoTestIedPlan
            {
                IedName = workspace.IedName,
                IpAddress = endpoint,
                IedRole = FirstNonEmpty(workspace.IedType, workspace.Manufacturer),
                TestPoints = signals.Select(signal => ToPointPlan(signal, descriptor, workspace, endpoint)).ToList()
            };
            plan.ApplyLiveDeviceBinding(
                device.DeviceId,
                device.IsMonitoring ? "Engineering acquisition active" : device.IsConnected ? "Engineering association ready" : "Engineering SCL model ready",
                device.IsConnected,
                device.IsMonitoring);
            plans.Add(plan);
        }

        var sourceDescriptors = canonicalDevices
            .Select(device => descriptorByPath[Path.GetFullPath(device.SclSourcePath)])
            .GroupBy(source => source.SourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(source => source.SourceId, StringComparer.Ordinal)
            .ToArray();
        var sourceFingerprint = IoFatSourceIdentity.ComputeSetFingerprint(sourceDescriptors);
        var project = new IoTestProject
        {
            ProjectId = "FAT-SCL-" + sourceFingerprint[..16],
            SchemaVersion = "ARSAS-FAT-SCL-1.0",
            ProjectName = sourceDescriptors.Length == 1
                ? Path.GetFileNameWithoutExtension(sourceDescriptors[0].FileName) + " FAT"
                : $"IEC 61850 SCL FAT ({sourceDescriptors.Length} sources)",
            DocumentControl = new IoFatDocumentControl
            {
                DocumentTitle = "IEC 61850 FAT",
                SourceDocumentName = string.Join("; ", sourceDescriptors.Select(source => source.FileName))
            },
            Ieds = plans
        };
        project.SetSources(sourceDescriptors, sourceFingerprint);
        project.InitializeRuntimeNotifications();

        var staticMemberCount = canonicalDevices.Sum(device =>
            device.SclWorkspace!.DesignModel.DataSets.Sum(dataSet => dataSet.Members.Count));
        if (project.SignalCount != verification.Signals.Count || project.SignalCount != staticMemberCount)
        {
            throw new InvalidDataException(
                $"Engineering FAT projection produced {project.SignalCount} row(s), but the authoritative static DataSet scope contains {staticMemberCount}. FAT refuses a partial projection.");
        }

        return new IoFatEngineeringWorkspaceProjection(
            project,
            sourceInputs,
            canonicalDevices.Select(device => device.SclWorkspace!).ToArray());
    }

    private static IReadOnlyList<Iec61850MonitorDevice> CanonicalizeEngineeringDevices(
        IReadOnlyCollection<Iec61850MonitorDevice> devices,
        IReadOnlyDictionary<string, IoFatSourceDescriptor> descriptorByPath)
    {
        var canonical = new List<Iec61850MonitorDevice>();
        foreach (var identityGroup in devices
                     .GroupBy(device => WorkspaceIdentity(device.SclWorkspace!), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = identityGroup
                .Select(device =>
                {
                    var path = Path.GetFullPath(device.SclSourcePath);
                    return new
                    {
                        Device = device,
                        Path = path,
                        Descriptor = descriptorByPath[path]
                    };
                })
                .ToArray();

            var distinctHashes = candidates
                .Select(candidate => candidate.Descriptor.Sha256)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (distinctHashes.Length > 1)
            {
                throw new InvalidDataException(
                    $"Conflicting Engineering SCL sources define the same IED/AccessPoint '{identityGroup.Key}'. " +
                    "FAT will not silently merge competing static DataSet authorities.");
            }

            var distinctEndpoints = candidates
                .Select(candidate => candidate.Device.IpAddress?.Trim() ?? string.Empty)
                .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (distinctEndpoints.Length > 1)
            {
                throw new InvalidDataException(
                    $"Engineering exposes IED/AccessPoint '{identityGroup.Key}' through multiple endpoints ({string.Join(", ", distinctEndpoints)}). " +
                    "FAT will not guess which physical IED owns the evidence session.");
            }

            // Exact same-content duplicates are already harmless according to the lower SCL
            // importer contract. Collapse them here as well so projection counts, runtime
            // workspaces and staged source files all share one canonical authority. Prefer the
            // currently monitoring/connected Engineering device so FAT remains attached to the
            // live session that the operator is already using.
            var selected = candidates
                .OrderByDescending(candidate => candidate.Device.IsMonitoring)
                .ThenByDescending(candidate => candidate.Device.IsConnected)
                .ThenBy(candidate => candidate.Descriptor.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Device.DeviceId, StringComparer.OrdinalIgnoreCase)
                .First();
            canonical.Add(selected.Device);
        }

        return canonical;
    }

    private static IoTestPointPlan ToPointPlan(
        FatVerificationSignal signal,
        IoFatSourceDescriptor source,
        SclIedWorkspace workspace,
        string endpoint)
    {
        var discrete = signal.SignalKind == FatSignalKind.Discrete;
        return new IoTestPointPlan
        {
            TestPointId = $"scl-{source.SourceId}-{signal.SignalId}",
            IedName = signal.IedName,
            IpAddress = endpoint,
            SignalName = signal.SignalName,
            ObjectReference = FirstNonEmpty(signal.RuntimeReference, signal.StaticMemberReference),
            FunctionalConstraint = signal.FunctionalConstraint,
            ExpectedOnText = discrete ? "TRUE" : "Value 1",
            ExpectedOffText = discrete ? "FALSE" : "Value 2",
            ExpectedOnRaw = 1,
            ExpectedOffRaw = 0,
            DataType = signal.DataType,
            SignalAddress = source.SourceId,
            DataSetName = signal.DataSetReference,
            SourceIecReference = signal.StaticMemberReference,
            ReportDisplayReference = signal.StaticMemberReference,
            EventLogSearchReference = signal.StaticMemberReference,
            EvidenceExpected = signal.CaptureMode == FatCaptureMode.AutomaticTransition
                ? "Automatic Value 1 / Value 2 transition capture"
                : "Operator Value 1 / Value 2 snapshot capture",
            SourceSheet = source.FileName,
            SourceRow = signal.DataSetMemberIndex + 1,
            SignalKind = signal.SignalKind,
            CaptureMode = signal.CaptureMode,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = "ENGINEERING_SCL_DATASET_AUTHORITY",
            BindingEvidence = string.Join(" • ", new[]
            {
                "shared Engineering ARIEC static DataSet authority",
                $"sourceId={source.SourceId}",
                $"sourceSha256={source.Sha256}",
                $"workspace={workspace.WorkspaceKey}",
                $"dataset={signal.DataSetReference}",
                $"memberIndex={signal.DataSetMemberIndex}",
                $"static={signal.StaticMemberReference}",
                $"kind={signal.SignalKind}",
                $"capture={signal.CaptureMode}"
            })
        };
    }

    private static string WorkspaceIdentity(SclIedWorkspace workspace)
        => WorkspaceIdentity(workspace.IedName, workspace.AccessPointName);

    private static string WorkspaceIdentity(string? iedName, string? accessPointName)
        => $"{(iedName ?? string.Empty).Trim()}|{(accessPointName ?? string.Empty).Trim()}";

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
