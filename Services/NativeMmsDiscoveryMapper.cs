using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed class NativeMmsDiscoverySnapshot
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DomainVariables { get; init; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DomainVariableLists { get; init; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}

public static class NativeMmsDiscoveryMapper
{
    private sealed record FallbackPoint(string DataObject, string FunctionalConstraint, string LeafPath, string DataType, string Category, string Unit = "");

    private static readonly string[] FunctionalConstraints =
    {
        "ST", "MX", "CO", "CF", "DC", "SP", "SG", "SE", "EX", "OR", "BL", "RP", "BR", "LG", "GO", "MS", "US", "SV"
    };

    private static readonly HashSet<string> ControlServiceLeafNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ctlModel", "ctlVal", "ctlNum", "stSeld", "SBO", "SBOw", "Oper", "Cancel",
        "origin", "T", "Test", "Check", "operTm", "sboClass", "sboTimeout", "operTimeout"
    };

    private static readonly FallbackPoint[] CommonLogicalNodeFallbacks =
    {
        new("Mod", "ST", "stVal", "Enum", "Status"),
        new("Beh", "ST", "stVal", "Enum", "Status"),
        new("Health", "ST", "stVal", "Enum", "Status")
    };

    private static readonly FallbackPoint[] AtccAvrFallbacks =
    {
        new("Loc", "ST", "stVal", "Boolean", "Status"),
        new("TapChg", "ST", "valWTr.posVal", "Int32", "Status"),
        new("ParOp", "ST", "stVal", "Boolean", "Status"),
        new("LTCBlk", "ST", "stVal", "Boolean", "Status"),
        new("MasterSel", "ST", "stVal", "Boolean", "Status"),
        new("FollowSel", "ST", "stVal", "Boolean", "Status"),
        new("CircASel", "ST", "stVal", "Boolean", "Status"),
        new("CircAPFSel", "ST", "stVal", "Boolean", "Status"),
        new("FuncMon", "ST", "stVal", "Boolean", "Status"),
        new("Auto", "ST", "stVal", "Boolean", "Status"),
        new("LTCBlkVLo", "ST", "stVal", "Boolean", "Status"),
        new("LTCBlkVHi", "ST", "stVal", "Boolean", "Status"),
        new("LTCBlkAHi", "ST", "stVal", "Boolean", "Status"),
        new("LDC", "ST", "stVal", "Boolean", "Status"),
        new("ErrPar", "ST", "stVal", "Boolean", "Status"),
        new("OpCntRs", "ST", "stVal", "Int32", "Status"),
        new("CtlV", "MX", "mag.f", "Float32", "Measurement", "V"),
        new("LodA", "MX", "mag.f", "Float32", "Measurement", "A"),
        new("CircA", "MX", "mag.f", "Float32", "Measurement", "A"),
        new("PhAng", "MX", "mag.f", "Float32", "Measurement", "deg"),
        new("CtlDv", "MX", "mag.f", "Float32", "Measurement", "V")
    };

    private static readonly FallbackPoint[] GgioFallbacks =
    {
        new("Ind1", "ST", "stVal", "Boolean", "Status"),
        new("Ind2", "ST", "stVal", "Boolean", "Status"),
        new("Ind3", "ST", "stVal", "Boolean", "Status"),
        new("Ind4", "ST", "stVal", "Boolean", "Status"),
        new("AnIn1", "MX", "mag.f", "Float32", "Measurement"),
        new("AnIn2", "MX", "mag.f", "Float32", "Measurement"),
        new("AnIn3", "MX", "mag.f", "Float32", "Measurement"),
        new("AnIn4", "MX", "mag.f", "Float32", "Measurement")
    };

    private static readonly FallbackPoint[] AvcoAvrFallbacks =
    {
        new("Loc", "ST", "stVal", "Boolean", "Status"),
        new("TapChg", "ST", "valWTr.posVal", "Int32", "Status")
    };

    private static readonly HashSet<string> AvrMeasuredValueObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "CtlV", "LodA", "CircA", "PhAng", "CtlDv"
    };

    private static readonly HashSet<string> AvrAnalogueSettingObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "RefPF", "BndCtr", "BndCtrV", "BndCtrV1", "BndCtrV2", "BndCtrV3",
        "BndWid", "CtlDlTms", "LDCR", "LDCX", "BlkLV", "LimLodA", "LDCZ"
    };

    private static readonly HashSet<string> AvrBooleanSettingObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "LDC", "TmDlChr", "RefLodTyp"
    };

    public static IReadOnlyList<SignalDefinition> BuildSignals(NativeMmsDiscoverySnapshot snapshot)
    {
        var now = DateTime.Now;
        var signals = new List<SignalDefinition>();

        foreach (var domainPair in snapshot.DomainVariables.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var domain = domainPair.Key?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(domain)) continue;

            foreach (var item in domainPair.Value.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                AddCandidates(signals, domain, item, now);
        }

        // Automation IEDs may publish a separate MMXU DataSet/RCB even when NamedVariable
        // browsing is shallow.  Do not let discovery depend on the first GGIO lane only; use
        // DataSet and report-control names as additional LN hints and create safe read targets.
        AddLogicalNodeFallbacksFromDataSetAndReportHints(signals, snapshot, now);

        if (signals.Count == 0)
        {
            foreach (var domain in snapshot.DomainVariables.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                AddDomainFallbackSignals(signals, domain, now);
        }

        var result = signals
            .Where(ShouldKeepCandidate)
            .GroupBy(s => s.ObjectReference, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.IsScadaCoreSignal).ThenByDescending(x => ConfidenceScore(x.Confidence)).First())
            .OrderBy(s => s.SortPriority)
            .ThenByDescending(s => ConfidenceScore(s.Confidence))
            .ThenBy(s => s.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.ObjectReference, StringComparer.OrdinalIgnoreCase)
            .Take(12000)
            .ToList();

        if (result.Count == 0)
        {
            foreach (var domain in snapshot.DomainVariables.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(new SignalDefinition
                {
                    Name = $"Logical Device {domain}",
                    ObjectReference = domain,
                    FunctionalConstraint = "-",
                    DataType = "Directory",
                    Category = "IED",
                    Confidence = "Low",
                    IsSelected = false,
                    IsReportCapable = false,
                    Source = "Native MMS GetNameList",
                    Value = "Online directory",
                    Quality = "Unknown",
                    Timestamp = now
                });
            }
        }

        return result;
    }

    private static void AddLogicalNodeFallbacksFromDataSetAndReportHints(List<SignalDefinition> signals, NativeMmsDiscoverySnapshot snapshot, DateTime now)
    {
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var domainPair in snapshot.DomainVariableLists.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var domain = domainPair.Key?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(domain)) continue;

            foreach (var raw in domainPair.Value.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var ln = ExtractLogicalNodeFromDataSetListName(raw);
                if (string.IsNullOrWhiteSpace(ln) || ln.Equals("LLN0", StringComparison.OrdinalIgnoreCase))
                    ln = InferLogicalNodeFromName(raw);

                AddHintedLogicalNodeFallback(signals, added, domain, ln, now, "Native MMS DataSet LN profile fallback");
            }
        }

        foreach (var domainPair in snapshot.DomainVariables.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var domain = domainPair.Key?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(domain)) continue;

            foreach (var raw in domainPair.Value.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var parts = raw.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var fcIndex = Array.FindIndex(parts, p => p.Equals("RP", StringComparison.OrdinalIgnoreCase) || p.Equals("BR", StringComparison.OrdinalIgnoreCase));
                if (fcIndex < 1) continue;

                AddHintedLogicalNodeFallback(signals, added, domain, parts[0], now, "Native MMS RCB LN profile fallback");
            }
        }
    }

    private static void AddHintedLogicalNodeFallback(List<SignalDefinition> signals, HashSet<string> added, string domain, string logicalNode, DateTime now, string source)
    {
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(logicalNode)) return;
        var cls = SignalDefinition.DetectLogicalNodeClass(logicalNode);

        // Only synthesize high-value SCADA LNs from hints.  Common LLN0/LPHD health/status is not a monitor signal.
        if (!IsHintedScadaLogicalNodeClass(cls)) return;

        var key = $"{domain}/{logicalNode}";
        if (!added.Add(key)) return;
        AddLogicalNodeFallbackSignals(signals, domain, logicalNode, now, source);
    }

    private static bool IsHintedScadaLogicalNodeClass(string logicalNodeClass)
    {
        var cls = (logicalNodeClass ?? string.Empty).ToUpperInvariant();
        return cls is "GGIO" or "MMXU" or "MMXN" or "CSWI" or "XCBR" or "XSWI" or
               "PTOC" or "PTRC" or "PDIF" or "PDIS" or "PIOC" or "PTOV" or "PTUV" or "PTEF" or "PDEF" or
               "ATCC" or "AVC" or "AVCO" or "YPTR";
    }

    private static string ExtractLogicalNodeFromDataSetListName(string raw)
    {
        var cleaned = (raw ?? string.Empty).Trim().Replace('$', '.');
        if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;

        var slash = cleaned.IndexOf('/');
        var item = slash >= 0 && slash < cleaned.Length - 1 ? cleaned[(slash + 1)..] : cleaned;
        var parts = item.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? parts[0] : string.Empty;
    }

    private static string InferLogicalNodeFromName(string raw)
    {
        var text = (raw ?? string.Empty).Replace('$', '.');
        var match = Regex.Match(text, @"(?<ln>[A-Za-z0-9_]*(?:GGIO|MMXU|MMXN|CSWI|XCBR|XSWI|PTOC|PTRC|PDIF|PDIS|PIOC|PTOV|PTUV|PTEF|PDEF|ATCC|AVCO|AVC|YPTR)\d*)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["ln"].Value : string.Empty;
    }

    private static void AddCandidates(List<SignalDefinition> signals, string domain, string rawItem, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(rawItem)) return;
        var resolvedDomain = (domain ?? string.Empty).Trim();
        var item = NormalizeMmsItemForCandidateParsing(rawItem, ref resolvedDomain);
        if (string.IsNullOrWhiteSpace(resolvedDomain) || string.IsNullOrWhiteSpace(item)) return;
        var parts = item.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
        {
            AddLogicalNodeFallbackSignals(signals, resolvedDomain, parts[0], now, "Native MMS shallow LN fallback");
            return;
        }

        var fcIndex = Array.FindIndex(parts, p => IsFunctionalConstraint(p));
        var logicalNode = parts[0];
        if (fcIndex >= 1)
        {
            // Some discovery paths include the MMS domain before the LN
            // (LD$CSWI1$CO$Pos$Oper$ctlVal) while others start directly with the LN.
            // The LN is always the segment immediately before the functional constraint.
            logicalNode = parts[fcIndex - 1];
            if (fcIndex >= 2 && LooksLikeLogicalDeviceDomain(parts[0], logicalNode))
                resolvedDomain = parts[0];
        }

        if (fcIndex < 1)
        {
            AddShallowDataObjectCandidates(signals, resolvedDomain, logicalNode, parts.Skip(1).ToArray(), now);
            return;
        }

        var fc = parts[fcIndex].ToUpperInvariant();
        if (fc is "BR" or "RP")
        {
            AddLogicalNodeFallbackSignals(signals, resolvedDomain, logicalNode, now, "Native MMS RCB LN profile fallback");
            return;
        }

        var pathParts = parts.Skip(fcIndex + 1).ToArray();
        if (pathParts.Length == 0)
        {
            AddLogicalNodeFallbackSignals(signals, resolvedDomain, logicalNode, now, $"Native MMS {fc} shallow fallback");
            return;
        }

        if (fc.Equals("CO", StringComparison.OrdinalIgnoreCase))
        {
            AddControlCandidate(signals, resolvedDomain, logicalNode, pathParts, now, "Native MMS CO discovery");
            return;
        }

        if (fc.Equals("CF", StringComparison.OrdinalIgnoreCase) &&
            pathParts.Any(part => part.Equals("ctlModel", StringComparison.OrdinalIgnoreCase)))
        {
            AddControlCandidate(signals, resolvedDomain, logicalNode, pathParts, now, "Native MMS ctlModel discovery");
            return;
        }

        // stSeld and ctlNum are status-side evidence that the parent data object is
        // controllable even when a vendor omits CO members from the first NameList page.
        if (fc.Equals("ST", StringComparison.OrdinalIgnoreCase) &&
            pathParts.Skip(1).Any(part =>
                part.Equals("stSeld", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("ctlNum", StringComparison.OrdinalIgnoreCase)))
        {
            AddControlCandidate(signals, resolvedDomain, logicalNode, pathParts, now, "Native MMS control-state evidence");
        }

        foreach (var path in ExpandLikelyLeafPaths(logicalNode, fc, pathParts))
        {
            if (path.Length == 0) continue;
            var reference = $"{resolvedDomain}/{logicalNode}.{string.Join('.', path)}";
            signals.Add(CreateSignal(reference, fc, now));
        }
    }

    private static void AddControlCandidate(
        ICollection<SignalDefinition> signals,
        string domain,
        string logicalNode,
        IReadOnlyList<string> pathParts,
        DateTime now,
        string source)
    {
        if (pathParts.Count == 0)
            return;

        var normalizedParts = pathParts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim())
            .ToArray();
        if (normalizedParts.Length == 0)
            return;

        var serviceLeafIndex = Array.FindIndex(normalizedParts, IsControlServiceLeaf);
        if (serviceLeafIndex == 0)
        {
            // A rootless leaf such as LN$CF$ctlModel or LN$CO$ctlVal is not a
            // controllable Data Object. Keep it out of the command list instead of
            // presenting the attribute itself as a command target.
            return;
        }

        var dataObject = normalizedParts[0];
        if (string.IsNullOrWhiteSpace(dataObject) || IsControlServiceLeaf(dataObject))
            return;

        var reference = $"{domain}/{logicalNode}.{dataObject}";
        if (signals.Any(signal => signal.IsControlSignal &&
                                  signal.ObjectReference.Equals(reference, StringComparison.OrdinalIgnoreCase)))
            return;

        var controlCdc = InferControlCdc(dataObject);
        signals.Add(new SignalDefinition
        {
            Name = $"{logicalNode} {dataObject} Control",
            ObjectReference = reference,
            FunctionalConstraint = "CO",
            DataType = string.IsNullOrWhiteSpace(controlCdc) ? "Control" : $"{controlCdc} Control",
            Category = "Control",
            Confidence = "Medium",
            IsSelected = false,
            IsControlSignal = true,
            ControlCdc = controlCdc,
            ControlModelReference = $"{reference}.ctlModel",
            ControlStatusReference = BuildControlStatusReference(reference, controlCdc),
            ControlModelText = "Auto-detect on command",
            ControlValueType = InferControlValueType(controlCdc),
            IsReportCapable = false,
            ReportCoverage = "Command object",
            ReportCoverageReason = "MMS discovery exposed CO/ctlModel members for this controllable data object.",
            Source = source,
            Value = "Control object",
            Quality = "-",
            DeviceTimestamp = "-",
            ProbeStatus = "Control discovered",
            Timestamp = now
        });
    }

    private static bool IsControlServiceLeaf(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var leaf = value.Contains('.') ? value[(value.LastIndexOf('.') + 1)..] : value;
        return ControlServiceLeafNames.Contains(leaf);
    }

    private static bool IsValidControlObjectReference(string? reference)
    {
        var normalized = (reference ?? string.Empty).Trim().Replace('$', '.').TrimEnd('.');
        if (string.IsNullOrWhiteSpace(normalized))
            return false;
        var leaf = normalized[(normalized.LastIndexOf('.') + 1)..];
        return !IsControlServiceLeaf(leaf);
    }

    private static string InferControlCdc(string dataObject)
    {
        var name = (dataObject ?? string.Empty).ToUpperInvariant();
        if (name.Contains("POS") || name.Contains("DPC")) return "DPC";
        if (name.Contains("TAPOPR") || name.Contains("TAPOPL") || name.Contains("RAISE") || name.Contains("LOWER")) return "SPC";
        if (name.Contains("SPC") || name.Contains("ON") || name.Contains("OFF") || name.Contains("ENA") || name.Contains("BLK")) return "SPC";
        if (name.Contains("INC") || name.Contains("TAPCHG") || name.Contains("STEP")) return "INC";
        if (name.Contains("BSC")) return "BSC";
        if (name.Contains("ISC")) return "ISC";
        if (name.Contains("BAC")) return "BAC";
        if (name.Contains("APC") || name.Contains("SET") || name.Contains("REF")) return "APC";
        return string.Empty;
    }

    private static bool LooksLikeLogicalDeviceDomain(string candidate, string logicalNode)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(logicalNode))
            return false;
        if (candidate.Equals(logicalNode, StringComparison.OrdinalIgnoreCase))
            return false;
        return !SignalDefinition.DetectLogicalNodeClass(candidate)
            .Equals(candidate, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildControlStatusReference(string reference, string cdc)
    {
        var target = (reference ?? string.Empty).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(target)) return string.Empty;
        if (cdc.Equals("BSC", StringComparison.OrdinalIgnoreCase))
            return $"{target}.valWTr.posVal";
        if (cdc.Equals("SPC", StringComparison.OrdinalIgnoreCase) &&
            (target.EndsWith(".TapOpR", StringComparison.OrdinalIgnoreCase) ||
             target.EndsWith(".TapOpL", StringComparison.OrdinalIgnoreCase)))
        {
            var separator = target.LastIndexOf('.');
            return separator > 0 ? $"{target[..separator]}.TapChg.valWTr.posVal" : string.Empty;
        }
        return cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase) ||
               cdc.Equals("SPC", StringComparison.OrdinalIgnoreCase) ||
               cdc.Equals("INC", StringComparison.OrdinalIgnoreCase) ||
               cdc.Equals("ISC", StringComparison.OrdinalIgnoreCase)
            ? $"{target}.stVal"
            : string.Empty;
    }

    private static string InferControlValueType(string cdc)
        => (cdc ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "DPC" => "Dbpos",
            "SPC" => "Boolean",
            "INC" or "BSC" or "ISC" => "Integer",
            "APC" or "BAC" => "Float32",
            _ => "Unknown"
        };

    private static string NormalizeMmsItemForCandidateParsing(string rawItem, ref string domain)
    {
        var item = (rawItem ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(item)) return string.Empty;

        var slash = item.IndexOf('/');
        if (slash > 0 && slash < item.Length - 1)
        {
            var candidateDomain = item[..slash].Trim();
            if (!string.IsNullOrWhiteSpace(candidateDomain))
                domain = candidateDomain;
            item = item[(slash + 1)..];
        }

        // Discovery artifacts are not consistent: ARIEC61850 and the clean-room MMS type-tree
        // can return either MMXU3$MX$PhV$phsA$cVal$mag$f or
        // MMXU3.MX.PhV.phsA.cVal.mag.f.  Parse both as the same MMS item path.
        item = item.Replace('.', '$').Trim('$');
        while (item.Contains("$$", StringComparison.Ordinal))
            item = item.Replace("$$", "$", StringComparison.Ordinal);
        return item;
    }

    private static IEnumerable<string[]> ExpandLikelyLeafPaths(string logicalNode, string fc, string[] pathParts)
    {
        var current = pathParts.Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        if (current.Length == 0) yield break;

        if (LooksLikeReadableLeaf(current))
        {
            yield return current;
            yield break;
        }

        var lnClass = SignalDefinition.DetectLogicalNodeClass(logicalNode).ToUpperInvariant();
        var first = current[0];
        var last = current[^1];

        if (string.Equals(fc, "ST", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(first, "Pos", StringComparison.OrdinalIgnoreCase))
            {
                yield return Append(current, "stVal");
                yield break;
            }

            if (IsProtectionClass(lnClass) && (EqualsAny(first, "Op", "Str", "Tr") || EqualsAny(last, "Op", "Str", "Tr")))
            {
                yield return Append(current, "general");
                yield break;
            }

            if (!EqualsAny(first, "q", "t"))
            {
                yield return Append(current, "stVal");
                yield break;
            }
        }

        if (string.Equals(fc, "MX", StringComparison.OrdinalIgnoreCase))
        {
            if (EqualsAny(first, "A", "PhV", "PPV"))
            {
                foreach (var expanded in ExpandMeasurementMagnitude(current, first))
                    yield return expanded;
                yield break;
            }

            if (current.Any(p => string.Equals(p, "mag", StringComparison.OrdinalIgnoreCase)))
            {
                yield return Append(current, "f");
                yield break;
            }
        }

        // Keep conservative raw leaf for searchable diagnostics. The later read path will mark it Bad if it is structural only.
        yield return current;
    }

    private static void AddShallowDataObjectCandidates(List<SignalDefinition> signals, string domain, string logicalNode, string[] pathParts, DateTime now)
    {
        if (pathParts.Length == 0)
        {
            AddLogicalNodeFallbackSignals(signals, domain, logicalNode, now, "Native MMS shallow DO fallback");
            return;
        }

        var first = pathParts[0];
        foreach (var point in InferFallbackPoints(logicalNode, first))
        {
            var path = string.IsNullOrWhiteSpace(point.LeafPath)
                ? first
                : $"{first}.{point.LeafPath}";
            signals.Add(CreateSignal($"{domain}/{logicalNode}.{path}", point.FunctionalConstraint, now, "Native MMS shallow object fallback", point.DataType, point.Category, point.Unit));
        }
    }

    private static void AddDomainFallbackSignals(List<SignalDefinition> signals, string domain, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return;

        if (!LooksLikeAvrDomain(domain))
        {
            signals.Add(new SignalDefinition
            {
                Name = $"Logical Device {domain}",
                ObjectReference = domain,
                FunctionalConstraint = "-",
                DataType = "Directory",
                Category = "IED",
                Confidence = "Low",
                IsSelected = false,
                IsReportCapable = false,
                Source = "Native MMS GetNameList",
                Value = "Online directory",
                Quality = "Unknown",
                Timestamp = now
            });
            return;
        }

        // Some AVR IEDs expose only the logical-device shell through GetNameList(NamedVariable)
        // while commercial browsers still show the LN/DO tree by reading common AVR objects.
        // Keep this as low-risk discovery scaffolding: every point is still probed/polled normally.
        foreach (var ln in new[] { "LLN0", "LPHD1", "ATCC1", "AVC01", "GGIO1", "GGIO2", "MMXU1", "YPTR1" })
            AddLogicalNodeFallbackSignals(signals, domain, ln, now, "Native MMS AVR profile fallback");
    }

    private static void AddLogicalNodeFallbackSignals(List<SignalDefinition> signals, string domain, string logicalNode, DateTime now, string source)
    {
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(logicalNode))
            return;

        foreach (var point in InferLogicalNodeFallbackPoints(logicalNode))
        {
            var reference = $"{domain}/{logicalNode}.{point.DataObject}.{point.LeafPath}";
            signals.Add(CreateSignal(reference, point.FunctionalConstraint, now, source, point.DataType, point.Category, point.Unit));
        }
    }

    private static IEnumerable<FallbackPoint> InferLogicalNodeFallbackPoints(string logicalNode)
    {
        var lnClass = SignalDefinition.DetectLogicalNodeClass(logicalNode).ToUpperInvariant();

        foreach (var point in CommonLogicalNodeFallbacks)
            yield return point;

        if (lnClass == "ATCC")
        {
            foreach (var point in AtccAvrFallbacks)
                yield return point;
            yield break;
        }

        if (lnClass is "AVC" or "AVCO")
        {
            foreach (var point in AvcoAvrFallbacks)
                yield return point;
            yield break;
        }

        if (lnClass == "GGIO")
        {
            foreach (var point in GgioFallbacks)
                yield return point;
            yield break;
        }

        if (lnClass == "MMXU")
        {
            foreach (var point in new[]
            {
                new FallbackPoint("PhV", "MX", "phsA.cVal.mag.f", "Float32", "Measurement", "V"),
                new FallbackPoint("PhV", "MX", "phsB.cVal.mag.f", "Float32", "Measurement", "V"),
                new FallbackPoint("PhV", "MX", "phsC.cVal.mag.f", "Float32", "Measurement", "V"),
                new FallbackPoint("A", "MX", "phsA.cVal.mag.f", "Float32", "Measurement", "A"),
                new FallbackPoint("A", "MX", "phsB.cVal.mag.f", "Float32", "Measurement", "A"),
                new FallbackPoint("A", "MX", "phsC.cVal.mag.f", "Float32", "Measurement", "A"),
                new FallbackPoint("PPV", "MX", "phsAB.cVal.mag.f", "Float32", "Measurement", "V"),
                new FallbackPoint("PPV", "MX", "phsBC.cVal.mag.f", "Float32", "Measurement", "V"),
                new FallbackPoint("PPV", "MX", "phsCA.cVal.mag.f", "Float32", "Measurement", "V")
            })
                yield return point;
            yield break;
        }

        // Do not synthesize YPTR TapPos from an LN shell alone. Some AVR IEDs expose YPTR
        // as limit/exceeded flags only; real TapPos is still discovered when the DO exists.
    }

    private static IEnumerable<FallbackPoint> InferFallbackPoints(string logicalNode, string dataObjectName)
    {
        var name = dataObjectName.Trim();
        var lnClass = SignalDefinition.DetectLogicalNodeClass(logicalNode).ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(name))
            yield break;

        var knownAtcc = AtccAvrFallbacks
            .Concat(CommonLogicalNodeFallbacks)
            .FirstOrDefault(p => p.DataObject.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (knownAtcc != null)
        {
            yield return knownAtcc with { DataObject = name };
            yield break;
        }

        if (lnClass is "ATCC" or "AVC" or "AVCO")
        {
            if (AvrMeasuredValueObjects.Contains(name))
            {
                yield return new FallbackPoint(name, "MX", "mag.f", "Float32", "Measurement", InferUnitFromDataObject(name));
                yield break;
            }

            if (AvrAnalogueSettingObjects.Contains(name))
            {
                yield return new FallbackPoint(name, "SP", "setMag.f", "Float32", "Setting", InferUnitFromDataObject(name));
                yield break;
            }

            if (AvrBooleanSettingObjects.Contains(name))
            {
                yield return new FallbackPoint(name, "SP", "setVal", "Boolean", "Setting");
                yield break;
            }

            if (LooksLikeAvrStatusDataObject(name))
            {
                yield return new FallbackPoint(name, "ST", "stVal", "Boolean", "Status");
                yield break;
            }
        }

        if (lnClass == "MMXU" && EqualsAny(name, "A", "PhV", "PPV"))
        {
            var unit = name.Equals("A", StringComparison.OrdinalIgnoreCase) ? "A" : "V";
            foreach (var path in ExpandMeasurementMagnitude(new[] { name }, name))
                yield return new FallbackPoint(name, "MX", string.Join('.', path.Skip(1)), "Float32", "Measurement", unit);
            yield break;
        }

        if (LooksLikeAnalogDataObject(name))
        {
            yield return new FallbackPoint(name, "MX", "mag.f", "Float32", "Measurement", InferUnitFromDataObject(name));
            yield break;
        }

        if (LooksLikeIntegerStatusDataObject(name))
        {
            yield return new FallbackPoint(name, "ST", "stVal", "Int32", "Status");
            yield break;
        }

        var type = LooksLikeModeStatusDataObject(name) ? "Enum" : "Boolean";
        yield return new FallbackPoint(name, "ST", "stVal", type, "Status");
    }

    private static bool LooksLikeAvrDomain(string domain)
        => domain.Contains("AVR", StringComparison.OrdinalIgnoreCase) ||
           domain.Contains("ATCC", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeAnalogDataObject(string name)
    {
        return name.Contains("V", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Amp", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("LodA", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("CircA", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("PhAng", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("PF", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Bnd", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Lim", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("LDC", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("CtlD", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeAvrStatusDataObject(string name)
        => name.EndsWith("Sel", StringComparison.OrdinalIgnoreCase) ||
           name.EndsWith("Act", StringComparison.OrdinalIgnoreCase) ||
           name.EndsWith("Ex", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Blk", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Err", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("ParOp", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("FuncMon", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("Loc", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeIntegerStatusDataObject(string name)
        => name.Contains("Cnt", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("TapChg", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("TapPos", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeModeStatusDataObject(string name)
        => EqualsAny(name, "Mod", "Beh", "Health");

    private static string InferUnitFromDataObject(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains('v')) return "V";
        if (lower.Contains("loda") || lower.Contains("circa") || lower.Contains("limloda")) return "A";
        if (lower.Contains("phang")) return "deg";
        if (lower.Contains("tms")) return "s";
        return string.Empty;
    }

    private static IEnumerable<string[]> ExpandMeasurementMagnitude(string[] current, string first)
    {
        if (current.Length >= 4 && current.Any(p => string.Equals(p, "cVal", StringComparison.OrdinalIgnoreCase)) && current.Any(p => string.Equals(p, "mag", StringComparison.OrdinalIgnoreCase)))
        {
            yield return current.Last().Equals("f", StringComparison.OrdinalIgnoreCase) ? current : Append(current, "f");
            yield break;
        }

        if (string.Equals(first, "A", StringComparison.OrdinalIgnoreCase) || string.Equals(first, "PhV", StringComparison.OrdinalIgnoreCase))
        {
            yield return new[] { first, "phsA", "cVal", "mag", "f" };
            yield return new[] { first, "phsB", "cVal", "mag", "f" };
            yield return new[] { first, "phsC", "cVal", "mag", "f" };
            yield break;
        }

        if (string.Equals(first, "PPV", StringComparison.OrdinalIgnoreCase))
        {
            yield return new[] { first, "phsAB", "cVal", "mag", "f" };
            yield return new[] { first, "phsBC", "cVal", "mag", "f" };
            yield return new[] { first, "phsCA", "cVal", "mag", "f" };
            yield break;
        }

        yield return Append(current, "cVal", "mag", "f");
    }

    private static SignalDefinition CreateSignal(string reference, string fc, DateTime now)
        => CreateSignal(reference, fc, now, "Native MMS GetNameList", null, null, null);

    private static SignalDefinition CreateSignal(string reference, string fc, DateTime now, string source, string? dataTypeOverride, string? categoryOverride, string? unitOverride)
    {
        var ln = ExtractLogicalNode(reference);
        var category = string.IsNullOrWhiteSpace(categoryOverride) ? InferCategory(reference, ln) : categoryOverride;
        var dataType = string.IsNullOrWhiteSpace(dataTypeOverride) ? InferDataType(reference, fc) : dataTypeOverride;
        var unit = string.IsNullOrWhiteSpace(unitOverride) ? InferUnit(reference) : unitOverride;
        var isCore = SignalDefinition.IsCoreScadaSignal(reference, SignalDefinition.DetectLogicalNodeClass(ln), dataType, category);
        var confidence = InferConfidence(reference, dataType, category, isCore);

        TryBuildCompanionReference(reference, "q", out var qRef);
        TryBuildCompanionReference(reference, "t", out var tRef);

        return new SignalDefinition
        {
            Name = MakeFriendlyName(reference, category),
            ObjectReference = reference,
            FunctionalConstraint = fc,
            DataType = dataType,
            Category = category,
            Unit = unit,
            Confidence = confidence,
            IsSelected = isCore,
            IsReportCapable = false,
            ReportCoverage = "Polling fallback",
            ReportCoverageReason = "Signal is readable by MMS; report DataSet coverage has not been proven yet.",
            QualityReference = qRef,
            TimestampReference = tRef,
            Source = source,
            Value = "Pending read",
            Quality = "Pending",
            Timestamp = now
        };
    }

    private static bool ShouldKeepCandidate(SignalDefinition signal)
    {
        if (signal.DataType == "Directory") return true;
        if (signal.IsControlSignal) return IsValidControlObjectReference(signal.ObjectReference);
        if (signal.IsScadaCoreSignal) return true;

        var normalized = Normalize(signal.ObjectReference);
        if (signal.IsRawAttribute) return false;
        if (SignalDefinition.IsStatisticsOrHarmonicNoise(normalized)) return false;
        if (normalized.EndsWith(".q") || normalized.EndsWith(".t") || normalized.EndsWith(".tm")) return false;
        if (normalized.Contains(".origin") || normalized.Contains(".ctlmodel") || normalized.Contains(".ctlval")) return false;
        if (normalized.Contains(".numpts") || normalized.Contains(".olddata") || normalized.Contains(".configrev")) return false;
        if (normalized.Contains(".mod.") || normalized.Contains(".beh.") || normalized.Contains(".health") || normalized.Contains(".eehealth")) return false;

        return (signal.FunctionalConstraint is "ST" or "MX") &&
               (signal.DataType is "Boolean" or "Enum" or "Float32" or "Int32" or "UInt16" or "Dbpos") &&
               IsKnownScalarSignalReference(normalized, signal.DataType);
    }

    private static bool IsKnownScalarSignalReference(string normalizedReference, string dataType)
    {
        if (normalizedReference.EndsWith(".stval") ||
            normalizedReference.EndsWith(".general") ||
            normalizedReference.EndsWith(".posval") ||
            normalizedReference.EndsWith(".actval") ||
            normalizedReference.EndsWith(".setval") ||
            normalizedReference.EndsWith(".ctlval") ||
            normalizedReference.EndsWith(".ctlmodel") ||
            normalizedReference.EndsWith(".f") ||
            normalizedReference.EndsWith(".i"))
        {
            return true;
        }

        if (string.Equals(dataType, "Float32", StringComparison.OrdinalIgnoreCase))
            return normalizedReference.Contains(".mag.") || normalizedReference.Contains(".ang.");

        return false;
    }

    private static bool LooksLikeReadableLeaf(string[] parts)
    {
        var last = parts[^1];
        if (EqualsAny(last, "stVal", "posVal", "q", "t", "general", "f", "i", "ctlVal", "mag", "ang", "setVal", "actVal")) return true;
        return parts.Length >= 4 && parts.Any(p => string.Equals(p, "mag", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFunctionalConstraint(string text) => FunctionalConstraints.Contains(text, StringComparer.OrdinalIgnoreCase);

    private static string[] Append(string[] input, params string[] suffix)
    {
        var output = new string[input.Length + suffix.Length];
        Array.Copy(input, output, input.Length);
        Array.Copy(suffix, 0, output, input.Length, suffix.Length);
        return output;
    }

    private static bool EqualsAny(string text, params string[] candidates)
        => candidates.Any(c => string.Equals(text, c, StringComparison.OrdinalIgnoreCase));

    private static bool IsProtectionClass(string lnClass)
        => lnClass is "PTOC" or "PTRC" or "PDIF" or "PDIS" or "PIOC" or "PTOV" or "PTUV" or "PTEF" or "PDEF" or "RREC" or "RBRF";

    private static string ExtractLogicalNode(string reference)
    {
        var slash = reference.IndexOf('/');
        if (slash < 0 || slash >= reference.Length - 1) return string.Empty;
        var after = reference[(slash + 1)..];
        var dot = after.IndexOf('.');
        return dot > 0 ? after[..dot] : after;
    }

    private static string InferCategory(string reference, string ln)
    {
        var r = Normalize(reference);
        var cls = SignalDefinition.DetectLogicalNodeClass(ln).ToUpperInvariant();
        if (r.EndsWith(".q")) return "Quality";
        if (r.EndsWith(".t") || r.EndsWith(".tm")) return "Timestamp";
        if (r.EndsWith(".pos.stval")) return "Position";
        if (r.Contains(".setmag") || r.EndsWith(".setval")) return "Setting";
        if (r.EndsWith(".mag.f") || r.Contains(".cval.mag.f")) return "Measurement";
        if (IsProtectionClass(cls) || r.EndsWith(".op.general") || r.EndsWith(".str.general") || r.EndsWith(".tr.general")) return "Protection";
        if (cls is "ATCC" or "AVC" or "AVCO" or "GGIO" or "YPTR") return "Status";
        return "Status";
    }

    private static string InferDataType(string reference, string fc)
    {
        var r = Normalize(reference);
        if (r.EndsWith(".pos.stval")) return "Dbpos";
        if (r.EndsWith(".posval")) return "Int32";
        if (r.EndsWith(".q")) return "Quality";
        if (r.EndsWith(".t") || r.EndsWith(".tm")) return "Timestamp";
        if (r.Contains(".setmag")) return "Float32";
        if (r.EndsWith(".setval")) return "Boolean";
        if (r.EndsWith(".mag.f") || r.EndsWith(".ang.f")) return "Float32";
        if (r.EndsWith(".general")) return "Boolean";
        if (r.Contains("cnt") || r.Contains("tapchg") || r.Contains("tappos")) return "Int32";
        if (r.EndsWith(".stval")) return fc.Equals("ST", StringComparison.OrdinalIgnoreCase) ? "Enum" : "Int32";
        return fc.Equals("MX", StringComparison.OrdinalIgnoreCase) ? "Float32" : "Enum";
    }

    private static string InferUnit(string reference)
    {
        var r = Normalize(reference);
        if (r.Contains(".a.")) return "A";
        if (r.Contains("loda") || r.Contains("circa") || r.Contains("limloda")) return "A";
        if (r.Contains(".phv.") || r.Contains(".ppv.")) return "V";
        if (r.Contains("ctlv") || r.Contains("bndctrv") || r.Contains("ctldv")) return "V";
        if (r.Contains("phang")) return "deg";
        if (r.Contains("tms")) return "s";
        if (r.Contains(".hz")) return "Hz";
        return string.Empty;
    }

    private static string InferConfidence(string reference, string dataType, string category, bool isCore)
    {
        if (isCore) return "High";
        if ((category is "Status" or "Protection") && (dataType is "Boolean" or "Enum")) return "Medium";
        if (category == "Measurement" && dataType == "Float32") return "Medium";
        return "Low";
    }

    private static string MakeFriendlyName(string reference, string category)
    {
        var ln = ExtractLogicalNode(reference);
        var afterSlash = reference.Contains('/') ? reference[(reference.IndexOf('/') + 1)..] : reference;
        var dot = afterSlash.IndexOf('.');
        var path = dot >= 0 ? afterSlash[(dot + 1)..] : afterSlash;
        path = Regex.Replace(path, @"\.", " ");
        return string.IsNullOrWhiteSpace(ln) ? $"{category} {path}" : $"{ln} {path}";
    }

    private static bool TryBuildCompanionReference(string reference, string companion, out string companionReference)
    {
        companionReference = string.Empty;
        if (!companion.Equals("q", StringComparison.OrdinalIgnoreCase) && !companion.Equals("t", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(reference)) return false;

        var normalized = reference.Replace('$', '.').Trim();
        if (normalized.EndsWith(".q", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith(".t", StringComparison.OrdinalIgnoreCase)) return false;

        var parent = normalized;
        if (parent.EndsWith(".valWTr.posVal", StringComparison.OrdinalIgnoreCase)) parent = parent[..^14];
        else if (parent.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase)) parent = parent[..^6];
        else if (parent.EndsWith(".general", StringComparison.OrdinalIgnoreCase)) parent = parent[..^8];
        else if (parent.EndsWith(".cVal.mag.f", StringComparison.OrdinalIgnoreCase)) parent = parent[..^11];
        else if (parent.EndsWith(".mag.f", StringComparison.OrdinalIgnoreCase)) parent = parent[..^6];
        else
        {
            var slash = parent.IndexOf('/');
            var dot = parent.LastIndexOf('.');
            if (dot <= slash) return false;
            parent = parent[..dot];
        }

        if (string.IsNullOrWhiteSpace(parent)) return false;
        companionReference = $"{parent}.{companion.ToLowerInvariant()}";
        return true;
    }

    private static int ConfidenceScore(string confidence) => confidence switch
    {
        "High" => 3,
        "Medium" => 2,
        "Low" => 1,
        _ => 0
    };

    private static string Normalize(string reference)
        => (reference ?? string.Empty).Replace('$', '.').Replace("..", ".").ToLowerInvariant();
}
