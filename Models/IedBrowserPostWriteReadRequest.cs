using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// One-shot, exact-leaf MMS Read after an operator Write. Resolves targets
/// from the same live discovery model and association as the completed dialog.
/// Nothing is published to the Browser until the normal typed Read pipeline
/// independently returns evidence from the IED.
/// </summary>
public sealed record IedBrowserPostWriteReadRequest(
    string DeviceId, long Association, LiveIedModelDiscoveryDocument Model,
    IReadOnlyList<IedBrowserReadTarget> Targets)
{
    public static IedBrowserPostWriteReadRequest? Create(
        Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument? model,
        long association,
        IReadOnlyCollection<(string Reference, string Fc)> attempted)
    {
        if (model is null || !device.IsConnected || device.IsDemo ||
            device.LiveValues.IsOffline || device.LiveValues.Association != association ||
            !ReferenceEquals(device.LiveDiscoveryModel, model) ||
            attempted.Count is < 1 or > 64)
            return null;

        var attributes = model.LogicalDevices.SelectMany(ld => ld.LogicalNodes)
            .SelectMany(ln => ln.DataObjects)
            .SelectMany(obj => obj.Attributes)
            .Where(attr => attr.FunctionalConstraint is "CF" or "DC" or "SP" or "SG" or "SE")
            .ToLookup(attr => IedBrowserLiveValues.CanonicalReference(attr.MmsReference) +
                              "|" + attr.FunctionalConstraint.ToUpperInvariant(),
                      StringComparer.Ordinal);
        var targets = new List<IedBrowserReadTarget>(attempted.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (reference, fc) in attempted)
        {
            if (string.IsNullOrWhiteSpace(reference) ||
                fc is not ("CF" or "DC" or "SP" or "SG" or "SE")) return null;
            var key = IedBrowserLiveValues.CanonicalReference(reference) + "|" + fc;
            if (!seen.Add(key)) continue;
            var group = attributes[key].Take(2).ToArray();
            if (group.Length != 1) return null; // never borrow another DA or ambiguous FC
            var attr = group[0];
            targets.Add(new IedBrowserReadTarget(attr.MmsReference,
                attr.FunctionalConstraint,
                string.IsNullOrWhiteSpace(attr.SclBType) ? attr.MmsType : attr.SclBType));
        }
        return targets.Count == 0 ? null :
            new IedBrowserPostWriteReadRequest(device.DeviceId, association, model, targets);
    }

    public bool Matches(Iec61850MonitorDevice device)
        => device.DeviceId == DeviceId && device.IsConnected && !device.IsDemo &&
           !device.LiveValues.IsOffline && device.LiveValues.Association == Association &&
           ReferenceEquals(device.LiveDiscoveryModel, Model);
}
