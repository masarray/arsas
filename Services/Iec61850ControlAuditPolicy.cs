using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Exact wire/feedback classification for the unified operator Event Log.
/// A successful MMS service is not a positive breaker movement. No generic
/// denial is treated as proof of Interlock, Local/Remote or authority.
/// </summary>
public static class Iec61850ControlAuditPolicy
{
    public static string Outcome(Iec61850ControlCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.CompletionState.Equals("NotSent", StringComparison.OrdinalIgnoreCase))
            return "Control not sent";
        if (result.WireSteps.Any(step => !step.RequestAccepted) ||
            result.CommandTerminationReceived && !result.PositiveTermination)
            return "Control rejected";
        if (result.FeedbackConfirmed && result.CommandTerminationReceived &&
            result.PositiveTermination && result.IsSuccess)
            return "Control confirmed";
        if (result.ServiceAccepted && result.IsSuccess)
            return "Control accepted"; // physical response not independently proven
        if (result.WireSteps.Count > 0 || result.ServiceAccepted ||
            result.RequestHex.Length > 0)
            return "Control unverified";
        return "Control failed"; // no claim of a remotely received rejection
    }

    public static string Evidence(Iec61850ControlCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var steps = result.WireSteps.Count == 0 ? "no ordered MMS step captured"
            : string.Join(" → ", result.WireSteps.Select(step =>
                step.Action + ":" + (step.RequestAccepted ? "accepted" : "rejected")));
        var termination = result.CommandTerminationReceived
            ? result.PositiveTermination ? "positive" : "negative" : "not evidenced";
        var feedback = result.FeedbackConfirmed
            ? "confirmed " + result.FeedbackValue : "not confirmed";
        var classified = result.IsSuccess ? string.Empty
            : Iec61850ControlFailureReason.Explain(result) is { } failure
                ? $"; reason={failure.Summary}; confidence={failure.Confidence}; " +
                  $"reasonEvidence={failure.Evidence}; operatorChecks={failure.Checks}"
                : string.Empty;
        return $"MMS={steps}; termination={termination}; feedback={feedback}; " +
               $"completion={result.CompletionState}; controlError={result.ControlError}; " +
               $"addCause={result.AddCause}; stage={result.Stage}; detail={result.Message}" +
               classified;
    }
}
