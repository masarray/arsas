using System.Globalization;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Stateless admission of already-decoded, bounded SCL-assisted initial MMS reads
/// into the existing per-IED observation ledger. No network I/O, state cache,
/// event synthesis or second decoder. The caller verifies the live association.
/// </summary>
internal static class IedBrowserInitialEvidenceProjection
{
    internal static int Publish(
        Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument verifiedModel,
        Func<string, TrustedSclInitialValue?> resolve)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(verifiedModel);
        ArgumentNullException.ThrowIfNull(resolve);
        if (!device.IsConnected || device.LiveValues.IsOffline ||
            !ReferenceEquals(device.LiveDiscoveryModel, verifiedModel))
            return 0;

        var association = device.LiveValues.Association;
        var attributes = verifiedModel.LogicalDevices
            .SelectMany(ld => ld.LogicalNodes)
            .SelectMany(ln => ln.DataObjects)
            .SelectMany(obj => obj.Attributes)
            .Where(attr => !string.IsNullOrWhiteSpace(attr.MmsReference) &&
                           !string.IsNullOrWhiteSpace(attr.FunctionalConstraint))
            .ToArray();
        // Ambiguous duplicate declarations cannot authorize an observed value.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var attr in attributes)
        {
            var key = Key(attr.MmsReference, attr.FunctionalConstraint);
            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        var bindings = new IedBrowserReadBindingIndex(device);
        var accepted = 0;
        foreach (var attr in attributes)
        {
            if (!device.IsConnected || device.LiveValues.Association != association ||
                !bindings.IsCurrent)
                break;
            if (counts[Key(attr.MmsReference, attr.FunctionalConstraint)] != 1)
                continue;

            // ARIEC versions can return the same exact leaf as MMS LN$FC$...
            // or canonical IEC LN.DO.DA. Never borrow its FC or change case.
            var observation = resolve(attr.MmsReference) ??
                resolve(IedBrowserLiveValues.CanonicalReference(attr.MmsReference));
            if (observation is null ||
                !string.Equals(observation.FunctionalConstraint,
                    attr.FunctionalConstraint, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(IedBrowserLiveValues.CanonicalReference(observation.Reference),
                    IedBrowserLiveValues.CanonicalReference(attr.MmsReference),
                    StringComparison.Ordinal) ||
                !TypeCompatible(attr.SclBType, observation.SclBType) ||
                !TryScalar(observation.Value, out var rendered) ||
                !IedBrowserReadValuePolicy.TryAcceptDisplay(rendered, out var value, out _))
                continue;

            if (!device.LiveValues.PublishForAssociation(association,
                    attr.MmsReference, attr.FunctionalConstraint, value, "", "", "MMS Read"))
                continue;

            accepted++;
            // Reuse the existing compatibility adapter for Explorer signal/point
            // rows; the ledger alone owns the observational value and Report wins.
            bindings.Apply(new IedBrowserReadResult(
                new IedBrowserReadTarget(attr.MmsReference,
                    attr.FunctionalConstraint, attr.SclBType),
                value, "", "", ""), device.LiveValues, association);
        }
        return accepted;
    }

    private static string Key(string reference, string fc)
        => IedBrowserLiveValues.CanonicalReference(reference) + "|" +
           fc.Trim().ToUpperInvariant();

    private static bool TypeCompatible(string? declared, string? observed)
        => string.IsNullOrWhiteSpace(declared) || string.IsNullOrWhiteSpace(observed) ||
           string.Equals(declared.Trim(), observed.Trim(),
               StringComparison.OrdinalIgnoreCase);

    // No conversion of untyped structures, dates or raw quality/time summaries
    // into fabricated scalar, bit-level quality or UTC wire evidence.
    private static bool TryScalar(object? raw, out string display)
    {
        display = "";
        if (raw is string text)
            display = text;
        else if (raw is bool boolean)
            display = boolean ? "true" : "false";
        else if (raw is float f && float.IsFinite(f))
            display = f.ToString(CultureInfo.InvariantCulture);
        else if (raw is double d && double.IsFinite(d))
            display = d.ToString(CultureInfo.InvariantCulture);
        else if (raw is sbyte or byte or short or ushort or int or uint or long or ulong or decimal)
            display = ((IFormattable)raw).ToString(null, CultureInfo.InvariantCulture);
        return display.Length != 0;
    }
}
