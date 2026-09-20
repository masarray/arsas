using System.Text.Json;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P07ReleaseCandidateLockRegressionTests
{
    [Fact]
    public void LockFile_FreezesThePhysicallyAcceptedRuntimeBaselineAndOpenSclSemanticGap()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(FindRepoFile("evidence/p0.7-release-candidate-lock.json")));
        var root = document.RootElement;

        Assert.Equal("P0.7-RELEASE-CANDIDATE-LOCK", root.GetProperty("contractId").GetString());
        Assert.Equal(
            "locked-good-baseline-scl-semantic-parity-open",
            root.GetProperty("status").GetString());

        var physical = root.GetProperty("physicalReference");
        Assert.Equal(
            "0d0b9204d6637e3d62e2eee94000386ae43cd0e9",
            physical.GetProperty("consumerAcceptedHead").GetString());
        Assert.Equal(
            "648124097621046f5f127ceb1cf853fea54db730",
            physical.GetProperty("engineBaseline").GetString());

        var runtime = root.GetProperty("acceptedRuntime");
        Assert.Equal(58, runtime.GetProperty("staticMembers").GetInt32());
        Assert.Equal(58, runtime.GetProperty("selectedLiveRows").GetInt32());
        Assert.Equal(22, runtime.GetProperty("analogRows").GetInt32());
        Assert.Equal(36, runtime.GetProperty("digitalRows").GetInt32());
        Assert.Equal(58, runtime.GetProperty("reportBackedRows").GetInt32());
        Assert.Equal(0, runtime.GetProperty("cyclicMmsProcessPolling").GetInt32());
        Assert.True(runtime.GetProperty("actualInformationReportObserved").GetBoolean());
        Assert.True(runtime.GetProperty("saveSclWhileMonitoring").GetBoolean());
        Assert.True(runtime.GetProperty("savedSclReusable").GetBoolean());
        Assert.Equal(58, runtime.GetProperty("reopenedSclLiveRows").GetInt32());

        var presentation = root.GetProperty("acceptedPresentation");
        Assert.Equal("Dbpos", presentation.GetProperty("dpcRuntimeType").GetString());
        Assert.Equal("DP", presentation.GetProperty("dpcBadge").GetString());
        Assert.Equal("Boolean", presentation.GetProperty("spcRuntimeType").GetString());
        Assert.Equal("B", presentation.GetProperty("spcBadge").GetString());
        Assert.Equal("True [1]", presentation.GetProperty("booleanTrue").GetString());
        Assert.Equal("False [0]", presentation.GetProperty("booleanFalse").GetString());
        Assert.Equal("Open [01]", presentation.GetProperty("positionOpen").GetString());
        Assert.Equal("Close [10]", presentation.GetProperty("positionClose").GetString());
        Assert.True(presentation.GetProperty("fatRawEvidenceRemainsImmutable").GetBoolean());

        var structured = root.GetProperty("acceptedStructuredAnalogMembers")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Equal(12, structured.Length);
        Assert.Contains("A.phsA", structured);
        Assert.Contains("PPV.phsCA", structured);
        Assert.Contains("ThdA.phsC", structured);
        Assert.Contains("ThdPPV.phsBC", structured);

        var scl = root.GetProperty("sclSemanticParity");
        Assert.Equal("open-before-release", scl.GetProperty("status").GetString());
        var known = scl.GetProperty("knownExample");
        Assert.Equal("CBClsCounter", known.GetProperty("object").GetString());
        Assert.Equal("INS", known.GetProperty("expectedCdc").GetString());
        Assert.Equal("integer", known.GetProperty("expectedTypeFamily").GetString());
        Assert.Equal(139, known.GetProperty("engineCandidatePullRequest").GetInt32());
        Assert.Equal(
            "9123c8aa1cc51a1e13c6750c0e1f8bee2a2de3b7",
            known.GetProperty("engineCandidateHead").GetString());
    }

    [Fact]
    public void ExportOnlyCandidate_PreservesPhysicalEngineAuthorityAndPinsOnlyTheIsolatedExporterRevision()
    {
        using var baseline = JsonDocument.Parse(
            File.ReadAllText(FindRepoFile("evidence/p0.7-release-candidate-lock.json")));
        using var candidate = JsonDocument.Parse(
            File.ReadAllText(FindRepoFile("evidence/scl-export-only-semantic-candidate.json")));
        using var engine = JsonDocument.Parse(
            File.ReadAllText(FindRepoFile("engines/ARIEC61850.lock.json")));

        var physical = baseline.RootElement
            .GetProperty("physicalReference")
            .GetProperty("engineBaseline")
            .GetString();
        var replacement = candidate.RootElement.GetProperty("replacementCandidate");

        Assert.Equal("648124097621046f5f127ceb1cf853fea54db730", physical);
        Assert.Equal(140, replacement.GetProperty("enginePullRequest").GetInt32());
        Assert.Equal(
            "e58b42479e46fbbb42a1b17b03a074d8a6fb3b44",
            replacement.GetProperty("engineCommit").GetString());
        Assert.Equal(
            replacement.GetProperty("engineCommit").GetString(),
            engine.RootElement.GetProperty("commit").GetString());
        Assert.True(replacement.GetProperty("discoveryRuntimeSourceMustMatchPhysicalBaseline").GetBoolean());
        Assert.False(replacement.GetProperty("liveDiscoveryModelMutationAllowed").GetBoolean());
        Assert.False(replacement.GetProperty("acquisitionBehaviorChangeAllowed").GetBoolean());
    }

    [Theory]
    [InlineData("DPC", "Enum", "", "Dbpos", "DP")]
    [InlineData("DPC", "bit-string", "", "Dbpos", "DP")]
    [InlineData("SPC", "bit-string", "", "Boolean", "B")]
    [InlineData("SPC", "Boolean", "", "Boolean", "B")]
    public void DiscoveryFeedbackSemanticType_RemainsEquivalentToOpenedSclPresentation(
        string cdc,
        string mmsType,
        string sclBType,
        string expectedType,
        string expectedToken)
    {
        var resolved = Iec61850StaticControlStatusProjectionService
            .ResolveDeclaredFeedbackDataType(cdc, mmsType, sclBType);

        Assert.Equal(expectedType, resolved);
        Assert.Equal(expectedToken, Iec61850ValueStatePresentation.TypeToken(resolved));
    }

    [Theory]
    [InlineData("true", "Boolean", "True [1]")]
    [InlineData("false", "SPS", "False [0]")]
    [InlineData("1", "SPC", "True [1]")]
    [InlineData("0", "Boolean", "False [0]")]
    public void BooleanOperatorVocabulary_RemainsStable(
        object value,
        string dataType,
        string expected)
    {
        Assert.Equal(
            expected,
            Iec61850ValueFormatter.FormatReportProcessValue(
                value,
                dataType,
                string.Empty,
                "Status",
                "IEDLD/GGIO1.SwRem.stVal"));
    }

    [Fact]
    public void SaveSclWhileMonitoring_RemainsSnapshotOnlyAndCannotStopTheActiveSession()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.xaml.cs"));
        var method = ExtractMethod(source, "private async void IedSaveScl_Click");

        Assert.Contains("if (device.IsMonitoring)", method, StringComparison.Ordinal);
        Assert.Contains(
            "monitoring remains active; Save SCL uses the current canonical model",
            method,
            StringComparison.Ordinal);
        Assert.DoesNotContain("StopDeviceConnectionAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("StopDeviceMonitorAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("StartDeviceMonitorAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticReportOnlyAndFatPresentationContracts_RemainInstalled()
    {
        var runtime = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));
        Assert.Contains("cyclic MMS process polling=0", runtime, StringComparison.Ordinal);
        Assert.Contains("state.NextPollUtc = DateTime.MaxValue", runtime, StringComparison.Ordinal);
        Assert.Contains(
            "process leaves are never repurposed as cyclic MMS heartbeat reads",
            runtime,
            StringComparison.Ordinal);

        var preview = File.ReadAllText(
            FindRepoFile("Services/IoTesting/NativeFatPrintPreviewSnapshot.cs"));
        Assert.Contains("NativeFatCanonicalEvidenceOverlay.ReadRaw", preview, StringComparison.Ordinal);
        Assert.Contains("DisplayProcessValue(", preview, StringComparison.Ordinal);
        Assert.Contains(
            "Iec61850ValueFormatter.FormatReportProcessValue",
            preview,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredAnalogParityGuard_RemainsPartOfTheRegressionSuite()
    {
        var guard = File.ReadAllText(
            FindRepoFile("tests/ARSAS.Tests/DiscoveryStructuredAnalogParityTests.cs"));

        Assert.Contains(
            "TwelveStructuredAnalogMembers_AllResolveToUniquePublishableFloatLeaves",
            guard,
            StringComparison.Ordinal);
        Assert.Contains("Assert.Equal(12, result.MandatoryCatalogCount)", guard, StringComparison.Ordinal);
        Assert.Contains("Assert.True(signal.CanPublishToRuntime", guard, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method '{signature}'.");

        var openBrace = source.IndexOf('{', start);
        Assert.True(openBrace >= 0, $"Could not find opening brace for '{signature}'.");

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[start..(index + 1)];
        }

        throw new InvalidDataException($"Method '{signature}' has no balanced closing brace.");
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
