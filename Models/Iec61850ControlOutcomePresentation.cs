namespace ArIED61850Tester.Models;

/// <summary>
/// UI-only interpretation of an actual IEC 61850 control-service result.
/// Never equate MMS acceptance with independent IED process feedback, or an
/// unknown transport outcome with an IED rejection.
/// </summary>
public sealed record Iec61850ControlOutcomeView(
    string Stage, string Summary, string Tone);

public static class Iec61850ControlOutcomePresentation
{
    /// <summary>
    /// Compact user-facing wire sequence derived only from actual reported steps.
    /// Does not imply that a physical breaker/contact position changed.
    /// </summary>
    public static string WireTimeline(Iec61850ControlCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var stages = result.WireSteps.Select(step =>
        {
            var action = step.Action.Equals("SelectWithValue", StringComparison.OrdinalIgnoreCase)
                ? "SBOw" : step.Action;
            return action + (step.RequestAccepted ? " ✓" : " rejected");
        }).ToList();
        if (result.CommandTerminationReceived)
            stages.Add(result.PositiveTermination ? "Termination ✓" : "Termination negative");
        return string.Join("  →  ", stages);
    }

    public static Iec61850ControlOutcomeView From(
        Iec61850ControlCommandResult result, bool testMode)
    {
        ArgumentNullException.ThrowIfNull(result);
        static Iec61850ControlOutcomeView View(string stage, string summary, string tone)
            => new(stage, summary, tone);

        if (result.CompletionState.Equals("NotSent", StringComparison.OrdinalIgnoreCase))
            return View("Not sent", "No MMS control request reached the dispatch path.", "Warning");

        if (result.CommandTerminationReceived && !result.PositiveTermination)
            return View("Negative termination",
                "The IED returned a negative CommandTermination. Check diagnostics.", "Error");

        if (result.WireSteps.Any(step => !step.RequestAccepted) ||
            result.CompletionState.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
            return View("IED rejected", "The IED rejected a control-service request. Check diagnostics.", "Error");

        if (result.CompletionState is "TimedOut" or "AssociationLost" or "Unknown" ||
            result.CompletionState.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            return View("Outcome uncertain",
                "The request may have reached the IED. Verify its state before retry.", "Warning");

        if (result.FeedbackConfirmed && result.ServiceAccepted && result.IsSuccess)
            return View("Feedback confirmed",
                "IED service accepted; independent process feedback verified.", "Success");

        if (result.ServiceAccepted)
        {
            if (testMode)
                return View("Test response accepted",
                    "Test=true was sent and accepted; this does not prove equipment state.", "Warning");
            return View("Feedback not verified",
                "IED accepted the control request; independent process feedback is not confirmed.", "Warning");
        }

        // Without affirmative service evidence, generic failures cannot claim
        // either that the IED rejected the control or that nothing was sent.
        return View("Outcome unresolved",
            "No definitive IED acceptance was observed. Verify before another attempt.", "Warning");
    }
}
