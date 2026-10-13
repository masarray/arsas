namespace ArIED61850Tester.Services;

/// <summary>
/// Read-only IEC 61850 event presentation. A report reason is derived only
/// from the decoder's actual reason-for-inclusion; neither process edges nor
/// BRCB use alone prove a spontaneous trigger or a buffered replay.
/// </summary>
public static class Iec61850EventOriginPolicy
{
    public static string Cause(string? reportReason, string? acquisition)
    {
        var reason = (reportReason ?? string.Empty).Trim();
        var tokens = reason.Split(
            new[] { ' ', ',', ';', '|', '/', '+', '(', ')', ':' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Includes(string longName, string shortName) =>
            tokens.Any(x => x.Equals(longName, StringComparison.OrdinalIgnoreCase) ||
                            x.Equals(shortName, StringComparison.OrdinalIgnoreCase));

        // Reason-for-inclusion may contain multiple flags. GI and integrity
        // must not be relabeled spontaneous simply because the value changed.
        if (Includes("general-interrogation", "gi")) return "General interrogation";
        if (Includes("integrity", "intg")) return "Integrity";
        if (Includes("data-change", "dchg")) return "Spontaneous";
        if (Includes("quality-change", "qchg")) return "Quality change";
        if (Includes("data-update", "dupd")) return "Data update";

        // Polling is an observation mechanism, not an IEC trigger reason.
        if ((acquisition ?? string.Empty).Contains("poll", StringComparison.OrdinalIgnoreCase))
            return "Observed change";
        return "State change (cause unknown)";
    }

    public static string Channel(string? acquisition)
    {
        var text = acquisition ?? string.Empty;
        // BRCB is buffer-capable; this does NOT prove that a particular
        // received report was a replay of an older stored entry.
        if (text.Contains("brcb", StringComparison.OrdinalIgnoreCase)) return "BRCB";
        if (text.Contains("urcb", StringComparison.OrdinalIgnoreCase)) return "URCB";
        if (text.Contains("poll", StringComparison.OrdinalIgnoreCase)) return "MMS read";
        if (text.Contains("demo", StringComparison.OrdinalIgnoreCase)) return "Simulation";
        return "Unspecified";
    }
}
