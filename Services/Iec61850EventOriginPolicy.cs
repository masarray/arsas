namespace ArIED61850Tester.Services;

/// <summary>
/// Small operator-facing labels backed by actual IEC reporting provenance.
/// Original wire reason, ReportId and RCB reference remain in event records.
/// A BRCB source does not by itself prove buffered replay.
/// </summary>
public static class Iec61850EventOriginPolicy
{
    public static string Cause(string? reportReason, string? acquisition)
    {
        var tokens = (reportReason ?? string.Empty).Split(
            new[] { ' ', ',', ';', '|', '/', '+', '(', ')', ':' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Includes(string longName, string shortName) =>
            tokens.Any(x => x.Equals(longName, StringComparison.OrdinalIgnoreCase) ||
                            x.Equals(shortName, StringComparison.OrdinalIgnoreCase));

        // A GI or integrity snapshot is not a spontaneous event even when
        // another trigger flag accompanies it.
        if (Includes("general-interrogation", "gi")) return "GI";
        if (Includes("integrity", "intg")) return "Intg";
        if (Includes("data-change", "dchg")) return "Spont";
        if (Includes("quality-change", "qchg")) return "Qchg";
        if (Includes("data-update", "dupd")) return "DUpd";
        if (IsPolling(acquisition)) return "Poll";

        // SOE already requires an observed process edge. The actual IEC
        // reason was not supplied, so do NOT label it spontaneous.
        return "Chg";
    }

    public static string Acquisition(
        string? reportId, string? reportControlReference, string? sourceMode)
    {
        if (IsPolling(sourceMode)) return "MMS Polling";

        // Actual decoded header ID is preferred over a configured RCB hint.
        // Keep the ReportId literal (it need not be a path or an RCB name).
        if (!string.IsNullOrWhiteSpace(reportId))
            return reportId.Trim();

        if (!string.IsNullOrWhiteSpace(reportControlReference))
        {
            var parts = reportControlReference.Trim().Split(
                new[] { '/', '$', '.' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
                return parts[^1];
        }

        // These are TYPE labels, not fabricated report identifiers.
        return Channel(sourceMode) switch
        {
            "BRCB" => "BRCB",
            "URCB" => "URCB",
            "MMS read" => "MMS Polling",
            "Simulation" => "Demo",
            _ => "Unknown"
        };
    }

    public static string Channel(string? acquisition)
    {
        var text = acquisition ?? string.Empty;
        if (IsPolling(text)) return "MMS read";
        if (text.Contains("brcb", StringComparison.OrdinalIgnoreCase)) return "BRCB";
        if (text.Contains("urcb", StringComparison.OrdinalIgnoreCase)) return "URCB";
        if (text.Contains("demo", StringComparison.OrdinalIgnoreCase)) return "Simulation";
        return "Unspecified";
    }

    private static bool IsPolling(string? sourceMode)
    {
        var text = sourceMode ?? string.Empty;
        return text.Contains("poll", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("MMS fallback", StringComparison.OrdinalIgnoreCase);
    }
}
