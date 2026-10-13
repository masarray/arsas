using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Presentation-only outcomes. No IED semantic or process value is inferred
/// from UI text; the native command result remains authoritative.
/// </summary>
internal enum Iec61850ControlNoticeTone { Info, Success, Warning, Error }

/// <summary>
/// Compact, sanitized command feedback for the operator. A confirmed MMS
/// service response is not proof that the requested primary state was reached.
/// </summary>
internal static class Iec61850ControlShout
{
    internal sealed record Notice(string Title, string Detail, Iec61850ControlNoticeTone Tone);

    internal static Notice FromResult(Iec61850ControlCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var stage = (result.Stage ?? string.Empty).Trim();
        var feedback = Safe(result.FeedbackValue, 65);
        var requested = Safe(result.RequestedValue, 55);

        // The preflight optimization returns IsSuccess=true even though no
        // Select/Operate was sent. Never claim command success in this case.
        if (result.IsSuccess && stage.Equals("Already at requested state",
                StringComparison.OrdinalIgnoreCase) &&
            result.CompletionState.Equals("NotSent", StringComparison.OrdinalIgnoreCase))
            return new("Already at requested state",
                Safe($"Live position: {feedback}. No control command was sent.", 170),
                Iec61850ControlNoticeTone.Info);

        // In test mode process movement is deliberately not required.
        if (result.IsSuccess && stage.StartsWith("Test command", StringComparison.OrdinalIgnoreCase))
            return new("Test command accepted",
                "IED test response received. Physical position change was not verified.",
                Iec61850ControlNoticeTone.Info);

        if (result.IsSuccess && result.ServiceAccepted && result.FeedbackConfirmed)
        {
            var isTap = requested.Equals("Raise", StringComparison.OrdinalIgnoreCase) ||
                        requested.Equals("Lower", StringComparison.OrdinalIgnoreCase);
            var isPosition = requested.Contains("Open", StringComparison.OrdinalIgnoreCase) ||
                             requested.Contains("Clos", StringComparison.OrdinalIgnoreCase);
            var title = isTap ? "Tap movement confirmed" :
                        isPosition ? "Position reached" : "Feedback confirmed";
            return new(title, Safe($"IED feedback: {feedback} • Requested: {requested}", 170),
                Iec61850ControlNoticeTone.Success);
        }

        // The control service may be accepted without verified process feedback.
        // Neither positive termination nor Test=true proves a position reached.
        if (result.IsSuccess)
            return new(result.ServiceAccepted ? "Command accepted" : "Command outcome unverified",
                result.ServiceAccepted
                    ? "IED accepted the service; the requested process state was not confirmed."
                    : "No accepted control service or verified position change is recorded.",
                Iec61850ControlNoticeTone.Warning);

        var explanation = Iec61850ControlFailureReason.Explain(result);
        if (explanation.Confidence == "IEDExplicitAddCause")
        {
            var cause = (result.AddCause ?? string.Empty).Trim().ToLowerInvariant();
            var title = cause switch
            {
                "blocked-by-interlocking" => "Blocked by interlock",
                "blocked-by-synchrocheck" => "Blocked by synchrocheck",
                "blocked-by-mode" => "Blocked by control mode",
                "blocked-by-switching-hierarchy" => "Blocked by switching hierarchy",
                "no-access-authority" => "Authorization denied by IED",
                _ => explanation.Summary
            };
            // An authorization denial alone is NOT proof of local switch position.
            return new(Safe(title, 70),
                Safe($"IED AddCause: {cause}. {explanation.Checks}", 190),
                Iec61850ControlNoticeTone.Error);
        }

        // A rejected or negative-terminated command must not be softened just
        // because an earlier Select/Operate service received a positive response.
        if (result.CommandTerminationReceived && !result.PositiveTermination)
            return new("Command termination rejected",
                Safe($"IED negative termination. {explanation.Evidence}", 190),
                Iec61850ControlNoticeTone.Error);

        if (stage.Equals("Feedback timeout", StringComparison.OrdinalIgnoreCase) &&
            result.ServiceAccepted)
            return new("Feedback not confirmed",
                Safe($"Command service accepted, but expected process feedback did not arrive. Last: {feedback}", 190),
                Iec61850ControlNoticeTone.Warning);

        if (result.CompletionState.Equals("WireStateUnknown", StringComparison.OrdinalIgnoreCase))
            return new("Command outcome unknown",
                "Transport interrupted; verify the actual IED state before another command.",
                Iec61850ControlNoticeTone.Warning);

        var otherTitle = explanation.Confidence switch
        {
            "ConfirmedClient" => "Command not sent",
            "LocalValidation" => "Command not sent — validation failed",
            "MmsServiceOnly" => "IED rejected command — reason unspecified",
            "TerminationOnly" => "IED reported negative command termination",
            _ => "Command failed — reason unavailable"
        };
        var detail = explanation.Confidence == "MmsServiceOnly"
            ? "MMS access denied; exact cause not supplied. Check BCU Local/Remote, authorization and interlocks."
            : $"Evidence: {explanation.Evidence}. Check: {explanation.Checks}.";
        return new(Safe(otherTitle, 95), Safe(detail, 190),
            explanation.Confidence is "ConfirmedClient" or "LocalValidation"
                ? Iec61850ControlNoticeTone.Warning
                : Iec61850ControlNoticeTone.Error);
    }

    internal static Notice FromLocalRejection(string reason) => new(
        "Command not sent",
        Safe($"ARSAS blocked the request before dispatch: {reason}.", 175),
        Iec61850ControlNoticeTone.Warning);

    internal static Notice FromDemo(string feedback) => new(
        "Simulation updated",
        Safe($"Demo feedback: {feedback}. No command was sent to a physical IED.", 175),
        Iec61850ControlNoticeTone.Info);

    internal static Notice FromUnexpectedFailure() =>
        new("Command could not complete",
            "A local or communication error occurred. Exact evidence is recorded in Diagnostics.",
            Iec61850ControlNoticeTone.Error);

    private static string Safe(string? value, int max)
    {
        // Avoid multi-line and unbounded input from an untrusted IED.
        var flattened = new string((value ?? string.Empty).Select(ch =>
            char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();
        return flattened.Length <= max ? flattened : flattened[..max] + "…";
    }
}
