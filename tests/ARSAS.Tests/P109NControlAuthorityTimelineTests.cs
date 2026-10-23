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
        Assert.Contains("await ResetAssociationControlAuthorityAsync(session.Device).ConfigureAwait(false);", source);
        Assert.Contains("dispatcher.InvokeAsync(", source);
        var start = source.IndexOf("private async Task TryReconnectAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private void ScheduleReconnectRetry(", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        var body = source[start..end];
        Assert.True(body.IndexOf("await ResetAssociationControlAuthorityAsync(session.Device).ConfigureAwait(false);", StringComparison.Ordinal) <
                    body.IndexOf("ConnectUsingSelectedFastPathAsync(", StringComparison.Ordinal));
        Assert.Contains("signal.ClearLiveControlAuthority();", source);
        Assert.Contains("signal.ControlModelText = \"Auto-detect\";", source);
    }

    [Fact]
    public void CompactDockKeepsFullLiveAreaAndActionButtonsWithinRows()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MainWindow.xaml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(dir.FullName, "MainWindow.xaml.cs"));
        Assert.DoesNotContain("ExplorerLiveRow", xaml);
        Assert.DoesNotContain("ExplorerCommandRow", xaml);
        Assert.DoesNotContain("ExplorerLiveRow.Height", code);
        Assert.Contains("RowHeight=\"43\" MinRowHeight=\"42\"", xaml);
        Assert.Contains("Height=\"29\" MinHeight=\"29\" Padding=\"10,3\"", xaml);
        Assert.Contains("Content=\"ctlModel\"", xaml);
        Assert.Contains("Style=\"{StaticResource CommandDockCtlModelButton}\"", xaml);
        Assert.Contains("CommandParameter=\"Raise\" Click=\"ControlStageAction_Click\"", xaml);
        Assert.Contains("CommandParameter=\"Lower\" Click=\"ControlStageAction_Click\"", xaml);
    }

    [Fact]
    public void ZivRaiseLowerRequiresExplicitConfirmBeforeCommandClaim()
    {
        var tap = Control("ZIVAVRLD1/ATCC1.TapChg", "BSC");
        tap.ControlModelText = "Select Before Operate (SBO) • Normal security";
        tap.AcceptVerifiedLiveControlCdc("BSC");
        tap.ControlSemanticVerified = true;
        Assert.True(tap.TryStageControlConfirmation("Raise", "Raise", out _));
        Assert.True(tap.ControlConfirmationPending);
        Assert.Equal("Confirm Raise", tap.ControlPendingConfirmationLabel);
        Assert.False(tap.ControlCommandBusy);
        tap.ClearControlConfirmation();
        Assert.False(tap.ControlConfirmationPending);
        Assert.False(tap.TryClaimControlConfirmation(out _, out _));
        Assert.True(tap.TryStageControlConfirmation("Lower", "Lower", out _));
        Assert.True(tap.TryClaimControlConfirmation(out var claim, out _));
        Assert.NotNull(claim);
        Assert.True(tap.ControlCommandBusy);
        Assert.True(tap.CompleteControlCommand(claim!));
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
