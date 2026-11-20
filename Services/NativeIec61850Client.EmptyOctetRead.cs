using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    /// <summary>
    /// Respect the actual wire type: an empty octet string is an observed
    /// zero-length value, not an MMS Read failure. Do not change unknown tags,
    /// failed AccessResults, empty VisibleString, absent FC data or structures.
    /// The display is a labelled rendering and not fabricated payload bytes.
    /// </summary>
    private static Iec61850ReadValue? PreserveVerifiedEmptyOctet(
        Iec61850ReadValue? projected, MmsDataValue? wire, string modelType)
    {
        if (projected is null || projected.Value is not string { Length: 0 })
            return projected;
        var proved = IedBrowserReadValuePolicy.TryFormatVerifiedEmptyOctet(
            wire, modelType, out var display) ||
            IedBrowserReadValuePolicy.TryFormatVerifiedEmptyVisible(
                wire, modelType, out display);
        if (!proved) return projected;

        return new Iec61850ReadValue
        {
            Value = projected.Value,
            DisplayValue = display,
            Quality = projected.Quality,
            DeviceTimestamp = projected.DeviceTimestamp,
            SourceReference = projected.SourceReference,
            ReadReference = projected.ReadReference,
            Projection = projected.Projection + "; verified zero-length wire string (" + modelType + ")",
            QualityEvidence = projected.QualityEvidence,
            UtcTimeEvidence = projected.UtcTimeEvidence
        };
    }
}
