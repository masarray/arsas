using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// On-demand read-only acceptance counts over the SAME association-fenced
/// canonical ledger. No network call, second value cache, synthetic DataSet
/// member or unproven inter-consumer parity claim.
/// </summary>
public sealed record IedBrowserAcceptanceMetrics(
    int ReadableAttributes,
    int FreshObserved,
    int MissingOrDeferred,
    int FailedReadSamples,
    int CfPairOwners,
    int CfPairsProven,
    int SgEnumObserved,
    int SgEnumDecoded,
    int QualityObserved,
    int QualityDecoded,
    int TimestampObserved,
    int TimestampDecoded,
    int? ConsumerMismatchCount = null)
{
    public string Summary =>
        $"IEC SSOT · readable {ReadableAttributes:N0} · fresh {FreshObserved:N0} " +
        $"· missing/deferred {MissingOrDeferred:N0} · failed read samples {FailedReadSamples:N0} " +
        $"· CF {CfPairsProven}/{CfPairOwners} · SG enum {SgEnumDecoded}/{SgEnumObserved} " +
        $"· q {QualityDecoded}/{QualityObserved} · t {TimestampDecoded}/{TimestampObserved} " +
        "· consumer mismatch not measured";

    public static IedBrowserAcceptanceMetrics Capture(
        LiveIedModelDiscoveryDocument model,
        IedBrowserLiveValues ledger,
        IedBrowserSclEnumEvidence? enumEvidence = null,
        IReadOnlyList<IedBrowserReadFailureSample>? failures = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(ledger);
        var readableFc = new HashSet<string>(
            ["ST", "MX", "CF", "DC", "SP", "SG", "SE", "EX"],
            StringComparer.OrdinalIgnoreCase);
        var targets = model.LogicalDevices.SelectMany(ld => ld.LogicalNodes)
            .SelectMany(ln => ln.DataObjects).SelectMany(o => o.Attributes)
            .Where(a => readableFc.Contains(a.FunctionalConstraint) &&
                !string.IsNullOrWhiteSpace(a.MmsReference))
            .GroupBy(a => Key(a.MmsReference, a.FunctionalConstraint),
                StringComparer.Ordinal)
            // Duplicate declarations are never counted as independently
            // verified attributes, nor used to fabricate coverage.
            .Where(g => g.Count() == 1).Select(g => g.Single()).ToArray();
        var fresh = 0;
        var qObserved = 0;
        var qDecoded = 0;
        var tObserved = 0;
        var tDecoded = 0;
        var sgObserved = 0;
        var sgDecoded = 0;
        var missing = 0;

        foreach (var target in targets)
        {
            var reference = IedBrowserLiveValues.CanonicalReference(target.MmsReference);
            var fc = target.FunctionalConstraint;
            var isQuality = reference.EndsWith(".q", StringComparison.Ordinal);
            var isTime = reference.EndsWith(".t", StringComparison.Ordinal);
            var kind = isQuality ? "Quality" : isTime ? "Timestamp" : "";
            if (!ledger.TryGet(reference, fc, out var observed,
                    requiredEvidence: kind) ||
                observed.IsStale || observed.Association != ledger.Association)
            {
                missing++;
                continue;
            }
            fresh++;
            if (isQuality)
            {
                qObserved++;
                if (observed.QualityBits is not null) qDecoded++;
            }
            if (isTime)
            {
                tObserved++;
                if (observed.TimeBits is { IsDecoded: true,
                    HasWireProvenance: true }) tDecoded++;
            }
            if (fc.Equals("SG", StringComparison.OrdinalIgnoreCase) &&
                reference.EndsWith(".setVal", StringComparison.Ordinal) &&
                (target.SclBType.Equals("Enum", StringComparison.OrdinalIgnoreCase) ||
                 target.MmsType.Equals("enumerated", StringComparison.OrdinalIgnoreCase) ||
                 target.MmsType.Equals("enum", StringComparison.OrdinalIgnoreCase)))
            {
                sgObserved++;
                if (enumEvidence?.TryFormat(reference, fc, observed.Value,
                        out _, out _) == true) sgDecoded++;
            }
        }
        var allCf = targets.Where(a =>
            a.FunctionalConstraint.Equals("CF", StringComparison.OrdinalIgnoreCase))
            .Select(a => IedBrowserLiveValues.CanonicalReference(a.MmsReference))
            .ToHashSet(StringComparer.Ordinal);
        var cfOwners = allCf.Where(x =>
            x.EndsWith(".units.SIUnit", StringComparison.Ordinal) &&
            allCf.Contains(x[..^".SIUnit".Length] + ".multiplier"))
            .Select(x => x[..^".SIUnit".Length])
            .ToArray();
        var cfEvidence = new IedBrowserCfEvidencePresentation(ledger, model, null);
        var proved = cfOwners.Count(x =>
            cfEvidence.TryFormatUnitsParent(x, "CF", out _, out _, out var authority) &&
            authority == "Verified IEC CF configuration");
        var failedSamples = failures?.Count(x =>
            x.Kind is IedBrowserReadAbsenceKind.Failed or
                IedBrowserReadAbsenceKind.Interrupted) ?? 0;
        return new IedBrowserAcceptanceMetrics(targets.Length, fresh,
            missing, failedSamples, cfOwners.Length, proved,
            sgObserved, sgDecoded, qObserved, qDecoded, tObserved, tDecoded);
    }

    private static string Key(string reference, string fc)
        => IedBrowserLiveValues.CanonicalReference(reference) + "|" +
           (fc ?? "").Trim().ToUpperInvariant();
}
