using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Evidence-graded, bounded, human-readable operator notification.
/// ARIEC61850 remains the sole authority for MMS/AddCause and for control
/// acceptance. No control write or status inference is performed here.
/// </summary>
internal static class Iec61850ControlShout
{
    internal sealed record Notice(string Title, string Detail);

    internal static Notice FromResult(Iec61850ControlCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var explanation = Iec61850ControlFailureReason.Explain(result);
        if (result.IsSuccess)
            return new("Command succeeded", "The IED accepted the command.");

        var title = explanation.Confidence switch
        {
            "ConfirmedClient" => "Command not sent",
            "IEDExplicitAddCause" => explanation.Summary,
            "MmsServiceOnly" => "IED rejected command — reason unspecified",
            "TerminationOnly" => "IED reported negative command termination",
            _ => "Command failed — reason unavailable"
        };

        // The context is guidance, NOT a confirmed diagnosis for generic MMS
        // object-access-denied. A local BCU position must be substantiated by
        // distinct IED status/LastApplError evidence, never guessed here.
        var detail = explanation.Confidence == "MmsServiceOnly"
            ? "MMS access denied; exact cause not supplied. Check BCU Local/Remote, authorization and interlocks."
            : $"Evidence: {explanation.Evidence}. Check: {explanation.Checks}.";
        return new(Safe(title, 95), Safe(detail, 250));
    }

    internal static Notice FromUnexpectedFailure() =>
        new("Command could not complete",
            "A local or communication error occurred. The exact exception is recorded in Diagnostics.");

    private static string Safe(string? value, int max)
    {
        // No CR/LF or control-character injection into the command overlay.
        var flattened = new string((value ?? string.Empty).Select(ch =>
            char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();
        return flattened.Length <= max ? flattened : flattened[..max] + "…";
    }
}
