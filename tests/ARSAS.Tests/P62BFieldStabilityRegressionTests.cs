using AR.Iec61850.Mms;
using ArIED61850Tester.Services;
using System.Text.Json;

namespace ARSAS.Tests;

public sealed class P62BFieldStabilityRegressionTests
{
    [Fact]
    public void EngineLock_PreservesReviewedP62BPolicyAcrossLaterEnginePins()
    {
        var source = ReadRepoFile("engines/ARIEC61850.lock.json");
        using var document = JsonDocument.Parse(source);
        var root = document.RootElement;

        Assert.Equal("masarray/ARIEC61850", root.GetProperty("repository").GetString());
        Assert.Equal("main", root.GetProperty("ref").GetString());
        Assert.Matches("^[0-9a-f]{40}$", root.GetProperty("commit").GetString() ?? string.Empty);
        Assert.True(root.GetProperty("sourcePullRequest").GetInt32() >= 89);
        Assert.Contains("PR #89", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quarantines automatic full dynamic DataSet activation", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("successful one-member NVL probation does not guarantee association survival", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ambiguous structures remain raw", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FieldObservedMxFcdShape_ProjectsExactSelectedMagLeafWithQualityAndTimestamp()
    {
        var timestamp = new DateTimeOffset(2026, 8, 19, 2, 8, 21, TimeSpan.Zero);
        var frame = new MmsReportFrame
        {
            ReceivedAt = timestamp,
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    Member = new MmsDataSetDirectoryMember
                    {
                        UserReference = "IEDLD/TTMP1.WidTmpU",
                        FunctionalConstraint = "MX"
                    },
                    Value = MmsDataValue.Structure([
                        MmsDataValue.Structure([MmsDataValue.Integer(32766)]),
                        MmsDataValue.Structure([MmsDataValue.Integer(42)]),
                        MmsDataValue.BitString(3, [0x00, 0x00]),
                        MmsDataValue.UtcTime(new Iec61850UtcTime(timestamp, 0))
                    ]),
                    ReasonForInclusion = ["general-interrogation"]
                }
            ]
        };

        var projection = MmsReportValueProjector.Project(frame);
        var selected = Assert.Single(projection.Updates.Where(update =>
            update.Reference.Equals("IEDLD/TTMP1.WidTmpU.mag.f", StringComparison.OrdinalIgnoreCase)));

        Assert.Equal("42", selected.Value);
        Assert.Equal("good", selected.Quality);
        Assert.True(selected.HasQuality);
        Assert.True(selected.HasTimestamp);
        Assert.Equal("projected-mx-pair", selected.ProjectionStatus);
        Assert.True(ReportProcessValueSafety.IsSafe(
            selected.Value,
            selected.Value,
            "Float32",
            selected.Reference,
            out var rejectionReason), rejectionReason);
    }

    [Fact]
    public void AmbiguousStructuredStaticValue_CannotOverwriteScalarProcessState()
    {
        var safe = ReportProcessValueSafety.IsSafe(
            "Structure(6) {general=..., phsA=..., q=..., t=...}",
            "Structure(6) {general=..., phsA=..., q=..., t=...}",
            "Boolean",
            "IEDADD/GGIO2.CBClsCmdRecv.stVal",
            out var rejectionReason);

        Assert.False(safe);
        Assert.Contains("structured report value", rejectionReason, StringComparison.OrdinalIgnoreCase);

        var runtime = ReadRepoFile("Services/Iec61850MonitorRuntime.cs");
        Assert.Contains("MMS verification/fallback remains authoritative", runtime, StringComparison.Ordinal);
        Assert.Contains("state.ReportTrafficSeen = true;", runtime, StringComparison.Ordinal);
        Assert.Contains("state.ReportChangeVerified = false;", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void P61StaticFailureIsolation_RemainsIntact()
    {
        var source = ReadRepoFile("Services/NativeIec61850Client.HybridReporting.P4.cs");

        Assert.Contains("no dynamic DataSet/RCB write was attempted", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DefineNamedVariableList", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StartPersistentReportMonitorWithAttemptEvidenceAsync", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
