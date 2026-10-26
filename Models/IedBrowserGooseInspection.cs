using System.Globalization;
using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// The existing subscriber owns decoding and wire state. This stateless adapter
/// only accepts uniquely verified IEC identities; it never matches by GoID alone.
/// </summary>
public sealed record IedBrowserGooseExpectedIdentity(
    string IedName, string GoCbRef, string DataSetReference, string GoId,
    ushort? AppId, uint? ConfRev, string SclDestinationMac,
    string SclGoId, ushort? SclAppId,
    ushort? SclVlanId = null, byte? SclVlanPriority = null,
    uint? SclMinTimeMs = null, uint? SclMaxTimeMs = null);

public static class IedBrowserGooseInspection
{
    /// <summary>
    /// Optional static SCL GSE address/time properties, accepted only after
    /// caller's exact GoCB/SCL scope and conflict checks. Never claim live
    /// GoEna or subscriber frame state from these configuration values.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> SclNetworkProperties(
        IedBrowserGooseExpectedIdentity expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var values = new List<(string Name, string Value)>(4);
        if (expected.SclVlanId is { } vid)
            values.Add(("VLAN ID", vid.ToString(CultureInfo.InvariantCulture)));
        if (expected.SclVlanPriority is { } priority)
            values.Add(("VLAN priority", priority.ToString(CultureInfo.InvariantCulture)));
        if (expected.SclMinTimeMs is { } min)
            values.Add(("Minimum time (ms)", min.ToString(CultureInfo.InvariantCulture)));
        if (expected.SclMaxTimeMs is { } max)
            values.Add(("Maximum time (ms)", max.ToString(CultureInfo.InvariantCulture)));
        return values;
    }

    /// <summary>
    /// A passive frame is current only within its *received* IEC 61850
    /// timeAllowedToLive. The subscriber owns wire decoding and sequence
    /// supervision; this is a stateless UI admission rule. Invalid/zero TTL,
    /// future timestamps and expired frames may be shown as historical
    /// evidence, never as current Browser values.
    /// </summary>
    public static bool IsFrameCurrent(
        DateTimeOffset capturedUtc, long timeAllowedToLiveMilliseconds,
        DateTimeOffset nowUtc)
    {
        if (timeAllowedToLiveMilliseconds <= 0 || capturedUtc > nowUtc)
            return false;
        // No int-sized millisecond conversion: preserve the exact unsigned
        // PDU TTL range in long arithmetic, without wraparound.
        return (nowUtc - capturedUtc).TotalMilliseconds <
            timeAllowedToLiveMilliseconds;
    }

    public static string Canonical(string? value) => (value ?? "").Trim().Replace('$', '.');

    public static GooseStreamSnapshot? Select(
        IedBrowserGooseExpectedIdentity expected, IEnumerable<GooseStreamSnapshot> candidates)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(candidates);
        var control = Canonical(expected.GoCbRef);
        var dataSet = Canonical(expected.DataSetReference);
        // GoCB full wire reference is the mandatory authority. Some IEDs
        // expose GO controls but not their DatSet attributes via MMS; a
        // received, exact GoCB can still be inspected in *unbound allData*
        // order. Never infer an FCDA reference from its position.
        if (control.Length == 0 || !control.Contains('/'))
            return null;

        // Two publishers with the same GoCb identity are ambiguous, never last-one-wins.
        var matches = candidates.Where(frame =>
            (string.IsNullOrEmpty(frame.ModelIedName) ||
                string.Equals(frame.ModelIedName, expected.IedName, StringComparison.Ordinal)) &&
            string.Equals(Canonical(frame.GoCbRef), control, StringComparison.Ordinal) &&
            (dataSet.Length == 0 ||
                string.Equals(Canonical(frame.DataSetReference), dataSet,
                    StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(expected.GoId) ||
                string.Equals(frame.GoId, expected.GoId, StringComparison.Ordinal)) &&
            (!expected.AppId.HasValue || AppId(frame.AppIdText) == expected.AppId.Value) &&
            (!expected.ConfRev.HasValue ||
                uint.TryParse(frame.ConfigurationRevisionText, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var revision) &&
                revision == expected.ConfRev.Value)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>
    /// Admit only a complete and contiguous received allData vector when no
    /// exact FCDA directory can be verified. The returned entries remain
    /// explicitly unbound: their position never proves an IEC reference or FC.
    /// Validate the WHOLE PDU before showing even the first bounded UI rows.
    /// </summary>
    public static IReadOnlyList<GooseLeafValueSnapshot>? OrderedUnboundWireEntries(
        GooseStreamSnapshot frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var count = frame.WireDataSetEntryCount;
        if (count <= 0 || count != frame.Leaves.Count)
            return null;
        for (var index = 0; index < count; index++)
        {
            var leaf = frame.Leaves[index];
            if (leaf.DataSetIndex != index || leaf.Order != index + 1 ||
                string.IsNullOrWhiteSpace(leaf.Value) ||
                leaf.Value == "<missing in frame>")
                return null;
        }
        return frame.Leaves;
    }

    public static IReadOnlyList<GooseLeafValueSnapshot>? VerifiedMembers(
        GooseStreamSnapshot frame, LiveIedDataSetModel directory)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(directory);
        var members = directory.Members.OrderBy(x => x.Index).ToArray();
        if (members.Length == 0 || frame.WireDataSetEntryCount != members.Length ||
            frame.Leaves.Count != members.Length ||
            members.Select(x => x.Index).Distinct().Count() != members.Length)
            return null;
        // Validate entire ordered vector before exposing any member value.
        for (var slot = 0; slot < members.Length; slot++)
        {
            var declared = members[slot];
            var wire = frame.Leaves[slot];
            if (wire.DataSetIndex != slot ||
                Canonical(wire.SignalReference) != Canonical(declared.Reference) ||
                !string.Equals(wire.FunctionalConstraint, declared.FunctionalConstraint,
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(wire.Value) || wire.Value == "<missing in frame>")
                return null;
        }
        return frame.Leaves;
    }

    private static ushort? AppId(string value)
        => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
           ushort.TryParse(value.AsSpan(2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
