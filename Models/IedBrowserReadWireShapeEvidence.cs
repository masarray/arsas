using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Bounded failure-diagnostic metadata from one decoded MMS result. Never
/// copies raw BER, object payload or vendor strings into the evidence CSV.
/// Wire kind is NOT invoke-ID, GVA status or proof of successful scalar read.
/// </summary>
public static class IedBrowserReadWireShapeEvidence
{
    public static string Describe(MmsDataValue? decoded)
    {
        if (decoded is null) return "wire=null";
        var type = decoded.Value?.GetType().Name ?? "null";
        var kind = decoded.Kind.ToString();
        var scalarOrTree = decoded.Kind is MmsDataKind.Structure or MmsDataKind.Array
            ? "composite" : "primitive";
        return "wireKind=" + kind +
            ", category=" + scalarOrTree +
            ", clrKind=" + type +
            ", rawOctets=" + decoded.RawValue.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture) +
            ", childCount=" + decoded.Children.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture) +
            (decoded.Kind == MmsDataKind.Unknown
                ? ", unknownTag=" + decoded.UnknownTagNumber.ToString()
                : "");
    }
}
