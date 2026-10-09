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

    [Fact]
    public void Shout_FromSuccess_DoesNotClaimControlRejection()
    {
        var result = new Iec61850ControlCommandResult { IsSuccess = true };
        var notice = Iec61850ControlShout.FromResult(result);
        Assert.Equal("Command succeeded", notice.Title);
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
        Assert.Contains("Iec61850ControlIdentity.DistinctOperable(Signals)", model, StringComparison.Ordinal);
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
