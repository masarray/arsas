using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Real ZIV sequence: live typed BSC verified first, then a stale static DataSet
/// announces SPC, then operator requests directional tap change. No network IO.
/// </summary>
public sealed class P109NControlAuthorityTimelineTests
{
    private static SignalDefinition Control(string name, string cdc) => new()
    {
        Name = name[(name.LastIndexOf('.') + 1)..],
        ObjectReference = name,
        FunctionalConstraint = "CO",
        IsControlSignal = true,
        IsSelected = true,
        ControlCdc = cdc
    };

    [Fact]
    public void ZivLiveBscSurvivesStaticProjection_AndRetainsRaiseLower()
    {
        var tap = Control("ZIVAVRLD1/ATCC1.TapChg", "SPC");
        tap.ControlModelText = "Select Before Operate (SBO) • Normal security";
        tap.AcceptVerifiedLiveControlCdc("BSC");
        tap.ControlSemanticVerified = true;
        Assert.True(tap.ControlSupportsOperate);
        Assert.True(tap.IsRaiseLowerControl);
        Assert.False(tap.IsBooleanControl);

        // Exact regression: static DataSet arrives AFTER verified control inspection.
        var accepted = tap.ApplyDataSetAdvisoryControlMetadata(
            "SPC", "ZIVAVRLD1/ATCC1.TapChg.valWTr.posVal");
        Assert.False(accepted);
        Assert.Equal("SPC", tap.DataSetAdvisoryCdc);
        Assert.Equal("BSC", tap.VerifiedLiveControlCdc);
        Assert.Equal("BSC", tap.ControlCdc);
        Assert.True(tap.IsRaiseLowerControl);
        Assert.False(tap.IsBooleanControl);
        Assert.True(tap.ControlSupportsOperate);

        // Second pre-existing producer also cannot replace typed live authority.
        tap.ControlCdc = "SPC";
        Assert.Equal("BSC", tap.ControlCdc);
        Assert.True(tap.IsRaiseLowerControl);
    }

    [Fact]
    public void LiveReadOnlyControlNeverBecomesOperableDueToStaticMetadata()
    {
        var pos = Control("ZIVAVRLD1/CSWI1.Pos", "DPC");
        pos.ControlModelText = "Status only";
        pos.ApplyDataSetAdvisoryControlMetadata("DPC", "ZIVAVRLD1/CSWI1.Pos.stVal");
        Assert.True(pos.IsPositionControl);
        Assert.False(pos.ControlSupportsOperate);
        Assert.False(pos.TryStageControlConfirmation("Closed [10]", "Close", out _));
        Assert.False(pos.TryBeginDirectControlCommand("Closed [10]", "Close", out _, out _));
    }

    [Fact]
    public void NewAssociationResetsLiveAuthorityAndRequiresReinspection()
    {
        var tap = Control("ZIVAVRLD1/ATCC1.TapChg", "BSC");
        tap.ControlModelText = "Select Before Operate (SBO) • Normal security";
        tap.AcceptVerifiedLiveControlCdc("BSC");
        tap.ControlSemanticVerified = true;
        tap.ClearLiveControlAuthority();
        tap.ControlModelText = "Auto-detect";
        Assert.Equal(string.Empty, tap.VerifiedLiveControlCdc);
        Assert.False(tap.ControlSupportsOperate);
        Assert.False(tap.ControlModelResolved);
        Assert.True(tap.ApplyDataSetAdvisoryControlMetadata("SPC", null));
        Assert.Equal("SPC", tap.ControlCdc);
        Assert.False(tap.ControlSupportsOperate);
    }

    [Fact]
    public void LocalValidationMismatchHasExactClientDiagnostic_NotIEDDenial()
    {
        var failure = new Iec61850ControlCommandResult
        {
            CompletionState = "NotSent",
            Stage = "Validation",
            Message = "Unsafe or ambiguous IEC 61850 control semantics: Declared CDC 'SPC' conflicts with live CDC evidence 'BSC'."
        };
        var explanation = Iec61850ControlFailureReason.Explain(failure);
        Assert.Equal("LocalValidation", explanation.Confidence);
        Assert.Contains("SPC", explanation.Evidence);
        Assert.Contains("BSC", explanation.Evidence);
        Assert.Contains("no MMS control write", explanation.Checks);
        var unrelatedDenied = new Iec61850ControlCommandResult {
            CompletionState = "Rejected", Stage = "Operate",
            Message = "MMS object-access-denied"
        };
        Assert.Equal("MmsServiceOnly", Iec61850ControlFailureReason.Explain(unrelatedDenied).Confidence);
    }

    [Fact]
    public void AutomaticReconnectAlsoRevokesAllAssociationScopedControlAuthority()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(
            dir.FullName, "Services", "Iec61850MonitorRuntime.cs")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(
            dir!.FullName, "Services", "Iec61850MonitorRuntime.cs"));
        Assert.Contains("ResetAssociationControlAuthority(session.Device);", source);
        var start = source.IndexOf("private async Task TryReconnectAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private void ScheduleReconnectRetry(", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        var body = source[start..end];
        Assert.True(body.IndexOf("ResetAssociationControlAuthority(session.Device);", StringComparison.Ordinal) <
                    body.IndexOf("ConnectUsingSelectedFastPathAsync(", StringComparison.Ordinal));
        Assert.Contains("signal.ClearLiveControlAuthority();", source);
        Assert.Contains("signal.ControlModelText = \"Auto-detect\";", source);
    }

    [Fact]
    public void ProjectionProducerUsesAdvisoryApi_NotDirectCdcAssignment()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(
            dir.FullName, "Services", "Iec61850StaticControlStatusProjectionService.cs")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(
            dir!.FullName, "Services", "Iec61850StaticControlStatusProjectionService.cs"));
        var method = source[source.IndexOf("private static void LinkControlToMembership(", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("private static SignalDefinition CreateRuntimeFeedback(", StringComparison.Ordinal)];
        Assert.Contains("control.ApplyDataSetAdvisoryControlMetadata(cdc, exactFeedbackReference)", method);
        Assert.DoesNotContain("control.ControlCdc = cdc;", method);
    }
}
