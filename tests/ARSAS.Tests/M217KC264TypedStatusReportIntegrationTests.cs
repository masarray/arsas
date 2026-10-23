using System.Text.Json;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Consumer integration against the EXACT engine revision in the ARSAS lock.
/// These are real engine typed projectors, not string-only source guards.
/// C264 Report payloads are shaped from the operator's physical capture; the
/// tests do not claim a byte-identical pcap replay or physical peer acceptance.
/// </summary>
public sealed class M217KC264TypedStatusReportIntegrationTests
{
    private const string ExpectedEngineSha =
        "51c2eefd61d6bcbe1116fda6b781b74bb9cf06da";
    private static readonly DateTimeOffset ReportTime =
        new(2026, 10, 17, 2, 24, 5, TimeSpan.Zero);

    private static MmsReportFrame Frame(string reference, string fc,
        MmsDataValue value)
        => new()
        {
            ReceivedAt = ReportTime,
            Header = new MmsReportHeader
            {
                DataSetReference = "C264CONTROL/LLN0.LLN0BRptStDs"
            },
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    Member = new MmsDataSetDirectoryMember
                    {
                        UserReference = reference,
                        FunctionalConstraint = fc
                    },
                    Value = value,
                    ReasonForInclusion = ["general-interrogation"]
                }
            ]
        };

    private static MmsDataValue StatusTuple(bool extraStSeld)
    {
        var parts = new List<MmsDataValue>
        {
            MmsDataValue.Integer(1),
            MmsDataValue.BitString(3, [0x00, 0x00]),
            MmsDataValue.UtcTime(new Iec61850UtcTime(ReportTime, 0))
        };
        if (extraStSeld)
            parts.Add(MmsDataValue.Boolean(false));
        return MmsDataValue.Structure(parts);
    }

    [Fact]
    public void TestProductUsesExplicitlyApprovedEngineCommitNotPreviouslyPinnedDll()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
               !File.Exists(Path.Combine(root.FullName,
                   "engines", "ARIEC61850.lock.json")))
            root = root.Parent;
        Assert.NotNull(root);
        using var lockDocument = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root!.FullName, "engines", "ARIEC61850.lock.json")));
        var lockRoot = lockDocument.RootElement;
        var identityTrial = lockRoot.GetProperty("p0AIdentityTrial");
        Assert.Equal(ExpectedEngineSha, identityTrial.GetProperty("baseEngineCommit").GetString());
        var rcbTrial = lockRoot.GetProperty("p0BRcbPlannerTrial");
        Assert.Equal(identityTrial.GetProperty("exactCommit").GetString(),
            rcbTrial.GetProperty("baseEngineCommit").GetString());
        var typedReadTrial = lockRoot.GetProperty("p0CTypedReadTrial");
        Assert.Equal(rcbTrial.GetProperty("exactCommit").GetString(),
            typedReadTrial.GetProperty("baseEngineCommit").GetString());
        var controlTrial = lockRoot.GetProperty("controlReportIsolationTrial");
        Assert.Equal(typedReadTrial.GetProperty("exactCommit").GetString(),
            controlTrial.GetProperty("baseEngineCommit").GetString());
        Assert.Equal("c85d4e20db722bd0c2729f2533b323b13d3ef735",
            controlTrial.GetProperty("exactCommit").GetString());
        // The current code-only stage observation trial must be descended
        // EXACTLY from historical #183. Physical acceptance does not transfer.
        var stageTrial = lockRoot.GetProperty("p0CStageObservationTrial");
        Assert.Equal(controlTrial.GetProperty("exactCommit").GetString(),
            stageTrial.GetProperty("baseEngineCommit").GetString());
        Assert.Equal("9c7cb40364023a7b217e5ec31ab3105ba90142eb",
            stageTrial.GetProperty("exactCommit").GetString());
        var typedTrial = lockRoot.GetProperty("globalTypedControlStageTrial");
        Assert.Equal(stageTrial.GetProperty("exactCommit").GetString(),
            typedTrial.GetProperty("baseEngineCommit").GetString());
        Assert.Equal("f75e323e9f8443aa20e0a3818a1d5d77255b2c0f",
            typedTrial.GetProperty("exactCommit").GetString());
        Assert.Equal(typedTrial.GetProperty("exactCommit").GetString(),
            lockRoot.GetProperty("commit").GetString());
        Assert.Equal(192, lockRoot.GetProperty("sourcePullRequest").GetInt32());
        Assert.Equal(192, typedTrial.GetProperty("enginePullRequest").GetInt32());
        Assert.Equal(39909260816L, typedTrial.GetProperty("engineCiRun").GetInt64());
        Assert.Equal("engine-ci-success-consumer-trial-not-physical",
            typedTrial.GetProperty("status").GetString());
        foreach (var flag in new[] {
            "typedStatusReadRequired", "sameDataObjectFeedbackRequired",
            "ctlModelAndOperShapeRequired", "sbowEnhancedSafetyRequired",
            "noAutomaticOperate", "physicalQualificationRequired",
            "mainlinePromotionForbidden" })
            Assert.True(typedTrial.GetProperty(flag).GetBoolean());
        Assert.Equal(184, stageTrial.GetProperty("enginePullRequest").GetInt32());
        Assert.Equal(185, stageTrial.GetProperty("engineCiOnlyPullRequest").GetInt32());
        Assert.Equal(1182, stageTrial.GetProperty("engineTestsPassed").GetInt32());
        Assert.True(stageTrial.GetProperty("sbo400msSafetyGatePreserved").GetBoolean());
        Assert.True(stageTrial.GetProperty("negativeAndDelayedLastApplErrorRegression").GetBoolean());
        Assert.True(stageTrial.GetProperty("noUnqualifiedImmediateOperate").GetBoolean());
        Assert.True(stageTrial.GetProperty("physicalLatencyQualificationRequired").GetBoolean());
        Assert.True(stageTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.True(controlTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(controlTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.True(controlTrial.GetProperty("controlChecksUnchanged").GetBoolean());
        Assert.True(controlTrial.GetProperty("controlTimeoutsUnchanged").GetBoolean());
        Assert.True(typedReadTrial.GetProperty("strictInvokeId").GetBoolean());
        Assert.True(typedReadTrial.GetProperty("strictAccessOrdinal").GetBoolean());
        Assert.True(typedReadTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(typedReadTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        var trial = lockRoot.GetProperty("m217KTypedStatusReportTrial");
        Assert.Equal("036e2c00d83a0815b6160147b14ea05975057965",
            trial.GetProperty("previousConsumerPin").GetString());
        Assert.True(trial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(trial.GetProperty("mainlinePromotionForbidden").GetBoolean());
    }

    [Theory]
    [InlineData("C264CONTROL/CILO1.Mod", true)]
    [InlineData("C264CONTROL/CSWI1.Mod", true)]
    [InlineData("C264SYSTEM/LPHD1.PhyHealth", false)]
    [InlineData("C264CONTROL/CILO1.Beh", false)]
    public void EngineTypedStReportReachesSingleAssociationLedgerAsProvenScalar(
        string member, bool extraStSeld)
    {
        var frame = Frame(member, "ST", StatusTuple(extraStSeld));
        var projected = MmsReportValueProjector.Project(frame);
        var scalar = Assert.Single(projected.Updates);
        Assert.Equal(member + ".stVal", scalar.Reference);
        Assert.Equal("ST", scalar.FunctionalConstraint);
        Assert.Equal("projected-typed-enumerated-status", scalar.ProjectionStatus);
        Assert.Equal("1", scalar.Value);
        Assert.Equal("good", scalar.Quality);
        Assert.Contains("2026-10-17", scalar.Timestamp, StringComparison.Ordinal);
        Assert.True(scalar.HasValue);
        Assert.True(scalar.HasQuality);
        Assert.True(scalar.HasTimestamp);
        Assert.DoesNotContain(projected.Warnings, x =>
            x.StartsWith("REPORT_RAW_STRUCT:", StringComparison.OrdinalIgnoreCase));

        Assert.True(ReportProcessValueSafety.IsSafe(
            scalar.Value, scalar.Value, "Enum", scalar.Reference, out var why), why);
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association, scalar.Reference,
            scalar.FunctionalConstraint, scalar.Value, scalar.Quality,
            scalar.Timestamp, "Report"));
        Assert.True(ledger.TryGet(scalar.Reference, "ST", out var evidence));
        Assert.Equal("1", evidence.Value);
        Assert.Equal("good", evidence.Quality);
        Assert.StartsWith("Report", evidence.Source, StringComparison.Ordinal);
        Assert.False(ledger.TryGet(scalar.Reference, "MX", out _));

        // Retired associations cannot resurrect the old status after reconnect.
        ledger.BeginAssociation();
        Assert.False(ledger.PublishForAssociation(association, scalar.Reference,
            "ST", scalar.Value, scalar.Quality, scalar.Timestamp, "Report"));
        Assert.False(ledger.TryGet(scalar.Reference, "ST", out _));
    }

    [Theory]
    [InlineData("C264CONTROL/CILO1.Mod", "MX")]
    [InlineData("C264CONTROL/CILO1.Misleading", "ST")]
    public void WrongReferenceOrFcNeverBecomesAnInventedStatusLeaf(
        string member, string fc)
    {
        var projected = MmsReportValueProjector.Project(
            Frame(member, fc, StatusTuple(extraStSeld: true)));
        Assert.DoesNotContain(projected.Updates, item =>
            item.Reference == member + ".stVal");
        Assert.Contains(projected.Warnings, item =>
            item.StartsWith("REPORT_RAW_STRUCT:", StringComparison.OrdinalIgnoreCase));
        Assert.False(ReportProcessValueSafety.IsSafe(
            "Structure(4)", "Structure(4)", "Enum", member + ".stVal",
            out _));
    }
}
