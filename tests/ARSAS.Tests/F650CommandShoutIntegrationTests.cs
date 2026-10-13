using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Cross-workstream G1/F650 acceptance: canonical target and non-blocking
/// evidence-graded rejection notification must coexist in the same build.
/// </summary>
public sealed class F650CommandShoutIntegrationTests
{
    [Fact]
    public void MmsDenied_StatesNoExactCause_AndSuggestsLocalRemoteCheck()
    {
        var result = new Iec61850ControlCommandResult
        {
            Message = "MMS Confirmed-Write rejected: item[0]=object-access-denied (3)",
            CompletionState = "Rejected"
        };
        var notice = Iec61850ControlShout.FromResult(result);
        Assert.Contains("reason unspecified", notice.Title, StringComparison.Ordinal);
        Assert.Contains("Check BCU Local/Remote", notice.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("IED confirmed Local", notice.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("blocked-by-interlocking", "interlocking")]
    [InlineData("blocked-by-synchrocheck", "synchrocheck")]
    [InlineData("blocked-by-mode", "control mode")]
    [InlineData("no-access-authority", "access authority")]
    public void ExplicitIedAddCause_IsShoutedAsEngineEvidence(string addCause, string message)
    {
        var result = new Iec61850ControlCommandResult
        {
            AddCause = addCause,
            Message = "IED refused operation",
            CompletionState = "Rejected"
        };
        var notice = Iec61850ControlShout.FromResult(result);
        Assert.Contains(message, notice.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LastApplError AddCause", notice.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("exact cause not supplied", notice.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoWireTransmission_ReportsNotSentRatherThanIedRejected()
    {
        var result = new Iec61850ControlCommandResult { CompletionState = "NotSent" };
        var notice = Iec61850ControlShout.FromResult(result);
        Assert.Equal("Command not sent", notice.Title);
        Assert.Contains("Client rejected before MMS Operate", notice.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Closed [10]", "Position reached")]
    [InlineData("Open [01]", "Position reached")]
    [InlineData("Raise", "Tap movement confirmed")]
    [InlineData("Lower", "Tap movement confirmed")]
    public void ConfirmedFeedback_ProducesTruthfulSuccessToast(string requested, string expected)
    {
        var result = new Iec61850ControlCommandResult
        {
            IsSuccess = true, ServiceAccepted = true, FeedbackConfirmed = true,
            RequestedValue = requested, FeedbackValue = requested == "Raise" ? "12" : "Open [01]"
        };
        var notice = Iec61850ControlShout.FromResult(result);
        Assert.Equal(expected, notice.Title);
        Assert.Equal(Iec61850ControlNoticeTone.Success, notice.Tone);
        Assert.Contains("IED feedback", notice.Detail);
    }

    [Fact]
    public void UnverifiedServiceSuccess_NeverClaimsPositionReached()
    {
        var result = new Iec61850ControlCommandResult
        {
            IsSuccess = true, ServiceAccepted = true,
            FeedbackConfirmed = false, RequestedValue = "Closed [10]",
            Stage = "Command accepted"
        };
        var notice = Iec61850ControlShout.FromResult(result);
        Assert.Equal("Command accepted", notice.Title);
        Assert.Equal(Iec61850ControlNoticeTone.Warning, notice.Tone);
        Assert.Contains("not confirmed", notice.Detail);
    }

    [Fact]
    public void BareSuccessFlag_DoesNotInventMmsCommand()
    {
        var notice = Iec61850ControlShout.FromResult(new() { IsSuccess = true });
        Assert.Equal("Command outcome unverified", notice.Title);
        Assert.NotEqual(Iec61850ControlNoticeTone.Success, notice.Tone);
    }

    [Fact]
    public void AlreadyAtState_ReturnsNeutralNoticeAndNoSentClaim()
    {
        var notice = Iec61850ControlShout.FromResult(new()
        {
            IsSuccess = true, ServiceAccepted = false, FeedbackConfirmed = true,
            CompletionState = "NotSent", Stage = "Already at requested state",
            RequestedValue = "Open [01]", FeedbackValue = "Open [01]"
        });
        Assert.Equal("Already at requested state", notice.Title);
        Assert.Equal(Iec61850ControlNoticeTone.Info, notice.Tone);
        Assert.Contains("No control command was sent", notice.Detail);
    }

    [Fact]
    public void FeedbackTimeout_DistinguishesServiceAcceptanceFromPositionReached()
    {
        var notice = Iec61850ControlShout.FromResult(new()
        {
            IsSuccess = false, ServiceAccepted = true,
            FeedbackConfirmed = false, Stage = "Feedback timeout",
            RequestedValue = "Raise", FeedbackValue = "11"
        });
        Assert.Equal("Feedback not confirmed", notice.Title);
        Assert.Equal(Iec61850ControlNoticeTone.Warning, notice.Tone);
    }

    [Fact]
    public void TestModeNeverClaimsPrimaryProcessMovement()
    {
        var notice = Iec61850ControlShout.FromResult(new()
        {
            IsSuccess = true, ServiceAccepted = true,
            Stage = "Test command accepted", RequestedValue = "Closed [10]"
        });
        Assert.Equal("Test command accepted", notice.Title);
        Assert.Equal(Iec61850ControlNoticeTone.Info, notice.Tone);
        Assert.Contains("not verified", notice.Detail);
    }

    [Fact]
    public void NegativeTerminationAlwaysRemainsError()
    {
        var notice = Iec61850ControlShout.FromResult(new()
        {
            ServiceAccepted = true, IsSuccess = false,
            CommandTerminationReceived = true, PositiveTermination = false
        });
        Assert.Equal(Iec61850ControlNoticeTone.Error, notice.Tone);
        Assert.DoesNotContain("reached", notice.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoAccessAuthorityFromIedNeverInventsPhysicalLocalMode()
    {
        var notice = Iec61850ControlShout.FromResult(new()
        {
            IsSuccess = false, AddCause = "no-access-authority", CompletionState = "Rejected"
        });
        Assert.Equal("Authorization denied by IED", notice.Title);
        Assert.Equal(Iec61850ControlNoticeTone.Error, notice.Tone);
        Assert.Contains("AddCause", notice.Detail);
        Assert.DoesNotContain("confirmed Local", notice.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalPrewireBlockIsNotRepresentedAsIedRejection()
    {
        var notice = Iec61850ControlShout.FromLocalRejection("live model is not ready");
        Assert.Equal("Command not sent", notice.Title);
        Assert.Equal(Iec61850ControlNoticeTone.Warning, notice.Tone);
        Assert.Contains("before dispatch", notice.Detail);
    }

    [Fact]
    public void UnexpectedFailure_PreservesExactErrorInDiagnosticsNotOverlay()
    {
        var notice = Iec61850ControlShout.FromUnexpectedFailure();
        Assert.Contains("Diagnostics", notice.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void IedErrorString_CannotInjectMultilineOrUnboundedText()
    {
        var result = new Iec61850ControlCommandResult
        {
            AddCause = "blocked-by-interlocking",
            Message = "reason",
            CompletionState = "Rejected"
        };
        var notice = Iec61850ControlShout.FromResult(result);
        Assert.DoesNotContain("\n", notice.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", notice.Detail, StringComparison.Ordinal);
        Assert.True(notice.Title.Length <= 100);
        Assert.True(notice.Detail.Length <= 251);
    }

    [Fact]
    public void UiOverlay_IsNonmodalAndReusesDispatcherTimer()
    {
        var xaml = Read("MainWindow.xaml");
        var code = Read("MainWindow.xaml.cs");
        Assert.Contains("x:Name=\"ControlShoutCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Panel.ZIndex=\"90\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ControlShoutAccent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ControlShoutGlyph\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowControlShout(Iec61850ControlShout.FromResult(result));", code, StringComparison.Ordinal);
        Assert.Contains("if (!result.IsSuccess)", code, StringComparison.Ordinal);
        Assert.Contains("Interval = TimeSpan.FromSeconds(8)", code, StringComparison.Ordinal);
        Assert.Contains("ControlShoutCard.Visibility = Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.CheckAccess()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBoxImage.Warning", code, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityProjection_AndSingleFlightProtection_StillActive()
    {
        var model = Read("Models/MonitorModels.cs");
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        Assert.Contains("Iec61850ControlIdentity.DistinctSelected(Signals)", model, StringComparison.Ordinal);
        Assert.Contains("ControlSupportsOperate", model, StringComparison.Ordinal);
        Assert.Contains("_activeControlTargets.TryAdd(targetKey, 0)", runtime, StringComparison.Ordinal);
        Assert.Contains("_activeControlTargets.TryRemove(targetKey, out _)", runtime, StringComparison.Ordinal);
    }

    private static string Read(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var file = Path.Combine(dir.FullName, path);
            if (File.Exists(file)) return File.ReadAllText(file);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
