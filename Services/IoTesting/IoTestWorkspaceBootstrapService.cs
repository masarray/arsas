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

    public static async Task<IoTestWorkspaceLaunchResult> OpenWorkbookAsync(
        IoTestProject importedProject,
        string workbookPath,
        string localProjectsRoot,
        string evidenceRoot,
        Func<IoTestProject, string, IoTestSessionController> sessionFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importedProject);
        ArgumentNullException.ThrowIfNull(sessionFactory);

        var localDirectory = ProjectDirectory(localProjectsRoot, importedProject);
        var snapshotPath = Path.Combine(localDirectory, "project.snapshot.json");
        var backupPath = snapshotPath + ".bootstrap-" + Guid.NewGuid().ToString("N");
        var warnings = new List<string>();
        var restored = false;
        var movedSnapshot = false;

        try
        {
            if (File.Exists(snapshotPath))
            {
                try
                {
                    ApplySnapshotProgress(importedProject, snapshotPath);
                    restored = true;
                    Directory.CreateDirectory(localDirectory);
                    File.Move(snapshotPath, backupPath, true);
                    movedSnapshot = true;
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
                {
                    warnings.Add($"Local progress could not be restored; a fresh FAT workspace was opened: {ex.Message}");
                }
            }

            var session = sessionFactory(importedProject, evidenceRoot);
            try
            {
                var opened = await IoTestWorkspacePersistence.OpenWorkbookAsync(
                    importedProject,
                    session,
                    workbookPath,
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
        IoTestSessionController? createdSession = null;
        var opened = await IoTestWorkspacePersistence.ImportPackageAsync(
            packagePath,
            (project, root) => createdSession = sessionFactory(project, root),
            localProjectsRoot,
            evidenceRoot,
            cancellationToken).ConfigureAwait(false);
        ExcludeCompletedFromNextSession(opened.Project);
        opened.Workspace.SaveNow();
        return new IoTestWorkspaceLaunchResult(
            opened.Project,
            createdSession ?? throw new InvalidOperationException("The IO FAT package did not create a session controller."),
            opened.Workspace,
            true,
            opened.Warnings);
    }

    private static void ApplySnapshotProgress(IoTestProject project, string snapshotPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(snapshotPath));
        var root = document.RootElement;
        var version = RequiredString(root, "snapshotVersion");
        if (!version.Equals(IoTestWorkspacePersistence.SnapshotVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported local snapshot version '{version}'.");

        var savedProject = RequiredObject(root, "project");
        if (!RequiredString(savedProject, "projectId").Equals(project.ProjectId, StringComparison.OrdinalIgnoreCase) ||
            !RequiredString(savedProject, "schemaVersion").Equals(project.SchemaVersion, StringComparison.OrdinalIgnoreCase) ||
            !RequiredString(savedProject, "sourceWorkbookSha256").Equals(project.SourceWorkbookSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The local snapshot belongs to a different workbook or schema.");
        }

        var savedPoints = RequiredArray(savedProject, "ieds")
            .EnumerateArray()
            .SelectMany(ied => RequiredArray(ied, "testPoints").EnumerateArray())
            .ToDictionary(point => RequiredString(point, "testPointId"), StringComparer.OrdinalIgnoreCase);

        foreach (var point in project.Ieds.SelectMany(ied => ied.TestPoints))
        {
            if (!savedPoints.TryGetValue(point.TestPointId, out var saved))
                continue;

            if (saved.TryGetProperty("testEnabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
                point.TestEnabled = enabled.GetBoolean();
            if (!saved.TryGetProperty("runtime", out var runtime) || runtime.ValueKind != JsonValueKind.Object)
                continue;

            point.Runtime.Attempt = OptionalInt(runtime, "attempt", 0);
            point.Runtime.OnEvidence = OptionalEvidence(runtime, "onEvidence");
            point.Runtime.OffEvidence = OptionalEvidence(runtime, "offEvidence");
            point.Runtime.LastObservedState = null;
            point.Runtime.LastSequence = -1;
            point.Runtime.ConnectionGeneration = -1;
            point.Runtime.CurrentValue = "-";
            point.Runtime.CurrentQuality = "Unknown";
            point.Runtime.CurrentSource = "Restored · live baseline required";

            var savedStateText = OptionalString(runtime, "state", IoTestPointState.NotStarted.ToString());
            _ = Enum.TryParse<IoTestPointState>(savedStateText, ignoreCase: true, out var savedState);
            if (savedState is IoTestPointState.Passed or IoTestPointState.Review or IoTestPointState.Failed)
            {
                point.Runtime.State = savedState;
                point.Runtime.StatusReason = OptionalString(runtime, "statusReason", "Restored completed result");
                point.TestEnabled = false;
            }
            else if (point.Runtime.OnEvidence != null && point.Runtime.OffEvidence == null)
            {
                point.Runtime.State = IoTestPointState.Review;
                point.Runtime.StatusReason = "Progress was restored after the live session ended; OFF continuity after saved ON evidence cannot be proven.";
                point.TestEnabled = false;
            }
            else
            {
                point.Runtime.State = IoTestPointState.NotStarted;
                point.Runtime.StatusReason = "Progress restored; a new good-quality live baseline is required before continuing.";
            }
        }
        project.InitializeRuntimeNotifications();
    }

    private static void ExcludeCompletedFromNextSession(IoTestProject project)
    {
        foreach (var point in project.Ieds.SelectMany(ied => ied.TestPoints))
        {
            if (point.Runtime.IsComplete)
                point.TestEnabled = false;
        }
    }

    private static IoTestTransitionEvidence? OptionalEvidence(JsonElement runtime, string property)
    {
        if (!runtime.TryGetProperty(property, out var element) || element.ValueKind == JsonValueKind.Null)
            return null;
        return element.Deserialize<IoTestTransitionEvidence>(JsonOptions);
    }

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

    private static int OptionalInt(JsonElement parent, string property, int fallback)
        => parent.TryGetProperty(property, out var value) && value.TryGetInt32(out var number)
            ? number
            : fallback;

    private static string ProjectDirectory(string root, IoTestProject project)
    {
        var hash = string.IsNullOrWhiteSpace(project.SourceWorkbookSha256)
            ? "nohash"
            : project.SourceWorkbookSha256[..Math.Min(12, project.SourceWorkbookSha256.Length)];
        return Path.Combine(root, $"{Sanitize(project.ProjectId)}_{hash}");
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string((value ?? "IO-TEST").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return result.Length == 0 ? "IO-TEST" : result;
    }
}
