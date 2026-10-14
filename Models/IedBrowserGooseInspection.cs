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
    string SclGoId, ushort? SclAppId);

public static class IedBrowserGooseInspection
{
    public static string Canonical(string? value) => (value ?? "").Trim().Replace('$', '.');

    public static GooseStreamSnapshot? Select(
        IedBrowserGooseExpectedIdentity expected, IEnumerable<GooseStreamSnapshot> candidates)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(candidates);
        var control = Canonical(expected.GoCbRef);
        var dataSet = Canonical(expected.DataSetReference);
        if (control.Length == 0 || dataSet.Length == 0 ||
            string.IsNullOrWhiteSpace(expected.IedName)) return null;

        // Two publishers with the same GoCb identity are ambiguous, never last-one-wins.
        var matches = candidates.Where(frame =>
            string.Equals(frame.ModelIedName, expected.IedName, StringComparison.Ordinal) &&
            string.Equals(Canonical(frame.GoCbRef), control, StringComparison.Ordinal) &&
            string.Equals(Canonical(frame.DataSetReference), dataSet, StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(expected.GoId) ||
                string.Equals(frame.GoId, expected.GoId, StringComparison.Ordinal)) &&
            (!expected.AppId.HasValue || AppId(frame.AppIdText) == expected.AppId.Value) &&
            (!expected.ConfRev.HasValue ||
                uint.TryParse(frame.ConfigurationRevisionText, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var revision) &&
                revision == expected.ConfRev.Value)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
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
