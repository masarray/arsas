using System.Text;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// IEC 61850-7-2: preserve exact ARIEC RptEna/GI write outcomes and decoded
/// ReasonForInclusion without misclassifying a SqNum reset as harmless.
/// </summary>
public sealed class ReportActivationCausalityP76ETests
{
    [Fact]
    public void ExactEngineWrites_AreReadInLastAttemptOrderWithoutNetworkWork()
    {
        var steps = new List<MmsReportAttributeWriteStep>
        {
            new() { Attribute = "RptEna", Attempted = true, IsSuccess = false },
            new() { Attribute = "RptEna", Attempted = true, IsSuccess = true },
            new() { Attribute = "GI", Attempted = true, IsSuccess = true }
        };
        Assert.True(NativeIec61850Client.ReadStaticActivationWriteResult(steps, "RptEna"));
        Assert.True(NativeIec61850Client.ReadStaticActivationWriteResult(steps, "GI"));
        Assert.Null(NativeIec61850Client.ReadStaticActivationWriteResult(steps, "OptFlds"));
        Assert.Null(NativeIec61850Client.ReadStaticActivationWriteResult(
            new List<MmsReportAttributeWriteStep>(), "GI"));
    }

    [Fact]
    public void RealBackwardSequence_WithExplicitGiReason_StaysAnomaly()
    {
        var state = ActivatedState();
        var first = new NativeReportFrameMetadata
        {
            SequenceNumber = 5,
            OptFldsRawHex = "7880",
            OptFldsSequenceNumber = true,
            OptFldsEntryId = false,
            OptFldsReasonForInclusion = true,
            GeneralInterrogationReasonSeen = false,
            IntegrityReasonSeen = false
        };
        Assert.Empty(Iec61850ReportContinuityInspector.Observe(state, first, true));
        var warnings = Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata
            {
                SequenceNumber = 0,
                OptFldsRawHex = "7880",
                OptFldsSequenceNumber = true,
                OptFldsEntryId = false,
                OptFldsReasonForInclusion = true,
                GeneralInterrogationReasonSeen = true,
                IntegrityReasonSeen = false,
                ReceivedAt = DateTimeOffset.UtcNow
            }, true);
        Assert.Single(warnings);
        Assert.Contains("previous=5, current=0", warnings[0], StringComparison.Ordinal);
        Assert.Contains("GIwrite=accepted/yes", warnings[0], StringComparison.Ordinal);
        Assert.Contains("GIreason=accepted/yes", warnings[0], StringComparison.Ordinal);
        Assert.Contains("not proof of a harmless reset", warnings[0], StringComparison.Ordinal);
        Assert.Equal(1, state.GiReasonFrames);
        Assert.Equal(1, state.AnomalyFindings);
        Assert.True(state.LastAnomalyGiReason);
        var diagnostic = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(diagnostic,
            Iec61850ReportContinuityInspector.Snapshot(
                new Dictionary<string, Iec61850ReportContinuityState> { ["rcb"] = state }, 7, 0));
        Assert.Contains("GIWrite=YES", diagnostic.ToString(), StringComparison.Ordinal);
        Assert.Contains("GIreasonFrames=1", diagnostic.ToString(), StringComparison.Ordinal);
        Assert.Contains("lastAnomalySqNum=5→0", diagnostic.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingReasonEvidence_IsUnknownNotNegativeGiProof()
    {
        var state = ActivatedState();
        Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata { SequenceNumber = 4 }, true);
        var warnings = Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata { SequenceNumber = 0 }, true);
        Assert.Contains(warnings, w => w.Contains("GIreason=UNKNOWN", StringComparison.Ordinal));
        Assert.Equal(2, state.ReasonUnavailableFrames);
        Assert.Null(state.LastAnomalyGiReason);
        Assert.Equal(1, state.AnomalyFindings);
    }

    [Fact]
    public void GiWriteRejected_DoesNotSuppressSqNumAlert()
    {
        var state = ActivatedState();
        state.GiWriteAccepted = false;
        Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata { SequenceNumber = 4 }, true);
        var warnings = Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata { SequenceNumber = 0 }, true);
        Assert.Contains(warnings, w => w.Contains("GIwrite=rejected/no", StringComparison.Ordinal));
        Assert.Equal(1, state.AnomalyFindings);
    }

    [Fact]
    public void ValidIncrement_WithGIReason_StillHasNoFalseAlert()
    {
        var state = ActivatedState();
        Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata { SequenceNumber = 2 }, true);
        var warnings = Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata
            {
                SequenceNumber = 3,
                GeneralInterrogationReasonSeen = true,
                IntegrityReasonSeen = false
            }, true);
        Assert.Empty(warnings);
        Assert.Equal(1, state.GiReasonFrames);
        Assert.Null(state.LastAnomalyCurrentSqNum);
    }

    [Fact]
    public void EngineProvenance_IsMappedOnBothSameSourceStaticIngressPaths()
    {
        var staticPath = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");
        Assert.Contains("ReadStaticActivationWriteResult(start.WriteSteps, \"RptEna\")",
            staticPath, StringComparison.Ordinal);
        Assert.Contains("ReadStaticActivationWriteResult(start.WriteSteps, \"GI\")",
            staticPath, StringComparison.Ordinal);
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        Assert.Contains("session.ReportActivationByPlanId[plan.PlanId] = result;",
            runtime, StringComparison.Ordinal);
        Assert.Equal(3, runtime.Split("session.ReportActivationByPlanId.Clear();",
            StringSplitOptions.None).Length - 1);
        var adapter = Read("Services/NativeIec61850Client.cs");
        Assert.Contains("optFlds.HasReasonForInclusion", adapter, StringComparison.Ordinal);
        Assert.Contains("general-interrogation", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("DecodeReasonForInclusion(", adapter, StringComparison.Ordinal);
    }

    private static Iec61850ReportContinuityState ActivatedState() => new()
    {
        ReportControlReference = "IED/LLN0.BR.rpt01",
        RptEnaWriteAccepted = true,
        GiWriteAccepted = true,
        ActivationReturnedAtUtc = DateTimeOffset.UtcNow
    };

    private static string Read(string path)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var full = Path.Combine(directory.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
