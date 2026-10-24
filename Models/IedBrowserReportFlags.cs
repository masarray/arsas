using System.Text.RegularExpressions;
using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Read-only IEC report-control flag projection. Delegates all bit numbering
/// and BER padding semantics to the protocol engine, never to GUI code.
/// </summary>
public static class IedBrowserReportFlags
{
    private static readonly Regex HexTrigger = new(@"^02[0-9a-fA-F]{2}$", RegexOptions.Compiled);
    private static readonly Regex HexOptional = new(@"^06[0-9a-fA-F]{4}$", RegexOptions.Compiled);
    private static readonly Regex BitsRendered = new(@"^bits\((?:0x)?[0-9a-fA-F]+\s*(?:,\s*unused=\d+)?\)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> TriggerTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "dchg", "data-change", "datachange", "qchg", "quality-change", "qualitychange",
        "dupd", "data-update", "dataupdate", "integrity", "intg", "period", "gi",
        "general-interrogation", "generalinterrogation"
    };
    private static readonly HashSet<string> OptionalTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "sequence-number", "sequencenumber", "seqnum", "report-timestamp", "reporttimestamp",
        "time-of-entry", "timeofentry", "timestamp", "reason-for-inclusion",
        "reasonforinclusion", "reason", "data-set", "data-set-name", "dataset",
        "data-reference", "datareference", "dataref", "buffer-overflow",
        "bufferoverflow", "bufovfl", "entryid", "entry-id", "conf-revision",
        "confrevision", "confrev", "configref", "segmentation"
    };

    public static IReadOnlyList<IedBrowserFlagOption> TriggerOptions(string? text)
    {
        var valid = HasDecodableEvidence(text, true);
        var f = valid ? MmsReportControlFieldCodec.DecodeTriggerOptions(text) : null;
        return
        [
            new("Data change", f?.DataChange),
            new("Quality change", f?.QualityChange),
            new("Data update", f?.DataUpdate),
            new("Integrity", f?.Integrity),
            new("General interrogation (GI)", f?.GeneralInterrogation)
        ];
    }

    public static IReadOnlyList<IedBrowserFlagOption> OptionalFields(string? text)
    {
        var valid = HasDecodableEvidence(text, false);
        var f = valid ? MmsReportControlFieldCodec.DecodeOptionalFields(text) : null;
        return
        [
            new("Sequence number", f?.SequenceNumber),
            new("Report timestamp", f?.ReportTimestamp),
            new("Reason for inclusion", f?.ReasonForInclusion),
            new("DataSet name", f?.DataSetName),
            new("Data reference", f?.DataReference),
            new("Buffer overflow", f?.BufferOverflow),
            new("Entry ID", f?.EntryId),
            new("Configuration revision", f?.ConfigurationRevision),
            new("Segmentation", f?.Segmentation)
        ];
    }

    private static bool HasDecodableEvidence(string? text, bool trigger)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.Trim();
        if ((trigger ? HexTrigger : HexOptional).IsMatch(value)) return true;
        if (BitsRendered.IsMatch(value)) return true;
        var names = value.Split(new[] { ',', ';', '|', '+', ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tokens = trigger ? TriggerTokens : OptionalTokens;
        return names.Length > 0 && names.All(token =>
            tokens.Contains(token.Trim('[', ']', '(', ')')));
    }
}
