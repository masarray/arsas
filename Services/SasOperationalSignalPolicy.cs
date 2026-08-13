using System.Text.RegularExpressions;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Operator-facing IEC 61850 point policy. The full typed live/SCL model remains attached
/// to the IED for engineering, diagnostics, comparison, and SCL export. This policy only
/// decides which exact value leaves belong in Signal Selection and Live Monitor by default.
/// </summary>
public static class SasOperationalSignalPolicy
{
    private static readonly Regex GgioIndication = new(@"\.ind\d+\.stval$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GgioAnalog = new(@"\.anin\d+\.(?:mag\.)?f$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MeterEnergy = new(@"\.(?:totwh|totvarh|supwh|dmdwh|rcvwh)\.(?:actval|stval)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FundamentalPower = new(@"\.(?:w|var|va|pf)\.(?:phsa|phsb|phsc|net|tot)\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FundamentalScalarPower = new(@"\.(?:totw|totvar|totva|totpf|hz)\.(?:instmag|mag)\.f$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StatisticOrHarmonic = new(@"(^|[./])(?:har|harm|min|max|mean|avg|average|dmd|dmmd)\d*(?:mmxu|mmxn)|\.(?:mean|min|max|avg|average|dmd|har|harm|thd|tdd)(?:[.$/]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GgioControl = new(@"\.(?:spcso|dpcso|inc|iscso|apcso|bscso)\d*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> ProtectionClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "PTOC", "PTRC", "PDIF", "PDIS", "PIOC", "PTOV", "PTUV", "PTEF", "PDEF", "PSCH", "RREC", "RBRF"
    };

    public static bool IsVisible(SignalDefinition? signal)
    {
        if (signal is null || string.IsNullOrWhiteSpace(signal.ObjectReference))
            return false;

        return signal.IsControlSignal ? IsOperationalControl(signal) : IsOperationalValue(signal);
    }

    public static bool IsOperationalValue(SignalDefinition signal)
    {
        var fc = Normalize(signal.FunctionalConstraint).ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(fc) && fc is not ("ST" or "MX"))
            return false;

        var reference = NormalizeReference(signal.ObjectReference);
        var lnClass = ResolveLnClass(signal.LogicalNodeClass, signal.LogicalNode, reference);
        var dataType = Normalize(signal.DataType);

        if (IsKnownFailure(signal) || IsEngineeringAttribute(reference, dataType) || !IsExactValueLeaf(reference))
            return false;

        if (IsUnprovenSyntheticCandidate(signal))
            return false;

        if (lnClass is "CSWI" or "XCBR" or "XSWI")
            return reference.EndsWith(".pos.stval", StringComparison.OrdinalIgnoreCase);

        if (lnClass == "CILO")
            return reference.EndsWith(".enaopn.stval", StringComparison.OrdinalIgnoreCase) ||
                   reference.EndsWith(".enacls.stval", StringComparison.OrdinalIgnoreCase);

        if (ProtectionClasses.Contains(lnClass))
            return IsOperationalProtectionLeaf(reference, lnClass);

        if (lnClass is "MMXU" or "MMXN")
            return IsFundamentalMeasurement(reference);

        if (lnClass == "MMTR")
            return MeterEnergy.IsMatch(reference);

        if (lnClass == "GGIO")
            return GgioIndication.IsMatch(reference) || GgioAnalog.IsMatch(reference);

        if (lnClass == "YPTR")
            return reference.EndsWith(".tappos.posval", StringComparison.OrdinalIgnoreCase) ||
                   reference.EndsWith(".tapchg.valwtr.posval", StringComparison.OrdinalIgnoreCase) ||
                   reference.EndsWith(".tapchg.stval", StringComparison.OrdinalIgnoreCase);

        if (lnClass is "ATCC" or "AVC" or "AVCO")
            return IsAvrOperationalValue(reference, dataType, signal.Category);

        return false;
    }

    public static bool IsOperationalControl(SignalDefinition signal)
    {
        if (!signal.IsControlSignal || string.IsNullOrWhiteSpace(signal.ObjectReference))
            return false;

        var reference = NormalizeReference(signal.ObjectReference).TrimEnd('.');
        var leaf = reference[(reference.LastIndexOf('.') + 1)..];
        if (leaf is "ctlmodel" or "ctlval" or "ctlnum" or "stseld" or "sbo" or "sbow" or "oper" or "cancel" or "origin" or "check" or "test")
            return false;

        var lnClass = ResolveLnClass(signal.LogicalNodeClass, signal.LogicalNode, reference);
        var dataObject = ExtractDataObject(reference);
        if (dataObject is "mod" or "beh" or "health" or "eehealth" or "blk" or "reset")
            return false;

        if (lnClass is "CSWI" or "XCBR" or "XSWI")
            return dataObject == "pos";

        if (lnClass is "ATCC" or "AVC" or "AVCO")
            return dataObject.Contains("raise", StringComparison.OrdinalIgnoreCase) ||
                   dataObject.Contains("lower", StringComparison.OrdinalIgnoreCase) ||
                   dataObject.Contains("tap", StringComparison.OrdinalIgnoreCase) ||
                   dataObject.Contains("op", StringComparison.OrdinalIgnoreCase);

        return lnClass == "GGIO" && GgioControl.IsMatch(reference);
    }

    private static bool IsOperationalProtectionLeaf(string reference, string lnClass)
    {
        // Only the standard aggregate indications used by station/bay SCADA are shown by
        // default. Phase/neutral detail (for example Op.phsA.general or O.phsA.stVal) remains
        // in the engineering model but is not promoted to the operator point list.
        if (reference.EndsWith(".op.general", StringComparison.OrdinalIgnoreCase) ||
            reference.EndsWith(".str.general", StringComparison.OrdinalIgnoreCase))
            return true;

        if (lnClass == "PTRC" && reference.EndsWith(".tr.general", StringComparison.OrdinalIgnoreCase))
            return true;
        if (lnClass == "RBRF" && reference.EndsWith(".opex.general", StringComparison.OrdinalIgnoreCase))
            return true;
        if (lnClass == "RREC" && reference.EndsWith(".autorecst.stval", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsFundamentalMeasurement(string reference)
    {
        if (StatisticOrHarmonic.IsMatch(reference))
            return false;

        if (FundamentalScalarPower.IsMatch(reference))
            return true;

        var hasMagnitude = reference.Contains(".cval.mag.f", StringComparison.OrdinalIgnoreCase) ||
                           reference.Contains(".instcval.mag.f", StringComparison.OrdinalIgnoreCase);
        if (!hasMagnitude)
            return false;

        return reference.Contains(".a.phsa.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".a.phsb.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".a.phsc.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".a.neut.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".a.net.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".phv.phsa.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".phv.phsb.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".phv.phsc.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".ppv.phsab.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".ppv.phsbc.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".ppv.phsca.", StringComparison.OrdinalIgnoreCase) ||
               FundamentalPower.IsMatch(reference);
    }

    private static bool IsAvrOperationalValue(string reference, string dataType, string category)
    {
        if (reference.Contains(".oper.", StringComparison.OrdinalIgnoreCase) || reference.EndsWith(".oper", StringComparison.OrdinalIgnoreCase))
            return false;

        if (category.Equals("Measurement", StringComparison.OrdinalIgnoreCase) &&
            dataType.Contains("float", StringComparison.OrdinalIgnoreCase))
        {
            return Regex.IsMatch(reference, @"\.(?:ctlv|loda|circa|phang|ctldv)\.(?:mag\.)?f$", RegexOptions.IgnoreCase);
        }

        return reference.Contains(".tapchg.valwtr.posval", StringComparison.OrdinalIgnoreCase) ||
               reference.EndsWith(".tapchg.stval", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".loc.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".ltcblk", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".auto.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".parop.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExactValueLeaf(string reference)
        => reference.EndsWith(".stval", StringComparison.OrdinalIgnoreCase) ||
           reference.EndsWith(".general", StringComparison.OrdinalIgnoreCase) ||
           reference.EndsWith(".posval", StringComparison.OrdinalIgnoreCase) ||
           reference.EndsWith(".actval", StringComparison.OrdinalIgnoreCase) ||
           reference.EndsWith(".mag.f", StringComparison.OrdinalIgnoreCase) ||
           reference.EndsWith(".f", StringComparison.OrdinalIgnoreCase) ||
           reference.EndsWith(".i", StringComparison.OrdinalIgnoreCase);

    private static bool IsEngineeringAttribute(string reference, string dataType)
    {
        if (dataType.Equals("Quality", StringComparison.OrdinalIgnoreCase) ||
            dataType.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) ||
            dataType.Equals("Directory", StringComparison.OrdinalIgnoreCase))
            return true;

        return reference.EndsWith(".q", StringComparison.OrdinalIgnoreCase) ||
               reference.EndsWith(".t", StringComparison.OrdinalIgnoreCase) ||
               reference.EndsWith(".tm", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".rp.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".br.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".ctlmodel", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".ctlval", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".origin", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".namplt.", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".configrev", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".vendor", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".swrev", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".mod.", StringComparison.OrdinalIgnoreCase) ||
               reference.EndsWith(".mod.stval", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".beh.", StringComparison.OrdinalIgnoreCase) ||
               reference.EndsWith(".beh.stval", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains(".health.", StringComparison.OrdinalIgnoreCase) ||
               reference.EndsWith(".health.stval", StringComparison.OrdinalIgnoreCase) ||
               StatisticOrHarmonic.IsMatch(reference);
    }

    private static bool IsUnprovenSyntheticCandidate(SignalDefinition signal)
    {
        var source = Normalize(signal.Source);
        var synthetic = source.Contains("fallback", StringComparison.OrdinalIgnoreCase) ||
                        source.Contains("inferred", StringComparison.OrdinalIgnoreCase) ||
                        source.Contains("profile candidate", StringComparison.OrdinalIgnoreCase) ||
                        source.Contains("sibling proof-probe", StringComparison.OrdinalIgnoreCase);
        return synthetic && !Normalize(signal.ProbeStatus).Equals("Readable", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownFailure(SignalDefinition signal)
    {
        var probe = Normalize(signal.ProbeStatus);
        if (probe.Equals("Readable", StringComparison.OrdinalIgnoreCase) ||
            probe.Equals("Not probed", StringComparison.OrdinalIgnoreCase) ||
            probe.Equals("Reading...", StringComparison.OrdinalIgnoreCase))
            return false;

        return probe.Equals("Not readable", StringComparison.OrdinalIgnoreCase) ||
               probe.EndsWith("Exception", StringComparison.OrdinalIgnoreCase) ||
               (Normalize(signal.Quality).Equals("Bad", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(signal.Value) || signal.Value == "-"));
    }

    private static string ResolveLnClass(string explicitClass, string logicalNode, string reference)
    {
        if (!string.IsNullOrWhiteSpace(explicitClass))
            return explicitClass.ToUpperInvariant();

        var candidate = $"{logicalNode} {reference}".ToUpperInvariant();
        foreach (var cls in new[] { "CSWI", "XCBR", "XSWI", "CILO", "PTOC", "PTRC", "PDIF", "PDIS", "PIOC", "PTOV", "PTUV", "PTEF", "PDEF", "PSCH", "RREC", "RBRF", "MMXU", "MMXN", "MMTR", "GGIO", "YPTR", "ATCC", "AVC", "AVCO" })
        {
            if (candidate.Contains(cls, StringComparison.OrdinalIgnoreCase))
                return cls;
        }
        return string.Empty;
    }

    private static string ExtractDataObject(string reference)
    {
        var slash = reference.LastIndexOf('/');
        var afterLn = slash >= 0 ? reference[(slash + 1)..] : reference;
        var parts = afterLn.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? parts[1].ToLowerInvariant() : string.Empty;
    }

    private static string NormalizeReference(string value)
        => Normalize(value).Replace('$', '.').Replace("..", ".", StringComparison.Ordinal).ToLowerInvariant();

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
