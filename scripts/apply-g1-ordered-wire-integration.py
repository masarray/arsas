from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"expected block not found in {path}:\n{old[:700]}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")

# Final immutable G1 engine pin.
lock_path = ROOT / "engines/ARIEC61850.lock.json"
lock = json.loads(lock_path.read_text(encoding="utf-8"))
lock["commit"] = "438d14b0dd6dce1b86b9d1c63d6bddd13510b11a"
lock["sourcePullRequest"] = 90
if "ordered SBO/SBOw" not in lock["purpose"]:
    lock["purpose"] = lock["purpose"].rstrip(".") + ", and G1 final PR #90 preserves ordered SBO/SBOw-to-Operate wire evidence in the native control result so field acceptance can prove each control service independently before process-feedback confirmation."
lock_path.write_text(json.dumps(lock, indent=2) + "\n", encoding="utf-8", newline="\n")

models = ROOT / "Models/ControlModels.cs"
replace_once(
    models,
    """public sealed class Iec61850ControlCommandResult\n{\n""",
    """public sealed class Iec61850ControlWireEvidence\n{\n    public string Action { get; init; } = string.Empty;\n    public string Reference { get; init; } = string.Empty;\n    public bool RequestAccepted { get; init; }\n    public string RequestHex { get; init; } = string.Empty;\n    public string ResponseHex { get; init; } = string.Empty;\n    public string Detail { get; init; } = string.Empty;\n}\n\npublic sealed class Iec61850ControlCommandResult\n{\n""")
replace_once(
    models,
    """    public string RequestHex { get; init; } = string.Empty;\n    public string ResponseHex { get; init; } = string.Empty;\n}\n""",
    """    public string RequestHex { get; init; } = string.Empty;\n    public string ResponseHex { get; init; } = string.Empty;\n    public IReadOnlyList<Iec61850ControlWireEvidence> WireSteps { get; init; } = Array.Empty<Iec61850ControlWireEvidence>();\n}\n""")

native = ROOT / "Services/NativeIec61850Client.cs"
replace_once(
    native,
    """            TotalElapsedText = totalElapsed.HasValue ? $\"{totalElapsed.Value.TotalMilliseconds:0.###} ms\" : $\"{result.Elapsed.TotalMilliseconds:0.###} ms\",\n            RequestHex = result.RequestHex,\n            ResponseHex = result.ResponseHex\n        };\n""",
    """            TotalElapsedText = totalElapsed.HasValue ? $\"{totalElapsed.Value.TotalMilliseconds:0.###} ms\" : $\"{result.Elapsed.TotalMilliseconds:0.###} ms\",\n            RequestHex = result.RequestHex,\n            ResponseHex = result.ResponseHex,\n            WireSteps = result.WireSteps.Select(step => new Iec61850ControlWireEvidence\n            {\n                Action = step.Action.ToString(),\n                Reference = step.Reference,\n                RequestAccepted = step.RequestAccepted,\n                RequestHex = step.RequestHex,\n                ResponseHex = step.ResponseHex,\n                Detail = step.Detail\n            }).ToArray()\n        };\n""")

runtime = ROOT / "Services/Iec61850MonitorRuntime.cs"
replace_once(
    runtime,
    """        var wireState = result.CompletionState.Equals(\"NotSent\", StringComparison.OrdinalIgnoreCase)\n            ? \"NOT SENT TO IED\"\n            : !string.IsNullOrWhiteSpace(result.ResponseHex)\n                ? \"MMS response received\"\n                : !string.IsNullOrWhiteSpace(result.RequestHex)\n                    ? \"MMS request encoded / no response captured\"\n                    : result.ServiceAccepted\n                        ? \"MMS service accepted\"\n                        : \"no wire evidence returned\";\n""",
    """        var wireState = result.CompletionState.Equals(\"NotSent\", StringComparison.OrdinalIgnoreCase)\n            ? \"NOT SENT TO IED\"\n            : result.WireSteps.Count > 0 && result.WireSteps.All(step => !string.IsNullOrWhiteSpace(step.ResponseHex))\n                ? $\"{result.WireSteps.Count} ordered MMS control response(s) captured\"\n                : result.WireSteps.Count > 0\n                    ? $\"{result.WireSteps.Count} ordered MMS control step(s); incomplete response evidence\"\n                    : !string.IsNullOrWhiteSpace(result.ResponseHex)\n                        ? \"MMS response received\"\n                        : !string.IsNullOrWhiteSpace(result.RequestHex)\n                            ? \"MMS request encoded / no response captured\"\n                            : result.ServiceAccepted\n                                ? \"MMS service accepted\"\n                                : \"no wire evidence returned\";\n""")
