from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"expected block not found: {path}\n---\n{old[:500]}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")


# Pin exact G1 engine candidate and preserve provenance history.
lock_path = ROOT / "engines/ARIEC61850.lock.json"
lock = json.loads(lock_path.read_text(encoding="utf-8"))
lock["commit"] = "e2c26fc4c081b785c2fe12005ada26ba9580bd61"
lock["sourcePullRequest"] = 90
suffix = (
    ", and PR #90 fixes live MMS TypeSpecification size semantics for Smart Control by decoding signed primitive constraints "
    "(-N = variable length with maximum N, +N = fixed N), so the IEC 61850 two-bit Check field is no longer misread as 254 bits; "
    "the control builder keeps exact two-bit synchro/interlock semantics and fixed-width validation remains fail-closed."
)
if "PR #90" not in lock["purpose"]:
    lock["purpose"] = lock["purpose"].rstrip(".") + suffix
lock_path.write_text(json.dumps(lock, indent=2) + "\n", encoding="utf-8", newline="\n")

native = ROOT / "Services/NativeIec61850Client.cs"
replace_once(
    native,
    '''        ArControl.Iec61850ControlActionResult action;\n        try\n        {\n            action = await RunMmsOperationAsync(\n                () => control.OperateAsync(nativeRequest, cancellationToken),\n                cancellationToken).ConfigureAwait(false);\n        }\n        catch (OperationCanceledException)\n        {\n            throw;\n        }\n        catch (Exception ex)\n        {\n            return ControlFailure(\n                "Control exception",\n                $"{ex.GetType().Name}: {ex.Message}",\n                capabilities,\n                expectedValue);\n        }\n''',
    '''        ArControl.Iec61850ControlActionResult action;\n        var wireRequestBeforeControl = _session.LastReadRequestHex;\n        var wireResponseBeforeControl = _session.LastReadResponseHex;\n        try\n        {\n            action = await RunMmsOperationAsync(\n                () => control.OperateAsync(nativeRequest, cancellationToken),\n                cancellationToken).ConfigureAwait(false);\n        }\n        catch (OperationCanceledException)\n        {\n            throw;\n        }\n        catch (Exception ex)\n        {\n            var requestChanged = !string.Equals(\n                wireRequestBeforeControl,\n                _session.LastReadRequestHex,\n                StringComparison.Ordinal);\n            var responseChanged = !string.Equals(\n                wireResponseBeforeControl,\n                _session.LastReadResponseHex,\n                StringComparison.Ordinal);\n\n            return requestChanged\n                ? ControlWireUnknownFailure(\n                    ex,\n                    capabilities,\n                    expectedValue,\n                    _session.LastReadRequestHex,\n                    responseChanged ? _session.LastReadResponseHex : string.Empty)\n                : ControlNotSentFailure(ex, capabilities, expectedValue);\n        }\n''')

replace_once(
    native,
    '''    private static Iec61850ControlCommandResult ControlFailure(\n        string stage,\n        string message,\n        Iec61850ControlCapabilities capabilities,\n        string requestedValue)\n        => new()\n        {\n            IsSuccess = false,\n            ServiceAccepted = false,\n            FeedbackConfirmed = false,\n            CompletionState = "Rejected",\n            Stage = stage,\n            Message = message,\n            ControlModelText = capabilities.ControlModelText,\n            SequenceText = capabilities.SequenceText,\n            RequestedValue = requestedValue,\n            FeedbackValue = capabilities.CurrentValue\n        };\n''',
    '''    private static Iec61850ControlCommandResult ControlFailure(\n        string stage,\n        string message,\n        Iec61850ControlCapabilities capabilities,\n        string requestedValue)\n        => new()\n        {\n            IsSuccess = false,\n            ServiceAccepted = false,\n            FeedbackConfirmed = false,\n            CompletionState = "Rejected",\n            Stage = stage,\n            Message = message,\n            ControlModelText = capabilities.ControlModelText,\n            SequenceText = capabilities.SequenceText,\n            RequestedValue = requestedValue,\n            FeedbackValue = capabilities.CurrentValue\n        };\n\n    private static Iec61850ControlCommandResult ControlNotSentFailure(\n        Exception exception,\n        Iec61850ControlCapabilities capabilities,\n        string requestedValue)\n        => new()\n        {\n            IsSuccess = false,\n            ServiceAccepted = false,\n            FeedbackConfirmed = false,\n            CompletionState = "NotSent",\n            Stage = "NOT SENT TO IED",\n            Message = $"Local IEC 61850 control preparation failed before any MMS control request was built or sent. {exception.GetType().Name}: {exception.Message}",\n            ControlModelText = capabilities.ControlModelText,\n            SequenceText = capabilities.SequenceText,\n            RequestedValue = requestedValue,\n            FeedbackValue = capabilities.CurrentValue\n        };\n\n    private static Iec61850ControlCommandResult ControlWireUnknownFailure(\n        Exception exception,\n        Iec61850ControlCapabilities capabilities,\n        string requestedValue,\n        string requestHex,\n        string responseHex)\n        => new()\n        {\n            IsSuccess = false,\n            ServiceAccepted = false,\n            FeedbackConfirmed = false,\n            CompletionState = "WireStateUnknown",\n            Stage = "MMS control transport incomplete",\n            Message = $"An MMS control request was encoded and transport may have started, but the control sequence did not complete. {exception.GetType().Name}: {exception.Message}",\n            ControlModelText = capabilities.ControlModelText,\n            SequenceText = capabilities.SequenceText,\n            RequestedValue = requestedValue,\n            FeedbackValue = capabilities.CurrentValue,\n            RequestHex = requestHex ?? string.Empty,\n            ResponseHex = responseHex ?? string.Empty\n        };\n''')

