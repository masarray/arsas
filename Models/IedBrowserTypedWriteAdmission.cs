using System.Text;
using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Fail-closed admission for an explicit Browser data Write. The live GVA
/// descriptor is a wire-type authority, not proof of server write permission.
/// Control operations, setting-group lifecycles and control-block configuration
/// must use their dedicated IEC services, never this generic path.
/// </summary>
public static class IedBrowserTypedWriteAdmission
{
    /// <summary>Exact scalar descriptor fingerprint, including wire size/constraint.</summary>
    public static string Fingerprint(MmsTypeSpecificationNode type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return $"{type.Signature}|{type.MmsType}|{type.Size?.ToString() ?? "?"}|{type.SizeConstraintKind}|{type.RawSizeConstraint?.ToString() ?? "?"}";
    }

    public static string? RejectReason(
        MmsObjectReference reference,
        MmsVariableAccessAttributesResult live,
        string expectedSignature,
        MmsDataValue proposed)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(proposed);

        if (string.IsNullOrWhiteSpace(reference.Domain) ||
            string.IsNullOrWhiteSpace(reference.Item) ||
            string.IsNullOrWhiteSpace(reference.FunctionalConstraint))
            return "An exact domain, item and functional constraint are required.";

        var fc = reference.FunctionalConstraint.ToUpperInvariant();
        var segments = reference.Item.Split('$');
        if (segments.Length < 4 ||
            !string.Equals(segments[1], fc, StringComparison.OrdinalIgnoreCase))
            return "MMS item does not contain the exact requested FC at its IEC position.";

        if (fc == "CO" || segments.Skip(2).Any(x =>
                x is "Oper" or "SBO" or "SBOw" or "Cancel"))
            return "Control-service members must use IEC Control, not generic Write.";

        // Existing ctlModel workflow invalidates cached control authority and
        // re-inspects the descriptor. Generic data Write MUST NOT bypass it.
        if (segments[^1] == "ctlModel")
            return "ctlModel requires the existing guarded ConfigureControlModel workflow.";

        // Deliberately conservative initial admission. SV substitution,
        // setting groups, report/GOOSE blocks, BL and vendor-specific FCs need
        // individual lifecycles/authorization before promotion.
        if (fc is not ("CF" or "SP" or "DC"))
            return $"FC={fc} has no approved generic data-write policy.";

        if (!live.IsSuccess || live.TypeSpecification is null ||
            live.Reference != reference ||
            string.IsNullOrWhiteSpace(expectedSignature) ||
            !string.Equals(expectedSignature, Fingerprint(live.TypeSpecification), StringComparison.Ordinal))
            return "Current live GVA type or exact expected signature is unavailable/mismatched.";

        var type = live.TypeSpecification;
        if (type.Children.Count != 0 ||
            type.MmsType is "structure" or "array" or "unknown")
            return "Composite or unknown MMS type requires a separately verified schema editor.";

        switch (type.MmsType)
        {
            case "boolean":
                return proposed.Kind == MmsDataKind.Boolean && proposed.Value is bool
                    ? null : "Expected an MMS boolean.";
            case "integer":
                if (proposed.Kind != MmsDataKind.Integer || proposed.Value is not long signed)
                    return "Expected a signed MMS integer.";
                if (!ValidWidth(type.Size)) return "Signed integer wire width is unknown.";
                if (type.Size < 64)
                {
                    var min = -(1L << (type.Size!.Value - 1));
                    var max = (1L << (type.Size!.Value - 1)) - 1;
                    if (signed < min || signed > max)
                        return "Signed value is outside the live MMS width.";
                }
                return null;
            case "unsigned":
                if (proposed.Kind != MmsDataKind.Unsigned || proposed.Value is not ulong unsigned)
                    return "Expected an unsigned MMS integer.";
                if (!ValidWidth(type.Size)) return "Unsigned integer wire width is unknown.";
                if (type.Size < 64 && unsigned >= (1UL << type.Size!.Value))
                    return "Unsigned value is outside the live MMS width.";
                return null;
            case "floating-point":
                // MmsDataCodec currently emits single precision. Never
                // silently narrow FLOAT64 or an unknown wire format.
                return type.Size == 32 && proposed.Kind == MmsDataKind.FloatingPoint &&
                       proposed.Value is float f && float.IsFinite(f)
                    ? null : "Only live FLOAT32 with a finite single-precision value is supported.";
            case "visible-string":
            case "mms-string":
                if (proposed.Kind != (type.MmsType == "visible-string"
                        ? MmsDataKind.VisibleString : MmsDataKind.MmsString) ||
                    proposed.Value is not string s)
                    return "String MMS kind does not match the live type.";
                if (s.Any(c => c > 0x7f || char.IsControl(c)))
                    return "Non-ASCII/control characters cannot be losslessly encoded by the current codec.";
                return ValidByteLength(type, Encoding.ASCII.GetByteCount(s))
                    ? null : "String length violates the live MMS size constraint.";
            case "octet-string":
                return proposed.Kind == MmsDataKind.OctetString &&
                       ValidByteLength(type, proposed.RawValue.Count)
                    ? null : "Octet-string type or declared size mismatch.";
            case "bit-string":
                if (proposed.Kind != MmsDataKind.BitString ||
                    proposed.RawValue.Count < 2) return "Invalid MMS bit-string.";
                var unused = proposed.RawValue[0];
                var count = (proposed.RawValue.Count - 1) * 8 - unused;
                if (unused > 7 || count < 1 ||
                    (proposed.RawValue[^1] & ((1 << unused) - 1)) != 0)
                    return "Bit-string unused bits or padding are invalid.";
                if (type.Size is null ||
                    (type.SizeConstraintKind == MmsTypeSizeConstraintKind.Fixed && count != type.Size) ||
                    (type.SizeConstraintKind == MmsTypeSizeConstraintKind.Maximum && count > type.Size))
                    return "Bit-string width is not verified by live GVA.";
                return null;
            // Unverified UTC/binary time quality and all other primitive
            // kinds remain read-only until exact write-format tests exist.
            default:
                return $"MMS type {type.MmsType} is not enabled for generic Write.";
        }
    }

    private static bool ValidWidth(int? width)
        => width is 8 or 16 or 32 or 64;

    private static bool ValidByteLength(MmsTypeSpecificationNode type, int bytes)
        => type.Size is >= 0 &&
           (type.SizeConstraintKind switch
           {
               MmsTypeSizeConstraintKind.Fixed => bytes == type.Size,
               MmsTypeSizeConstraintKind.Maximum => bytes <= type.Size,
               _ => false
           });
}
