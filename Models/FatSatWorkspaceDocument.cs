using System.Text.Json.Serialization;

namespace ArIED61850Tester.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FatSatTestResult
{
    NotRun,
    Pass,
    Fail,
    Review,
    Blocked,
    NotApplicable
}

public sealed class FatSatWorkspaceDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid WorkspaceId { get; set; } = Guid.NewGuid();
    public string ProjectName { get; set; } = "IEC 61850 FAT/SAT Workspace";
    public string SiteName { get; set; } = string.Empty;
    public string BayOrSystem { get; set; } = string.Empty;
    public string IedIdentity { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string WitnessName { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string ApplicationVersion { get; set; } = string.Empty;
    public string ApplicationCommit { get; set; } = string.Empty;
    public string EngineRepository { get; set; } = string.Empty;
    public string EngineReference { get; set; } = string.Empty;
    public string EngineCommit { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<FatSatTestCase> TestCases { get; set; } = [];
}

public sealed class FatSatTestCase
{
    public Guid TestCaseId { get; set; } = Guid.NewGuid();
    public string Sequence { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Procedure { get; set; } = string.Empty;
    public string ExpectedResult { get; set; } = string.Empty;
    public FatSatTestResult Result { get; set; } = FatSatTestResult.NotRun;
    public string ActualResult { get; set; } = string.Empty;
    public string OperatorNote { get; set; } = string.Empty;
    public string ExceptionOrDeviation { get; set; } = string.Empty;
    public DateTimeOffset? ExecutedAtUtc { get; set; }
    public string ExecutedBy { get; set; } = string.Empty;
    public List<FatSatEvidenceReference> Evidence { get; set; } = [];
}

public sealed class FatSatEvidenceReference
{
    public Guid EvidenceId { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string MediaType { get; set; } = "application/octet-stream";
    public DateTimeOffset AttachedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Description { get; set; } = string.Empty;
}

public sealed record FatSatWorkspaceSummary(
    int Total,
    int NotRun,
    int Passed,
    int Failed,
    int Review,
    int Blocked,
    int NotApplicable,
    int EvidenceFiles)
{
    public bool HasBlockingOutcome => Failed > 0 || Review > 0 || Blocked > 0;
    public bool IsComplete => Total > 0 && NotRun == 0 && !HasBlockingOutcome;
}