replace_once(
    runtime,
    """        if (!string.IsNullOrWhiteSpace(result.RequestHex))\n            Log(\"INFO\", session.Device.Name,\n                $\"CONTROL_WIRE_REQUEST: {request.Signal.ObjectReference}; requestHEX={result.RequestHex}\");\n        if (!string.IsNullOrWhiteSpace(result.ResponseHex))\n            Log(\"INFO\", session.Device.Name,\n                $\"CONTROL_WIRE_RESPONSE: {request.Signal.ObjectReference}; responseHEX={result.ResponseHex}\");\n\n        return result;\n""",
    """        if (result.WireSteps.Count > 0)\n        {\n            for (var index = 0; index < result.WireSteps.Count; index++)\n            {\n                var step = result.WireSteps[index];\n                Log(step.RequestAccepted ? \"INFO\" : \"WARN\", session.Device.Name,\n                    $\"CONTROL_WIRE_STEP: order={index + 1}; action={step.Action}; reference={step.Reference}; accepted={step.RequestAccepted}; requestCaptured={!string.IsNullOrWhiteSpace(step.RequestHex)}; responseCaptured={!string.IsNullOrWhiteSpace(step.ResponseHex)}; detail={step.Detail}\");\n                if (!string.IsNullOrWhiteSpace(step.RequestHex))\n                    Log(\"INFO\", session.Device.Name,\n                        $\"CONTROL_WIRE_REQUEST: order={index + 1}; action={step.Action}; reference={step.Reference}; requestHEX={step.RequestHex}\");\n                if (!string.IsNullOrWhiteSpace(step.ResponseHex))\n                    Log(\"INFO\", session.Device.Name,\n                        $\"CONTROL_WIRE_RESPONSE: order={index + 1}; action={step.Action}; reference={step.Reference}; responseHEX={step.ResponseHex}\");\n            }\n        }\n        else\n        {\n            // Compatibility fallback for a local failure or older action result without\n            // ordered service evidence. Never infer server acceptance from request HEX alone.\n            if (!string.IsNullOrWhiteSpace(result.RequestHex))\n                Log(\"INFO\", session.Device.Name,\n                    $\"CONTROL_WIRE_REQUEST: {request.Signal.ObjectReference}; requestHEX={result.RequestHex}\");\n            if (!string.IsNullOrWhiteSpace(result.ResponseHex))\n                Log(\"INFO\", session.Device.Name,\n                    $\"CONTROL_WIRE_RESPONSE: {request.Signal.ObjectReference}; responseHEX={result.ResponseHex}\");\n        }\n\n        return result;\n""")

window = ROOT / "ControlCommandWindow.xaml.cs"
replace_once(
    window,
    """        else if (!string.IsNullOrWhiteSpace(result.RequestHex))\n            details.Add(\"MMS request encoding was captured, but no MMS response was captured.\");\n        if (result.CommandTerminationReceived)\n""",
    """        else if (!string.IsNullOrWhiteSpace(result.RequestHex))\n            details.Add(\"MMS request encoding was captured, but no MMS response was captured.\");\n        if (result.WireSteps.Count > 0)\n            details.Add($\"Wire sequence: {string.Join(\" → \", result.WireSteps.Select(step => step.Action))}.\");\n        if (result.CommandTerminationReceived)\n""")

print("G1 final ordered wire integration applied")
