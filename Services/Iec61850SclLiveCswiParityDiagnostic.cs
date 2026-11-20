using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Read-only comparison of an opened SCD design and the exact live MMS model.
/// A DO declared in SCL, an MMS-discovered CO/CF attribute and a live proven
/// command are THREE different facts. This diagnostic never reads, writes,
/// promotes a design-only command or changes an operator's selection.
/// </summary>
public static class Iec61850SclLiveCswiParityDiagnostic
{
    private static readonly string[] RequiredCommandNames = ["Oper", "SBO", "SBOw", "Cancel"];

    public static IReadOnlyList<string> Summarize(Iec61850MonitorDevice device, int sampleBudget = 12)
    {
        ArgumentNullException.ThrowIfNull(device);
        sampleBudget = Math.Clamp(sampleBudget, 0, 24);
        var design = device.SclWorkspace?.DesignModel;
        var live = device.LiveDiscoveryModel;
        if (design is null)
            return ["SCD ↔ live CSWI: NO OPEN SCD for this IED; import the original SCD/IID to compare exact CSWI.Pos command ownership. Do not infer control from XCBR/XSWI status."];

        static Dictionary<string, LiveIedDataObjectModel> Index(LiveIedModelDiscoveryDocument model)
        {
            var result = new Dictionary<string, LiveIedDataObjectModel>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ld in model.LogicalDevices)
            foreach (var ln in ld.LogicalNodes)
            {
                if (!SignalDefinition.DetectLogicalNodeClass(ln.Name)
                        .Equals("CSWI", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var obj in ln.DataObjects)
                {
                    if (!obj.Name.Equals("Pos", StringComparison.Ordinal) ||
                        string.IsNullOrWhiteSpace(obj.Reference)) continue;
                    // Strict exact object identity; duplicate declarations
                    // are not arbitrarily resolved by first insertion.
                    var key = Iec61850ControlIdentity.Normalize(obj.Reference);
                    if (duplicates.Contains(key)) continue;
                    if (!result.TryAdd(key, obj))
                    {
                        duplicates.Add(key);
                        result.Remove(key);
                    }
                }
            }
            return result;
        }

        static bool HasLeaf(LiveIedDataObjectModel obj, string name, string fc)
            => obj.Attributes.Any(a =>
                a.FunctionalConstraint.Equals(fc, StringComparison.OrdinalIgnoreCase) &&
                a.AttributePath.Split('.').FirstOrDefault()
                    ?.Equals(name, StringComparison.Ordinal) == true);

        var designIndex = Index(design);
        var liveIndex = live is null
            ? new Dictionary<string, LiveIedDataObjectModel>(StringComparer.Ordinal)
            : Index(live);
        var keys = designIndex.Keys.Union(liveIndex.Keys, StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var matched = designIndex.Keys.Count(liveIndex.ContainsKey);
        var liveCo = liveIndex.Values.Count(obj =>
            RequiredCommandNames.Any(name => HasLeaf(obj, name, "CO")));
        var verifiedOperable = 0;
        var observations = new List<string>(Math.Min(keys.Length, sampleBudget));
        var signalIndex = device.Signals
            .Where(s => s.IsValidControlObject)
            .GroupBy(s => Iec61850ControlIdentity.Normalize(s.ObjectReference),
                StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);

        foreach (var key in keys)
        {
            designIndex.TryGetValue(key, out var sclObject);
            liveIndex.TryGetValue(key, out var liveObject);
            signalIndex.TryGetValue(key, out var signal);
            if (signal?.ControlModelResolved == true && signal.ControlSupportsOperate &&
                liveObject is not null)
                verifiedOperable++;
            if (observations.Count >= sampleBudget) continue;
            var designOperations = sclObject is null ? "-" :
                string.Join("/", RequiredCommandNames.Where(n => HasLeaf(sclObject, n, "CO")));
            var liveOperations = liveObject is null ? "-" :
                string.Join("/", RequiredCommandNames.Where(n => HasLeaf(liveObject, n, "CO")));
            var liveCtlModelDeclared = liveObject is not null && HasLeaf(liveObject, "ctlModel", "CF");
            var verdict = liveObject is null
                ? "SCL_ONLY / NOT_LIVE_PROVEN"
                : signal?.ControlModelResolved == true && signal.ControlSupportsOperate
                    ? "OPERABLE / LIVE_INSPECTED"
                    : "MODEL_ONLY / LIVE_COMMAND_UNVERIFIED";
            var cdc = signal?.VerifiedLiveControlCdc ?? "";
            observations.Add("  " + key +
                " | SCD_CO=" + (designOperations.Length == 0 ? "-" : designOperations) +
                " | MMS_CO=" + (liveOperations.Length == 0 ? "-" : liveOperations) +
                " | MMS_CF_ctlModel=" + liveCtlModelDeclared +
                " | liveCDC=" + (cdc.Length == 0 ? "unverified" : cdc) +
                " | " + verdict);
        }

        var output = new List<string>
        {
            "SCD ↔ live CSWI: design=" + designIndex.Count +
            ", live=" + liveIndex.Count +
            ", sameExactDO=" + matched +
            ", liveCOdeclared=" + liveCo +
            ", liveOperable=" + verifiedOperable +
            ", SCDnotLive=" + designIndex.Keys.Count(key => !liveIndex.ContainsKey(key)) +
            "; SCD ctlModel configuration is NOT live MMS ctlModel proof"
        };
        output.AddRange(observations);
        if (keys.Length > sampleBudget)
            output.Add("  ... " + (keys.Length - sampleBudget) +
                " additional CSWI.Pos reference(s) omitted from bounded diagnostics");
        return output;
    }
}
