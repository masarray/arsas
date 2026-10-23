using System;
using System.Collections.Generic;
using System.Linq;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public static class NativeReportDiscoveryMapper
{
    private static readonly string[] ReportAttributeNames =
    {
        "RptID", "RptEna", "Resv", "ResvTms", "DatSet", "ConfRev", "OptFlds", "BufTm", "SqNum", "TrgOps", "IntgPd", "GI", "PurgeBuf", "EntryID", "TimeOfEntry"
    };

    public static NativeReportInventory BuildInventory(NativeMmsDiscoverySnapshot snapshot)
    {
        var inventory = new NativeReportInventory();
        inventory.DataSets.AddRange(BuildDataSets(snapshot.DomainVariableLists));
        inventory.ReportControls.AddRange(BuildReportControls(snapshot.DomainVariables, inventory.DataSets));
        return inventory;
    }

    public static void ApplyReportHints(IReadOnlyList<SignalDefinition> signals, NativeReportInventory inventory)
    {
        if (signals.Count == 0 || inventory.ReportControls.Count == 0)
            return;

        foreach (var signal in signals)
        {
            if (!IsReportCandidateSignal(signal))
            {
                if (!signal.CanPublishAsSignal)
                {
                    signal.ReportCoverage = "Hidden attribute";
                    signal.ReportCoverageReason = "q/t/Health/Beh/Mod/report-control attributes are sidecar or diagnostic attributes, not SCADA publish points.";
                }
                continue;
            }

            var best = PickBestReportControl(signal, inventory);
            if (best == null)
            {
                signal.IsReportCapable = false;
                signal.ReportCoverage = "Polling fallback";
                signal.ReportCoverageReason = "No RCB/DataSet candidate was found for this signal. Runtime will use safe MMS polling.";
                continue;
            }

            var score = ScoreReportControlForSignal(signal, best, ShouldPreferBuffered(signal, signal.Category ?? string.Empty, signal.ObjectReference.Replace('$', '.').ToLowerInvariant()));
            var exactLn = IsExactLogicalNodeDataSetMatch(signal, best);
            var sameClass = IsSameLogicalNodeClassMatch(signal, best);

            signal.ReportControlReference = best.Reference;
            signal.DataSetReference = best.DataSetReference;
            signal.IsReportCapable = true;
            signal.ReportCoverage = exactLn
                ? "Report covered + polling fallback"
                : sameClass
                    ? "Report candidate / verify DataSet"
                    : "Polling fallback";
            signal.ReportCoverageReason = exactLn
                ? "The signal logical node matches the discovered DataSet/RCB lane. Runtime will enable this RCB and keep MMS polling as fallback."
                : sameClass
                    ? "The signal matches the discovered DataSet/RCB logical-node class, but static DataSet membership must be confirmed from the IED directory at runtime. If the static DataSet does not contain this signal, ArIED 61850 will automatically use polling fallback."
                    : "The signal is readable, but this RCB/DataSet is only a weak hint. Runtime will not rely on reporting for this point unless the DataSet directory proves coverage.";

            if (!sameClass && !exactLn)
            {
                signal.ReportControlReference = string.Empty;
                signal.DataSetReference = string.Empty;
                signal.IsReportCapable = false;
            }

            signal.Source = string.IsNullOrWhiteSpace(signal.Source)
                ? "ARIEC61850 smart report planner"
                : signal.Source.Contains("RCB", StringComparison.OrdinalIgnoreCase) || signal.Source.Contains("report", StringComparison.OrdinalIgnoreCase)
                    ? signal.Source
                    : signal.Source + " + smart report planner";
        }
    }

    private static IEnumerable<NativeDataSetCandidate> BuildDataSets(IReadOnlyDictionary<string, IReadOnlyList<string>> domainVariableLists)
    {
        foreach (var domainPair in domainVariableLists.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var domain = domainPair.Key?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(domain)) continue;

            foreach (var raw in domainPair.Value.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var (ln, name, reference) = NormalizeDataSetReference(domain, raw);
                yield return new NativeDataSetCandidate
                {
                    Domain = domain,
                    LogicalNode = ln,
                    Name = name,
                    RawMmsName = raw,
                    Reference = reference
                };
            }
        }
    }

    private static IEnumerable<NativeReportControlCandidate> BuildReportControls(
        IReadOnlyDictionary<string, IReadOnlyList<string>> domainVariables,
        IReadOnlyList<NativeDataSetCandidate> dataSets)
    {
        var map = new Dictionary<string, NativeReportControlCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var domainPair in domainVariables.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var domain = domainPair.Key?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(domain)) continue;

            foreach (var raw in domainPair.Value.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!TryParseReportVariable(domain, raw, out var parsed))
                    continue;

                if (!map.TryGetValue(parsed.Reference, out var candidate))
                {
                    candidate = parsed;
                    candidate.DataSetReference = InferLikelyDataSet(candidate, dataSets);
                    map[candidate.Reference] = candidate;
                }

                foreach (var attr in parsed.Attributes)
                {
                    if (!candidate.Attributes.Contains(attr, StringComparer.OrdinalIgnoreCase))
                        candidate.Attributes.Add(attr);
                }
            }
        }

        return map.Values
            .OrderByDescending(x => x.Buffered)
            .ThenBy(x => x.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryParseReportVariable(string domain, string raw, out NativeReportControlCandidate candidate)
    {
        candidate = new NativeReportControlCandidate();
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var parts = raw.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3) return false;

        var fcIndex = Array.FindIndex(parts, p => p.Equals("RP", StringComparison.OrdinalIgnoreCase) || p.Equals("BR", StringComparison.OrdinalIgnoreCase));
        if (fcIndex < 1 || fcIndex + 1 >= parts.Length) return false;

        var ln = parts[0];
        var fc = parts[fcIndex].ToUpperInvariant();
        var name = parts[fcIndex + 1];
        if (string.IsNullOrWhiteSpace(ln) || string.IsNullOrWhiteSpace(name)) return false;

        var attrs = parts.Skip(fcIndex + 2)
            .Where(p => IsKnownReportAttribute(p) || !string.IsNullOrWhiteSpace(p))
            .ToList();

        candidate = new NativeReportControlCandidate
        {
            Domain = domain,
            LogicalNode = ln,
            FunctionalConstraint = fc,
            Name = name,
            Buffered = fc.Equals("BR", StringComparison.OrdinalIgnoreCase),
            Reference = $"{domain}/{ln}.{fc}.{name}",
            Attributes = attrs
        };
        return true;
    }

    private static bool IsKnownReportAttribute(string text)
        => ReportAttributeNames.Contains(text, StringComparer.OrdinalIgnoreCase);

    private static string InferLikelyDataSet(NativeReportControlCandidate rcb, IReadOnlyList<NativeDataSetCandidate> dataSets)
    {
        var sameDomain = dataSets
            .Where(ds => ds.Domain.Equals(rcb.Domain, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sameDomain.Count == 0) return string.Empty;

        var sameLn = sameDomain
            .Where(ds => ds.LogicalNode.Equals(rcb.LogicalNode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // IEC 61850 reports are commonly hosted in LLN0. If only one DataSet is visible in the
        // same domain/LN, it is a safe planner hint. If there are many, keep the RCB reference
        // without pretending that we know DatSet until the RCB attribute probe confirms it.
        if (sameLn.Count == 1) return sameLn[0].Reference;
        if (sameDomain.Count == 1) return sameDomain[0].Reference;

        var byName = sameDomain.FirstOrDefault(ds =>
            !string.IsNullOrWhiteSpace(ds.Name) &&
            (rcb.Name.Contains(ds.Name, StringComparison.OrdinalIgnoreCase) ||
             ds.Name.Contains(rcb.Name, StringComparison.OrdinalIgnoreCase)));
        return byName?.Reference ?? string.Empty;
    }

    private static NativeReportControlCandidate? PickBestReportControl(SignalDefinition signal, NativeReportInventory inventory)
    {
        var domain = ExtractDomain(signal.ObjectReference);
        if (string.IsNullOrWhiteSpace(domain)) return null;

        var candidates = inventory.ReportControls
            .Where(rcb => rcb.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) return null;

        var category = signal.Category ?? string.Empty;
        var reference = signal.ObjectReference.Replace('$', '.').ToLowerInvariant();
        var preferBuffered = ShouldPreferBuffered(signal, category, reference);

        // Important for automation IED models:
        // GGIO and MMXU can be exposed as different native DataSets and different RCBs
        // inside the same logical device.  The old planner selected the first suitable
        // RCB in the domain and accidentally routed MMXU measurements through the GGIO
        // DataSet/RCB.  Score by DataSet LN and RCB LN first, then use buffered/URCB
        // preference only as a tie-breaker.
        return candidates
            .Select(rcb => new { Rcb = rcb, Score = ScoreReportControlForSignal(signal, rcb, preferBuffered) })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => preferBuffered ? x.Rcb.Buffered : !x.Rcb.Buffered)
            .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.Rcb.DataSetReference))
            .ThenByDescending(x => HasUsefulRuntimeAttributes(x.Rcb))
            .ThenBy(x => x.Rcb.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Rcb.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.Rcb;
    }

    private static bool IsExactLogicalNodeDataSetMatch(SignalDefinition signal, NativeReportControlCandidate rcb)
    {
        var signalLn = signal.LogicalNode ?? string.Empty;
        if (string.IsNullOrWhiteSpace(signalLn)) return false;
        var dataSetLn = ExtractLogicalNodeFromDataSetReference(rcb.DataSetReference);
        if (!string.IsNullOrWhiteSpace(dataSetLn) && dataSetLn.Equals(signalLn, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(rcb.LogicalNode) && rcb.LogicalNode.Equals(signalLn, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(rcb.DataSetReference) && rcb.DataSetReference.Contains($"/{signalLn}.", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(rcb.DataSetReference) && rcb.DataSetReference.Contains(signalLn, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool IsSameLogicalNodeClassMatch(SignalDefinition signal, NativeReportControlCandidate rcb)
    {
        var signalClass = signal.LogicalNodeClass ?? string.Empty;
        if (string.IsNullOrWhiteSpace(signalClass)) return false;
        var dataSetLn = ExtractLogicalNodeFromDataSetReference(rcb.DataSetReference);
        if (MatchesLogicalNodeClass(dataSetLn, signalClass)) return true;
        if (MatchesLogicalNodeClass(rcb.LogicalNode, signalClass)) return true;
        return false;
    }

    private static bool ShouldPreferBuffered(SignalDefinition signal, string category, string reference)
    {
        if (category.Equals("Position", StringComparison.OrdinalIgnoreCase)) return true;
        if (category.Equals("Protection", StringComparison.OrdinalIgnoreCase)) return true;
        if (reference.Contains("xcbr") || reference.Contains("xswi") || reference.Contains("cswi")) return true;
        if (reference.EndsWith(".pos.stval") || reference.EndsWith(".op.general") || reference.EndsWith(".str.general") || reference.EndsWith(".tr.general")) return true;
        return false;
    }

    private static int ScoreReportControlForSignal(SignalDefinition signal, NativeReportControlCandidate rcb, bool preferBuffered)
    {
        var score = 0;
        var signalLn = signal.LogicalNode ?? string.Empty;
        var signalLnClass = signal.LogicalNodeClass ?? string.Empty;
        var signalReference = (signal.ObjectReference ?? string.Empty).Replace('$', '.');

        var dataSetLn = ExtractLogicalNodeFromDataSetReference(rcb.DataSetReference);
        if (!string.IsNullOrWhiteSpace(dataSetLn))
            score += ScoreLogicalNodeAffinity(signalLn, signalLnClass, dataSetLn, signalReference, strongPenalty: true);

        if (!string.IsNullOrWhiteSpace(rcb.LogicalNode))
            score += ScoreLogicalNodeAffinity(signalLn, signalLnClass, rcb.LogicalNode, signalReference, strongPenalty: false);

        if (!string.IsNullOrWhiteSpace(signalLn) &&
            !string.IsNullOrWhiteSpace(rcb.DataSetReference) &&
            rcb.DataSetReference.Contains(signalLn, StringComparison.OrdinalIgnoreCase))
            score += 20;

        if (preferBuffered == rcb.Buffered)
            score += 8;
        if (HasUsefulRuntimeAttributes(rcb))
            score += 6;
        if (!string.IsNullOrWhiteSpace(rcb.DataSetReference))
            score += 4;

        var fc = signal.FunctionalConstraint ?? string.Empty;
        if (fc.Equals("MX", StringComparison.OrdinalIgnoreCase) &&
            (signalLnClass.Equals("MMXU", StringComparison.OrdinalIgnoreCase) || signalLnClass.Equals("MMXN", StringComparison.OrdinalIgnoreCase)))
        {
            if (MatchesLogicalNodeClass(dataSetLn, "MMXU") || MatchesLogicalNodeClass(dataSetLn, "MMXN") ||
                MatchesLogicalNodeClass(rcb.LogicalNode, "MMXU") || MatchesLogicalNodeClass(rcb.LogicalNode, "MMXN"))
                score += 18;
        }

        return score;
    }

    private static int ScoreLogicalNodeAffinity(string signalLn, string signalLnClass, string candidateLn, string signalReference, bool strongPenalty)
    {
        if (string.IsNullOrWhiteSpace(candidateLn)) return 0;
        if (candidateLn.Equals("LLN0", StringComparison.OrdinalIgnoreCase)) return 2;

        var candidateClass = SignalDefinition.DetectLogicalNodeClass(candidateLn);
        if (!string.IsNullOrWhiteSpace(signalLn) && candidateLn.Equals(signalLn, StringComparison.OrdinalIgnoreCase))
            return 80;
        if (!string.IsNullOrWhiteSpace(signalLn) && signalReference.Contains($"/{candidateLn}.", StringComparison.OrdinalIgnoreCase))
            return 70;
        if (!string.IsNullOrWhiteSpace(signalLnClass) && candidateClass.Equals(signalLnClass, StringComparison.OrdinalIgnoreCase))
            return 45;

        // Separate native DataSets must not bleed into each other.  A GGIO DataSet should not
        // own MMXU measurements just because both are in the same MMS domain.
        return strongPenalty ? -60 : -25;
    }

    private static bool MatchesLogicalNodeClass(string logicalNode, string logicalNodeClass)
    {
        if (string.IsNullOrWhiteSpace(logicalNode) || string.IsNullOrWhiteSpace(logicalNodeClass)) return false;
        return SignalDefinition.DetectLogicalNodeClass(logicalNode).Equals(logicalNodeClass, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractLogicalNodeFromDataSetReference(string reference)
    {
        var text = (reference ?? string.Empty).Trim().Replace('$', '.');
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var slash = text.IndexOf('/');
        var item = slash >= 0 && slash < text.Length - 1 ? text[(slash + 1)..] : text;
        var dot = item.IndexOf('.');
        return dot > 0 ? item[..dot] : string.Empty;
    }

    private static bool HasUsefulRuntimeAttributes(NativeReportControlCandidate rcb)
        => rcb.Attributes.Contains("RptEna", StringComparer.OrdinalIgnoreCase) ||
           rcb.Attributes.Contains("DatSet", StringComparer.OrdinalIgnoreCase) ||
           rcb.Attributes.Contains("ConfRev", StringComparer.OrdinalIgnoreCase);

    private static bool IsReportCandidateSignal(SignalDefinition signal)
    {
        if (!signal.IsScadaCoreSignal || !signal.CanPublishAsSignal) return false;
        var fc = signal.FunctionalConstraint ?? string.Empty;
        return fc.Equals("ST", StringComparison.OrdinalIgnoreCase) || fc.Equals("MX", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractDomain(string reference)
    {
        var slash = reference.IndexOf('/');
        return slash > 0 ? reference[..slash] : string.Empty;
    }

    private static (string LogicalNode, string Name, string Reference) NormalizeDataSetReference(string domain, string raw)
    {
        var cleaned = raw.Trim().Replace('$', '.');
        if (string.IsNullOrWhiteSpace(cleaned))
            return ("LLN0", raw, $"{domain}/LLN0.{raw}");

        var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
            return (parts[0], parts[^1], $"{domain}/{cleaned}");

        // Many IEDs expose domain-specific NamedVariableList as just the DataSet name.
        // In IEC 61850, operational DataSets used by reports are commonly hosted under LLN0.
        return ("LLN0", cleaned, $"{domain}/LLN0.{cleaned}");
    }
}