runtime = ROOT / "Services/Iec61850MonitorRuntime.cs"
replace_once(
    runtime,
    '''        var protocolEvidence = string.Join("; ", new[]\n        {\n            string.IsNullOrWhiteSpace(result.CompletionState) ? null : $"completion={result.CompletionState}",\n            result.CommandTerminationReceived ? $"termination={(result.PositiveTermination ? "positive" : "negative")}" : null,\n            string.IsNullOrWhiteSpace(result.ControlError) ? null : $"controlError={result.ControlError}",\n            string.IsNullOrWhiteSpace(result.AddCause) ? null : $"addCause={result.AddCause}",\n            result.ControlNumber == "-" ? null : $"ctlNum={result.ControlNumber}",\n            result.ElapsedText == "-" ? null : $"control={result.ElapsedText}",\n            result.FeedbackElapsedText == "-" ? null : $"feedback={result.FeedbackElapsedText}",\n            result.TotalElapsedText == "-" ? null : $"engineTotal={result.TotalElapsedText}",\n            $"clientTotal={clientStopwatch.Elapsed.TotalMilliseconds:0.###} ms"\n        }.Where(text => !string.IsNullOrWhiteSpace(text)));\n\n        Log(result.IsSuccess ? "INFO" : "ERROR", session.Device.Name,\n            $"Control {result.Stage}: {request.Signal.ObjectReference}; sequence={result.SequenceText}; requested={result.RequestedValue}; feedback={result.FeedbackValue}; {protocolEvidence}; {result.Message}");\n        return result;\n''',
    '''        var wireState = result.CompletionState.Equals("NotSent", StringComparison.OrdinalIgnoreCase)\n            ? "NOT SENT TO IED"\n            : !string.IsNullOrWhiteSpace(result.ResponseHex)\n                ? "MMS response received"\n                : !string.IsNullOrWhiteSpace(result.RequestHex)\n                    ? "MMS request encoded / no response captured"\n                    : result.ServiceAccepted\n                        ? "MMS service accepted"\n                        : "no wire evidence returned";\n\n        var protocolEvidence = string.Join("; ", new[]\n        {\n            string.IsNullOrWhiteSpace(result.CompletionState) ? null : $"completion={result.CompletionState}",\n            $"wire={wireState}",\n            result.CommandTerminationReceived ? $"termination={(result.PositiveTermination ? "positive" : "negative")}" : null,\n            string.IsNullOrWhiteSpace(result.ControlError) ? null : $"controlError={result.ControlError}",\n            string.IsNullOrWhiteSpace(result.AddCause) ? null : $"addCause={result.AddCause}",\n            result.ControlNumber == "-" ? null : $"ctlNum={result.ControlNumber}",\n            result.ElapsedText == "-" ? null : $"control={result.ElapsedText}",\n            result.FeedbackElapsedText == "-" ? null : $"feedback={result.FeedbackElapsedText}",\n            result.TotalElapsedText == "-" ? null : $"engineTotal={result.TotalElapsedText}",\n            $"clientTotal={clientStopwatch.Elapsed.TotalMilliseconds:0.###} ms"\n        }.Where(text => !string.IsNullOrWhiteSpace(text)));\n\n        Log(result.IsSuccess ? "INFO" : "ERROR", session.Device.Name,\n            $"Control {result.Stage}: {request.Signal.ObjectReference}; sequence={result.SequenceText}; requested={result.RequestedValue}; feedback={result.FeedbackValue}; {protocolEvidence}; {result.Message}");\n\n        if (!string.IsNullOrWhiteSpace(result.RequestHex))\n            Log("INFO", session.Device.Name,\n                $"CONTROL_WIRE_REQUEST: {request.Signal.ObjectReference}; requestHEX={result.RequestHex}");\n        if (!string.IsNullOrWhiteSpace(result.ResponseHex))\n            Log("INFO", session.Device.Name,\n                $"CONTROL_WIRE_RESPONSE: {request.Signal.ObjectReference}; responseHEX={result.ResponseHex}");\n\n        return result;\n''')

