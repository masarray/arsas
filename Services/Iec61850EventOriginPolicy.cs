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

    // UI Acquisition means the actual RCB that routed this report, NOT
    // IEC ReportId (RptID). RptID can legitimately be "Rpt_0" while
    // the configured BRCB object is named "brcb01".
    public static string Acquisition(
        string? reportId, string? reportControlReference, string? sourceMode)
    {
        if (IsPolling(sourceMode)) return "MMS Polling";
        var rcbName = RcbName(reportControlReference);
        if (rcbName.Length > 0) return rcbName;

        // Preserve missing provenance; do not invent an RCB from RptID.
        return Channel(sourceMode) switch
        {
            "BRCB" => "BRCB",
            "URCB" => "URCB",
            "MMS read" => "MMS Polling",
            "Simulation" => "Demo",
            _ => "Unknown"
        };
    }

    public static string PointAcquisition(
        string? lastLiveRcbReference, string? sourceMode, bool offline)
    {
        if (offline) return "Offline";
        if (IsPolling(sourceMode)) return "MMS Polling";
        var rcbName = RcbName(lastLiveRcbReference);
        if (rcbName.Length > 0) return rcbName;
        if (sourceMode?.Contains("report", StringComparison.OrdinalIgnoreCase) == true ||
            Channel(sourceMode) is "BRCB" or "URCB")
            return "RCB pending";
        return Channel(sourceMode) == "Simulation" ? "Demo" : "Unknown";
    }

    // Both MMS ('$') and normalized SCL ('.') references are accepted.
    // The full reference is retained separately as unabridged evidence.
    public static string RcbName(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return string.Empty;
        var parts = reference.Trim().Split(
            new[] { '/', '$', '.' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? string.Empty : parts[^1];
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
