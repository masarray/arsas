using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Classifies engine-decoded IEC 61850 AddCause separately from a generic
/// MMS access-denied error. Local/Remote is never inferred from MMS alone.
/// </summary>
public static class Iec61850ControlFailureReason
{
    public sealed record Explanation(string Summary, string Confidence, string Evidence, string Checks);

    public static Explanation Explain(Iec61850ControlCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess) return new("Command accepted", "NotApplicable", "-", "-");
        if (result.CompletionState.Equals("NotSent", StringComparison.OrdinalIgnoreCase))
            return new("Command was not sent to the IED", "ConfirmedClient",
                "Client rejected before MMS Operate", "Review local command validation");

        var addCause = (result.AddCause ?? "").Trim().ToLowerInvariant();
        var explicitReason = addCause switch
        {
            "blocked-by-interlocking" => "IED blocked the command by interlocking",
            "blocked-by-synchrocheck" => "IED blocked the command by synchrocheck",
            "blocked-by-mode" => "IED blocked the command by its active control mode",
            "blocked-by-switching-hierarchy" => "IED blocked the command by switching hierarchy",
            "blocked-by-process" => "IED blocked the command by process conditions",
            "blocked-by-health" => "IED blocked the command by equipment health conditions",
            "no-access-authority" => "IED reports insufficient control access authority",
            "object-not-selected" => "IED reports the object was not selected",
            "object-already-selected" => "IED reports the object is already selected",
            "locked-by-other-client" => "IED reports this object is locked by another client",
            "command-already-in-execution" => "IED reports another command is in execution",
            "inconsistent-parameters" => "IED rejected inconsistent control parameters",
            "not-supported" => "IED does not support the requested control operation",
            _ => ""
        };
        if (explicitReason.Length > 0)
            return new(explicitReason, "IEDExplicitAddCause", "LastApplError AddCause=" + addCause,
                addCause is "blocked-by-mode" or "no-access-authority" or "blocked-by-switching-hierarchy"
                    ? "Check Local/Remote mode, origin/role authorization and control-mode policy"
                    : "Inspect IED event log and the reported IEC 61850 AddCause");

        var rejected = result.WireSteps.FirstOrDefault(s => !s.RequestAccepted);
        if (((result.Message ?? "") + " " + (rejected?.Detail ?? ""))
            .Contains("object-access-denied", StringComparison.OrdinalIgnoreCase))
            return new("IED rejected MMS access; specific cause not provided",
                "MmsServiceOnly", "MMS object-access-denied; no explicit AddCause",
                "Check BCU Local/Remote, authorization, control mode and interlock. None is proven by this response");

        if (result.CommandTerminationReceived && !result.PositiveTermination)
            return new("IED returned negative CommandTermination without a specific cause",
                "TerminationOnly", "Negative termination, AddCause absent", "Check the IED event log");

        return new("Command failed; exact IED reason unavailable",
            "Unclassified", string.IsNullOrWhiteSpace(result.ControlError)
                ? "No decoded failure-specific AddCause" : "ControlError=" + result.ControlError,
            "Review wire diagnostic and IED event log");
    }
}