window = ROOT / "ControlCommandWindow.xaml.cs"
replace_once(
    window,
    '''    private static string BuildCommandResultText(Iec61850ControlCommandResult result)\n    {\n        var details = new List<string> { result.Message };\n        if (result.CommandTerminationReceived)\n''',
    '''    private static string BuildCommandResultText(Iec61850ControlCommandResult result)\n    {\n        var details = new List<string> { result.Message };\n        if (result.CompletionState.Equals("NotSent", StringComparison.OrdinalIgnoreCase))\n            details.Add("No MMS control request was sent to the IED.");\n        else if (!string.IsNullOrWhiteSpace(result.ResponseHex))\n            details.Add("MMS request/response wire evidence was captured.");\n        else if (!string.IsNullOrWhiteSpace(result.RequestHex))\n            details.Add("MMS request encoding was captured, but no MMS response was captured.");\n        if (result.CommandTerminationReceived)\n''')

# Focused source/provenance regression: G1 must not touch acquisition/reconnect semantics.
test = ROOT / "tests/ARSAS.Tests/G1ControlCorrectnessRegressionTests.cs"
test.write_text(r'''using System.Text.Json;

namespace ARSAS.Tests;

public sealed class G1ControlCorrectnessRegressionTests
{
    [Fact]
    public void EngineLock_PinsExactG1ControlEngine()
    {
        var root = RepoRoot();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "engines", "ARIEC61850.lock.json")));
        var json = doc.RootElement;
        Assert.Equal("masarray/ARIEC61850", json.GetProperty("repository").GetString());
        Assert.Equal("main", json.GetProperty("ref").GetString());
        Assert.Equal("e2c26fc4c081b785c2fe12005ada26ba9580bd61", json.GetProperty("commit").GetString());
        Assert.Equal(90, json.GetProperty("sourcePullRequest").GetInt32());
        Assert.Contains("signed primitive constraints", json.GetProperty("purpose").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalControlPreparationFailure_IsExplicitlyNotSent()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "NativeIec61850Client.cs"));
        Assert.Contains("wireRequestBeforeControl", source, StringComparison.Ordinal);
        Assert.Contains("ControlNotSentFailure", source, StringComparison.Ordinal);
        Assert.Contains("CompletionState = \"NotSent\"", source, StringComparison.Ordinal);
        Assert.Contains("Stage = \"NOT SENT TO IED\"", source, StringComparison.Ordinal);
        Assert.Contains("before any MMS control request was built or sent", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ControlWireUnknownFailure", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_EmitsExactWireEvidenceOnlyWhenReturnedByControlStack()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "Iec61850MonitorRuntime.cs"));
        Assert.Contains("CONTROL_WIRE_REQUEST:", source, StringComparison.Ordinal);
        Assert.Contains("CONTROL_WIRE_RESPONSE:", source, StringComparison.Ordinal);
        Assert.Contains("MMS request encoded / no response captured", source, StringComparison.Ordinal);
        Assert.Contains("MMS response received", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MMS command submitted", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void G1_DoesNotReenableDynamicReportingOrChangeReconnectPolicy()
    {
        var engineLock = File.ReadAllText(Path.Combine(RepoRoot(), "engines", "ARIEC61850.lock.json"));
        Assert.Contains("PR #89 quarantines automatic full dynamic DataSet activation", engineLock, StringComparison.OrdinalIgnoreCase);

        var runtime = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "Iec61850MonitorRuntime.cs"));
        Assert.Contains("SmartReconnectPolicy", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicBrcb = true", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicUrcb = true", runtime, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ArIED61850Tester.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("ARSAS repository root not found.");
    }
}
''', encoding="utf-8", newline="\n")

print("G1 ARSAS control integration patch applied")
