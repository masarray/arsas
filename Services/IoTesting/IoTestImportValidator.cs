using System.Net;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public enum IoTestImportFindingSeverity
{
    Warning,
    Error
}

public sealed record IoTestImportFinding(
    IoTestImportFindingSeverity Severity,
    string Code,
    string Message,
    string? TestPointId = null,
    string? SourceSheet = null,
    int? SourceRow = null);

public sealed record IoTestImportValidationResult(
    IReadOnlyList<IoTestImportFinding> Findings,
    int IedCount,
    int SignalCount,
    int ReadySignalCount)
{
    public bool IsValid => Findings.All(finding => finding.Severity != IoTestImportFindingSeverity.Error);
}

public sealed class IoTestImportValidator
{
    public const string SupportedSchemaVersion = "ARSAS-FAT-IO-1.0";

    public IoTestImportValidationResult Validate(IoTestProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var findings = new List<IoTestImportFinding>();
        if (!string.Equals(project.SchemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
        {
            findings.Add(Error(
                "SCHEMA_UNSUPPORTED",
                $"Schema '{project.SchemaVersion}' is not supported. Expected '{SupportedSchemaVersion}'."));
        }

        if (string.IsNullOrWhiteSpace(project.ProjectId))
            findings.Add(Error("PROJECT_ID_REQUIRED", "ProjectId is required."));

        if (project.Ieds.Count == 0)
            findings.Add(Error("IED_LIST_EMPTY", "The imported project contains no IED plans."));

        var testPointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var endpointOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ied in project.Ieds)
        {
            ValidateIed(ied, findings, endpointOwners);
            foreach (var point in ied.TestPoints)
            {
                ValidatePoint(ied, point, findings);
                if (!testPointIds.Add(point.TestPointId))
                {
                    findings.Add(Error(
                        "TEST_POINT_DUPLICATE",
                        $"TestPointId '{point.TestPointId}' occurs more than once.",
                        point));
                }
            }
        }

        return new IoTestImportValidationResult(
            findings,
            project.Ieds.Count,
            project.SignalCount,
            project.ReadySignalCount);
    }

    private static void ValidateIed(
        IoTestIedPlan ied,
        ICollection<IoTestImportFinding> findings,
        IDictionary<string, string> endpointOwners)
    {
        if (string.IsNullOrWhiteSpace(ied.IedName))
            findings.Add(Error("IED_NAME_REQUIRED", "IEDName is required."));

        if (!IPAddress.TryParse(ied.IpAddress, out _))
        {
            findings.Add(Error(
                "IED_IP_INVALID",
                $"IED '{ied.IedName}' has invalid IP address '{ied.IpAddress}'."));
        }
        else if (endpointOwners.TryGetValue(ied.IpAddress, out var existingIed) &&
                 !string.Equals(existingIed, ied.IedName, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new IoTestImportFinding(
                IoTestImportFindingSeverity.Warning,
                "IED_IP_SHARED",
                $"IP address '{ied.IpAddress}' is assigned to both '{existingIed}' and '{ied.IedName}'."));
        }
        else
        {
            endpointOwners[ied.IpAddress] = ied.IedName;
        }

        if (ied.TestPoints.Count == 0)
        {
            findings.Add(new IoTestImportFinding(
                IoTestImportFindingSeverity.Warning,
                "IED_SIGNALS_EMPTY",
                $"IED '{ied.IedName}' has no SDI test points."));
        }
    }

    private static void ValidatePoint(
        IoTestIedPlan ied,
        IoTestPointPlan point,
        ICollection<IoTestImportFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(point.TestPointId))
            findings.Add(Error("TEST_POINT_ID_REQUIRED", "TestPointId is required.", point));

        if (!string.Equals(point.IedName, ied.IedName, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(Error(
                "TEST_POINT_IED_MISMATCH",
                $"Test point '{point.TestPointId}' belongs to '{point.IedName}' but is grouped under '{ied.IedName}'.",
                point));
        }

        if (!string.Equals(point.IpAddress, ied.IpAddress, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(Error(
                "TEST_POINT_IP_MISMATCH",
                $"Test point '{point.TestPointId}' uses IP '{point.IpAddress}' but its IED plan uses '{ied.IpAddress}'.",
                point));
        }

        if (!string.Equals(point.DataType, "SDI", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(Error(
                "TEST_POINT_TYPE_UNSUPPORTED",
                $"The first IO Testing release accepts DataType=SDI; received '{point.DataType}'.",
                point));
        }

        if (point.ExpectedOnRaw == point.ExpectedOffRaw ||
            point.ExpectedOnRaw is not (0 or 1) ||
            point.ExpectedOffRaw is not (0 or 1))
        {
            findings.Add(Error(
                "TEST_POINT_EXPECTED_STATE_INVALID",
                $"Expected ON/OFF raw states for '{point.TestPointId}' must be distinct binary values 0 and 1.",
                point));
        }

        if (string.IsNullOrWhiteSpace(point.SignalName))
            findings.Add(Error("SIGNAL_NAME_REQUIRED", "SignalName is required.", point));

        if (point.ImportReady && string.IsNullOrWhiteSpace(point.ObjectReference))
        {
            findings.Add(Error(
                "REFERENCE_REQUIRED",
                "Import-ready signals require ObjectReference.",
                point));
        }

        if (point.ImportReady &&
            !string.Equals(point.FunctionalConstraint, "ST", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(Error(
                "SDI_FC_INVALID",
                $"The first IO Testing release accepts SDI status points with FC=ST; received '{point.FunctionalConstraint}'.",
                point));
        }

        if (!point.ImportReady)
        {
            findings.Add(new IoTestImportFinding(
                IoTestImportFindingSeverity.Warning,
                "SIGNAL_REVIEW_REQUIRED",
                $"Signal '{point.SignalName}' is retained for review and will not be auto-tested.",
                point.TestPointId,
                point.SourceSheet,
                point.SourceRow));
        }
    }

    private static IoTestImportFinding Error(
        string code,
        string message,
        IoTestPointPlan? point = null)
    {
        return new IoTestImportFinding(
            IoTestImportFindingSeverity.Error,
            code,
            message,
            point?.TestPointId,
            point?.SourceSheet,
            point?.SourceRow);
    }
}
